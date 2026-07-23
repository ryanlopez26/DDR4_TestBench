use std::sync::{LazyLock, RwLock};
use crate::types::ConfigCmd;


pub const SIMULATION_MODE: bool = false; // If true, the app will simulate read/write operations instead of performing real hardware access. Useful for testing and development without hardware.

pub const SYNC_MARKER: u32 = 0xDEAD_BEEF;
pub const TERM_MARKER: u32 = 0xCAFE_BABE;

//Scaling block factor (8KB)
pub const SCALING_BLOCK_SIZE: u32  = 0x0000_2000;

pub const CMD_WRITE: u8 = 0x01;
pub const CMD_VERIFY: u8 = 0x02;
pub const CMD_DUMP: u8 = 0x03;
pub const CMD_CONFIG: u8 = 0x04;
pub const CMD_DYNAMIC: u8 = 0x05;
pub const CMD_INFO: u8 = 0x06;
pub const CMD_UUID: u8 = 0x07;
pub const PAGE_SIZE: usize = 1024; // Size of data pages for dump responses

pub const UPDATE_FREQUENCY_MS: f32 = 100.0; // Frequency of progress updates during long operations

//Global configuration variable, protected by a RwLock for concurrent access. Initialized with default values.
pub static CONFIG: LazyLock<RwLock<ConfigCmd>> = LazyLock::new(|| {
    RwLock::new(ConfigCmd {         // x16 default
        block_size : 0x0010_0000, //  1MB default
        block_factor : 1,
        num_blocks : 0x0000_0100, // Test 256MB
        enable_logging: true
    })
});