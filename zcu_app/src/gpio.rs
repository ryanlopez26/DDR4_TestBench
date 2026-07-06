//! gpio.rs — EMIO GPIO channels for the ZCU104 DDR4 tester.
//!
//! Exposes four EMIO lines on the ZynqMP PS GPIO controller (gpiochip1,
//! `zynqmp_gpio`). MIO lines occupy 0..=77, so EMIO bit N lands at line
//! 78 + N. Confirmed on this board with `gpioget -c gpiochip1 <line>`.
//!
//!   EMIO bit  |  gpiochip1 line  |  Direction |  Channel
//!   ----------|------------------|------------|---------------------------
//!       0     |        78        |   output   | FPGA Reset
//!       1     |        79        |   output   | DDR4 Controller Reset
//!       2     |        80        |   input    | Beam Signal
//!       3     |        81        |   input    | DDR4 Controller Calibrated
//!
//! If a kernel update shifts the EMIO base away from 78, adjust the
//! `*_LINE_OFFSET` constants below (or override via the per-channel env
//! vars documented on `init`).
//!
//! Usage:
//!     gpio::init()?;                                   // once, at startup
//!     gpio::set_fpga_reset(true)?;                      // assert reset
//!     gpio::set_ddr4_controller_reset(false)?;          // deassert reset
//!     let beam = gpio::get_beam_signal();                // hot path
//!     let cal  = gpio::get_ddr4_controller_calibrated();  // hot path
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
/// On this board gpiochip1 is `zynqmp_gpio` (174 lines = 78 MIO + 96 EMIO);
/// gpiochip0 is the 4-line firmware GPIO and gpiochip2 is an I2C expander.
const GPIO_CHIP: &str = "/dev/gpiochip1";

/// Line offsets within the chip for each EMIO channel. MIO 0..=77, then
/// EMIO begins at 78. Verify with `gpioinfo`.
const FPGA_RESET_LINE_OFFSET: u32 = 78;
const DDR4_CTRL_RESET_LINE_OFFSET: u32 = 79;
const BEAM_LINE_OFFSET: u32 = 80;
const DDR4_CAL_LINE_OFFSET: u32 = 81;

/// Consumer labels that show up in `gpioinfo` so it's clear who holds each line.
const FPGA_RESET_CONSUMER: &str = "ddr4-tester-fpga-reset";
const DDR4_CTRL_RESET_CONSUMER: &str = "ddr4-tester-ddr4-ctrl-reset";
const BEAM_CONSUMER: &str = "ddr4-tester-beam";
const DDR4_CAL_CONSUMER: &str = "ddr4-tester-ddr4-cal";

/// All four line handles, initialized once by `init()`.
///
/// OnceLock gives a lock-free read after init, which matters because the
/// input channels (beam, calibrated) are polled very often on the hot path.
static GPIO_LINES: OnceLock<GpioLines> = OnceLock::new();

struct GpioLines {
    fpga_reset: LineHandle,
    ddr4_ctrl_reset: LineHandle,
    beam: LineHandle,
    ddr4_cal: LineHandle,
}

/// Errors surfaced from this module.
#[derive(Debug)]
pub enum GpioError {
    /// init() was already called successfully; the lines are already held.
    AlreadyInitialized,
    /// A read/write was attempted before init() acquired the lines.
    NotInitialized,
    /// Underlying gpio-cdev failure (chip open, line request, etc.).
    Cdev(gpio_cdev::Error),
}

impl std::fmt::Display for GpioError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            GpioError::AlreadyInitialized => write!(f, "GPIO already initialized"),
            GpioError::NotInitialized => write!(f, "GPIO not initialized; call init() first"),
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

/// Open the GPIO chip and acquire all four channels, holding the handles
/// open for the lifetime of the process.
///
/// Call exactly once at startup, before any other function in this module.
/// Holding the lines open (rather than re-requesting them per call) is what
/// keeps the input getters cheap on the hot path: a single ioctl per poll,
/// no open/request/close churn. The two outputs are requested with an
/// initial value of 0 (deasserted); call `set_fpga_reset` /
/// `set_ddr4_controller_reset` explicitly if you need a different initial
/// state.
///
/// Each line offset defaults to the `*_LINE_OFFSET` constant above. To
/// override any of them without recompiling — e.g. if `gpioinfo` shows a
/// different base on your kernel — set the corresponding environment
/// variable:
///     FPGA_RESET_GPIO_LINE, DDR4_CTRL_RESET_GPIO_LINE,
///     BEAM_GPIO_LINE, DDR4_CAL_GPIO_LINE
pub fn init() -> Result<(), GpioError> {
    if GPIO_LINES.get().is_some() {
        return Err(GpioError::AlreadyInitialized);
    }

    let mut chip = Chip::new(GPIO_CHIP)?;

    let fpga_reset_offset = line_offset_from_env("FPGA_RESET_GPIO_LINE", FPGA_RESET_LINE_OFFSET);
    let ddr4_ctrl_reset_offset =
        line_offset_from_env("DDR4_CTRL_RESET_GPIO_LINE", DDR4_CTRL_RESET_LINE_OFFSET);
    let beam_offset = line_offset_from_env("BEAM_GPIO_LINE", BEAM_LINE_OFFSET);
    let ddr4_cal_offset = line_offset_from_env("DDR4_CAL_GPIO_LINE", DDR4_CAL_LINE_OFFSET);

    let fpga_reset = chip
        .get_line(fpga_reset_offset)?
        .request(LineRequestFlags::OUTPUT, 0, FPGA_RESET_CONSUMER)?;
    let ddr4_ctrl_reset = chip
        .get_line(ddr4_ctrl_reset_offset)?
        .request(LineRequestFlags::OUTPUT, 0, DDR4_CTRL_RESET_CONSUMER)?;
    let beam = chip
        .get_line(beam_offset)?
        .request(LineRequestFlags::INPUT, 0, BEAM_CONSUMER)?;
    let ddr4_cal = chip
        .get_line(ddr4_cal_offset)?
        .request(LineRequestFlags::INPUT, 0, DDR4_CAL_CONSUMER)?;

    let lines = GpioLines {
        fpga_reset,
        ddr4_ctrl_reset,
        beam,
        ddr4_cal,
    };

    // If two threads race init(), only the first store wins; the loser's
    // handles are dropped (releasing their requests) and we report success
    // either way, since the lines are now held.
    let _ = GPIO_LINES.set(lines);
    Ok(())
}

fn line_offset_from_env(var: &str, default: u32) -> u32 {
    std::env::var(var)
        .ok()
        .and_then(|s| s.parse::<u32>().ok())
        .unwrap_or(default)
}

fn lines() -> &'static GpioLines {
    GPIO_LINES
        .get()
        .expect("gpio::init() must be called before using any gpio:: function")
}

fn try_lines() -> Result<&'static GpioLines, GpioError> {
    GPIO_LINES.get().ok_or(GpioError::NotInitialized)
}

// ---------------------------------------------------------------------
// FPGA Reset (output, EMIO bit 0 / line 78)
// ---------------------------------------------------------------------

/// Drive the FPGA Reset line high (`true`) or low (`false`).
///
/// # Panics
/// Panics if called before a successful `init()`.
pub fn set_fpga_reset(high: bool) -> Result<(), GpioError> {
    lines()
        .fpga_reset
        .set_value(high as u8)
        .map_err(GpioError::from)
}

/// Non-panicking variant of `set_fpga_reset`.
pub fn try_set_fpga_reset(high: bool) -> Result<(), GpioError> {
    try_lines()?.fpga_reset.set_value(high as u8)?;
    Ok(())
}

// ---------------------------------------------------------------------
// DDR4 Controller Reset (output, EMIO bit 1 / line 79)
// ---------------------------------------------------------------------

/// Drive the DDR4 Controller Reset line high (`true`) or low (`false`).
///
/// # Panics
/// Panics if called before a successful `init()`.
pub fn set_ddr4_controller_reset(high: bool) -> Result<(), GpioError> {
    lines()
        .ddr4_ctrl_reset
        .set_value(high as u8)
        .map_err(GpioError::from)
}

/// Non-panicking variant of `set_ddr4_controller_reset`.
pub fn try_set_ddr4_controller_reset(high: bool) -> Result<(), GpioError> {
    try_lines()?.ddr4_ctrl_reset.set_value(high as u8)?;
    Ok(())
}

// ---------------------------------------------------------------------
// Beam Signal (input, EMIO bit 2 / line 80)
// ---------------------------------------------------------------------

/// Return `true` if the Beam Signal GPIO reads high, `false` if low.
///
/// Hot path: designed to be polled very often. After `init()`, this is a
/// single `get_value` ioctl on an already-open line.
///
/// # Panics
/// Panics if called before a successful `init()`. This is deliberate: a
/// poll loop running against an uninitialized line is a programming error,
/// and a panic surfaces it immediately rather than silently returning a
/// bogus `false`. If you prefer a non-panicking variant, see
/// `try_get_beam_signal`.
pub fn get_beam_signal() -> bool {
    if crate::config::SIMULATION_MODE {
        return false;
    }

    // A read failure here is unexpected for an already-acquired input line;
    // treat it as "not high" rather than panicking on the hot path. Adjust
    // to taste if you'd rather propagate the error.
    matches!(lines().beam.get_value(), Ok(1))
}

/// Non-panicking variant of `get_beam_signal`.
///
/// Returns `Ok(true)` / `Ok(false)` on a successful read, `Err` if the line
/// was never initialized or the read ioctl failed. Use this if the caller
/// wants to distinguish "low" from "read failed".
pub fn try_get_beam_signal() -> Result<bool, GpioError> {
    Ok(try_lines()?.beam.get_value()? == 1)
}

// ---------------------------------------------------------------------
// DDR4 Controller Calibrated (input, EMIO bit 3 / line 81)
// ---------------------------------------------------------------------

/// Return `true` if the DDR4 Controller Calibrated GPIO reads high (MIG
/// calibration complete), `false` if low.
///
/// Hot path: designed to be polled very often. After `init()`, this is a
/// single `get_value` ioctl on an already-open line.
///
/// # Panics
/// Panics if called before a successful `init()`. See `get_beam_signal` for
/// rationale. If you prefer a non-panicking variant, see
/// `try_get_ddr4_controller_calibrated`.
pub fn get_ddr4_controller_calibrated() -> bool {
    if crate::config::SIMULATION_MODE {
        return true;
    }

    matches!(lines().ddr4_cal.get_value(), Ok(1))
}

/// Non-panicking variant of `get_ddr4_controller_calibrated`.
pub fn try_get_ddr4_controller_calibrated() -> Result<bool, GpioError> {
    Ok(try_lines()?.ddr4_cal.get_value()? == 1)
}
