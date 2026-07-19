// ======================= Performs the commands ========================

use std::net::TcpStream;
use std::time::{Duration, SystemTime};

use bincode::Options;

use crate::{chip, gpio, recorder, types::*, utils};

use crate::server::send_response;
use crate::config::*;

// ============================================================================
//  Feature-gated debug logging.
//
//  Enable with:  cargo build --features debug
//
//  In Cargo.toml add:
//      [features]
//      debug = []
//
//  This macro is `#[macro_export]`, so it lands at the crate root and is
//  callable anywhere as `crate::dbg_log!(...)`. If you prefer, move the two
//  macro definitions below into main.rs / lib.rs near the top — they only need
//  to be defined once per crate.
// ============================================================================

#[cfg(feature = "debug")]
#[macro_export]
macro_rules! dbg_log {
    ($($arg:tt)*) => {
        eprintln!("[dbg] {}", format_args!($($arg)*));
    };
}

#[cfg(not(feature = "debug"))]
#[macro_export]
macro_rules! dbg_log {
    ($($arg:tt)*) => {{}};
}

pub fn config_command(stream: &mut TcpStream, cmd: ConfigCmd){

    crate::dbg_log!(
        "config_command: incoming ConfigCmd chip_index={}, bus_bytes_per_chip={}, chip_size_bytes={}, bus_size_in_bytes={}, enable_chip_select={}, address_multiplier={}",
        cmd.chip_index, cmd.bus_bytes_per_chip, cmd.chip_size_bytes, cmd.bus_size_in_bytes, cmd.enable_chip_select, cmd.address_multiplier
    );

    //Prevent invalid address multiplier
    if cmd.address_multiplier == 0 {
            
        //Invalid status response 
        let payload: Vec<u8> = vec![0];
        send_response(stream, CMD_CONFIG, payload).unwrap();

        //Failed
        return;

    }

    //Load new configuration settings
    {
        let mut config = CONFIG.write().unwrap();
        config.chip_index = cmd.chip_index;
        config.bus_bytes_per_chip = cmd.bus_bytes_per_chip;
        config.chip_size_bytes = cmd.chip_size_bytes;
        config.bus_size_in_bytes = cmd.bus_size_in_bytes;
        config.enable_chip_select = cmd.enable_chip_select;
        config.address_multiplier = cmd.address_multiplier;

        // NOTE: address_multiplier is NOT set from ConfigCmd here. Every loop
        // reads config.address_multiplier; if nothing else assigns it, it holds
        // its default. Logging it so the stale/default value is visible.
        crate::dbg_log!(
            "config_command: post-apply CONFIG chip_size_bytes={}, address_multiplier={} (address_multiplier is NOT written by this handler)",
            config.chip_size_bytes, config.address_multiplier
        );
    }

    //Status response 
    let payload: Vec<u8> = vec![0];

    //Send ACK response
    send_response(stream, CMD_CONFIG, payload).unwrap();
    
}

pub fn uuid_command(stream: &mut TcpStream, cmd: UUIDCmd){

    crate::dbg_log!(
        "uuid_command: incoming ConfigCmd uuid={}",
        cmd.uuid
    );

    //Check UUID
    let rsp = UUIDRsp {
        success: recorder::check_uuid(cmd.uuid),
    };

    crate::dbg_log!(
        "uuid_command: success = {}",
        rsp.success
    );

    //Send ACK response
    send_response(stream, CMD_UUID, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}


pub fn dynamic_command(stream: &mut TcpStream, cmd: DynamicCmd){

    //Load configuration
    let config = CONFIG.read().unwrap();

    //Init the pseudo-random generator with the provided seed
    crate::rand::set_seed(cmd.seed);

    //Response structure
    let mut rsp = DynamicRsp {
        exposure_time_ms: 0.0,
        adj_err_bins: [0, 0, 0, 0, 0, 0, 0, 0],
        err_bins: [0, 0, 0, 0, 0, 0, 0, 0, 0],
        total_time_ms: 0.0,
        total_bytes: 0,
        error_rate: 0.0,
        error_rate_per_second: 0.0,
        error_rate_percent: 0.0,
        beam_signal: gpio::get_beam_signal(),
        controller_calibrated: gpio::get_calibration_signal(),
        exposure_started: false,
        sefi_detected: false,
        time_to_sefi: 0.0,
        test_completed: false,
        ui_clock: gpio::get_ui_clock_signal(),
        pl_clock: gpio::get_pl_clock_signal(),
        fpga_loaded: gpio::get_fpga_loaded_status(),
        pass_counter: 0,
        start_address: 0x00000000,
        end_address: 0x00000000,
        current_address: 0x00000000,
    };

    //Create log
    if config.enable_logging {
        recorder::new(vec![
            "Exposure Time (ms)",
            "Total Time (ms)",
            "Total Bytes",
            "Error Rate",
            "Error Rate (per second)",
            "Error Rate (%)", 
            "SEFI Threshold",
            "Beam Signal",
            "Controller Calibrated",
            "Exposure Started",
            "SEFI Detected",
            "Time to SEFI",
            "Test Completed",
            "Pass Counter",
            "Current Address",
            "Start Address",
            "End Address",
            "adj_err[0]", 
            "adj_err[1]",
            "adj_err[2]",
            "adj_err[3]",
            "adj_err[4]",
            "adj_err[5]",
            "adj_err[6]",
            "adj_err[7]",
            "num_err[0]", 
            "num_err[1]",
            "num_err[2]",
            "num_err[3]",
            "num_err[4]",
            "num_err[5]",
            "num_err[6]",
            "num_err[7]",
            "num_err[8]"
        ]);
    }

    crate::dbg_log!(
        "dynamic_command: seed={}, pattern={}, wait_for_beam={}, sample_size_in_bytes={}, trigger_threshold={}, chip_size_bytes={}, address_multiplier={}",
        cmd.seed, cmd.pattern, cmd.wait_for_beam, cmd.sample_size_in_bytes, cmd.trigger_threshold, config.chip_size_bytes, config.address_multiplier
    );

    if config.address_multiplier == 0 {
        crate::dbg_log!("dynamic_command: WARNING address_multiplier==0, step_by(0) will panic");
    }
    if config.chip_size_bytes == 0 {
        crate::dbg_log!("dynamic_command: WARNING chip_size_bytes==0, loop range is empty and will not execute");
    }

    //Instant when test first started
    let first_start_instant = SystemTime::now();
    let mut last_update_instant = SystemTime::now();

    //Check if we need to wait for the beam
    if cmd.wait_for_beam {

        crate::dbg_log!("dynamic_command: waiting for beam signal to go high...");

        //Wait for the beam signal to be high
        while !gpio::get_beam_signal() {
        
            //Check if we need to send status update
            if last_update_instant.elapsed().unwrap().as_millis() as f32 >= UPDATE_FREQUENCY_MS {

                //Send status update
                rsp.beam_signal = gpio::get_beam_signal();
                rsp.controller_calibrated = gpio::get_calibration_signal();
                rsp.ui_clock = gpio::get_ui_clock_signal();
                rsp.pl_clock = gpio::get_pl_clock_signal();
                rsp.fpga_loaded = gpio::get_fpga_loaded_status();
                rsp.total_time_ms = first_start_instant.elapsed().unwrap().as_millis() as f32;

                //Log if enabled
                if config.enable_logging {

                    recorder::log(
                        vec![
                            format!("{}", rsp.exposure_time_ms),
                            format!("{}", rsp.total_time_ms),
                            format!("{}", rsp.total_bytes),
                            format!("{}", rsp.error_rate),
                            format!("{}", rsp.error_rate_per_second),
                            format!("{}", rsp.error_rate_percent),
                            format!("{}", cmd.trigger_threshold),
                            format!("{}", rsp.beam_signal),
                            format!("{}", rsp.controller_calibrated),
                            format!("{}", rsp.exposure_started),
                            
                        ]
                    );

                }

                let payload = crate::server::codec().serialize(&rsp).unwrap();

                if let Err(e) = send_response(stream, CMD_DYNAMIC, payload) {
                    eprintln!("[!] Failed to send progress update: {}", e);
                    return;
                }
                
                //Reset timer for next update
                last_update_instant = SystemTime::now();
            }
            

        }

        crate::dbg_log!("dynamic_command: beam signal high, beginning exposure");
    }

    //Begin test
    let exposure_start_instant = SystemTime::now();
    let mut last_sample_instant = SystemTime::now();
    rsp.exposure_started = true;

    //Rate calculation
    let mut bits_sampled: u64 = 0;
    let mut bits_errored: u64 = 0;

    //Debug-only pass counter (how many full chip sweeps we complete)
    #[cfg(feature = "debug")]
    let mut pass_count: u64 = 0;

    
    //Exposure present - Perform test
    loop {

        #[cfg(feature = "debug")]
        {
            pass_count += 1;
            crate::dbg_log!("dynamic_command: starting chip sweep #{}", pass_count);
        }

        //Iterate over chip
        for i in (0..config.chip_size_bytes).step_by(config.address_multiplier as usize) {

            // Perform write and verify operation
            {
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
                        crate::rand::rand(i)
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

                            let diff_mask        = actual ^ v;
                            let diff_bits: usize = (diff_mask).count_ones() as usize;
                            let adj_bits         = diff_mask & (diff_mask >> 1);

                            //Collect statistics
                            rsp.err_bins[diff_bits] += 1;
                            
                            //Detect multi-bit upset
                            if diff_bits > 1 {

                                //Counter 
                                let mut c: usize = 0;

                                //We need to count more carefully, this is CPU intensive though :(
                                for i in 0..8 {
                                    
                                    //Check if bit is set
                                    if adj_bits & (1 << i) != 0 {
                                        
                                        //Bit is set
                                        c += 1;

                                    } else {
                                        
                                        //Bit is not set

                                        //Commit result if there is one
                                        if c > 0 { 
                                            rsp.adj_err_bins[c] += 1; 
                                            c = 0;
                                        }
                                    }
                                    
                                }
                                
                                //Commit just in case any pending result
                                if c > 0 { 
                                    rsp.adj_err_bins[c] += 1; 
                                }

                            } else {
                                //Optimization trick to prevent extensive check
                                rsp.adj_err_bins[0] += 1;
                            }

                            //Rate calculation
                            bits_sampled += 8;
                            bits_errored += diff_bits as u64;



                        } else {
                            bits_sampled += 8;
                        }
                    },
                    Err(e) => {
                        bits_sampled += 8;
                        bits_errored += 8;
                        eprintln!("[!] Error reading from chip at offset {}: {:?}", i, e);
                    }
                };

                //Totals
                rsp.total_bytes += 1;

            }

            // Perform rate calculation
            {
                if bits_sampled > cmd.sample_size_in_bytes as u64 * 8 {
                    rsp.error_rate_per_second = (bits_errored as f32 * 1000.0) / (last_sample_instant.elapsed().unwrap().as_millis() as f32); // Errors per second
                    rsp.error_rate_percent = bits_errored as f32 / bits_sampled as f32;
                    rsp.error_rate = bits_errored as f32;

                    crate::dbg_log!(
                        "dynamic_command: sample window closed error_rate={:.4}, error_percent={:.4}, bits_errored={}",
                        rsp.error_rate, rsp.error_rate_percent, bits_errored
                    );

                    //Clear sampling vars
                    bits_sampled = 0;
                    bits_errored = 0;
                    last_sample_instant = SystemTime::now();

                    //Check if a SEFI has been detected
                    if (rsp.error_rate_percent > cmd.trigger_threshold) && !rsp.sefi_detected {

                        //First SEFI trigger
                        rsp.sefi_detected = true;

                        //Record time this took to occur
                        rsp.time_to_sefi = exposure_start_instant.elapsed().unwrap().as_millis() as f32;

                        //End test if beam detection is disabled
                        if !cmd.wait_for_beam {
                            rsp.test_completed = true;
                        } 

                        crate::dbg_log!(
                            "dynamic_command: SEFI detected error_rate={:.4} > threshold={:.4}, time_to_sefi={}ms",
                            rsp.error_rate, cmd.trigger_threshold, rsp.time_to_sefi.as_millis()
                        );
                    }
                }
            }

            // Perform beam check (if enabled)
            {
                //Check if beam has gone inactive
                if !gpio::get_beam_signal() && cmd.wait_for_beam {
                    crate::dbg_log!("dynamic_command: beam signal went low, ending test");
                    rsp.test_completed = true;
                }
            }

            //Update required timings
            {
                //If beam is still active, increase exposure time
                if gpio::get_beam_signal() || !cmd.wait_for_beam {
                    rsp.exposure_time_ms = exposure_start_instant.elapsed().unwrap().as_millis() as f32;
                }

                //Increase total test time
                if !rsp.test_completed {
                    rsp.total_time_ms = first_start_instant.elapsed().unwrap().as_millis() as f32;
                }
            }

            // Perform progress update (if needed) - Progress update performed early if test is over
            {
                if last_update_instant.elapsed().unwrap().as_millis() as f32 >= UPDATE_FREQUENCY_MS || rsp.test_completed {
                    
                    //Update response structure
                    rsp.beam_signal = gpio::get_beam_signal();
                    rsp.controller_calibrated = gpio::get_calibration_signal();
                    rsp.ui_clock = gpio::get_ui_clock_signal();
                    rsp.pl_clock = gpio::get_pl_clock_signal();
                    rsp.fpga_loaded = gpio::get_fpga_loaded_status();

                    let payload = crate::server::codec().serialize(&rsp).unwrap();

                    if let Err(e) = send_response(stream, CMD_DYNAMIC, payload) {
                        eprintln!("[!] Failed to send progress update: {}", e);
                        return;
                    }

                    //Create log
                    if config.enable_logging {

                        recorder::log(vec![
                            rsp.exposure_time_ms.to_string(),
                            rsp.total_time_ms.to_string(),
                            rsp.total_bytes.to_string(),
                            rsp.error_rate.to_string(),
                            rsp.error_rate_per_second.to_string(),
                            rsp.error_rate_percent.to_string(), 
                            cmd.trigger_threshold.to_string(),
                            rsp.beam_signal.to_string(),
                            rsp.controller_calibrated.to_string(),
                            rsp.exposure_started.to_string(),
                            rsp.sefi_detected.to_string(),
                            rsp.time_to_sefi.to_string(),
                            rsp.test_completed.to_string(),
                            rsp.pass_counter.to_string(),
                            rsp.current_address.to_string(),
                            rsp.start_address.to_string(),
                            rsp.end_address.to_string(),
                            rsp.adj_err_bins[0].to_string(), 
                            rsp.adj_err_bins[1].to_string(),
                            rsp.adj_err_bins[2].to_string(),
                            rsp.adj_err_bins[3].to_string(),
                            rsp.adj_err_bins[4].to_string(),
                            rsp.adj_err_bins[5].to_string(),
                            rsp.adj_err_bins[6].to_string(),
                            rsp.adj_err_bins[7].to_string(),
                            rsp.err_bins[0].to_string(),
                            rsp.err_bins[1].to_string(),
                            rsp.err_bins[2].to_string(),
                            rsp.err_bins[3].to_string(),
                            rsp.err_bins[4].to_string(),
                            rsp.err_bins[5].to_string(),
                            rsp.err_bins[6].to_string(),
                            rsp.err_bins[7].to_string(),
                            rsp.err_bins[8].to_string()
                        ]);

                    }
                    
                    //Reset timer for next update
                    last_update_instant = SystemTime::now();
        
                }
            }

            //Check if the test is over
            if rsp.test_completed {

                if config.enable_logging {
                    
                    //Commit log file
                    recorder::write(cmd.uuid).unwrap();

                    //Generate test summary file
                    recorder::write_summary(cmd.uuid, vec![
                        format!("{:?}", config),
                        format!("{:?}", cmd),
                        format!("{:?}", rsp),
                    ]).unwrap();

                }

                //End test
                return;
            }
                    

        }
    };

    crate::dbg_log!(
        "dynamic_command: done total_errors={}, total_correct={}, total_bytes={}, sefi_detected={}",
        total_errors, total_correct, total_correct + total_errors, sefi_detected
    );

}


pub fn info_command(stream: &mut TcpStream){

    #[cfg(feature = "debug")]
    {
        crate::dbg_log!(
            "info_command: beam={}, calibrated={}, ui_clock={}, pl_clock={}, fpga_loaded={}",
            gpio::get_beam_signal(), gpio::get_calibration_signal(), gpio::get_ui_clock_signal(),
            gpio::get_pl_clock_signal(), gpio::get_fpga_loaded_status()
        );
    }

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

    crate::dbg_log!(
        "write_command: seed={}, pattern={}, chip_size_bytes={}, address_multiplier={}",
        cmd.seed, cmd.pattern, config.chip_size_bytes, config.address_multiplier
    );

    if config.address_multiplier == 0 {
        crate::dbg_log!("write_command: WARNING address_multiplier==0, step_by(0) will panic");
    }
    if config.chip_size_bytes == 0 {
        crate::dbg_log!("write_command: WARNING chip_size_bytes==0, loop range 0..0 is empty; nothing will be written");
    }
    crate::dbg_log!(
        "write_command: iterating range 0..{} step {} (expected {} writes)",
        config.chip_size_bytes, config.address_multiplier,
        if config.address_multiplier == 0 { 0 } else { config.chip_size_bytes / config.address_multiplier }
    );

    //Setup timers
    let start_time = SystemTime::now();
    let mut time_since_last_update = SystemTime::now();

    //Init the pseudo-random generator with the provided seed
    crate::rand::set_seed(cmd.seed);

    //Debug-only iteration counter
    #[cfg(feature = "debug")]
    let mut iterations: u64 = 0;

    // Iterate over chip 
    for i in (0..config.chip_size_bytes).step_by(config.address_multiplier as usize) {

        #[cfg(feature = "debug")]
        { iterations += 1; }

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
                chip::write(&config, i, crate::rand::rand(i))
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

            crate::dbg_log!("write_command: progress offset={}, {:.1}% complete, {}ms elapsed", i, percent_complete, elapsed);

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

    crate::dbg_log!(
        "write_command: loop complete, {} writes performed in {}ms",
        iterations, start_time.elapsed().unwrap().as_millis()
    );

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

    crate::dbg_log!(
        "verify_command: seed={}, pattern={}, chip_size_bytes={}, address_multiplier={}",
        cmd.seed, cmd.pattern, config.chip_size_bytes, config.address_multiplier
    );

    if config.address_multiplier == 0 {
        crate::dbg_log!("verify_command: WARNING address_multiplier==0, step_by(0) will panic");
    }
    if config.chip_size_bytes == 0 {
        crate::dbg_log!("verify_command: WARNING chip_size_bytes==0, loop range is empty; nothing will be verified");
    }

    //Setup timers
    let start_time = SystemTime::now();
    let mut time_since_last_update = SystemTime::now();

    //Init the pseudo-random generator with the provided seed
    crate::rand::set_seed(cmd.seed);

    //Create response structure
    let mut rsp = VerifyRsp {
        time_spent_ms: 0.0,
        percent_complete: 0.0,
        num_correct: 0,
        num_incorrect: 0,
        adj_err_bins: [0, 0, 0, 0, 0, 0, 0, 0],
        err_bins: [0, 0, 0, 0, 0, 0, 0, 0, 0],
        current_address: 0x00000000,
        start_address: 0x00000000,
        end_address: config.chip_size_bytes,
     };

    // Setup log
    if config.enable_logging {
    recorder::new(vec!["Time (ms)", 
        "Start Address",
        "End Address",
        "Current Address", 
        "Percent Complete", 
        "# Correct Bits", 
        "# Incorrect Bits", 
        "adj_err[0]", 
        "adj_err[1]",
        "adj_err[2]",
        "adj_err[3]",
        "adj_err[4]",
        "adj_err[5]",
        "adj_err[6]",
        "adj_err[7]",
        "num_err[0]", 
        "num_err[1]",
        "num_err[2]",
        "num_err[3]",
        "num_err[4]",
        "num_err[5]",
        "num_err[6]",
        "num_err[7]",
        "num_err[8]"
        ]);
    }

    //Current address iterator
    let mut i: u32 = 0;

    //Done flag
    let mut done = false;

    // Iterate over chip 
    loop {

        //Check if progress update is needed (or we are done)
        if time_since_last_update.elapsed().unwrap().as_millis() as f32 >= UPDATE_FREQUENCY_MS || done {

            //Calculate status
            let elapsed = start_time.elapsed().unwrap().as_millis() as f32;
            let percent_complete = (i as f32 / config.chip_size_bytes as f32) * 100.0;  

            crate::dbg_log!(
                "verify_command: progress offset={}, {:.1}% complete, errors={}, correct={}",
                i, percent_complete, rsp.num_errors, rsp.num_correct
            );

            //Update bytes verified and percent complete in response structure
            rsp.current_address = i;
            rsp.time_spent_ms = elapsed;
            rsp.percent_complete = percent_complete;

            let payload = crate::server::codec().serialize(&rsp).unwrap();

            if let Err(e) = send_response(stream, CMD_VERIFY, payload) {
                eprintln!("[!] Failed to send progress update: {}", e);
                return;
            }
            
            //Create log entry
            if config.enable_logging {
            recorder::log(vec![rsp.time_spent_ms.to_string(), 
                format!("{:#010X}", rsp.start_address), 
                format!("{:#010X}", rsp.end_address),
                format!("{:#010X}", rsp.current_address),
                rsp.percent_complete.to_string(), 
                rsp.num_correct.to_string(), 
                rsp.num_incorrect.to_string(), 
                rsp.adj_err_bins[0].to_string(), 
                rsp.adj_err_bins[1].to_string(),
                rsp.adj_err_bins[2].to_string(),
                rsp.adj_err_bins[3].to_string(),
                rsp.adj_err_bins[4].to_string(),
                rsp.adj_err_bins[5].to_string(),
                rsp.adj_err_bins[6].to_string(),
                rsp.adj_err_bins[7].to_string(),
                rsp.err_bins[0].to_string(),
                rsp.err_bins[1].to_string(),
                rsp.err_bins[2].to_string(),
                rsp.err_bins[3].to_string(),
                rsp.err_bins[4].to_string(),
                rsp.err_bins[5].to_string(),
                rsp.err_bins[6].to_string(),
                rsp.err_bins[7].to_string(),
                rsp.err_bins[8].to_string()
                ]);
            }

            //Reset timer for next update
            time_since_last_update = SystemTime::now();

            //If we are done, exit
            if done {

                if config.enable_logging {

                //Commit log file
                recorder::write(cmd.uuid).unwrap();

                //Generate test summary file
                recorder::write_summary(cmd.uuid, vec![
                    format!("{:?}", config),
                    format!("{:?}", cmd),
                    format!("{:?}", rsp),
                ]).unwrap();
                
                }

                return;
            }
        }

        //Expected value
        let expected = match cmd.pattern {
            0 => 0, // All zeros
            1 => 0xFF, // All ones
            2 => crate::rand::rand(i), // Pseudorandom pattern based on seed
            _ => {
                eprintln!("[!] Invalid pattern in VerifyCmd: {}", cmd.pattern);
                return;
            }
        };

        //Determine the expected contents to verify against
        match crate::chip::read(&config, i) {
            Ok(actual) => {
                if actual != expected {
                    crate::dbg_log!(
                        "verify_command: mismatch at offset {:#x} expected={:#x}, actual={:#x}",
                        i, expected, actual
                    );
                    
                    let diff_mask        = actual ^ expected;
                    let diff_bits: usize = (diff_mask).count_ones() as usize;
                    let adj_bits         = diff_mask & (diff_mask >> 1);

                    //Overall metrics
                    rsp.num_correct += 8 - diff_bits as u64;
                    rsp.num_incorrect += diff_bits as u64;

                    //Collect statistics
                    rsp.err_bins[diff_bits] += 1;
                    
                    //Detect multi-bit upset
                    if diff_bits > 1 {

                        //Counter 
                        let mut c: usize = 0;

                        //We need to count more carefully, this is CPU intensive though :(
                        for i in 0..8 {
                            
                            //Check if bit is set
                            if adj_bits & (1 << i) != 0 {
                                
                                //Bit is set
                                c += 1;

                            } else {
                                
                                //Bit is not set

                                //Commit result if there is one
                                if c > 0 { 
                                    rsp.adj_err_bins[c] += 1; 
                                    c = 0;
                                }
                            }
                            
                        }
                        
                        //Commit just in case any pending result
                        if c > 0 { 
                            rsp.adj_err_bins[c] += 1; 
                        }

                    } else {
                        //Optimization trick to prevent extensive check
                        rsp.adj_err_bins[0] += 1;
                    }

                } else {
                    rsp.num_correct += 8;
                }
            },
            Err(e) => {
                eprintln!("[!] Error reading from chip at offset {}: {:?}", i, e);
            }
        };


        //Update iterator
        i += config.address_multiplier;
        
        //Check if we are done
        if i >= config.chip_size_bytes {
            //We are done
            done = true;
        }


    }
    
}

pub fn dump_command(stream: &mut TcpStream, cmd: DumpCmd, v_cmd: &VerifyCmd){

    //Load configuration
    let config = CONFIG.read().unwrap();

    //Setup timers
    let time_since_start = SystemTime::now();

    //Get base page address
    let base_address = cmd.offset_start - (cmd.offset_start % PAGE_SIZE as u32); // Align down to page boundary

    crate::dbg_log!(
        "dump_command: offset_start={:#x}, base_address={:#x}, num_pages={}, comparison_mode={}, PAGE_SIZE={}, pattern={}",
        cmd.offset_start, base_address, cmd.num_pages, cmd.comparison_mode, PAGE_SIZE, v_cmd.pattern
    );

    //Init the pseudo-random generator with the provided seed
    crate::rand::set_seed(v_cmd.seed);

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
                            2 => crate::rand::rand(page_address + offset), // Pseudorandom pattern based on seed
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

        crate::dbg_log!(
            "dump_command: page {}/{} address={:#x}, bytes={}, errors={}",
            page_num + 1, cmd.num_pages, page_address, page_data.len(), num_errors
        );

        //Send page data in response
        let rsp = DumpRsp {
            num_errors: num_errors,
            address: page_address,
            time_spent_ms: time_since_start.elapsed().unwrap().as_millis() as f32,
            //Raw bytes are appended to this (3 byte pages)
        };

        let mut payload = crate::server::codec().serialize(&rsp).unwrap();
        payload.extend_from_slice(&page_data);

        if let Err(e) = send_response(stream, CMD_DUMP, payload) {
            eprintln!("[!] Failed to send dump response: {}", e);
            return;
        }

    }

    crate::dbg_log!("dump_command: complete, {} pages dumped in {}ms", cmd.num_pages, start_time.elapsed().unwrap().as_millis());

}