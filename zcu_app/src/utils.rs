use std::time::{SystemTime, UNIX_EPOCH};

pub fn now() -> f32 {
    let dur = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .expect("system clock is before UNIX epoch");
    dur.as_millis() as f32
}

pub fn get_uuid(uuid: [u8; 3]) -> Option<String> {
    if uuid.iter().all(|&b| b.is_ascii_uppercase()) {
        Some(String::from_utf8(uuid.to_vec()).unwrap())
    } else {
        None
    }
}