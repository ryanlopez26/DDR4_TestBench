// Deterministic pseudo-random byte source keyed by (seed, index).
//
// Internal state:
//   - SEED  : u64
//   - INDEX : u64
//
// Public API:
//   set_seed, get_seed, set_index, get_index, rand
//
// Guarantee: for any fixed (seed, index) pair, the byte produced is always
// the same. `rand()` returns the byte for the current index, then advances
// the index by 1. To replay a sequence, restore the seed and index with the
// setters.
//
// Usage:
//
//   use rand;
//
//   rand::set_seed(0xC0FFEE);
//   rand::set_index(0);
//   let a = rand::rand();          // byte at index 0, index now 1
//   let b = rand::rand();          // byte at index 1, index now 2
//
//   rand::set_index(0);
//   assert_eq!(rand::rand(), a);   // same seed + index -> same byte
//   assert_eq!(rand::rand(), b);

use std::sync::atomic::{AtomicU64, Ordering};

static SEED: AtomicU64 = AtomicU64::new(0);
static INDEX: AtomicU64 = AtomicU64::new(0);

/// Set the global seed.
pub fn set_seed(seed: u64) {
    SEED.store(seed, Ordering::Relaxed);
}

/// Get the current seed.
pub fn get_seed() -> u64 {
    SEED.load(Ordering::Relaxed)
}

/// Set the global index (the position in the pseudo-random stream).
pub fn set_index(index: u64) {
    INDEX.store(index, Ordering::Relaxed);
}

/// Get the current index.
pub fn get_index() -> u64 {
    INDEX.load(Ordering::Relaxed)
}

/// Return the pseudo-random byte at the current (seed, index), then
/// advance the index by 1.
pub fn rand() -> u8 {
    let seed = SEED.load(Ordering::Relaxed);
    // fetch_add returns the previous value — that's the index we hash.
    let index = INDEX.fetch_add(1, Ordering::Relaxed);
    mix(seed, index)
}

/// Pure mixing function: (seed, index) -> u8.
///
/// This is splitmix64 applied to `seed + index * φ`, where φ is the
/// 64-bit golden-ratio constant 0x9E37_79B9_7F4A_7C15. It gives strong
/// avalanche behavior so consecutive indices produce uncorrelated bytes,
/// while remaining a pure function of its inputs (no global state),
/// which is what guarantees reproducibility.
fn mix(seed: u64, index: u64) -> u8 {
    let mut z = seed.wrapping_add(index.wrapping_mul(0x9E37_79B9_7F4A_7C15));
    z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
    z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
    z ^= z >> 31;
    z as u8
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::Mutex;

    // Tests share global state, so serialize them.
    static LOCK: Mutex<()> = Mutex::new(());

    #[test]
    fn deterministic_for_same_seed_and_index() {
        let _g = LOCK.lock().unwrap();
        set_seed(0xDEAD_BEEF);
        set_index(42);
        let a = rand();
        set_seed(0xDEAD_BEEF);
        set_index(42);
        let b = rand();
        assert_eq!(a, b);
    }

    #[test]
    fn rand_advances_index() {
        let _g = LOCK.lock().unwrap();
        set_seed(1);
        set_index(0);
        let _ = rand();
        assert_eq!(get_index(), 1);
        let _ = rand();
        assert_eq!(get_index(), 2);
    }

    #[test]
    fn different_indices_generally_differ() {
        let _g = LOCK.lock().unwrap();
        set_seed(7);
        set_index(0);
        let a = rand();
        let b = rand();
        // Not guaranteed for every seed, but holds for this one.
        assert_ne!(a, b);
    }

    #[test]
    fn different_seeds_generally_differ() {
        let _g = LOCK.lock().unwrap();
        set_seed(1);
        set_index(100);
        let a = rand();
        set_seed(2);
        set_index(100);
        let b = rand();
        assert_ne!(a, b);
    }
}