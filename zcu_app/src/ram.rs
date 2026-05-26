// sodimm.rs — User-space access to the PL DDR4 SODIMM on the ZCU104.
//
// Approach: mmap the PL DDR4 physical region into the process address space
// via /dev/mem, then expose simple byte read/write helpers.
//
// Requirements:
//   * Linux on the PS (PetaLinux or similar).
//   * Process runs as root (or has CAP_SYS_RAWIO) to open /dev/mem.
//   * Kernel built with CONFIG_DEVMEM=y and CONFIG_STRICT_DEVMEM=n, or with
//     the PL DDR4 region added to the devmem whitelist.
//
// Cargo.toml:
//
//     [dependencies]
//     libc = "0.2"
//
// !!! IMPORTANT — VERIFY THESE CONSTANTS FOR YOUR DESIGN !!!
// The base address and size of PL DDR4 are determined by the MIG/DDR4
// controller IP in your Vivado block design. Open the Address Editor in
// Vivado and read the actual values; the defaults below are typical
// ZynqMP placements but will not be correct for every project.

use std::ffi::CString;
use std::io;
use std::ptr;
use std::sync::OnceLock;

/// Physical base address of the PL DDR4 region (set by Vivado Address Editor).
const PL_DDR4_BASE: libc::off_t = 0x4_0000_0000;

/// Size of the PL DDR4 region in bytes. ZCU104 reference designs are commonly
/// 512 MiB or up to 4 GiB depending on the SODIMM installed.
const PL_DDR4_SIZE: usize = 0x1_0000_0000; // 4 GiB

// Raw pointers aren't Send/Sync by default, but the mmap region itself is
// shared global memory that's safe to access from any thread (the caller is
// responsible for whatever higher-level synchronization their use case needs).
// Wrap the pointer so we can store it in a OnceLock.
struct Mapping(*mut u8);
unsafe impl Send for Mapping {}
unsafe impl Sync for Mapping {}

static MAPPING: OnceLock<Mapping> = OnceLock::new();

/// Open /dev/mem and mmap the PL DDR4 region. Call once at program start.
///
/// Subsequent calls are no-ops and return `Ok(())`.
pub fn init() -> io::Result<()> {
    if MAPPING.get().is_some() {
        return Ok(());
    }

    // --- open /dev/mem ---
    // O_SYNC requests an uncached mapping, which is what we want when other
    // AXI masters (DMA engines, PL logic) may touch the same memory and we
    // don't want stale CPU cache lines hiding their writes.
    let path = CString::new("/dev/mem").unwrap();
    let fd = unsafe { libc::open(path.as_ptr(), libc::O_RDWR | libc::O_SYNC) };
    if fd < 0 {
        return Err(io::Error::last_os_error());
    }

    // --- mmap the PL DDR4 region ---
    let ptr = unsafe {
        libc::mmap(
            ptr::null_mut(),                       // let the kernel choose vaddr
            PL_DDR4_SIZE,
            libc::PROT_READ | libc::PROT_WRITE,
            libc::MAP_SHARED,
            fd,
            PL_DDR4_BASE,
        )
    };

    // mmap holds its own reference to the underlying mapping; closing the fd
    // does not unmap. Close it eagerly so we don't leak the descriptor.
    unsafe { libc::close(fd) };

    if ptr == libc::MAP_FAILED {
        return Err(io::Error::last_os_error());
    }

    // Store the pointer. If another thread raced us and won, unmap ours.
    if let Err(_) = MAPPING.set(Mapping(ptr as *mut u8)) {
        unsafe { libc::munmap(ptr, PL_DDR4_SIZE) };
    }
    Ok(())
}

/// Internal accessor — returns the base pointer or panics if `init()` wasn't called.
fn base() -> *mut u8 {
    MAPPING
        .get()
        .expect("sodimm: init() must be called before read()/write()")
        .0
}

/// Write a single byte at `offset` bytes into PL DDR4.
///
/// Panics if `offset` is past the end of the mapped region or if `init()`
/// has not been called.
pub fn write(offset: u32, value: u8) {
    let off = offset as usize;
    assert!(
        off < PL_DDR4_SIZE,
        "sodimm::write: offset 0x{:X} out of range (size 0x{:X})",
        off,
        PL_DDR4_SIZE
    );
    // Volatile so the compiler can't elide or reorder the store relative to
    // other volatile accesses — important since the memory may be touched
    // by other masters on the AXI fabric.
    unsafe {
        ptr::write_volatile(base().add(off), value);
    }
}

/// Read a single byte at `offset` bytes into PL DDR4.
///
/// Panics if `offset` is past the end of the mapped region or if `init()`
/// has not been called.
pub fn read(offset: u32) -> u8 {
    let off = offset as usize;
    assert!(
        off < PL_DDR4_SIZE,
        "sodimm::read: offset 0x{:X} out of range (size 0x{:X})",
        off,
        PL_DDR4_SIZE
    );
    unsafe { ptr::read_volatile(base().add(off)) }
}
