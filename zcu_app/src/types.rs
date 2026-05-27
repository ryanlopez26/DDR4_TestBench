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
    pub delay: u32,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct VerifyCmd {
    pub pattern: u8,
    pub seed: u64,
    pub delay: u32,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct DumpCmd {
    pub offset_start: u32,
    pub num_pages: u32,
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
    pub num_errors: u32,
    pub num_correct: u32,
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct DumpRsp {
    pub time_spent_ms: f32,
    pub num_errors: u32,
    pub address: u32,
    //Raw bytes are appended to this (1024 byte pages)
}

#[repr(C)]
#[derive(Debug, Deserialize, Serialize)]
pub struct InfoRsp {
    pub manufacturer: String,
    pub model: String,
    pub uptime: f32,
    pub cpu_usage: f32,
    pub ram_usage: f32,
    pub uplink: f32,
    pub downlink: f32,
    pub ram_organization: u8,
    pub selected_chip: u8,
    pub start_addr: u32,
    pub end_addr: u32,
    pub sim_enabled: bool,
}



