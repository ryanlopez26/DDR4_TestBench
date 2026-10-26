use std::path::{Path, PathBuf};
use std::process::Command;
use std::sync::RwLock;

use crate::task_engine::STATUS;

//Currently loaded FPGA image
pub static current_image: RwLock<Option<String>> = RwLock::new(None);

/// Loads `/dvr/fpga/<image_name>.bit.bin` into the PL via `fpgautil -b`.
/// Blocks until fpgautil exits. Returns true only if the image existed and
/// fpgautil reported success.
pub fn flash_fpga(image_name: &str) -> bool {
    // Keep a network-supplied name from escaping /dvr/fpga
    if image_name.is_empty()
        || image_name.contains('/')
        || image_name.contains('\0')
        || image_name.contains("..")
    {
        eprintln!("flash_fpga: rejected image name {image_name:?}");
        return false;
    }

    let path: PathBuf = Path::new("/dvr/fpga").join(format!("{image_name}.bit.bin"));

    if !path.is_file() {
        eprintln!("flash_fpga: {} not found", path.display());
        return false;
    }

    //Make sure engine is idle before flashing, refuse to flash if busy
    if !STATUS.read().unwrap().is_idle {
        //Not idle, abort
        return false;
    }

    match Command::new("fpgautil").arg("-b").arg(&path).output() {
        Ok(out) if out.status.success() => {
            //Update the currently loaded image
            *current_image.write().unwrap() = Some(image_name.to_owned());
            true
        },
        Ok(out) => {
            eprintln!(
                "flash_fpga: fpgautil exited with {} — {}",
                out.status,
                String::from_utf8_lossy(&out.stderr).trim()
            );
            //Update the currently loaded image
            *current_image.write().unwrap() = None;
            false
        }
        Err(e) => {
            eprintln!("flash_fpga: failed to spawn fpgautil: {e}");
            //Update the currently loaded image
            *current_image.write().unwrap() = None;
            false
        }
    }
}