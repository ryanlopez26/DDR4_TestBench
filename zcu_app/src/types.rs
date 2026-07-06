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
    pub pattern: u8,
    pub seed: u64,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct DynamicCmd {

    //Pattern generation
    pub pattern: u8,
    pub seed: u64,

    //Test configuration
    pub sample_size_in_bytes: u32,
    pub wait_for_beam: bool,

    //SEFI Threshold
    pub trigger_threshold: f32,
}


#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct DumpCmd {
    pub offset_start: u32,
    pub num_pages: u32,
    pub comparison_mode: bool
}

// Configuration Structure

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct ConfigCmd {
    pub chip_index: u8,
    pub bus_bytes_per_chip: u8,
    pub bus_size_in_bytes: u32,
    pub chip_size_bytes: u32,
    pub enable_chip_select: bool,
    pub address_multiplier: u32,
}

//Reset Command
#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct ResetCmd {
    pub fpga_reset: bool,
    pub controller_reset: bool
}

// --- Response structures (unchanged; not deserialized from the wire) -------

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct WriteRsp {
    pub bytes_written: u32,
    pub time_spent_ms: f32,
    pub percent_complete: f32,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct VerifyRsp {
    pub bytes_verified: u32,
    pub time_spent_ms: f32,
    pub percent_complete: f32,
    // Verify specific statistics
    pub num_errors: u64,
    pub num_correct: u64,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct DumpRsp {
    pub time_spent_ms: f32,
    pub num_errors: u64,
    pub address: u32,
    //Raw bytes are appended to this (1024 byte pages)
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct DynamicRsp {
    
    //Time statistics
    pub exposure_time_ms: f32,
    pub total_time_ms: f32,
    pub time_to_sefi: f32,

    //Error statistics
    pub total_bytes: u64,
    pub total_errors: u64,
    pub error_rate: f32,
    pub error_percent: f32,

    //Capture status
    pub exposure_started: bool,
    pub sefi_detected: bool,

    //Beam and controller status
    pub beam_signal: bool,
    pub controller_calibrated: bool,
    
}


#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct InfoRsp {
    pub beam_signal: bool,
    pub controller_calibrated: bool,
}



