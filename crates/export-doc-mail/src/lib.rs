//! Optional mail capability. No GUI, database or host file access.
pub mod attachment;
pub mod content;
pub mod recipient;
pub mod transport;
pub use lettre::Message;
