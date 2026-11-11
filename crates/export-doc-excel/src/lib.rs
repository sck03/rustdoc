//! Optional Excel capability. Parsing, preview and OOXML editing use byte
//! streams; hosts own authorization, paths, persistence and background tasks.
mod archive;
mod import;
mod manual_mapping;
mod mapping;
mod reader;
mod sheet;
mod single_window;
mod supplements;
mod tabular;
mod tabular_reader;
pub use tabular_reader::read_table;
pub use tabular_reader::{TableData, read_sheet_data, read_table_data, sheet_names};
mod workbook;
mod xml;
pub use import::preview;
pub use single_window::preview as preview_single_window;
pub use workbook::{blank_template, booking_from_invoice, convert_booking, table, table_stream};

pub const MAX_INPUT: usize = 25 * 1024 * 1024;
pub type Result<T> = std::result::Result<T, String>;
pub type Check<'a> = &'a dyn Fn() -> Result<()>;

#[cfg(test)]
mod limits_tests;
#[cfg(test)]
mod single_window_tests;
#[cfg(test)]
mod tests;
