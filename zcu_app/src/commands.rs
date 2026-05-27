

// ======================= Performs the commands ========================

use std::net::TcpStream;
use std::time::SystemTime;

use bincode::Options;

use crate::{chip, types::*};

use crate::server::send_response;
use crate::config::*;

pub fn config_command(stream: &mut TcpStream, cmd: ConfigCmd){

    //Load new configuration settings
    {
        let mut config = CONFIG.write().unwrap();
        config.chip_index = cmd.chip_index;
        config.bus_bytes_per_chip = cmd.bus_bytes_per_chip;
        config.chip_size_bytes = cmd.chip_size_bytes;
        config.bus_size_in_bytes = cmd.bus_size_in_bytes;
        config.enable_chip_select = cmd.enable_chip_select;
    }

    //Status response 
    let payload: Vec<u8> = vec![0];

    //Send ACK response
    send_response(stream, CMD_CONFIG, payload).unwrap();
    
}

use std::fs;
use std::thread;
use std::time::{Duration, Instant};

// ===================== /proc sampling helpers =====================

struct Sample {
    instant: Instant,
    cpu_total: u64,
    cpu_idle:  u64,
    rx_bytes:  u64,
    tx_bytes:  u64,
}

fn sample_now() -> Sample {
    let (cpu_total, cpu_idle) = read_cpu_totals().unwrap_or((0, 0));
    let (rx_bytes, tx_bytes)  = read_net_bytes().unwrap_or((0, 0));
    Sample { instant: Instant::now(), cpu_total, cpu_idle, rx_bytes, tx_bytes }
}

/// `/proc/uptime` — first field is seconds since boot (as a float).
fn read_uptime_secs() -> f32 {
    fs::read_to_string("/proc/uptime")
        .ok()
        .and_then(|s| s.split_whitespace().next().and_then(|v| v.parse().ok()))
        .unwrap_or(0.0)
}

/// `/proc/stat` first line — sum of jiffies and the idle column.
/// Columns: user nice system idle iowait irq softirq steal guest guest_nice
fn read_cpu_totals() -> Option<(u64, u64)> {
    let s = fs::read_to_string("/proc/stat").ok()?;
    let fields: Vec<u64> = s.lines().next()?
        .split_whitespace()
        .skip(1) // drop the "cpu" label
        .filter_map(|x| x.parse().ok())
        .collect();
    if fields.len() < 4 { return None; }
    Some((fields.iter().sum(), fields[3]))
}

/// `/proc/meminfo` — (MemTotal − MemAvailable) in MB. MemAvailable is the
/// kernel's own estimate of memory free for new allocations *without*
/// reclaiming, which is what you actually want as "used" — using MemFree
/// counts page cache as used and gives misleadingly high numbers.
fn read_ram_used_mb() -> f32 {
    let Ok(s) = fs::read_to_string("/proc/meminfo") else { return 0.0; };
    let parse_kb = |rest: &str| -> u64 {
        rest.split_whitespace().next().and_then(|v| v.parse().ok()).unwrap_or(0)
    };
    let mut total_kb = 0u64;
    let mut avail_kb = 0u64;
    for line in s.lines() {
        if let Some(rest) = line.strip_prefix("MemTotal:")     { total_kb = parse_kb(rest); }
        else if let Some(rest) = line.strip_prefix("MemAvailable:") { avail_kb = parse_kb(rest); }
    }
    total_kb.saturating_sub(avail_kb) as f32 / 1024.0
}

/// `/proc/net/dev` — sum rx/tx bytes across every interface except loopback.
fn read_net_bytes() -> Option<(u64, u64)> {
    let s = fs::read_to_string("/proc/net/dev").ok()?;
    let mut rx = 0u64;
    let mut tx = 0u64;
    for line in s.lines().skip(2) { // first two lines are headers
        let Some((name, rest)) = line.split_once(':') else { continue };
        if name.trim() == "lo" { continue; }
        let f: Vec<u64> = rest.split_whitespace().filter_map(|x| x.parse().ok()).collect();
        // Columns: rx_bytes packets errs drop fifo frame compressed multicast
        //          tx_bytes packets errs drop fifo colls carrier compressed
        if f.len() < 16 { continue; }
        rx += f[0];
        tx += f[8];
    }
    Some((rx, tx))
}

// ============================ info_command ============================

pub fn info_command(stream: &mut TcpStream) {
    let config = CONFIG.read().unwrap();

    // Two samples 100 ms apart, so we can compute CPU% and net throughput.
    // Blocks the request handler for ~100 ms — fine for an info ping at human
    // polling rates. If you ever poll this faster than ~10 Hz, move sampling
    // to a background thread that updates atomics and just read them here.
    let s1 = sample_now();
    thread::sleep(Duration::from_millis(100));
    let s2 = sample_now();

    let dt_secs = (s2.instant - s1.instant).as_secs_f32();

    // CPU usage: 1 − idle_delta / total_delta, scaled to 0..100.
    let total_d = s2.cpu_total.saturating_sub(s1.cpu_total);
    let idle_d  = s2.cpu_idle.saturating_sub(s1.cpu_idle);
    let cpu_usage = if total_d > 0 {
        (total_d.saturating_sub(idle_d) as f32 / total_d as f32) * 100.0
    } else { 0.0 };

    // bytes/s → bits/s → Mbps. "Receive" on the device = downlink.
    let rx_d = s2.rx_bytes.saturating_sub(s1.rx_bytes) as f32;
    let tx_d = s2.tx_bytes.saturating_sub(s1.tx_bytes) as f32;
    let downlink = if dt_secs > 0.0 { (rx_d * 8.0) / (dt_secs * 1_000_000.0) } else { 0.0 };
    let uplink   = if dt_secs > 0.0 { (tx_d * 8.0) / (dt_secs * 1_000_000.0) } else { 0.0 };

    let rsp = InfoRsp {
        manufacturer:     crate::config::MANUFACTURER_NAME.to_owned(),
        model:            crate::config::MODEL_NAME.to_owned(),
        uptime:           read_uptime_secs(),
        cpu_usage,
        ram_usage:        read_ram_used_mb(),
        uplink,
        downlink,
        ram_organization: crate::config::RAM_ORGANIZATION,
        selected_chip:    config.chip_index,
        start_addr:       0,
        end_addr:         0,
        sim_enabled:      crate::config::SIMULATION_MODE,
    };

    send_response(stream, CMD_INFO, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
}

pub fn write_command(stream: &mut TcpStream, cmd: WriteCmd){

    //Load configuration
    let config = CONFIG.read().unwrap();

    //Setup timers
    let start_time = SystemTime::now();
    let mut time_since_last_update = SystemTime::now();

    //Init the pseudo-random generator with the provided seed
    crate::rand::set_seed(cmd.seed);
    crate::rand::set_index(0);

    // Iterate over chip 
    for i in 0..config.chip_size_bytes {

        //Determine the required contents to write
        match match cmd.pattern {
            0 => {
                // All zeros
                chip::write(&config, i, 0)
            },
            1 => {
                // All ones
                chip::write(&config, i, 0xFF)
            },
            2 => {
                // Pseudorandom pattern based on seed
                chip::write(&config, i, crate::rand::rand())
            },
            _ => {
                eprintln!("[!] Invalid pattern in WriteCmd: {}", cmd.pattern);
                return;
            }
        }
        {
            Ok(_) => {},
            Err(e) => {
                eprintln!("[!] Error writing to chip at offset {}: {:?}", i, e);
                return;
            }
        }

        //Check if progress update is needed
        if time_since_last_update.elapsed().unwrap().as_millis() as f32 >= UPDATE_FREQUENCY_MS {

            //Calculate status
            let elapsed = start_time.elapsed().unwrap().as_millis() as f32;
            let percent_complete = (i as f32 / config.chip_size_bytes as f32) * 100.0;  

            //Send status update
            let rsp = WriteRsp {
                bytes_written: i,
                time_spent_ms: elapsed,
                percent_complete,
            };

            let payload = crate::server::codec().serialize(&rsp).unwrap();

            if let Err(e) = send_response(stream, CMD_WRITE, payload) {
                eprintln!("[!] Failed to send progress update: {}", e);
                return;
            }
            
            //Reset timer for next update
            time_since_last_update = SystemTime::now();
        }
        
        //Wait the required amount of delay in ms
        std::thread::sleep(std::time::Duration::from_millis(cmd.delay as u64));
    }

    //Send final status response

    let rsp = WriteRsp {
        bytes_written: config.chip_size_bytes,
        time_spent_ms: start_time.elapsed().unwrap().as_millis() as f32,
        percent_complete: 100.0,
    };

    let payload = crate::server::codec().serialize(&rsp).unwrap();

    if let Err(e) = send_response(stream, CMD_WRITE, payload) {
        eprintln!("[!] Failed to send progress update: {}", e);
        return;
    }
    
}



pub fn verify_command(stream: &mut TcpStream, cmd: VerifyCmd){

    //Load configuration
    let config = CONFIG.read().unwrap();

    //Setup timers
    let start_time = SystemTime::now();
    let mut time_since_last_update = SystemTime::now();

    //Init the pseudo-random generator with the provided seed
    crate::rand::set_seed(cmd.seed);
    crate::rand::set_index(0);

    //Create response structure
    let mut rsp = VerifyRsp {
        bytes_verified: 0,
        time_spent_ms: 0.0,
        percent_complete: 0.0,
        num_errors: 0,
        num_correct: 0,
     };

    // Iterate over chip 
    for i in 0..config.chip_size_bytes {

        //Expected value
        let expected = match cmd.pattern {
            0 => 0, // All zeros
            1 => 0xFF, // All ones
            2 => crate::rand::rand(), // Pseudorandom pattern based on seed
            _ => {
                eprintln!("[!] Invalid pattern in VerifyCmd: {}", cmd.pattern);
                return;
            }
        };

        //Determine the expected contents to verify against
        match crate::chip::read(&config, i){
            Ok(actual) => {
                if actual != expected {
                    //eprintln!("[!] Verify error at offset {}: expected 0x{:02X}, got 0x{:02X}", i, expected, actual);

                    //Increment error count
                    rsp.num_errors += 1;

                } else {
                    //Increment correct count
                    rsp.num_correct += 1;
                }

            },
            Err(e) => {
                //Increment error count
                rsp.num_errors += 1;
                eprintln!("[!] Error reading from chip at offset {}: {:?}", i, e);
            }
        };

        //Check if progress update is needed
        if time_since_last_update.elapsed().unwrap().as_millis() as f32 >= UPDATE_FREQUENCY_MS {

            //Calculate status
            let elapsed = start_time.elapsed().unwrap().as_millis() as f32;
            let percent_complete = (i as f32 / config.chip_size_bytes as f32) * 100.0;  

            //Update bytes verified and percent complete in response structure
            rsp.bytes_verified = i;
            rsp.time_spent_ms = elapsed;
            rsp.percent_complete = percent_complete;

            let payload = crate::server::codec().serialize(&rsp).unwrap();

            if let Err(e) = send_response(stream, CMD_VERIFY, payload) {
                eprintln!("[!] Failed to send progress update: {}", e);
                return;
            }
            
            //Reset timer for next update
            time_since_last_update = SystemTime::now();
        }
        
        
        //Wait the required amount of delay in ms
        std::thread::sleep(std::time::Duration::from_millis(cmd.delay as u64));
    }


    //Send final status response
    rsp.time_spent_ms = start_time.elapsed().unwrap().as_millis() as f32;
    rsp.percent_complete = 100.0;
    rsp.bytes_verified = config.chip_size_bytes;
    send_response(stream, CMD_VERIFY, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}

pub fn dump_command(stream: &mut TcpStream, cmd: DumpCmd){

    //Load configuration
    let config = CONFIG.read().unwrap();

    //Setup timers
    let start_time = SystemTime::now();
    let mut time_since_last_update = SystemTime::now();

    //Get base page address
    let base_address = cmd.offset_start - (cmd.offset_start % PAGE_SIZE as u32); // Align down to page boundary

    // Iterate over requested pages
    for page_num in 0..cmd.num_pages {
        
        let page_address = base_address + (page_num * PAGE_SIZE as u32);

        //Read page data from chip
        let mut page_data = Vec::new();

        let mut num_errors = 0;

        for offset in 0..PAGE_SIZE as u32 {
            match crate::chip::read(&config, page_address + offset) {
                Ok(byte) => page_data.push(byte),
                Err(e) => {
                    eprintln!("[!] Error reading from chip at offset {}: {:?}", page_address + offset, e);
                    page_data.push(0xFE); // Push a placeholder byte on error
                    num_errors += 1;
                }
            }
        }

        //Send page data in response
        let rsp = DumpRsp {
            num_errors: num_errors,
            address: page_address,
            time_spent_ms: time_since_last_update.elapsed().unwrap().as_millis() as f32,
            //Raw bytes are appended to this (3 byte pages)
        };

        let mut payload = crate::server::codec().serialize(&rsp).unwrap();
        payload.extend_from_slice(&page_data);

        if let Err(e) = send_response(stream, CMD_DUMP, payload) {
            eprintln!("[!] Failed to send dump response: {}", e);
            return;
        }

    }

}