use std::{sync::{LazyLock, RwLock}};

use crate::{fpga::LaneStatus::UNKNOWN, gpio, types::ConfigCmd};


//Byte lane status
#[repr(C)]
pub enum LaneStatus {
    VALID,
    FAILED,
    UNKNOWN
}

pub struct FpgaState {
    
    // Whether or not the FPGA is ready
    ready: bool,

    // Byte lane status 
    byte_lane_status: [LaneStatus; 8],

    // Currently enabled lanes
    byte_lane_enabled: u8,

    // Available memory (bytes)
    available_memory: u32 

}

pub static FPGA_STATUS: LazyLock<RwLock<FpgaState>> = LazyLock::new(|| {
    RwLock::new(
        FpgaState { 
            ready: false, 
            byte_lane_enabled: 0xFF,
            byte_lane_status: [UNKNOWN, UNKNOWN, UNKNOWN, UNKNOWN, UNKNOWN, UNKNOWN, UNKNOWN, UNKNOWN], 
            available_memory: 0
        }
    )
});


//Flash the FPGA
pub fn init(){

    //Obtain RW lock
    match FPGA_STATUS.try_write(){
        Ok(mut fpga) => {

            //If current status is ready
            fpga.ready = gpio::get_fpga_loaded_status();

        },
        Err(_) => {
            eprintln!("Fatal fault. FPGA init failed.");
        },
    }
}