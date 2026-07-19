use std::fs;
use std::path::PathBuf;
use std::sync::Mutex;

use crate::utils;

pub static DATALOG: Mutex<Vec<String>> = Mutex::new(Vec::new());
pub static TAKEN_UUIDS: Mutex<Vec<String>> = Mutex::new(Vec::new());

pub fn init() -> std::io::Result<()> {
    let mut log_dir: PathBuf = dirs::home_dir()
        .ok_or_else(|| std::io::Error::new(
            std::io::ErrorKind::NotFound,
            "could not determine home directory",
        ))?;
    log_dir.push("zcuLogs");

    if !log_dir.exists() {
        fs::create_dir_all(&log_dir)?;
        return Ok(());
    }

    let mut uuids = TAKEN_UUIDS.lock().unwrap();

    for entry in fs::read_dir(&log_dir)? {
        let path = entry?.path();

        if !path.is_file() {
            continue;
        }

        match path.extension().and_then(|e| e.to_str()) {
            Some("csv") => {
                if let Some(stem) = path.file_stem().and_then(|s| s.to_str()) {
                    uuids.push(stem.to_string());
                }
            }
            Some("txt") => {
                //Ignore as this is most likely a test config summary
            }
            _ => eprintln!("invalid file type in zcuLogs: {}", path.display()),
        }
    }

    Ok(())
}

pub fn check_uuid(uuid: [u8; 3]) -> bool {
    
    match utils::get_uuid(uuid) {
        Some(uuid) => {

            !TAKEN_UUIDS
                .lock()
                .unwrap()
                .iter()
                .any(|u| *u == uuid)

        },
        None => false,
    }

}

pub fn write_summary(uuid: String, entries: Vec<String>) -> std::io::Result<()> {
    let mut log_path: PathBuf = dirs::home_dir()
        .ok_or_else(|| std::io::Error::new(
            std::io::ErrorKind::NotFound,
            "could not determine home directory",
        ))?;
    log_path.push("zcuLogs");
    log_path.push(format!("{uuid}.txt"));

    fs::write(&log_path, entries.join("\n"))?;

    Ok(())
}

pub fn record(entry: String) {
    DATALOG.lock().unwrap().push(entry);
}

pub fn clear() {
    DATALOG.lock().unwrap().clear();
}

pub fn write(uuid: String) -> std::io::Result<()> {
    let mut log_path: PathBuf = dirs::home_dir()
        .ok_or_else(|| std::io::Error::new(
            std::io::ErrorKind::NotFound,
            "could not determine home directory",
        ))?;
    log_path.push("zcuLogs");
    log_path.push(format!("{uuid}.csv"));

    let mut datalog = DATALOG.lock().unwrap();
    fs::write(&log_path, datalog.join("\n"))?;
    datalog.clear();

    TAKEN_UUIDS.lock().unwrap().push(uuid);

    Ok(())
}

pub fn new(entries: Vec<&str>) {
    let combined = entries
        .iter()
        .map(|s| format!("\"{s}\""))
        .collect::<Vec<_>>()
        .join(",");

    let mut datalog = DATALOG.lock().unwrap();
    datalog.clear();
    datalog.push(combined);
}

pub fn log(entries: Vec<String>) {
    let combined = entries
        .iter()
        .map(|s| format!("\"{s}\""))
        .collect::<Vec<_>>()
        .join(",");

    DATALOG.lock().unwrap().push(combined);
}