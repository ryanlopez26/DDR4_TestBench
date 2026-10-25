use serde::{Deserialize, Serialize};

use crate::{record::Record, run::Run, task_engine::TaskRequest};

// NOTE: `#[repr(C)]` controls the *in-memory* layout (for FFI / unsafe casts).
// It does NOT control how serde + bincode lays bytes on the wire — that is
// determined by the bincode configuration used to deserialize (see server.rs).
// The two are independent; keeping `#[repr(C)]` is fine if you also use these
// structs across an FFI boundary, but only the field *order and types* affect
// the bincode wire format.

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct WriteCmd {
    pub uuid: u16
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct VerifyCmd {
    pub uuid: u16
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct SaveRunCmd {
    pub run: Run
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct ReadCmd {
    pub uuid: u16
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct DumpCmd {
    pub uuid: u16,
}



#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct DynamicCmd {
    //UUID
    pub uuid: u16,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct GetRecordCmd {
    pub uuid: u16,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct GetCaptureCmd {
    pub uuid: u16,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct GetRunCmd {
    pub id: u16,
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

    // SEFI Threshold
    pub sefi_threshold: f64,

    // Data Mode
    pub data_pattern: u8,
    pub pattern_seed: u64,

    // FPGA image settings
    pub is_x16: bool,
    pub enabled_byte_lanes: u8,
    pub fpga_auto_config: bool,

    // Automated Dynamic Mode
    pub automated_sefi_mode: bool,
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
pub struct genRecordRsp {
    pub uuid: u16
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct genRunRsp {
    pub id: u16
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct GetRecordRsp {
    pub record: Option<Record>
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct SaveRunRsp {
    pub success: bool,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct GetRunRsp {
    pub run: Option<Run>
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct GetCaptureRsp {
    pub capture: Option<Vec<u8>>
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct GetPowerModeRsp {
    pub request_poweroff: bool,
    pub request_reboot: bool
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct EngineStatusRsp {
    pub active_record: Option<Record>,
    pub record_progress: f64,
    pub is_idle: bool,
    pub queue: Vec<TaskRequest>
}
