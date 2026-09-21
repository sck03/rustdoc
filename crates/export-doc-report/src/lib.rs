//! Report data, physical layout and PDF encoding. This crate has no database,
//! authentication, HTTP, GUI or browser dependency.
mod archive;
mod builtin;
mod canvas;
mod data;
mod document;
mod error;
mod fonts;
mod layout;
pub mod packing;

pub use archive::zip_documents;
pub use builtin::{BUILTINS, Builtin, render_builtin};
pub use data::{RasterImage, ReportData, chinese_money, english_money};
pub use document::{Document, Page, pdf_document};
pub use error::{Error, ErrorKind, Result};
pub use fonts::configure;
pub use layout::{field_value, pages, pdf, render_design};
