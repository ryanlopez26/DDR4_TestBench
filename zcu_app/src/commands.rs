// ======================= Performs the commands ========================

use std::net::TcpStream;
use std::path::Path;

use bincode::Options;

use crate::archive::{RECORD_LOG, RUN_LOG};
use crate::{PM, archive, config, gpio, task_engine, types::*};

use crate::server::send_response;
use crate::config::*;

pub fn config_command(stream: &mut TcpStream, cmd: ConfigCmd){


    //Update active configuration
    let mut c = CONFIG.write().unwrap();
    *c = cmd;

    //Save configuration to persistent storage
    config::save();

    //Status response 
    let payload: Vec<u8> = vec![0];

    //Send ACK response
    send_response(stream, CMD_CONFIG, payload).unwrap();
    
}

pub fn genrun_command(stream: &mut TcpStream){


    //Check UUID
    let rsp = genRunRsp {
        id: archive::gen_run_number(),
    };

    //Send ACK response
    send_response(stream, CMD_GENRUN, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}

pub fn genrecord_command(stream: &mut TcpStream){


    //Check UUID
    let rsp = genRecordRsp {
        uuid: archive::gen_record_number(),
    };

    //Send ACK response
    send_response(stream, CMD_GENRECORD, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
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

pub fn getrecord_command(stream: &mut TcpStream, cmd: GetRecordCmd){

    // Lock records archive
    let reclog = archive::RECORD_LOG.read().unwrap();

    // Response struct
    let mut rsp: GetRecordRsp = GetRecordRsp {
        record: None,
    };

    //Iterate over all records
    for r in &(*reclog) {
        if r.rid == cmd.uuid {
            //Found the record
            rsp.record = Some(r.clone());
        }
    }

    //Send response
    send_response(stream, CMD_GENRECORD, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}

pub fn saverun_command(stream: &mut TcpStream, cmd: SaveRunCmd){

    // Submit to run archive
    let rsp = SaveRunRsp {
        success: archive::submit_run(cmd.run),
    };    

    //Send response
    send_response(stream, CMD_SAVERUN, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}


pub fn getrun_command(stream: &mut TcpStream, cmd: GetRunCmd){

    // Lock records archive
    let runlog = archive::RUN_LOG.read().unwrap();

    // Response struct
    let mut rsp: GetRunRsp = GetRunRsp {
        run: None,
    };

    //Iterate over all records
    for r in &(*runlog) {
        if r.id == cmd.id {
            //Found the run
            rsp.run = Some(r.clone());
        }
    }

    //Send response
    send_response(stream, CMD_GETRUN, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
    
}

pub fn getcapture_command(stream: &mut TcpStream, cmd: GetCaptureCmd) {
    // Response struct
    let mut rsp = GetCaptureRsp {
        capture: None,
    };

    if !PM.read().unwrap().safe {

        let path = Path::new("/dvr/captures").join(format!("{:04X}.cap", cmd.uuid));
        match std::fs::read(&path) {
            Ok(bytes) => rsp.capture = Some(bytes),
            Err(e) => eprintln!("capture {} unavailable: {e}", path.display()),
        }
        
    }

    //Send response
    send_response(stream, CMD_GETRUN, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
}

pub fn enginestatus_command(stream: &mut TcpStream) {

    //Lock engine status and command structures
    let eng_status = task_engine::STATUS.read().unwrap();
    let eng_cmd = task_engine::CMD.read().unwrap();

    // Response struct
    let rsp = EngineStatusRsp {
        active_record: eng_status.active_record.clone(),
        record_progress: eng_status.task_progress,
        is_idle: eng_status.is_idle,
        queue: eng_cmd.task_queue.clone(),
    };

    //Send response
    send_response(stream, CMD_ENGINESTATUS, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
}

pub fn getpowermode_command(stream: &mut TcpStream) {

    //Lock power manager
    let pm = PM.read().unwrap();

    let mut rsp = GetPowerModeRsp {
        request_poweroff: false,
        request_reboot: false,
    };

    //Check if we are in safe mode
    if pm.safe {

        //Good, we don't have to worry about corruption now
        //Check if a state change is desired

        // Poweroff request
        if pm.power_request == 1 {
            rsp.request_poweroff = true;
        }

        // Reboot request
        if pm.power_request == 2 {
            rsp.request_reboot = true;
        }
        
    }

    //Send response
    send_response(stream, CMD_GETPOWERMODE, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
}

pub fn listrecords_command(stream: &mut TcpStream) {

    //Records for reading
    let list = RECORD_LOG.read().unwrap();

    let mut rsp = ListRecordsRsp {
        rid: vec![],
        time_started: vec![],
        time_duration: vec![],
        completed: vec![],
        aborted: vec![],
        rtype: vec![],
    };

    //Iterate over records
    for r in &(*list) {

        //Append to response structure
        rsp.rid.push(r.rid);
        rsp.time_started.push(r.time_started);
        rsp.completed.push(r.completed);
        rsp.aborted.push(r.aborted);
        rsp.rtype.push(r.rtype);
        rsp.time_duration.push(r.time_duration);

    } 

    //Send response
    send_response(stream, CMD_LISTRECORDS, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
}

pub fn listruns_command(stream: &mut TcpStream) {

    //Runs for reading
    let list = RUN_LOG.read().unwrap();

    let mut rsp = ListRunsRsp {
        id: vec![],
        time_modified:vec![],
        sodimm_id: vec![],
        energy: vec![],
        ion: vec![],
        LET: vec![],
        description: vec![],
        num_records: vec![],
        is_complete: vec![],
    };

    //Iterate over runs
    for r in &(*list) {

        //Append to response structure
        rsp.id.push(r.id);
        rsp.time_modified.push(r.time_modified);
        rsp.sodimm_id.push(r.sodimm_id.clone());
        rsp.energy.push(r.energy);
        rsp.ion.push(r.ion.clone());
        rsp.LET.push(r.LET);
        rsp.description.push(r.description.clone());
        rsp.num_records.push(0);
        rsp.is_complete.push(false);
    } 

    //Send response
    send_response(stream, CMD_LISTRUNS, crate::server::codec().serialize(&rsp).unwrap()).unwrap();
}