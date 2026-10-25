use std::fs;
use std::path::{Path, PathBuf};
use std::sync::{Mutex, RwLock};

use crate::record::Record;
use crate::run::Run;
use crate::{PM, PowerManager, tools};


pub static LOGDIR: &str = "/dvr/";

// Collection of all runs
pub static RUN_LOG: RwLock<Vec<Run>> = RwLock::new(Vec::new());

// Collection of all records
pub static RECORD_LOG: RwLock<Vec<Record>> = RwLock::new(Vec::new()); 

pub fn init() {

    //Obtain locks on necessary data structures
    let mut reclog = RECORD_LOG.write().unwrap();
    let mut runlog = RUN_LOG.write().unwrap();


    //Create necessary directories
    for d in ["/dvr/captures", "/dvr/records", "/dvr/runs"] {
        std::fs::create_dir_all(d).unwrap();
    }

    //Load records from disk
    if let Ok(entries) = fs::read_dir("/dvr/records") {
        for entry in entries.flatten() {
            let path = entry.path();
            if path.extension() != Some("rec".as_ref()) {
                continue;
            }
            if let Ok(s) = fs::read_to_string(&path) {
                if let Ok(rec) = serde_json::from_str::<Record>(&s) {
                    reclog.push(rec);
                }
            }
        }
    }

    //Load runs from disk
    if let Ok(entries) = fs::read_dir("/dvr/runs") {
        for entry in entries.flatten() {
            let path = entry.path();
            if path.extension() != Some("run".as_ref()) {
                continue;
            }
            if let Ok(s) = fs::read_to_string(&path) {
                if let Ok(run) = serde_json::from_str::<Run>(&s) {
                    runlog.push(run);
                }
            }
        }
    }
}

pub fn submit_record(r: Record) -> bool {


    //Refuse if safety is in place
    
    if PM.read().unwrap().safe {
        return false;
    }

    // Sync record to disk and add it to the archive

    // Path of new record file
    let path = Path::new("/dvr/records").join(format!("{:04X}.rec", r.rid));

    // Write the file to disk

    // Add the record to the archive registry
    let mut rlog = RECORD_LOG.write().unwrap();
    rlog.push(r.clone());

        // Convert the structure to a JSON string    
    match serde_json::to_string_pretty(&r){
        Ok(s) => {
            match std::fs::write(&path, s) {
                Ok(_) => true,
                Err(_) => false,
            }
        },
        Err(_) => false,
    }


}

pub fn submit_run(r: Run) -> bool {

    //Refuse if safety is in place
    
    if PM.read().unwrap().safe {
        return false;
    }

    // Sync record to disk and add it to the archive

    // Add the record to the archive registry
    let mut rlog = RUN_LOG.write().unwrap();

    // Path of new record file
    let path = Path::new("/dvr/runs").join(format!("{:04X}.run", r.id));
    rlog.push(r.clone());

    // Convert the structure to a JSON string    
    match serde_json::to_string_pretty(&r) {
        Ok(s) => {
            // Write the file to disk
            match std::fs::write(&path, s){
                Ok(_) => true,
                Err(_) => false,
            }
        },
        Err(_) => false,
    }

}

pub fn gen_run_number() -> u16 {
    let mut taken: Vec<u16> = {
        let runs = RUN_LOG.read().unwrap();
        runs.iter().map(|r| r.id).collect()
    };                                   // read guard dropped here
    taken.sort_unstable();

    let mut next: u32 = 0;
    for id in taken {
        match u32::from(id) {
            n if n == next => next += 1,  // contiguous, keep going
            n if n > next => break,       // found the gap
            _ => {}                       // duplicate, ignore
        }
    }
    next as u16
}

pub fn gen_record_number() -> u16 {
    let log = RECORD_LOG.read().unwrap();
    loop {
        let rid: u16 = tools::rand_u16();
        if !log.iter().any(|r| r.rid == rid) {
            return rid;
        }
    }
}

