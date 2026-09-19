mod commercial;
mod commercial_footer;
mod commercial_header;
mod customs;
mod payment;
use crate::{
    Document, ReportData, Result,
    error::{Error, ErrorKind, invalid},
};
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
            Self::Invoice => "Templates/Export/invoice_template.html",
            Self::PackingList => "Templates/Export/packing_list_template.html",
            Self::Contract => "Templates/Export/contract_template.html",
            Self::CustomsDeclaration => "Templates/Export/customs_declaration_template.html",
            Self::PaymentVoucher => "Templates/Internal/payment_voucher_template.html",
            Self::ExpenseReimbursement => "Templates/Internal/expense_reimbursement_template.html",
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
    pub fn source(self) -> &'static str {
        match self {
            Self::Invoice => include_str!("../../../../Templates/Export/invoice_template.html"),
            Self::PackingList => {
                include_str!("../../../../Templates/Export/packing_list_template.html")
            }
            Self::Contract => include_str!("../../../../Templates/Export/contract_template.html"),
            Self::CustomsDeclaration => {
                include_str!("../../../../Templates/Export/customs_declaration_template.html")
            }
            Self::PaymentVoucher => {
                include_str!("../../../../Templates/Internal/payment_voucher_template.html")
            }
            Self::ExpenseReimbursement => {
                include_str!("../../../../Templates/Internal/expense_reimbursement_template.html")
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
    let document = match template {
        Builtin::Invoice | Builtin::PackingList | Builtin::Contract => {
            commercial::render(template, data, cancelled)
        }
        Builtin::CustomsDeclaration => customs::render(data, cancelled),
        Builtin::PaymentVoucher | Builtin::ExpenseReimbursement => {
            payment::render(template, data, cancelled)
        }
    }?;
    if document.pages.len() > 500 {
        return Err(invalid("报表页数超过 500 页。"));
    }
    check(cancelled)?;
    Ok(document)
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
