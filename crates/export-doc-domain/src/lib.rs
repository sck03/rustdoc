#[cfg(test)]
extern crate self as export_doc_domain;
#[cfg(test)]
#[path = "../../../tests/ReportTemplateFixtures/designer_fixture.rs"]
pub(crate) mod test_design_fixture;

pub use export_doc_contracts::{contracts, generated_api, report_batches};
pub mod announcement;
pub mod approval;
pub mod approval_policy;
pub mod backup_plan;
pub mod business_date;
pub mod crm;
pub mod designer;
pub mod hs;
pub mod hs_import;
pub mod invoice;
pub mod invoice_adjustments;
pub mod invoice_calculation;
pub mod invoice_columns;
pub mod invoice_print_rows;
pub mod invoice_query;
pub mod oa;
pub mod organization;
pub mod packing;
pub mod party;
pub mod payment;
pub mod permissions;
pub mod personnel_organization;
pub mod product;
pub mod report_template_format;
pub mod sales;
pub mod supplier;
pub mod template;
pub mod template_lifecycle;

pub mod letter_of_credit;

pub mod number;
pub mod single_window;
