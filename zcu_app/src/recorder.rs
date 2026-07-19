use std::fs;
use std::path::PathBuf;
use std::sync::Mutex;


pub static LOGDIR: &str = "/mnt/zcuLogs";
pub static DATALOG: Mutex<Vec<String>> = Mutex::new(Vec::new());
pub static TAKEN_UUIDS: Mutex<Vec<u16>> = Mutex::new(Vec::new());

pub fn init() -> std::io::Result<()> {

    if !PathBuf::from(LOGDIR).exists() {
        fs::create_dir_all(LOGDIR)?;
        return Ok(());
    }

    let mut uuids = TAKEN_UUIDS.lock().unwrap();

    for entry in fs::read_dir(LOGDIR)? {
        let path = entry?.path();

        if !path.is_file() {
            continue;
        }

        match path.extension().and_then(|e| e.to_str()) {
            Some("csv") => {
                match path.file_stem() {
                    Some(stem) => {
                        uuids.push(stem.to_str().unwrap().parse::<u16>().unwrap());
                    },
                    None => {
                        eprintln!("No file stem found!");
                    },
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

pub fn check_uuid(uuid: u16) -> bool {
    
    !TAKEN_UUIDS
        .lock()
        .unwrap().contains(&uuid)

}

pub fn write_summary(uuid: u16, entries: Vec<String>) -> std::io::Result<()> {

    let mut log_path = PathBuf::from(LOGDIR);
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

pub fn write(uuid: u16) -> std::io::Result<()> {

    let mut log_path = PathBuf::from(LOGDIR);
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