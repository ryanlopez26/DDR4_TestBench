use std::sync::{LazyLock, RwLock};
use crate::types::ConfigCmd;

// ================= Generic System Info =======================================

pub const MANUFACTURER_NAME: &'static str = "AMD Xilinx";
pub const MODEL_NAME: &'static str = "ZCU104";

// ================ RAM Configuration (tied to FPGA bitstream) =================================================

pub const PL_RAM_ORGANIZATION: u8 = 16;
pub const PL_ROW:   u8 = 15;
pub const PL_COL:   u8 = 10;
pub const PL_BANK:  u8 = 2;
pub const PL_BG:    u8 = 2;
pub const PL_RANKS: u8 = 1;
pub const PL_STACK_HEIGHT: u8 = 1;
pub const PL_CAPACITY: u8 = 4; //GB
pub const PL_CAS: u8 = 11;
pub const PL_BANK_GROUPS: u8 = 2;

// =============================================================================================================


pub const SIMULATION_MODE: bool = true; // If true, the app will simulate read/write operations instead of performing real hardware access. Useful for testing and development without hardware.

pub const SYNC_MARKER: u32 = 0xDEAD_BEEF;
pub const TERM_MARKER: u32 = 0xCAFE_BABE;

pub const CMD_WRITE: u8 = 0x01;
pub const CMD_VERIFY: u8 = 0x02;
pub const CMD_DUMP: u8 = 0x03;
pub const CMD_CONFIG: u8 = 0x04;
pub const CMD_INFO: u8 = 0x05;

pub const PAGE_SIZE: usize = 1024; // Size of data pages for dump responses

pub const UPDATE_FREQUENCY_MS: f32 = 100.0; // Frequency of progress updates during long operations

//Global configuration variable, protected by a RwLock for concurrent access. Initialized with default values.
pub static CONFIG: LazyLock<RwLock<ConfigCmd>> = LazyLock::new(|| {
    RwLock::new(ConfigCmd {
        chip_index: 0,
        bus_bytes_per_chip: 2,             // x16 default
        chip_size_bytes: 512 * 1024 * 1024, // 512 MiB default
        bus_size_in_bytes: 8,             // 8 bytes per bus word (x64)
        enable_chip_select: false
    })
});