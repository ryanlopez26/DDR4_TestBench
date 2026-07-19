use std::time::{SystemTime, UNIX_EPOCH};

pub fn now() -> f32 {
    let dur = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .expect("system clock is before UNIX epoch");
    dur.as_millis() as f32
}