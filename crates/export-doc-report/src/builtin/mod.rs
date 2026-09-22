use crate::{
    Document, ReportData, Result,
    error::{Error, ErrorKind, invalid},
};
use export_doc_domain::designer::Design;
use std::sync::atomic::{AtomicBool, Ordering};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Builtin {
    Invoice,
    PackingList,
    Contract,
    CustomsDeclaration,
    PaymentVoucher,
    ExpenseReimbursement,
}
pub const BUILTINS: [Builtin; 6] = [
    Builtin::Invoice,
    Builtin::PackingList,
    Builtin::Contract,
    Builtin::CustomsDeclaration,
    Builtin::PaymentVoucher,
    Builtin::ExpenseReimbursement,
];
impl Builtin {
    pub fn path(self) -> &'static str {
        match self {
            Self::Invoice => "Templates/Export/invoice_template.dtpl",
            Self::PackingList => "Templates/Export/packing_list_template.dtpl",
            Self::Contract => "Templates/Export/contract_template.dtpl",
            Self::CustomsDeclaration => "Templates/Export/customs_declaration_template.dtpl",
            Self::PaymentVoucher => "Templates/Internal/payment_voucher_template.dtpl",
            Self::ExpenseReimbursement => "Templates/Internal/expense_reimbursement_template.dtpl",
        }
    }
    pub fn label(self) -> &'static str {
        match self {
            Self::Invoice => "商业发票",
            Self::PackingList => "装箱单",
            Self::Contract => "售货合同",
            Self::CustomsDeclaration => "出口货物报关单",
            Self::PaymentVoucher => "付款单",
            Self::ExpenseReimbursement => "费用报销明细单",
        }
    }
    pub fn report_type(self) -> &'static str {
        if matches!(self, Self::PaymentVoucher | Self::ExpenseReimbursement) {
            "PaymentVoucher"
        } else {
            "ExportDocument"
        }
    }
    pub fn find(path: &str) -> Option<Self> {
        BUILTINS
            .into_iter()
            .find(|template| template.path() == path)
    }
    pub fn source(self) -> &'static [u8] {
        match self {
            Self::Invoice => include_bytes!("../../../../Templates/Export/invoice_template.dtpl"),
            Self::PackingList => {
                include_bytes!("../../../../Templates/Export/packing_list_template.dtpl")
            }
            Self::Contract => include_bytes!("../../../../Templates/Export/contract_template.dtpl"),
            Self::CustomsDeclaration => {
                include_bytes!("../../../../Templates/Export/customs_declaration_template.dtpl")
            }
            Self::PaymentVoucher => {
                include_bytes!("../../../../Templates/Internal/payment_voucher_template.dtpl")
            }
            Self::ExpenseReimbursement => {
                include_bytes!("../../../../Templates/Internal/expense_reimbursement_template.dtpl")
            }
        }
    }
}
pub fn render_builtin(
    template: Builtin,
    data: &ReportData,
    cancelled: &AtomicBool,
) -> Result<Document> {
    if template.report_type() != data.report_type {
        return Err(invalid("模板与单据的数据域不一致。"));
    }
    check(cancelled)?;
    let design = Builtin::design(template)?;
    let document = crate::render_design(data, &design, cancelled)?;
    if document.pages.len() > 500 {
        return Err(invalid("报表页数超过 500 页。"));
    }
    check(cancelled)?;
    Ok(document)
}
impl Builtin {
    pub fn design(self) -> Result<Design> {
        let design =
            export_doc_domain::report_template_format::decode(self.source()).map_err(invalid)?;
        if design.report_type != self.report_type() {
            return Err(invalid("模板与单据的数据域不一致。"));
        }
        Ok(design)
    }
}
fn check(cancelled: &AtomicBool) -> Result<()> {
    if cancelled.load(Ordering::Relaxed) {
        Err(Error {
            kind: ErrorKind::Cancelled,
            message: "报表输出已取消。".into(),
        })
    } else {
        Ok(())
    }
}
