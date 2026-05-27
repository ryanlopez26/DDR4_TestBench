// Example single-threaded TCP server implementing a framed binary protocol.
//
// Payload structs live in `types.rs` and are deserialized via serde+bincode.
//
// Required Cargo.toml dependencies:
//
//   [dependencies]
//   serde   = { version = "1", features = ["derive"] }
//   bincode = "1"
//
// Packet layout (all framing integers big-endian / network byte order):
//
//   SYNC   : u32   - fixed marker used to (re)synchronize the connection
//   CMD    : u8    - command word; selects how `payload` is interpreted
//   LENGTH : u16   - number of bytes in `payload`
//   PAYLOAD: [u8; LENGTH]
//   TERM   : u32   - fixed marker terminating the packet
//
// Supported commands:
//   0x01 Write  -> WriteCmd  { pattern: u8, seed: u64, delay: u32 }   (13 bytes)
//   0x02 Verify -> VerifyCmd { pattern: u8, seed: u64, delay: u32 }   (13 bytes)
//   0x03 Dump   -> DumpCmd   { offset: u32, size: u32 }               (8 bytes)

use std::io::{self, Read, Write};
use std::net::{TcpListener, TcpStream, ToSocketAddrs};

use bincode::Options;
use serde::de::DeserializeOwned;

use crate::types::{ConfigCmd, DumpCmd, VerifyCmd, WriteCmd};
use crate::config::*;

// --- framing helpers -------------------------------------------------------

fn read_n(stream: &mut TcpStream, n: usize) -> io::Result<Vec<u8>> {
    let mut buf = vec![0u8; n];
    stream.read_exact(&mut buf)?;
    Ok(buf)
}

fn read_u32_be(stream: &mut TcpStream) -> io::Result<u32> {
    let mut buf = [0u8; 4];
    stream.read_exact(&mut buf)?;
    Ok(u32::from_be_bytes(buf))
}

fn read_u16_be(stream: &mut TcpStream) -> io::Result<u16> {
    let mut buf = [0u8; 2];
    stream.read_exact(&mut buf)?;
    Ok(u16::from_be_bytes(buf))
}

fn read_u8(stream: &mut TcpStream) -> io::Result<u8> {
    let mut buf = [0u8; 1];
    stream.read_exact(&mut buf)?;
    Ok(buf[0])
}

// --- serde / bincode payload parsing --------------------------------------

/// Bincode options matching our wire format: fixed-width integers, big-endian,
/// reject any leftover bytes (so a payload that's too long is treated as an error).
pub(crate) fn codec() -> impl Options {
    bincode::DefaultOptions::new()
        .with_big_endian()
        .with_fixint_encoding()
        .reject_trailing_bytes()
}

/// Deserialize a payload into any `T` that implements `Deserialize`.
fn parse_payload<T: DeserializeOwned>(payload: &[u8]) -> io::Result<T> {
    codec()
        .deserialize(payload)
        .map_err(|e| io::Error::new(io::ErrorKind::InvalidData, e))
}

// --- response framing ------------------------------------------------------

/// Frame and transmit a response packet to the client.
///
/// The wire format mirrors the incoming protocol:
///
///     [SYNC u32 BE][CMD u8][LENGTH u16 BE][payload bytes][TERM u32 BE]
///
/// The whole packet is built into a single buffer and written with one
/// `write_all` call so it goes out as one contiguous chunk rather than
/// several small writes.
///
/// Returns an error if `payload` is larger than `u16::MAX` (65535) bytes,
/// since the length field can't represent it.
pub fn send_response(stream: &mut TcpStream, cmd: u8, payload: Vec<u8>) -> io::Result<()> {
    let length: u16 = payload.len().try_into().map_err(|_| {
        io::Error::new(
            io::ErrorKind::InvalidInput,
            format!(
                "response payload too large: {} bytes (max {})",
                payload.len(),
                u16::MAX
            ),
        )
    })?;

    // 4 (SYNC) + 1 (CMD) + 2 (LENGTH) + payload + 4 (TERM)
    let mut packet = Vec::with_capacity(11 + payload.len());
    packet.extend_from_slice(&SYNC_MARKER.to_be_bytes());
    packet.push(cmd);
    packet.extend_from_slice(&length.to_be_bytes());
    packet.extend_from_slice(&payload);
    packet.extend_from_slice(&TERM_MARKER.to_be_bytes());

    stream.write_all(&packet)
}

// --- connection handler ----------------------------------------------------

fn handle_client(mut stream: TcpStream) -> io::Result<()> {
    let peer = stream.peer_addr()?;
    println!("[+] Client connected: {}", peer);

    loop {
        // --- SYNC ---
        let sync = read_u32_be(&mut stream)?;
        if sync != SYNC_MARKER {
            eprintln!(
                "[!] {}: bad SYNC 0x{:08X}, expected 0x{:08X} — dropping connection",
                peer, sync, SYNC_MARKER
            );
            return Ok(());
        }

        // --- CMD ---
        let cmd = read_u8(&mut stream)?;

        // --- LENGTH ---
        let length = read_u16_be(&mut stream)? as usize;

        // --- PAYLOAD ---
        let payload = read_n(&mut stream, length)?;

        // --- TERM ---
        let term = read_u32_be(&mut stream)?;
        if term != TERM_MARKER {
            eprintln!(
                "[!] {}: bad TERM 0x{:08X}, expected 0x{:08X} — dropping connection",
                peer, term, TERM_MARKER
            );
            return Ok(());
        }

        // Dispatch on CMD. Each arm hands `payload` to serde via bincode.
        match cmd {
            CMD_WRITE => match parse_payload::<WriteCmd>(&payload) {
                Ok(w) => {
                    println!(
                        "[{}] Write {{ pattern: 0x{:02X}, seed: 0x{:016X}, delay: {} }}",
                        peer, w.pattern, w.seed, w.delay
                    );
                    
                    //Execute command
                    crate::commands::write_command(&mut stream, w);

                }
                Err(e) => eprintln!("[!] {}: invalid Write payload: {}", peer, e),
            },

            CMD_VERIFY => match parse_payload::<VerifyCmd>(&payload) {
                Ok(v) => {
                    println!(
                        "[{}] Verify {{ pattern: 0x{:02X}, seed: 0x{:016X}, delay: {} }}",
                        peer, v.pattern, v.seed, v.delay
                    );
                    
                    //Execute command
                    crate::commands::verify_command(&mut stream, v);
                }
                Err(e) => eprintln!("[!] {}: invalid Verify payload: {}", peer, e),
            },

            CMD_DUMP => match parse_payload::<DumpCmd>(&payload) {
                Ok(d) => {
                    println!(
                        "[{}] Dump {{ offset: 0x{:08X}, num_pages: {} }}",
                        peer, d.offset_start, d.num_pages
                    );

                    //Execute command
                    crate::commands::dump_command(&mut stream, d);
                }
                Err(e) => eprintln!("[!] {}: invalid Dump payload: {}", peer, e),
            },

            CMD_CONFIG => match parse_payload::<ConfigCmd>(&payload) {
                Ok(c) => {
                    println!(
                        "[{}] Config {{ chip_index: {}, bus_bytes_per_chip: {}, chip_size_bytes: {} }}",
                        peer, c.chip_index, c.bus_bytes_per_chip, c.chip_size_bytes
                    );

                    //Execute command
                    crate::commands::config_command(&mut stream, c);
                }
                Err(e) => eprintln!("[!] {}: invalid Config payload: {}", peer, e),
            },

            CMD_INFO => {
                    println!(
                        "[{}] Info cmd",
                        peer
                    );

                    //Execute command
                    crate::commands::info_command(&mut stream);
   
            },


            other => eprintln!("[!] {}: unknown CMD 0x{:02X}", peer, other),
        }
    }
}

// --- entry point -----------------------------------------------------------

/// Bind to `addr` and serve clients one at a time, forever.
pub fn run<A: ToSocketAddrs>(addr: A) -> io::Result<()> {
    let listener = TcpListener::bind(addr)?;
    println!("Server listening on {}", listener.local_addr()?);

    for incoming in listener.incoming() {
        match incoming {
            Ok(stream) => {
                let peer = stream.peer_addr().ok();
                match handle_client(stream) {
                    Ok(()) => println!("[-] client {:?} disconnected", peer),
                    Err(e) if e.kind() == io::ErrorKind::UnexpectedEof => {
                        println!("[-] client {:?} disconnected", peer);
                    }
                    Err(e) => eprintln!("[!] client {:?} error: {}", peer, e),
                }
                // Loop continues; next client is accepted only now.
            }
            Err(e) => eprintln!("[!] accept error: {}", e),
        }
    }
    Ok(())
}