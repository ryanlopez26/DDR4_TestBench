//! gpio.rs — Beam-status GPIO input for the ZCU104 DDR4 tester.
//!
//! Reads a single EMIO GPIO line wired to PMOD0_0 (package pin G8, LVCMOS33).
//! On the ZynqMP PS GPIO controller the MIO lines occupy 0..=77 and the first
//! EMIO bit lands at line 78, so that is the default offset here. Confirm on
//! the target with `gpioinfo` — if the kernel assigns a different base, change
//! `BEAM_LINE_OFFSET` (or set the BEAM_GPIO_LINE env var, see `init`).
//!
//! Usage:
//!     gpio::init()?;                  // once, at startup
//!     let high = gpio::getBeamStatus(); // hot path, called very often
//!
//! Depends on the `gpio-cdev` crate (modern character-device interface;
//! the legacy /sys/class/gpio sysfs path is deprecated). In Cargo.toml:
//!     gpio-cdev = "0.6"
//!
//! On the Yocto side, make sure the gpio chardev nodes exist in the image
//! (they do by default with the xlnx kernel) — no extra IMAGE_INSTALL needed
//! for this crate itself, though `libgpiod-tools` is handy for `gpioinfo`.

use std::sync::OnceLock;

use gpio_cdev::{Chip, LineHandle, LineRequestFlags};

/// GPIO chip device exposing the ZynqMP PS GPIO controller.
const GPIO_CHIP: &str = "/dev/gpiochip0";

/// Line offset within the chip for EMIO bit 0.
/// MIO 0..=77, then EMIO begins at 78. Verify with `gpioinfo`.
const BEAM_LINE_OFFSET: u32 = 78;

/// Consumer label that shows up in `gpioinfo` so it's clear who holds the line.
const CONSUMER: &str = "ddr4-tester-beam";

/// The opened line handle, initialized once by `init()` and read on the hot
/// path by `getBeamStatus()`. OnceLock gives us a lock-free read after init,
/// which matters because the status is polled very often.
static BEAM_LINE: OnceLock<LineHandle> = OnceLock::new();

/// Errors surfaced from this module.
#[derive(Debug)]
pub enum GpioError {
    /// init() was already called successfully; the line is already held.
    AlreadyInitialized,
    /// Underlying gpio-cdev failure (chip open, line request, etc.).
    Cdev(gpio_cdev::Error),
}

impl std::fmt::Display for GpioError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            GpioError::AlreadyInitialized => write!(f, "GPIO already initialized"),
            GpioError::Cdev(e) => write!(f, "gpio-cdev error: {e}"),
        }
    }
}

impl std::error::Error for GpioError {}

impl From<gpio_cdev::Error> for GpioError {
    fn from(e: gpio_cdev::Error) -> Self {
        GpioError::Cdev(e)
    }
}

/// Open the GPIO chip and acquire the beam-status line as an input, holding
/// the handle open for the lifetime of the process.
///
/// Call exactly once at startup, before any call to `getBeamStatus()`.
/// Holding the line open (rather than re-requesting it per read) is what keeps
/// `getBeamStatus()` cheap on the hot path: a single ioctl per poll, no
/// open/request/close churn.
///
/// The line offset defaults to `BEAM_LINE_OFFSET` (78). To override without
/// recompiling — e.g. if `gpioinfo` shows a different base on your kernel —
/// set the `BEAM_GPIO_LINE` environment variable to the desired offset.
pub fn init() -> Result<(), GpioError> {
    if BEAM_LINE.get().is_some() {
        return Err(GpioError::AlreadyInitialized);
    }

    let offset = std::env::var("BEAM_GPIO_LINE")
        .ok()
        .and_then(|s| s.parse::<u32>().ok())
        .unwrap_or(BEAM_LINE_OFFSET);

    let mut chip = Chip::new(GPIO_CHIP)?;
    let line = chip.get_line(offset)?;
    let handle = line.request(LineRequestFlags::INPUT, 0, CONSUMER)?;

    // If two threads race init(), only the first store wins; the loser's
    // handle is dropped (releasing its request) and we report success either
    // way, since the line is now held.
    let _ = BEAM_LINE.set(handle);
    Ok(())
}

/// Return `true` if the beam-status GPIO reads high, `false` if low.
///
/// Hot path: designed to be polled very often. After `init()`, this is a
/// single `get_value` ioctl on an already-open line.
///
/// # Panics
/// Panics if called before a successful `init()`. This is deliberate: a poll
/// loop running against an uninitialized line is a programming error, and a
/// panic surfaces it immediately rather than silently returning a bogus
/// `false`. If you prefer a non-panicking variant, see `try_get_beam_status`.
#[allow(non_snake_case)]
pub fn getBeamStatus() -> bool {

    if crate::config::SIMULATION_MODE { return false; }

    let handle = BEAM_LINE
        .get()
        .expect("gpio::init() must be called before getBeamStatus()");

    // A read failure here is unexpected for an already-acquired input line;
    // treat it as "not high" rather than panicking on the hot path. Adjust to
    // taste if you'd rather propagate the error.
    matches!(handle.get_value(), Ok(1))
}

/// Non-panicking variant of `getBeamStatus()`.
///
/// Returns `Ok(true)` / `Ok(false)` on a successful read, `Err` if the line
/// was never initialized or the read ioctl failed. Use this if the caller
/// wants to distinguish "low" from "read failed".
pub fn try_get_beam_status() -> Result<bool, GpioError> {
    let handle = BEAM_LINE.get().ok_or(GpioError::AlreadyInitialized)?;
    Ok(handle.get_value()? == 1)
}