//! gpio.rs — EMIO GPIO channels for the ZCU104 DDR4 tester.
//!
//! Exposes five EMIO lines on the ZynqMP PS GPIO controller (gpiochip1,
//! `zynqmp_gpio`). MIO lines occupy 0..=77, so EMIO bit N lands at line
//! 78 + N. Confirmed on this board with `gpioget -c gpiochip1 <line>`.
//!
//!   EMIO bit  |  gpiochip1 line  |  Direction |  Channel
//!   ----------|------------------|------------|---------------------------
//!       0     |        78        |   input    | Beam Signal
//!       1     |        79        |   input    | Calibration Signal
//!       2     |        80        |   input    | UI Clock Signal
//!       3     |        81        |   input    | PL Clock Signal
//!       4     |        82        |   input    | FPGA Loaded Status
//!
//! All five channels are inputs — this module is read-only. There are no
//! output lines here, so nothing in this file ever drives a pin.
//!
//! If a kernel update shifts the EMIO base away from 78, adjust the
//! `*_LINE_OFFSET` constants below (or override via the per-channel env
//! vars documented on `init`).
//!
//! Usage:
//!     gpio::init()?;                                  // once, at startup
//!     let beam   = gpio::get_beam_signal();             // hot path
//!     let cal    = gpio::get_calibration_signal();       // hot path
//!     let ui_clk = gpio::get_ui_clock_signal();           // hot path
//!     let pl_clk = gpio::get_pl_clock_signal();           // hot path
//!     let loaded = gpio::get_fpga_loaded_status();         // hot path
//!
//! ## The `hardware` feature
//!
//! Real GPIO access below (the `backend` module gated on
//! `feature = "hardware"`) depends on the `gpio-cdev` crate, which talks
//! to `/dev/gpiochipN` character devices - a Linux-only API. That's fine
//! on the board, but it means this file can't even compile on Windows/
//! macOS dev machines, independent of `SIMULATION_MODE`'s runtime value,
//! since Rust still compiles a module's code even if nothing at runtime
//! ever calls into it.
//!
//! `hardware` is on by default (see Cargo.toml), so nothing changes for
//! the existing board build. Building with `--no-default-features`
//! (e.g. `cargo build --no-default-features` on a Windows/Mac box) swaps
//! in the second `backend` module below: a pure-software stand-in with
//! the exact same public functions, so `main.rs` and `commands.rs` don't
//! need to know or care which one is compiled in.
//!
//! Depends on the `gpio-cdev` crate (modern character-device interface;
//! the legacy /sys/class/gpio sysfs path is deprecated) when `hardware`
//! is enabled. In Cargo.toml:
//!     gpio-cdev = { version = "0.6", optional = true }
//!
//! On the Yocto side, make sure the gpio chardev nodes exist in the image
//! (they do by default with the xlnx kernel) — no extra IMAGE_INSTALL needed
//! for this crate itself, though `libgpiod-tools` is handy for `gpioinfo`.

/// Errors surfaced from this module.
#[derive(Debug)]
pub enum GpioError {
    /// init() was already called successfully; the lines are already held.
    AlreadyInitialized,
    /// A read was attempted before init() acquired the lines.
    NotInitialized,
    /// Underlying gpio-cdev failure (chip open, line request, etc.).
    /// Only constructible when the `hardware` feature is enabled - the
    /// simulated backend never fails this way.
    #[cfg(feature = "hardware")]
    Cdev(gpio_cdev::Error),
}

impl std::fmt::Display for GpioError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            GpioError::AlreadyInitialized => write!(f, "GPIO already initialized"),
            GpioError::NotInitialized => write!(f, "GPIO not initialized; call init() first"),
            #[cfg(feature = "hardware")]
            GpioError::Cdev(e) => write!(f, "gpio-cdev error: {e}"),
        }
    }
}

impl std::error::Error for GpioError {}

#[cfg(feature = "hardware")]
impl From<gpio_cdev::Error> for GpioError {
    fn from(e: gpio_cdev::Error) -> Self {
        GpioError::Cdev(e)
    }
}

// =========================================================================
// Real backend: gpio-cdev against /dev/gpiochip1. Requires `hardware`.
// =========================================================================
#[cfg(feature = "hardware")]
mod backend {
    use super::GpioError;
    use std::sync::OnceLock;

    use gpio_cdev::{Chip, LineHandle, LineRequestFlags};

    /// GPIO chip device exposing the ZynqMP PS GPIO controller.
    /// On this board gpiochip1 is `zynqmp_gpio` (174 lines = 78 MIO + 96 EMIO);
    /// gpiochip0 is the 4-line firmware GPIO and gpiochip2 is an I2C expander.
    const GPIO_CHIP: &str = "/dev/gpiochip1";

    /// Line offsets within the chip for each EMIO channel. MIO 0..=77, then
    /// EMIO begins at 78. Verify with `gpioinfo`.
    const BEAM_LINE_OFFSET: u32 = 78;
    const CALIBRATION_LINE_OFFSET: u32 = 79;
    const UI_CLOCK_LINE_OFFSET: u32 = 80;
    const PL_CLOCK_LINE_OFFSET: u32 = 81;
    const FPGA_LOADED_LINE_OFFSET: u32 = 82;

    /// Consumer labels that show up in `gpioinfo` so it's clear who holds each line.
    const BEAM_CONSUMER: &str = "ddr4-tester-beam";
    const CALIBRATION_CONSUMER: &str = "ddr4-tester-calibration";
    const UI_CLOCK_CONSUMER: &str = "ddr4-tester-ui-clock";
    const PL_CLOCK_CONSUMER: &str = "ddr4-tester-pl-clock";
    const FPGA_LOADED_CONSUMER: &str = "ddr4-tester-fpga-loaded";

    /// All five line handles, initialized once by `init()`.
    ///
    /// OnceLock gives a lock-free read after init, which matters because these
    /// are all inputs polled very often on the hot path.
    static GPIO_LINES: OnceLock<GpioLines> = OnceLock::new();

    struct GpioLines {
        beam: LineHandle,
        calibration: LineHandle,
        ui_clock: LineHandle,
        pl_clock: LineHandle,
        fpga_loaded: LineHandle,
    }

    /// Open the GPIO chip and acquire all five channels, holding the handles
    /// open for the lifetime of the process.
    ///
    /// Call exactly once at startup, before any other function in this module.
    /// Holding the lines open (rather than re-requesting them per call) is what
    /// keeps the getters cheap on the hot path: a single ioctl per poll, no
    /// open/request/close churn. All five lines are requested as inputs; this
    /// module never drives a pin.
    ///
    /// Each line offset defaults to the `*_LINE_OFFSET` constant above. To
    /// override any of them without recompiling — e.g. if `gpioinfo` shows a
    /// different base on your kernel — set the corresponding environment
    /// variable:
    ///     BEAM_GPIO_LINE, CALIBRATION_GPIO_LINE,
    ///     UI_CLOCK_GPIO_LINE, PL_CLOCK_GPIO_LINE, FPGA_LOADED_GPIO_LINE
    pub fn init() -> Result<(), GpioError> {
        if GPIO_LINES.get().is_some() {
            return Err(GpioError::AlreadyInitialized);
        }

        let mut chip = Chip::new(GPIO_CHIP)?;

        let beam_offset = line_offset_from_env("BEAM_GPIO_LINE", BEAM_LINE_OFFSET);
        let calibration_offset =
            line_offset_from_env("CALIBRATION_GPIO_LINE", CALIBRATION_LINE_OFFSET);
        let ui_clock_offset = line_offset_from_env("UI_CLOCK_GPIO_LINE", UI_CLOCK_LINE_OFFSET);
        let pl_clock_offset = line_offset_from_env("PL_CLOCK_GPIO_LINE", PL_CLOCK_LINE_OFFSET);
        let fpga_loaded_offset =
            line_offset_from_env("FPGA_LOADED_GPIO_LINE", FPGA_LOADED_LINE_OFFSET);

        let beam = chip
            .get_line(beam_offset)?
            .request(LineRequestFlags::INPUT, 0, BEAM_CONSUMER)?;
        let calibration = chip
            .get_line(calibration_offset)?
            .request(LineRequestFlags::INPUT, 0, CALIBRATION_CONSUMER)?;
        let ui_clock = chip
            .get_line(ui_clock_offset)?
            .request(LineRequestFlags::INPUT, 0, UI_CLOCK_CONSUMER)?;
        let pl_clock = chip
            .get_line(pl_clock_offset)?
            .request(LineRequestFlags::INPUT, 0, PL_CLOCK_CONSUMER)?;
        let fpga_loaded = chip
            .get_line(fpga_loaded_offset)?
            .request(LineRequestFlags::INPUT, 0, FPGA_LOADED_CONSUMER)?;

        let lines = GpioLines {
            beam,
            calibration,
            ui_clock,
            pl_clock,
            fpga_loaded,
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

    // -------------------------------------------------------------------
    // Beam Signal (input, EMIO bit 0 / line 78)
    // -------------------------------------------------------------------

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
    /// was never initialized or the read ioctl failed.
    pub fn try_get_beam_signal() -> Result<bool, GpioError> {
        Ok(try_lines()?.beam.get_value()? == 1)
    }

    // -------------------------------------------------------------------
    // Calibration Signal (input, EMIO bit 1 / line 79)
    // -------------------------------------------------------------------

    /// Return `true` if the Calibration Signal GPIO reads high (MIG
    /// calibration complete), `false` if low.
    ///
    /// Hot path: designed to be polled very often. After `init()`, this is a
    /// single `get_value` ioctl on an already-open line.
    ///
    /// # Panics
    /// Panics if called before a successful `init()`. See `get_beam_signal` for
    /// rationale. If you prefer a non-panicking variant, see
    /// `try_get_calibration_signal`.
    pub fn get_calibration_signal() -> bool {
        if crate::config::SIMULATION_MODE {
            return true;
        }

        matches!(lines().calibration.get_value(), Ok(1))
    }

    /// Non-panicking variant of `get_calibration_signal`.
    pub fn try_get_calibration_signal() -> Result<bool, GpioError> {
        Ok(try_lines()?.calibration.get_value()? == 1)
    }

    // -------------------------------------------------------------------
    // UI Clock Signal (input, EMIO bit 2 / line 80)
    // -------------------------------------------------------------------

    /// Return `true` if the UI Clock Signal GPIO reads high.
    ///
    /// Intended as a presence/heartbeat check on `c0_ddr4_ui_clk` (e.g. via a
    /// clock-detector IP that latches high once toggling is observed), not as
    /// a way to sample the clock's actual waveform over a slow GPIO poll.
    ///
    /// Hot path: designed to be polled very often. After `init()`, this is a
    /// single `get_value` ioctl on an already-open line.
    ///
    /// # Panics
    /// Panics if called before a successful `init()`. See `get_beam_signal` for
    /// rationale. If you prefer a non-panicking variant, see
    /// `try_get_ui_clock_signal`.
    pub fn get_ui_clock_signal() -> bool {
        if crate::config::SIMULATION_MODE {
            return true;
        }

        matches!(lines().ui_clock.get_value(), Ok(1))
    }

    /// Non-panicking variant of `get_ui_clock_signal`.
    pub fn try_get_ui_clock_signal() -> Result<bool, GpioError> {
        Ok(try_lines()?.ui_clock.get_value()? == 1)
    }

    // -------------------------------------------------------------------
    // PL Clock Signal (input, EMIO bit 3 / line 81)
    // -------------------------------------------------------------------

    /// Return `true` if the PL Clock Signal GPIO reads high.
    ///
    /// Same intent as `get_ui_clock_signal`, but for `pl_clk0` — useful for
    /// confirming the PL fabric clock is present independent of whether the
    /// MIG UI clock has come up.
    ///
    /// Hot path: designed to be polled very often. After `init()`, this is a
    /// single `get_value` ioctl on an already-open line.
    ///
    /// # Panics
    /// Panics if called before a successful `init()`. See `get_beam_signal` for
    /// rationale. If you prefer a non-panicking variant, see
    /// `try_get_pl_clock_signal`.
    pub fn get_pl_clock_signal() -> bool {
        if crate::config::SIMULATION_MODE {
            return true;
        }

        matches!(lines().pl_clock.get_value(), Ok(1))
    }

    /// Non-panicking variant of `get_pl_clock_signal`.
    pub fn try_get_pl_clock_signal() -> Result<bool, GpioError> {
        Ok(try_lines()?.pl_clock.get_value()? == 1)
    }

    // -------------------------------------------------------------------
    // FPGA Loaded Status (input, EMIO bit 4 / line 82)
    // -------------------------------------------------------------------

    /// Return `true` if the FPGA Loaded Status GPIO reads high (bitstream
    /// loaded / PL fabric configured), `false` if low.
    ///
    /// Hot path: designed to be polled very often. After `init()`, this is a
    /// single `get_value` ioctl on an already-open line.
    ///
    /// # Panics
    /// Panics if called before a successful `init()`. See `get_beam_signal` for
    /// rationale. If you prefer a non-panicking variant, see
    /// `try_get_fpga_loaded_status`.
    pub fn get_fpga_loaded_status() -> bool {
        if crate::config::SIMULATION_MODE {
            return true;
        }

        matches!(lines().fpga_loaded.get_value(), Ok(1))
    }

    /// Non-panicking variant of `get_fpga_loaded_status`.
    pub fn try_get_fpga_loaded_status() -> Result<bool, GpioError> {
        Ok(try_lines()?.fpga_loaded.get_value()? == 1)
    }
}

// =========================================================================
// Simulated backend: no gpio-cdev, no real lines. Used whenever the
// `hardware` feature is off (e.g. `cargo build --no-default-features` on
// a Windows/Mac/dev box). Mirrors the real backend's own SIMULATION_MODE
// defaults exactly - beam low, everything else "healthy" (calibrated,
// clocks present, FPGA loaded) - so behaviour is identical to a hardware
// build that happens to have `SIMULATION_MODE = true`.
// =========================================================================
#[cfg(not(feature = "hardware"))]
mod backend {
    use super::GpioError;

    pub fn init() -> Result<(), GpioError> {
        Ok(())
    }

    pub fn get_beam_signal() -> bool {
        false
    }

    pub fn try_get_beam_signal() -> Result<bool, GpioError> {
        Ok(false)
    }

    pub fn get_calibration_signal() -> bool {
        true
    }

    pub fn try_get_calibration_signal() -> Result<bool, GpioError> {
        Ok(true)
    }

    pub fn get_ui_clock_signal() -> bool {
        true
    }

    pub fn try_get_ui_clock_signal() -> Result<bool, GpioError> {
        Ok(true)
    }

    pub fn get_pl_clock_signal() -> bool {
        true
    }

    pub fn try_get_pl_clock_signal() -> Result<bool, GpioError> {
        Ok(true)
    }

    pub fn get_fpga_loaded_status() -> bool {
        true
    }

    pub fn try_get_fpga_loaded_status() -> Result<bool, GpioError> {
        Ok(true)
    }
}

pub use backend::*;