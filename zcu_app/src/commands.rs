

// ======================= Performs the commands ========================

use std::net::TcpStream;
use std::time::SystemTime;

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
    }

    //Status response 
    let payload: Vec<u8> = vec![0];

    //Send ACK response
    send_response(stream, CMD_CONFIG, payload).unwrap();
    
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

            let payload = bincode::serialize(&rsp).unwrap();

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

    let payload = bincode::serialize(&rsp).unwrap();

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
                    eprintln!("[!] Verify error at offset {}: expected 0x{:02X}, got 0x{:02X}", i, expected, actual);

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

            let payload = bincode::serialize(&rsp).unwrap();

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
    send_response(stream, CMD_VERIFY, bincode::serialize(&rsp).unwrap()).unwrap();
    
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

        let mut payload = bincode::serialize(&rsp).unwrap();
        payload.extend_from_slice(&page_data);

        if let Err(e) = send_response(stream, CMD_DUMP, payload) {
            eprintln!("[!] Failed to send dump response: {}", e);
            return;
        }

    }

}