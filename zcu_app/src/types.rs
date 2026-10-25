use bytemuck::{Pod, Zeroable};
use serde::{Deserialize, Serialize};

// NOTE: `#[repr(C)]` controls the *in-memory* layout (for FFI / unsafe casts).
// It does NOT control how serde + bincode lays bytes on the wire — that is
// determined by the bincode configuration used to deserialize (see server.rs).
// The two are independent; keeping `#[repr(C)]` is fine if you also use these
// structs across an FFI boundary, but only the field *order and types* affect
// the bincode wire format.

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct WriteCmd {
    pub pattern: u8,
    pub seed: u64,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct VerifyCmd {
    pub uuid: u16,
    pub pattern: u8,
    pub seed: u64,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct DynamicCmd {

    //UUID
    pub uuid: u16,

    //Pattern generation
    pub pattern: u8,
    pub seed: u64,

    //Test configuration
    pub sample_size_in_bytes: u32,
    pub wait_for_beam: bool,

    //SEFI Thresholds
    pub trigger_threshold: f32,
}


#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct DumpCmd {
    pub block_offset: u32,
    pub num_blocks: u32,
    pub comparison_mode: bool
}


#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct UUIDCmd {
    pub uuid: u16,
}

// Configuration Structure

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Copy, Clone)]
pub struct ConfigCmd {

    //Log management
    pub enable_logging: bool,

    // Selection settings
    pub num_blocks: u32,
    pub block_size: u32,
    pub block_factor: u32,

    // Data Mode
    pub data_pattern: u8,
    pub pattern_seed: u64,

    // FPGA image settings
    pub is_x16: bool,
    pub enabled_byte_lanes: u8,
    pub fpga_auto_config: bool,
}


// --- Response structures (unchanged; not deserialized from the wire) -------

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct InfoRsp {
    pub beam_signal: bool,
    pub controller_calibrated: bool,
    pub ui_clock: bool,
    pub pl_clock: bool,
    pub fpga_loaded: bool
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct UUIDRsp {
    pub success: bool,
}



