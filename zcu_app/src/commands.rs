

// ======================= Performs the commands ========================

use std::net::TcpStream;
use std::time::{Duration, SystemTime};

use bincode::Options;

use crate::{chip, gpio, types::*};

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

pub fn dynamic_command(stream: &mut TcpStream, cmd: DynamicCmd){

    //Load configuration
    let config = CONFIG.read().unwrap();

    //Setup required vars
    let total_time = SystemTime::now();
    let mut time_since_last_update = SystemTime::now();

    //Check if we need to wait for the beam
    if(cmd.wait_for_beam){

        //Wait for the beam signal to be high
        while !gpio::get_beam_signal() {
        
            //Check if we need to send status update
            if time_since_last_update.elapsed().unwrap().as_millis() as f32 >= UPDATE_FREQUENCY_MS {

                //Calculate status
                let elapsed = total_time.elapsed().unwrap().as_millis() as f32;

                //Send status update
                let rsp = DynamicRsp {
                    exposure_time_ms: 0.0,
                    total_time_ms: elapsed,
                    total_bytes: 0,
                    total_errors: 0,
                    error_rate: 0.0,
                    error_percent: 0.0,
                    beam_signal: gpio::get_beam_signal(),
                    controller_calibrated: gpio::get_calibration_signal(),
                    exposure_started: false,
                    sefi_detected: false,
                    time_to_sefi: 0.0,
                };

                let payload = crate::server::codec().serialize(&rsp).unwrap();

                if let Err(e) = send_response(stream, CMD_DYNAMIC, payload) {
                    eprintln!("[!] Failed to send progress update: {}", e);
                    return;
                }
                
                //Reset timer for next update
                time_since_last_update = SystemTime::now();
            }
            

        }
    }

    //Begin test
    let time_since_exposure_start = SystemTime::now();
    let mut time_since_last_sample = SystemTime::now();

    //Error Statistics
    let mut total_errors: u64 = 0;
    let mut total_correct: u64 = 0;

    //Rate calculation
    let mut bits_sampled: u64 = 0;
    let mut bits_errored: u64 = 0;
    let mut error_rate: f32 = 0.0;
    let mut error_percent: f32 = 0.0;
    
    //SEFI Detection
    let mut sefi_detected: bool = false;
    let mut time_to_sefi: Duration = Duration::from_secs(0);


    //Wait for beam to become inactive
    while gpio::get_beam_signal() || !cmd.wait_for_beam {

        //Init the pseudo-random generator with the provided seed
        crate::rand::set_seed(cmd.seed);
        crate::rand::set_index(0);

        //Iterate over chip
        for i in (0..config.chip_size_bytes).step_by(config.address_multiplier as usize) {

            //Value to test
            let v = match cmd.pattern {
                0 => {
                    // Zero mode
                    0
                },
                1 => {
                    // All ones
                    1
                },
                2 => {
                    // Random
                    crate::rand::rand()
                },
                _ => {
                    eprintln!("[!] Invalid pattern in DynamicCmd: {}", cmd.pattern);
                    return;
                }};
            

            //Write test value to byte
            chip::write(&config, i, v);

            //Read back and verify the value
            match crate::chip::read(&config, i) {
                Ok(actual) => {
                    if actual != v {
                        eprintln!(
                            "[!] Error at address (expected: {:#x}, actual: {:#x}): {:#x}",
                            v, actual, i
                        );
                        let differing_bits = (actual ^ v).count_ones() as u64;

                        //Totals
                        total_errors += differing_bits;
                        total_correct += 8 - differing_bits;

                        //Rate calculation
                        bits_sampled += 8;
                        bits_errored += differing_bits;

                    } else {
                        bits_sampled += 8;
                        total_correct += 8;
                    }
                },
                Err(e) => {
                    bits_sampled += 8;
                    bits_errored += 8;
                    total_errors += 8;
                    eprintln!("[!] Error reading from chip at offset {}: {:?}", i, e);
                }
            };

            //Calculate rate if needed
            if bits_sampled > cmd.sample_size_in_bytes as u64 * 8 {
                error_rate = time_since_last_sample.elapsed().unwrap().as_millis() as f32 / bits_sampled as f32 * 1000.0; // Errors per second
                error_percent = bits_errored as f32 / bits_sampled as f32;

                //Clear sampling vars
                bits_sampled = 0;
                bits_errored = 0;
                time_since_last_sample = SystemTime::now();

                //Check if a SEFI has been detected
                if error_rate > cmd.trigger_threshold {
                    sefi_detected = true;
                    time_to_sefi = total_time.elapsed().unwrap();
                }
            }

            //Check if progress update is needed
            if time_since_last_update.elapsed().unwrap().as_millis() as f32 >= UPDATE_FREQUENCY_MS {

                //Calculate status
                let elapsed = total_time.elapsed().unwrap().as_millis() as f32;

                //Send status update
                let rsp = DynamicRsp {
                    exposure_time_ms: time_since_exposure_start.elapsed().unwrap().as_millis() as f32,
                    total_time_ms: elapsed,
                    total_bytes: total_correct + total_errors,
                    total_errors: total_errors,
                    error_rate: error_rate,
                    error_percent: error_percent,
                    beam_signal: gpio::get_beam_signal(),
                    controller_calibrated: gpio::get_calibration_signal(),
                    exposure_started: true,
                    sefi_detected: sefi_detected,
                    time_to_sefi: time_to_sefi.as_millis() as f32,
                };

                let payload = crate::server::codec().serialize(&rsp).unwrap();

                if let Err(e) = send_response(stream, CMD_DYNAMIC, payload) {
                    eprintln!("[!] Failed to send progress update: {}", e);
                    return;
                }
                
                //Reset timer for next update
                time_since_last_update = SystemTime::now();

                //Check if beam has gone inactive
                if !gpio::get_beam_signal() {
                    break;
                }

                //Check if SEFI has been detected and beam detection disabled
                if sefi_detected && !cmd.wait_for_beam {
                    break;
                }
    
            }


        }
    };


    
}


pub fn info_command(stream: &mut TcpStream){

    //Create info response struct
    let payload = crate::server::codec().serialize(&InfoRsp {
        beam_signal: gpio::get_beam_signal(),
        controller_calibrated: gpio::get_calibration_signal(),
        ui_clock: gpio::get_ui_clock_signal(),
        pl_clock: gpio::get_pl_clock_signal(),
        fpga_loaded: gpio::get_fpga_loaded_status(),
    }).unwrap();

    //Send ACK response
    send_response(stream, CMD_INFO, payload).unwrap();
    
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
    for i in (0..config.chip_size_bytes).step_by(config.address_multiplier as usize) {

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
                percent_complete
            };

            let payload = crate::server::codec().serialize(&rsp).unwrap();

            if let Err(e) = send_response(stream, CMD_WRITE, payload) {
                eprintln!("[!] Failed to send progress update: {}", e);
                return;
            }
            
            //Reset timer for next update
            time_since_last_update = SystemTime::now();
        }
        
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
    let mut start_time = SystemTime::now();
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
    for i in (0..config.chip_size_bytes).step_by(config.address_multiplier as usize) {

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
        match crate::chip::read(&config, i) {
            Ok(actual) => {
                if actual != expected {
                    // eprintln!(
                    //     "[!] Error at address (expected: {:#x}, actual: {:#x}): {:#x}",
                    //     expected, actual, i
                    // );
                    let differing_bits = (actual ^ expected).count_ones() as u64;
                    rsp.num_errors += differing_bits;
                    rsp.num_correct += 8 - differing_bits;
                } else {
                    rsp.num_correct += 8;
                }
            },
            Err(e) => {
                rsp.num_errors += 8;
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

    
    }

    //Send final status response
    rsp.time_spent_ms = start_time.elapsed().unwrap().as_millis() as f32;
    rsp.percent_complete = 100.0;
    rsp.bytes_verified = config.chip_size_bytes;
    send_response(stream, CMD_VERIFY, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}

pub fn dump_command(stream: &mut TcpStream, cmd: DumpCmd, v_cmd: &VerifyCmd){

    //Load configuration
    let config = CONFIG.read().unwrap();

    //Setup timers
    let start_time = SystemTime::now();
    let mut time_since_last_update = SystemTime::now();


    //Get base page address
    let base_address = cmd.offset_start - (cmd.offset_start % PAGE_SIZE as u32); // Align down to page boundary

    //Init the pseudo-random generator with the provided seed
    crate::rand::set_seed(v_cmd.seed);
    crate::rand::set_index(base_address as u64);


    // Iterate over requested pages
    for page_num in 0..cmd.num_pages {
        
        let page_address = base_address + (page_num * PAGE_SIZE as u32);

        //Read page data from chip
        let mut page_data = Vec::new();

        let mut num_errors = 0;

        //Check which mode we are in 

        if cmd.comparison_mode {

            //Comparison dump

            for offset in 0..PAGE_SIZE as u32 {
                match crate::chip::read(&config, page_address + offset) {
                    Ok(byte) => {

                        //Generate the expected byte
                        let expected = match v_cmd.pattern {
                            0 => 0, // All zeros
                            1 => 0xFF, // All ones
                            2 => crate::rand::rand(), // Pseudorandom pattern based on seed
                            _ => {
                                eprintln!("[!] Invalid pattern in VerifyCmd: {}", v_cmd.pattern);
                                return;
                            }
                        };
                        
                        //Write the XOR (1 if different) of the expected and actual
                        page_data.push(expected ^ byte);


                    },
                    Err(e) => {
                        eprintln!("[!] Error reading from chip at offset {}: {:?}", page_address + offset, e);
                        page_data.push(0xFE); // Push a placeholder byte on error
                        num_errors += 1;
                    }
                }
            }

        } else {

            //Standard direct dump

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