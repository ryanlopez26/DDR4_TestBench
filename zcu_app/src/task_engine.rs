use std::{path::Path, sync::{LazyLock, RwLock}, time::{Instant, SystemTime, UNIX_EPOCH}};
use gpio_cdev::LineDirection::In;
use serde::{Deserialize, Serialize};

use crate::{gpio, record::{self, Record}, tools::get_epoch, types::ConfigCmd};
use std::{thread, time::Duration};
use std::fs::File;
use std::io::{BufWriter, Write};


// PARAMS
const UPDATE_FREQUENCY: Duration = Duration::from_millis(200);
const SIGNAL_HOLD_REQUIREMENT: Duration = Duration::from_millis(200);


// Task request
pub struct TaskRequest {

    //Type of record that is desired
    rtype: record::RecordType,

    //UUID to give the record
    rid: u16
}

// Engine command 

pub struct EngineCommand {
    
    // Task queue
    task_queue: Vec<TaskRequest>,

    // Task abort (abandon the current task)
    abort_task: bool,

}


// Engine status - only the engine should write to this
pub struct EngineStatus {

    // Record of the request currently underway
    active_record: Option<record::Record>,

    // Status of the engine
    is_idle: bool,
    task_runtime: f64,
    task_progress: f64
}

// Global vars for engine

pub static CMD: RwLock<EngineCommand> = RwLock::new(EngineCommand { task_queue: vec![], abort_task: false });

pub static STATUS: RwLock<EngineStatus> = RwLock::new(
    EngineStatus{ 
        active_record: None, 
        is_idle: true, 
        task_runtime: 0.0, 
        task_progress: 100.0, 
    }
);

// Abort checker
fn must_abort() -> bool {

    //Obtain lock on command variable
    let cmd = CMD.read().unwrap();

    // Return status
    cmd.abort_task

}


// Main of the engine thread
pub fn engine_main(){

    // Active record to work on
    let mut rec: Option<Record> = None;

    // Last abort check
    let mut last_abort_check = Instant::now();
    let mut last_update = Instant::now();

    // Local copy of the config
    let mut config: ConfigCmd;

    // Begin main process loop
    loop{

        //Check the queue for a task to do
        {
            //Obtain an updated configuration
            {
                config = crate::config::CONFIG.read().unwrap().clone();
            }

            //Lock cmd variable
            let mut cmd = CMD.write().unwrap();

            // Check if there is nothing to do
            if cmd.task_queue.len() == 0 {

                // Nothing to do, release lock and wait
                drop(cmd);

                //Wait then try again
                thread::sleep(Duration::from_millis(1000));

                continue;

            }

            // There is a task request, perform it
            rec = Some(Record {
                time_started: get_epoch(),
                time_ended: 0,
                time_duration: 0,
                rid: cmd.task_queue[0].rid,
                rtype: cmd.task_queue[0].rtype,
                completed: false,
                aborted: false,
                adj_err_bins: Some([0,0,0,0,0,0,0,0]),
                err_bins: Some([0,0,0,0,0,0,0,0,0]),
                time_to_sefi: 0.0,
                exposure_time_ms: Some(0.0),
                log_timestamp: Vec::new(),
                log_bytes_processed: Vec::new(),
                log_bytes_errored: Vec::new(),
                log_error_rate: Vec::new(),
                log_current_address: Vec::new(),
                log_beam_signal: Vec::new(),
                log_sefi_detected: Vec::new(),
            });

        }

        // Begin task 
        match rec {
            Some(mut r) => {


                // Create capture file for dump 
                let cap_path = Path::new("/dvr/captures").join(format!("{:04X}.bin", r.rid)); 
                let cap_file = File::create(&cap_path).unwrap();
                let mut cap_buf = BufWriter::with_capacity(config.block_size as usize, cap_file);

                // Record start time
                r.time_started = get_epoch();

                // Start instant
                let start = Instant::now();

                //Split record based on operation that needs to be performed
                match r.rtype {
                    record::RecordType::Read => {

                        //Iterate over every block in the dataset
                        for blk_ind in (0..config.num_blocks).step_by(config.block_factor as usize) {

                            // Compute virtual memory addresses 
                            let block_start = blk_ind * config.block_size ;
                            let block_end   = block_start + config.block_size;

                            //Bit counts
                            let mut bad_bits: f64 = 0.0;

                            for addr in block_start..block_end {
                              
                               //Copy into capture buffer
                                match crate::chip::read(&config, addr) {
                                    Ok(byte) => {
                                        cap_buf.write_all(&[byte]);
                                    },
                                    Err(e) => {
                                        eprintln!("[!] Error reading chip at {:#x}: {:?}", addr, e);
                                        cap_buf.write_all(&[0xFE]);
                                        bad_bits += 8.0;
                                    }
                                }

                                //Check if we need to update
                                if last_update.duration_since(Instant::now()) > UPDATE_FREQUENCY {

                                    //Reset watchdog
                                    last_update = Instant::now();

                                    //Record datapoints
                                    r.log_timestamp.push(start.elapsed().as_secs_f64());
                                    r.log_bits_errored.push(bad_bits);
                                    r.log_beam_signal.push(gpio::get_beam_signal());
                                    r.log_current_address.push(block_end);

                                    //Lock the status struct
                                    let mut s = STATUS.write().unwrap();

                                    //Update the status struct
                                    s.active_record = Some(r.clone());
                                    s.task_progress = (blk_ind / config.num_blocks) as f64;
                                    s.task_runtime = (r.time_started - get_epoch()) as f64;

                                }
                                
                            }

                        }
                    
                    },
                    record::RecordType::Verify => {

                        // Iterate over number of blocks
                        for blk_ind in (0..config.num_blocks).step_by(config.block_factor as usize) {

                            //Iterate over bytes in block
                            for addr in (blk_ind * config.block_size)..((blk_ind + 1) * config.block_size){

                                //Expected value
                                let expected = match config.data_pattern {
                                    0 => 0, // All zeros
                                    1 => 0xFF, // All ones
                                    2 => crate::rand::rand(addr), // Pseudorandom pattern based on seed
                                    _ => {
                                        0
                                    }
                                };                              

                                //Verify the actual contents against the expected
                                match crate::chip::read(&config, addr) {
                                    Ok(actual) => {
                                        if actual != expected {
                                            
                                            let diff_mask        = actual ^ expected;
                                            let diff_bits: usize = (diff_mask).count_ones() as usize;
                                            let adj_bits         = diff_mask & (diff_mask >> 1);

                                            // Save the XOR'd version to the capture file
                                            cap_buf.write_all(&[diff_mask]);

                                            //Overall metrics
                                            r.correct_bits += 8 - diff_bits as u64;
                                            r.incorrect_bits += diff_bits as u64;

                                            //Collect statistics
                                            r.err_bins.unwrap()[diff_bits] += 1;
                                            
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
                                                            r.adj_err_bins.unwrap()[c] += 1; 
                                                            c = 0;
                                                        }
                                                    }
                                                    
                                                }
                                                
                                                //Commit just in case any pending result
                                                if c > 0 { 
                                                    r.adj_err_bins.unwrap()[c] += 1; 
                                                }

                                            } else {
                                                //Optimization trick to prevent extensive check
                                                r.adj_err_bins.unwrap()[0] += 1;
                                            }

                                        } else {
                                            r.correct_bits += 8;
                                        }
                                    },
                                    Err(e) => {
                                        
                                    }
                                };

                                //Check if we need to update
                                if last_update.duration_since(Instant::now()) > UPDATE_FREQUENCY {

                                    //Reset watchdog
                                    last_update = Instant::now();

                                    //Record datapoints
                                    r.log_timestamp.push(start.elapsed().as_secs_f64());
                                    r.log_bits_incorrect.push(r.incorrect_bits);
                                    r.log_bits_correct.push(r.correct_bits);
                                    r.log_beam_signal.push(gpio::get_beam_signal());
                                    r.log_current_address.push(blk_ind * config.block_size);
                                    r.log_err_bins.push(r.err_bins.unwrap());
                                    r.log_adj_err_bins.push(r.adj_err_bins.unwrap());

                                    //Lock the status struct
                                    let mut s = STATUS.write().unwrap();

                                    //Update the status struct
                                    s.active_record = Some(r.clone());
                                    s.task_progress = (blk_ind / config.num_blocks) as f64;
                                    s.task_runtime = (r.time_started - get_epoch()) as f64;

                                }
                                

                            }

                        }

                    },
                    record::RecordType::Write => {

                        // Iterate over number of blocks
                        for blk_ind in (0..config.num_blocks).step_by(config.block_factor as usize) {

                            //Iterate over bytes in block
                            for addr in (blk_ind * config.block_size)..((blk_ind + 1) * config.block_size){

                                //Determine the required contents to write
                                match match config.data_pattern {
                                    0 => {
                                        // All zeros
                                        crate::chip::write(&config, addr, 0)
                                    },
                                    1 => {
                                        // All ones
                                        crate::chip::write(&config, addr, 0xFF)
                                    },
                                    2 => {
                                        // Pseudorandom pattern based on seed
                                        crate::chip::write(&config, addr, crate::rand::rand(addr))
                                    },
                                    _ => {
                                        eprintln!("[!] Invalid pattern in WriteCmd: {}", config.data_pattern);
                                        return;
                                    }
                                }
                                {
                                    Ok(_) => {
                                        r.processed_bits += 8;
                                    },
                                    Err(e) => {
                                        r.incorrect_bits += 8;
                                        return;
                                    }
                                }

                                //Check if we need to update
                                if last_update.duration_since(Instant::now()) > UPDATE_FREQUENCY {

                                    //Reset watchdog
                                    last_update = Instant::now();

                                    //Record datapoints
                                    r.log_timestamp.push(start.elapsed().as_secs_f64());
                                    r.log_beam_signal.push(gpio::get_beam_signal());
                                    r.log_bits_incorrect.push(r.incorrect_bits);
                                    r.log_bits_processed.push(r.processed_bits);
                                    r.log_current_address.push(blk_ind * config.block_size);
                                    r.log_err_bins.push(r.err_bins.unwrap());
                                    r.log_adj_err_bins.push(r.adj_err_bins.unwrap());

                                    //Lock the status struct
                                    let mut s = STATUS.write().unwrap();

                                    //Update the status struct
                                    s.active_record = Some(r.clone());
                                    s.task_progress = (blk_ind / config.num_blocks) as f64;
                                    s.task_runtime = (r.time_started - get_epoch()) as f64;

                                }
                                
                            }


                        }

                    },
                    record::RecordType::Dynamic => {

                        // Wait for beam
                        let mut have_started = false;

                        // Beam timers (used to debounce)
                        let mut b_last_low = Instant::now();
                        let mut b_last_high = Instant::now();


                        // Main process loop for dynamic process
                        loop {

                            // Get beam signal 
                            let b = gpio::get_beam_signal();

                            // Update timers
                            match b {
                                true =>  {b_last_high = Instant::now();},
                                false => {b_last_low  = Instant::now();},
                            }

                            // Check if we are waiting for beam capture
                            if !have_started {

                                // Wait for beam capture
                                if b && b_last_low.elapsed() > SIGNAL_HOLD_REQUIREMENT {
                                    
                                }



                            }
                            

                            





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
                                                
                                                //Log if enabled
                                                if config.enable_logging {stream_log.push(actual ^ v); };

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





                    },
                }

                //Mark finish times
                r.time_ended = get_epoch();
                r.time_duration = r.time_ended - r.time_started;
                r.completed = true;

                //Close all files
                cap_buf.flush().unwrap();

            },
            None => {},
        }

    }
}