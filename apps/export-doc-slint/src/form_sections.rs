//! Page layout and disclosure state. Hidden fields remain in the same draft.
use crate::{FormSection, form_model::FormModel, model};
use export_doc_engine::contracts;
use serde_json::{Value, json};
use std::collections::{BTreeMap, BTreeSet};

pub type DisclosureState = BTreeMap<String, bool>;
pub struct Section {
    pub key: &'static str,
    pub title: &'static str,
    pub fields: &'static [&'static str],
    pub open: bool,
}
impl Section {
    pub const fn new(
        key: &'static str,
        title: &'static str,
        fields: &'static [&'static str],
        open: bool,
    ) -> Self {
        Self {
            key,
            title,
            fields,
            open,
        }
    }
}

pub fn schema(source: &Value, prefix: &str, selected: &[&str]) -> Value {
    let properties = contracts::properties(source)
        .into_iter()
        .flatten()
        .filter_map(|(key, field)| {
            let path = if prefix.is_empty() {
                key.clone()
            } else {
                format!("{prefix}.{key}")
            };
            if selected.contains(&path.as_str()) {
                Some((key.clone(), field.clone()))
            } else if selected
                .iter()
                .any(|selected| selected.starts_with(&(path.clone() + ".")))
            {
                Some((key.clone(), schema(field, &path, selected)))
            } else {
                None
            }
        })
        .collect::<serde_json::Map<_, _>>();
    json!({"type":"object","properties":properties})
}

fn group(
    form: &FormModel,
    key: &str,
    title: &str,
    fields: Vec<crate::FormField>,
    open: bool,
    state: &mut DisclosureState,
) -> FormSection {
    if !form.error.is_empty()
        && fields
            .iter()
            .any(|field| field.key.as_str() == form.invalid_field)
    {
        state.insert(key.into(), true);
    }
    let filled = fields
        .iter()
        .filter(|field| !["", "0", "false"].contains(&field.value.trim()))
        .count();
    FormSection {
        key: key.into(),
        title: title.into(),
        expanded: *state.get(key).unwrap_or(&open),
        filled: filled as i32,
        fields: model(fields),
    }
}

pub fn build(
    form: &FormModel,
    definitions: &[Section],
    state: &mut DisclosureState,
) -> Vec<FormSection> {
    definitions
        .iter()
        .filter_map(|section| {
            let mut fields: Vec<_> = form
                .fields_for(&schema(&form.schema, "", section.fields))
                .into_iter()
                .filter(|field| field.kind != "heading")
                .collect();
            fields.sort_by_key(|field| {
                section
                    .fields
                    .iter()
                    .position(|key| field.key == *key || field.key.starts_with(&format!("{key}.")))
                    .unwrap_or(usize::MAX)
            });
            if fields.is_empty() {
                return None;
            }
            Some(group(
                form,
                section.key,
                section.title,
                fields,
                section.open,
                state,
            ))
        })
        .collect()
}

pub fn complete(form: &FormModel, resource: &str, state: &mut DisclosureState) -> Vec<FormSection> {
    use slint::Model;
    let definitions = record(resource);
    if definitions.is_empty() {
        return vec![];
    }
    let mut sections = build(form, &definitions, state);
    let used: BTreeSet<_> = sections
        .iter()
        .flat_map(|section| section.fields.iter().map(|field| field.key.to_string()))
        .collect();
    let remaining: Vec<_> = form
        .fields()
        .into_iter()
        .filter(|field| field.kind != "heading" && !used.contains(field.key.as_str()))
        .collect();
    if !remaining.is_empty() {
        sections.push(group(
            form,
            &format!("{resource}.other"),
            "其他资料",
            remaining,
            false,
            state,
        ));
    }
    sections
}

pub fn invoice(tab: i32) -> Vec<Section> {
    match tab {
        0 => vec![
            Section::new(
                "invoice.basic",
                "基本信息",
                &["invoiceNo", "contractNo", "type", "invoiceDate", "currency"],
                true,
            ),
            Section::new(
                "invoice.parties",
                "出口商与客户",
                &[
                    "exporterId",
                    "customerId",
                    "exporterNameEN",
                    "customerNameEN",
                ],
                true,
            ),
            Section::new(
                "invoice.addresses",
                "名称、地址与报关代码",
                &[
                    "exporterNameCN",
                    "exporterAddressEN",
                    "exporterAddressCN",
                    "customerAddressEN",
                    "exporterCreditCode",
                    "exporterCustomsCode",
                ],
                false,
            ),
            Section::new(
                "invoice.notify",
                "通知人",
                &["notifyPartyMode", "notifyPartyName", "notifyPartyAddress"],
                false,
            ),
            Section::new(
                "invoice.bank",
                "银行信息",
                &["bankName", "bankAccount", "swiftCode"],
                false,
            ),
            Section::new(
                "invoice.spares",
                "备用字段",
                &[
                    "spare1", "spare2", "spare3", "spare4", "spare5", "spare6", "spare7", "spare8",
                    "spare9", "spare10",
                ],
                false,
            ),
        ],
        2 => vec![
            Section::new(
                "invoice.shipping",
                "运输与贸易条件",
                &[
                    "shipmentDate",
                    "transportMode",
                    "portOfLoading",
                    "portOfDestination",
                    "destinationCountry",
                    "tradeTerms",
                    "paymentTerms",
                ],
                true,
            ),
            Section::new(
                "invoice.customs",
                "报关资料",
                &["supervisionMode", "customsBrokerName", "customsBrokerCode"],
                false,
            ),
        ],
        3 => vec![
            Section::new("invoice.profit", "利润计算", &["exchangeRate"], true),
            Section::new(
                "invoice.credit",
                "信用证信息",
                &["letterOfCreditNo", "issuingBank", "letterOfCreditContent"],
                false,
            ),
        ],
        _ => vec![],
    }
}

pub fn payment(tab: i32) -> Vec<Section> {
    match tab {
        0 => vec![
            Section::new(
                "payment.basic",
                "基本信息",
                &[
                    "voucherNo",
                    "invoiceNo",
                    "paymentDate",
                    "receiptDate",
                    "payeeId",
                    "payeeName",
                    "payerName",
                    "paymentMethod",
                ],
                true,
            ),
            Section::new(
                "payment.bank",
                "银行与账号",
                &["bankName", "accountNo"],
                false,
            ),
            Section::new("payment.notes", "备注", &["notes"], false),
        ],
        1 => vec![
            Section::new(
                "payment.business",
                "业务信息",
                &[
                    "goodsName",
                    "quantity",
                    "quantityUnit",
                    "tradeMethod",
                    "taxRebateRate",
                    "department",
                    "project",
                    "shipmentCountry",
                    "shipmentDate",
                ],
                true,
            ),
            Section::new(
                "payment.spares",
                "备用字段",
                &[
                    "spare1", "spare2", "spare3", "spare4", "spare5", "spare6", "spare7", "spare8",
                    "spare9", "spare10",
                ],
                false,
            ),
        ],
        2 => vec![
            Section::new(
                "payment.amounts",
                "付款金额",
                &["usdAmount", "cnyAmount"],
                true,
            ),
            Section::new(
                "payment.expenses",
                "费用明细",
                &[
                    "travelExpense",
                    "businessEntertainmentExpense",
                    "telephoneExpense",
                    "officeExpense",
                    "repairExpense",
                    "freightMiscExpense",
                    "inspectionExpense",
                    "otherExpense",
                ],
                true,
            ),
        ],
        _ => vec![],
    }
}

pub fn record(resource: &str) -> Vec<Section> {
    match resource {
        "products" => vec![
            Section::new(
                "product.basic",
                "商品资料",
                &[
                    "productCode",
                    "nameEN",
                    "nameCN",
                    "description",
                    "brand",
                    "material",
                    "origin",
                ],
                true,
            ),
            Section::new(
                "product.pricing",
                "计价与单位",
                &["defaultPrice", "unitEN", "unitCN", "taxRebateRate"],
                true,
            ),
            Section::new(
                "product.package",
                "包装与尺寸",
                &[
                    "packageUnitEN",
                    "packageUnitCN",
                    "pcsPerCtn",
                    "length",
                    "width",
                    "height",
                    "gwPerCtn",
                    "nwPerCtn",
                ],
                false,
            ),
            Section::new(
                "product.customs",
                "申报资料",
                &[
                    "hsCode",
                    "elements",
                    "supervisionConditions",
                    "inspectionCategory",
                ],
                false,
            ),
        ],
        "companies" => vec![Section::new(
            "organization.company",
            "公司信息",
            &["code", "name", "isActive"],
            true,
        )],
        "departments" => vec![
            Section::new(
                "organization.department",
                "部门信息",
                &["code", "name", "parentCode", "isActive"],
                true,
            ),
            Section::new(
                "organization.manager",
                "部门负责人",
                &["managerEmployeeId"],
                true,
            ),
        ],
        "customers" => vec![
            Section::new(
                "customer.basic",
                "客户信息",
                &[
                    "customerNameEN",
                    "customerNameCN",
                    "country",
                    "contactPerson",
                    "phone",
                    "email",
                ],
                true,
            ),
            Section::new(
                "customer.address",
                "地址与通知人",
                &[
                    "addressEN",
                    "addressCN",
                    "notifyPartyMode",
                    "notifyPartyName",
                    "notifyPartyAddress",
                ],
                false,
            ),
        ],
        "exporters" => vec![
            Section::new(
                "exporter.basic",
                "出口商信息",
                &[
                    "exporterNameEN",
                    "exporterNameCN",
                    "contactPerson",
                    "phone",
                    "email",
                ],
                true,
            ),
            Section::new(
                "exporter.address",
                "地址与登记代码",
                &["addressEN", "addressCN", "creditCode", "customsCode"],
                false,
            ),
            Section::new(
                "exporter.bank",
                "银行信息",
                &["bankName", "bankAccount", "swiftCode"],
                false,
            ),
        ],
        "payees" => vec![
            Section::new(
                "payee.basic",
                "收款方信息",
                &["name", "category", "bankName"],
                true,
            ),
            Section::new(
                "payee.accounts",
                "人民币与美金账号",
                &["rmbAccount", "usdAccount", "bankAddress", "swiftCode"],
                false,
            ),
        ],
        "crm-customers" => vec![
            Section::new(
                "crm.basic",
                "客户信息",
                &["name", "countryRegion", "website", "source", "status"],
                true,
            ),
            Section::new(
                "crm.linked",
                "关联单证客户",
                &["linkedDocumentCustomerId"],
                false,
            ),
            Section::new("crm.notes", "备注", &["notes"], false),
        ],
        "crm-contacts" | "supplier-contacts" => vec![Section::new(
            "contact.details",
            "联系人",
            &[
                "crmCustomerId",
                "supplierCompanyId",
                "name",
                "title",
                "email",
                "phone",
                "instantMessaging",
                "isPrimary",
            ],
            true,
        )],
        "crm-follow-ups" => vec![Section::new(
            "followup.details",
            "跟进记录",
            &[
                "crmCustomerId",
                "crmContactId",
                "type",
                "summary",
                "followedUpAt",
                "nextAction",
                "nextFollowUpAt",
            ],
            true,
        )],
        "supplier-products" => vec![Section::new(
            "supplier.product",
            "供应产品",
            &[
                "supplierCompanyId",
                "productId",
                "supplierProductCode",
                "referencePrice",
                "currency",
                "leadTimeDays",
                "status",
            ],
            true,
        )],
        "supplier-assessments" => vec![
            Section::new(
                "supplier.assessment",
                "评价信息",
                &[
                    "supplierCompanyId",
                    "assessmentDate",
                    "assessmentKind",
                    "conclusion",
                    "status",
                ],
                true,
            ),
            Section::new(
                "supplier.scores",
                "四项评分（1 至 5 分）",
                &[
                    "qualityScore",
                    "deliveryScore",
                    "serviceScore",
                    "priceScore",
                    "averageScore",
                ],
                true,
            ),
            Section::new(
                "supplier.assessment-notes",
                "评价备注",
                &["notes", "assessedBy", "confirmedBy", "confirmedAt"],
                false,
            ),
        ],
        "suppliers" => vec![
            Section::new(
                "supplier.basic",
                "供应商信息",
                &[
                    "name",
                    "category",
                    "countryRegion",
                    "contactPerson",
                    "phone",
                    "email",
                ],
                true,
            ),
            Section::new(
                "supplier.details",
                "资质与供货资料",
                &[
                    "address",
                    "website",
                    "mainProducts",
                    "creditCode",
                    "paymentTerms",
                    "currency",
                    "bankName",
                    "bankAccount",
                ],
                false,
            ),
            Section::new("supplier.notes", "备注", &["notes"], false),
        ],
        "people" => vec![
            Section::new(
                "person.basic",
                "基本资料",
                &[
                    "employeeNumber",
                    "departmentId",
                    "jobTitle",
                    "employmentType",
                    "hireDate",
                    "onProbation",
                    "profile.fullName",
                    "profile.gender",
                    "profile.birthDate",
                    "profile.workPhone",
                    "profile.workEmail",
                ],
                true,
            ),
            Section::new(
                "person.contact",
                "个人联系与紧急联系人",
                &[
                    "profile.phone",
                    "profile.personalPhone",
                    "profile.personalEmail",
                    "profile.address",
                    "profile.emergencyContactName",
                    "profile.emergencyContactPhone",
                ],
                false,
            ),
            Section::new(
                "person.identity",
                "证件资料",
                &[
                    "profile.identityNumber",
                    "profile.identityIssuingAuthority",
                    "profile.identityAddress",
                    "profile.identityValidFrom",
                    "profile.identityValidUntil",
                    "profile.identityLongTerm",
                ],
                false,
            ),
        ],
        _ => vec![],
    }
}
