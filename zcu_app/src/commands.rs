// ======================= Performs the commands ========================

use std::fs;
use std::net::TcpStream;
use std::time::SystemTime;

use bincode::Options;

use crate::{chip, gpio, recorder, types::*};

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
        // `file!():line!()` makes every line traceable back to its call site,
        // which matters once these are interleaved across commands/threads.
        eprintln!("[dbg {}:{}] {}", file!(), line!(), format_args!($($arg)*));
    };
}

#[cfg(not(feature = "debug"))]
#[macro_export]
macro_rules! dbg_log {
    ($($arg:tt)*) => {{}};
}

pub fn config_command(stream: &mut TcpStream, cmd: ConfigCmd){

    crate::dbg_log!(
        "config_command: incoming ConfigCmd chip_index={}, bus_bytes_per_chip={}, chip_size_bytes={} ({:#x}), bus_size_in_bytes={}, enable_chip_select={}, address_multiplier={}, enable_logging={}",
        cmd.chip_index, cmd.bus_bytes_per_chip, cmd.chip_size_bytes, cmd.chip_size_bytes, cmd.bus_size_in_bytes, cmd.enable_chip_select, cmd.address_multiplier, cmd.enable_logging
    );

    // //Prevent invalid address multiplier
    // if  {

    //     crate::dbg_log!(
    //         "config_command: REJECTED address_multiplier==0 (would make step_by(0) panic); config left unchanged, sending failure ACK"
    //     );

    //     //Invalid status response 
    //     let payload: Vec<u8> = vec![0];
    //     send_response(stream, CMD_CONFIG, payload).unwrap();

    //     //Failed
    //     return;

    // }

    //Load new configuration settings
    {
        let mut config = CONFIG.write().unwrap();
        config.enable_logging = cmd.enable_logging;
        config.block_factor = cmd.block_factor;
        config.block_size  = cmd.block_size;
        config.num_blocks = cmd.num_blocks;

        // Log the full config as actually applied, so the active geometry the
        // loops will read is visible (this handler DID just write every field).
        crate::dbg_log!(
            "config_command: applied CONFIG chip_index={}, bus_bytes_per_chip={}, chip_size_bytes={} ({:#x}), bus_size_in_bytes={}, enable_chip_select={}, address_multiplier={}, enable_logging={}",
            config.chip_index, config.bus_bytes_per_chip, config.chip_size_bytes, config.chip_size_bytes,
            config.bus_size_in_bytes, config.enable_chip_select, config.address_multiplier, config.enable_logging
        );
    }

    //Status response 
    let payload: Vec<u8> = vec![0];

    //Send ACK response
    send_response(stream, CMD_CONFIG, payload).unwrap();
    
}

pub fn uuid_command(stream: &mut TcpStream, cmd: UUIDCmd){

    crate::dbg_log!(
        "uuid_command: incoming UUIDCmd uuid={}",
        cmd.uuid
    );

    //Check UUID
    let rsp = UUIDRsp {
        success: recorder::check_uuid(cmd.uuid),
    };

    crate::dbg_log!(
        "uuid_command: uuid={} available={} (true = not yet used; a log file for it does not exist)",
        cmd.uuid, rsp.success
    );

    //Send ACK response
    send_response(stream, CMD_UUID, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}


pub fn dynamic_command(stream: &mut TcpStream, cmd: DynamicCmd){

    //Load configuration
    let config = CONFIG.read().unwrap();

    //Init the pseudo-random generator with the provided seed
    crate::rand::set_seed(cmd.seed);

    //Verification stream log (will get very big)
    let mut stream_log: Vec<u8> = Vec::new();

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
        end_address: config.num_blocks * config.block_size,
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
        "dynamic_command: uuid={}, seed={:#018X}, pattern={}, wait_for_beam={}, sample_size_in_bytes={}, trigger_threshold={}, chip_size_bytes={} ({:#x}), address_multiplier={}",
        cmd.uuid, cmd.seed, cmd.pattern, cmd.wait_for_beam, cmd.sample_size_in_bytes, cmd.trigger_threshold, config.chip_size_bytes, config.chip_size_bytes, config.address_multiplier
    );

    //Instant when test first started
    let first_start_instant = SystemTime::now();
    let mut last_update_instant = SystemTime::now();

    //Check if we need to wait for the beam
    if cmd.wait_for_beam {
        //Beam must read high continuously for this long before we start
        const BEAM_HOLD_MS: u128 = 500;

        crate::dbg_log!("dynamic_command: waiting for beam signal to go high...");

        //Timestamp of when the beam most recently went high; None while it reads low
        let mut beam_high_since: Option<SystemTime> = None;

        //Wait for the beam signal to stay high for at least BEAM_HOLD_MS
        loop {
            let beam = gpio::get_beam_signal();

            if beam {
                match beam_high_since {
                    //Rising edge: start the hold timer
                    None => {
                        crate::dbg_log!(
                            "dynamic_command: beam went high, confirming it holds for {}ms...",
                            BEAM_HOLD_MS
                        );
                        beam_high_since = Some(SystemTime::now());
                    }
                    //Still high: proceed once we've cleared the hold threshold
                    Some(since) => {
                        if since.elapsed().unwrap().as_millis() >= BEAM_HOLD_MS {
                            break;
                        }
                    }
                }
            } else if beam_high_since.is_some() {
                //Beam dropped mid-confirmation; reset and keep waiting
                crate::dbg_log!(
                    "dynamic_command: beam dropped before {}ms hold, resetting",
                    BEAM_HOLD_MS
                );
                beam_high_since = None;
            }

            //Check if we need to send status update
            if last_update_instant.elapsed().unwrap().as_millis() as f32 >= UPDATE_FREQUENCY_MS {
                //Send status update
                rsp.beam_signal = beam;
                rsp.controller_calibrated = gpio::get_calibration_signal();
                rsp.ui_clock = gpio::get_ui_clock_signal();
                rsp.pl_clock = gpio::get_pl_clock_signal();
                rsp.fpga_loaded = gpio::get_fpga_loaded_status();
                rsp.total_time_ms = first_start_instant.elapsed().unwrap().as_millis() as f32;

                crate::dbg_log!(
                    "dynamic_command: still waiting for beam ({}ms elapsed) beam={}, calibrated={}, ui_clock={}, pl_clock={}, fpga_loaded={}",
                    rsp.total_time_ms, rsp.beam_signal, rsp.controller_calibrated, rsp.ui_clock, rsp.pl_clock, rsp.fpga_loaded
                );

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


    //Exposure present - Perform test
    loop {

        // Iterate over number of blocks
        for blk_ind in (0..config.num_blocks).step_by(config.block_factor as usize) {

            //Iterate over bytes in block
            for addr in (blk_ind * config.block_size)..((blk_ind + 1) * config.block_size){

                // Perform write and verify operation
                {

                    //Update current address
                    rsp.current_address = addr;

                    //Value to test
                    let v = match cmd.pattern {
                        0 => {
                            // Zero mode
                            0x00
                        },
                        1 => {
                            // All ones
                            0xFF
                        },
                        2 => {
                            // Random
                            crate::rand::rand(addr)
                        },
                        _ => {
                            eprintln!("[!] Invalid pattern in DynamicCmd: {}", cmd.pattern);
                            return;
                        }};
                    
                    //Write test value to byte
                    chip::write(&config, addr, v).unwrap();

                    //Read back and verify the value
                    match crate::chip::read(&config, addr) {
                        Ok(actual) => {
                            if actual != v {
                                crate::dbg_log!(
                                    "dynamic_command: mismatch at offset {:#x} expected={:#04X}, actual={:#04X}",
                                    i, v, actual
                                );

                                let diff_mask        = actual ^ v;
                                let diff_bits: usize = (diff_mask).count_ones() as usize;
                                let adj_bits         = diff_mask & (diff_mask >> 1);

                                //Collect statistics
                                rsp.err_bins[diff_bits] += 1;

                                //Log if enabled
                                if config.enable_logging {stream_log.push(diff_mask); };
                                
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
                            crate::dbg_log!("dynamic_command: chip read error at offset {:#x}: {:?} (counted as 8 errored bits)", i, e);
                        }
                    };

                    //Totals
                    rsp.total_bytes += 1;

                }

                // Perform rate calculation
                {
                    if bits_sampled >= cmd.sample_size_in_bytes as u64 * 8 {
                        
                        let secs = last_sample_instant.elapsed().unwrap().as_secs_f32();
                        rsp.error_rate_per_second = if secs > 0.0 { bits_errored as f32 / secs } else { 0.0 };
                        rsp.error_rate_percent = bits_errored as f32 / bits_sampled as f32;
                        rsp.error_rate = bits_errored as f32;

                        crate::dbg_log!(
                            "dynamic_command: sample window closed error_rate={} bits, error_percent={:.6} ({}/{} bits), error_rate_per_second={:.2}",
                            rsp.error_rate, rsp.error_rate_percent, bits_errored, bits_sampled, rsp.error_rate_per_second
                        );

                        //Clear sampling vars
                        bits_sampled = 0;
                        bits_errored = 0;
                        last_sample_instant = SystemTime::now();

                        rsp.adj_err_bins = [0,0,0,0,0,0,0,0];
                        rsp.err_bins = [0,0,0,0,0,0,0,0,0];

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
                                rsp.error_rate, cmd.trigger_threshold, rsp.time_to_sefi
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

                    crate::dbg_log!(
                        "dynamic_command: test complete — sefi_detected={}, time_to_sefi={}ms, passes={}, total_bytes={}, exposure_time={}ms, total_time={}ms, err_bins={:?}, adj_err_bins={:?}",
                        rsp.sefi_detected, rsp.time_to_sefi, rsp.pass_counter, rsp.total_bytes,
                        rsp.exposure_time_ms, rsp.total_time_ms, rsp.err_bins, rsp.adj_err_bins
                    );

                    if config.enable_logging {
                        
                        //Commit log file
                        recorder::write(cmd.uuid).unwrap();

                        //Generate test summary file
                        recorder::write_summary(cmd.uuid, vec![
                            format!("{:?}", config),
                            format!("{:?}", cmd),
                            format!("{:?}", rsp),
                        ]).unwrap();

                        recorder::write_raw(cmd.uuid, stream_log).unwrap();

                    }

                    //End test
                    return;
                }
                        

            }
        }
        
        //Increment pass count
        rsp.pass_counter += 1;

        crate::dbg_log!(
            "dynamic_command: completed full chip sweep #{} (total_bytes={}, sefi_detected={})",
            rsp.pass_counter, rsp.total_bytes, rsp.sefi_detected
        );

    }


}


pub fn info_command(stream: &mut TcpStream){

    crate::dbg_log!(
        "info_command: beam={}, calibrated={}, ui_clock={}, pl_clock={}, fpga_loaded={}",
        gpio::get_beam_signal(), gpio::get_calibration_signal(), gpio::get_ui_clock_signal(),
        gpio::get_pl_clock_signal(), gpio::get_fpga_loaded_status()
    );

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
        "write_command: seed={:#018X}, pattern={}, chip_size_bytes={} ({:#x}), address_multiplier={}",
        cmd.seed, cmd.pattern, config.chip_size_bytes, config.chip_size_bytes, config.address_multiplier
    );

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


    // Iterate over number of blocks
    for blk_ind in (0..config.num_blocks).step_by(config.block_factor as usize) {

        //Iterate over bytes in block
        for addr in (blk_ind * config.block_size)..((blk_ind + 1) * config.block_size){

            #[cfg(feature = "debug")]
            { iterations += 1; }

            //Determine the required contents to write
            match match cmd.pattern {
                0 => {
                    // All zeros
                    chip::write(&config, addr, 0)
                },
                1 => {
                    // All ones
                    chip::write(&config, addr, 0xFF)
                },
                2 => {
                    // Pseudorandom pattern based on seed
                    chip::write(&config, addr, crate::rand::rand(addr))
                },
                _ => {
                    eprintln!("[!] Invalid pattern in WriteCmd: {}", cmd.pattern);
                    return;
                }
            }
            {
                Ok(_) => {},
                Err(e) => {
                    eprintln!("[!] Error writing to chip at offset {}: {:?}", addr, e);
                    return;
                }
            }

            //Check if progress update is needed
            if time_since_last_update.elapsed().unwrap().as_millis() as f32 >= UPDATE_FREQUENCY_MS {

                //Calculate status
                let elapsed = start_time.elapsed().unwrap().as_millis() as f32;
                let percent_complete = (blk_ind as f32 / config.num_blocks as f32) * 100.0;  

                crate::dbg_log!("write_command: progress offset={}, {:.1}% complete, {}ms elapsed", i, percent_complete, elapsed);

                //Send status update
                let rsp = WriteRsp {
                    bytes_written: blk_ind * config.block_size,
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


    }

    crate::dbg_log!(
        "write_command: loop complete, {} writes performed in {}ms",
        iterations, start_time.elapsed().unwrap().as_millis()
    );

    //Send final status response

    let rsp = WriteRsp {
        bytes_written: config.block_size * (config.num_blocks / config.block_factor),
        time_spent_ms: start_time.elapsed().unwrap().as_millis() as f32,
        percent_complete: 100.0,
    };

    let payload = crate::server::codec().serialize(&rsp).unwrap();

    if let Err(e) = send_response(stream, CMD_WRITE, payload) {
        eprintln!("[!] Failed to send progress update: {}", e);
    }
    
}



pub fn verify_command(stream: &mut TcpStream, cmd: VerifyCmd){

    //Load configuration
    let config = CONFIG.read().unwrap();

    crate::dbg_log!(
        "verify_command: uuid={}, seed={:#018X}, pattern={}, chip_size_bytes={} ({:#x}), address_multiplier={}",
        cmd.uuid, cmd.seed, cmd.pattern, config.chip_size_bytes, config.chip_size_bytes, config.address_multiplier
    );

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
        end_address: config.num_blocks * config.block_size,
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



    // Iterate over number of blocks
    for blk_ind in (0..config.num_blocks).step_by(config.block_factor as usize) {

        //Iterate over bytes in block
        for addr in (blk_ind * config.block_size)..((blk_ind + 1) * config.block_size){

            let done = addr == (((blk_ind + 1) * config.block_size) - 1);

            //Check if progress update is needed (or we are done)
            if time_since_last_update.elapsed().unwrap().as_millis() as f32 >= UPDATE_FREQUENCY_MS || done {

                //Calculate status
                let elapsed = start_time.elapsed().unwrap().as_millis() as f32;
                let percent_complete = (blk_ind as f32 / config.num_blocks as f32) * 100.0;  

                crate::dbg_log!(
                    "verify_command: progress offset={}, {:.1}% complete, errors={}, correct={}",
                    i, percent_complete, rsp.num_incorrect, rsp.num_correct
                );

                //Update bytes verified and percent complete in response structure
                rsp.current_address = addr;
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

                    crate::dbg_log!(
                        "verify_command: complete — {:.1}% at offset {:#x}, num_correct={}, num_incorrect={}, err_bins={:?}, adj_err_bins={:?}, elapsed={}ms",
                        rsp.percent_complete, rsp.current_address, rsp.num_correct, rsp.num_incorrect,
                        rsp.err_bins, rsp.adj_err_bins, rsp.time_spent_ms
                    );

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
                2 => crate::rand::rand(addr), // Pseudorandom pattern based on seed
                _ => {
                    eprintln!("[!] Invalid pattern in VerifyCmd: {}", cmd.pattern);
                    return;
                }
            };

            //Determine the expected contents to verify against
            match crate::chip::read(&config, addr) {
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
                    crate::dbg_log!("verify_command: chip read error at offset {:#x}: {:?}", i, e);
                }
            };

        }

    }
    
}

use std::time::Instant;

fn flush_page(
    stream: &mut TcpStream,
    block_data: &mut Vec<u8>,
    num_errors: &mut u64,      // match your DumpRsp field types
    page_addr: u32,          // ditto — cast if address is u32
    start: Instant ) -> Result<(), ()> 
    {
    if block_data.is_empty() {
        return Ok(());
    }
    let rsp = DumpRsp {
        num_errors: *num_errors,
        address: page_addr,
        time_spent_ms: start.elapsed().as_millis() as f32,
    };
    let mut payload = crate::server::codec().serialize(&rsp).unwrap();
    payload.extend_from_slice(block_data);

    block_data.clear();
    *num_errors = 0;

    send_response(stream, CMD_DUMP, payload).map_err(|e| {
        eprintln!("[!] Failed to send dump response: {}", e);
    })
}

pub fn dump_command(stream: &mut TcpStream, cmd: DumpCmd, v_cmd: &VerifyCmd) {
    let config = CONFIG.read().unwrap();
    let start = Instant::now();

    crate::dbg_log!(
        "dump_command: offset_start={:#x}, num_pages={}, comparison_mode={}, PAGE_SIZE={}, pattern={} (last Verify: uuid={}, seed={:#018X})",
        cmd.offset_start, cmd.num_pages, cmd.comparison_mode, PAGE_SIZE, v_cmd.pattern, v_cmd.uuid, v_cmd.seed
    );

    crate::rand::set_seed(v_cmd.seed);

    // These must persist across byte iterations.
    let mut block_data: Vec<u8> = Vec::with_capacity(PAGE_SIZE);
    let mut num_errors: u64 = 0;
    let mut page_addr: u32 = 0;

    for blk_ind in (cmd.block_offset..cmd.num_blocks).step_by(config.block_factor as usize) {
        // usize math to avoid the u32 overflow we discussed
        let block_start = blk_ind * config.block_size ;
        let block_end   = block_start + config.block_size;

        for addr in block_start..block_end {
            if block_data.is_empty() {
                page_addr = addr; // record where this page begins
            }

            if cmd.comparison_mode {
                match crate::chip::read(&config, addr) {
                    Ok(byte) => {
                        let expected = match v_cmd.pattern {
                            0 => 0x00,
                            1 => 0xFF,
                            2 => crate::rand::rand(addr as u32),
                            _ => {
                                eprintln!("[!] Invalid pattern in VerifyCmd: {}", v_cmd.pattern);
                                return;
                            }
                        };
                        block_data.push(expected ^ byte);
                    }
                    Err(e) => {
                        eprintln!("[!] Error reading chip at {:#x}: {:?}", addr, e);
                        block_data.push(0xFE);
                        num_errors += 1;
                    }
                }
            } else {
                match crate::chip::read(&config, addr) {
                    Ok(byte) => block_data.push(byte),
                    Err(e) => {
                        eprintln!("[!] Error reading chip at {:#x}: {:?}", addr, e);
                        block_data.push(0xFE);
                        num_errors += 1;
                    }
                }
            }

            if block_data.len() >= PAGE_SIZE {
                if flush_page(stream, &mut block_data, &mut num_errors, page_addr, start).is_err() {
                    return;
                }
            }
        }

        // Flush the block's trailing partial so it can't spill into the
        // next (non-contiguous) sampled block.
        if flush_page(stream, &mut block_data, &mut num_errors, page_addr, start).is_err() {
            return;
        }
    }

    crate::dbg_log!("dump_command: complete in {}ms", start.elapsed().as_millis());
}

