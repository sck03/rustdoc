use crate::{contracts, engine::catalog, generated_api::*};
use serde_json::{Value, json};
use std::collections::BTreeMap;

#[derive(Clone, Copy)]
pub struct NavItem {
    pub key: &'static str,
    pub label: &'static str,
    pub description: &'static str,
}
pub struct NavGroup {
    pub key: &'static str,
    pub label: &'static str,
    pub items: &'static [NavItem],
}
macro_rules! item {
    ($key:literal,$label:literal,$description:literal) => {
        NavItem {
            key: $key,
            label: $label,
            description: $description,
        }
    };
}
pub const NAVIGATION: &[NavGroup] = &[
    NavGroup {
        key: "workspace",
        label: "工作台",
        items: &[
            item!(
                "worklist",
                "我的待办",
                "查看需要办理的业务事项，进入对应业务继续处理"
            ),
            item!("dashboard", "工作概览", "查看业务金额、近期单据与单证进度"),
            item!(
                "sales-dashboard",
                "销售概览",
                "查看客户、商机和近期跟进情况"
            ),
            item!("jobs", "文件任务", "查看导入、导出和报表处理进度"),
        ],
    },
    NavGroup {
        key: "customers",
        label: "客户与供应链",
        items: &[
            item!(
                "crm-customers",
                "客户与跟进",
                "维护销售客户，记录沟通和下一步跟进"
            ),
            item!(
                "opportunities",
                "商机与报价",
                "跟踪销售阶段、报价金额和下一步动作"
            ),
            item!("suppliers", "供应商管理", "维护供应商、联系人、产品和评价"),
            item!("email", "邮件中心", "发送业务邮件，查询投递记录并管理模板"),
        ],
    },
    NavGroup {
        key: "documents",
        label: "单证与申报",
        items: &[
            item!("invoices", "发票管理", "新建和维护出口发票，核对并输出单据"),
            item!(
                "query",
                "统计查询",
                "按日期、客户和商品检索业务明细，汇总并导出数据"
            ),
            item!("payments", "付款报销", "维护付款、费用和报销记录并生成凭证"),
            item!(
                "attachments",
                "业务资料",
                "归档原始资料、确认文件与交付文件，查看历史版本"
            ),
            item!(
                "single-window",
                "单一窗口",
                "准备申报资料，处理交接批次与回执"
            ),
            item!(
                "hs-codes",
                "HS 编码知识",
                "查询和维护税则编码、归类与申报经验"
            ),
        ],
    },
    NavGroup {
        key: "office",
        label: "公司行政",
        items: &[
            item!("people", "人员档案", "查找人员，办理入职、转正、调岗和离职"),
            item!(
                "directory",
                "公司通讯录",
                "查找同事的工作电话、邮箱和办公地点"
            ),
            item!(
                "bookings",
                "会议室预约",
                "查看日程，办理会议室预约与钥匙交接"
            ),
            item!(
                "supply-requests",
                "物品领用",
                "登记办公物品领用，办理发放、归还与库存补充"
            ),
        ],
    },
    NavGroup {
        key: "resources",
        label: "资料与工具",
        items: &[
            item!("ocr", "智能 OCR", "选择或粘贴图片，识别文字并查看原图位置"),
            item!(
                "pdf-merge",
                "PDF 合并",
                "按顺序合并 PDF，保留页面尺寸与矢量文字"
            ),
            item!(
                "excel",
                "Excel 工具",
                "导入 Excel 发票、导出模板和转换订舱托单"
            ),
            item!(
                "master-data",
                "基础资料",
                "维护制单使用的客户、出口商、商品、港口和单位"
            ),
            item!(
                "report-templates",
                "报表模板管理",
                "选择默认模板，维护模板文件、名称和版式"
            ),
            item!(
                "container-projects",
                "装柜规划",
                "规划装柜方案，核对货物尺寸、数量和装载情况"
            ),
            item!(
                "exchange-rates",
                "今日汇率",
                "查看常用币种汇率与业务换算口径"
            ),
        ],
    },
    NavGroup {
        key: "system",
        label: "系统管理",
        items: &[
            item!(
                "settings",
                "系统设置",
                "设置运行环境、邮件和业务参数，备份与维护数据"
            ),
            item!("users", "账号与权限", "管理登录账号、权限方案和数据范围"),
            item!("companies", "组织架构", "定义公司、部门层级和负责人"),
            item!("audit", "审计日志", "查阅和导出关键业务操作记录"),
            item!("about", "关于与支持", "查看产品信息、授权注册和软件更新"),
        ],
    },
];
pub fn navigation(key: &str) -> Option<(&'static NavGroup, &'static NavItem)> {
    NAVIGATION.iter().find_map(|group| {
        group
            .items
            .iter()
            .find(|item| item.key == key)
            .map(|item| (group, item))
    })
}
pub fn tabs(key: &str) -> Vec<(&'static str, &'static str)> {
    match key {
        "crm-customers" => vec![("crm-customers", "客户"), ("crm-follow-ups", "跟进工作台")],
        "suppliers" => vec![("suppliers", "供应商"), ("supplier-overview", "评价分析")],
        "bookings" => vec![("bookings", "预约登记"), ("rooms", "会议室管理")],
        "supply-requests" => vec![("supply-requests", "领用登记"), ("supplies", "物品管理")],
        "master-data" => vec![
            ("customers", "客户"),
            ("exporters", "出口商"),
            ("products", "商品"),
            ("payees", "收款人"),
            ("ports", "港口"),
            ("units", "单位"),
        ],
        "users" => vec![("users", "账号"), ("permission-templates", "权限方案")],
        "companies" => vec![("companies", "公司目录"), ("departments", "部门目录")],
        "email" => vec![
            ("email", "写邮件"),
            ("email-deliveries", "投递记录"),
            ("email-templates", "邮件模板"),
        ],
        "single-window" => vec![
            ("single-window", "申报业务"),
            ("coo", "原产地证"),
            ("acd", "代理委托"),
            ("sw-dictionary", "申报词典"),
        ],
        "settings" => vec![
            ("settings", "常规与业务"),
            ("backup", "备份与恢复"),
            ("runtime", "运行环境"),
        ],
        _ => vec![],
    }
}
pub fn read_operation(key: &str) -> Option<Operation> {
    catalog::resource(key)
        .map(|resource| resource.list)
        .or(match key {
            "excel" => Some(PREVIEW_EXCEL_IMPORT),
            "dashboard" => Some(GET_DASHBOARD),
            "sales-dashboard" => Some(GET_CRM_DASHBOARD),
            "supplier-overview" => Some(GET_SUPPLIER_ASSESSMENT_OVERVIEW),
            "worklist" => Some(GET_WORKLIST),
            "query" => Some(LIST_QUERIED_INVOICES),
            "jobs" => Some(LIST_JOBS),
            "directory" => Some(LIST_PERSONNEL),
            "attachments" => Some(LIST_BUSINESS_ATTACHMENTS),
            "single-window" => Some(LIST_SINGLE_WINDOW_OPERATION_CENTER),
            "ocr" => Some(RECOGNIZE_OCR_IMAGE),
            "pdf-merge" => Some(START_PDF_MERGE_SAVE_TO_PATH_JOB),
            "email" => Some(GET_EMAIL_TOOL_STATUS),
            "email-deliveries" => Some(LIST_EMAIL_DELIVERIES),
            "exchange-rates" => Some(LIST_EXCHANGE_RATES),
            "settings" => Some(GET_SETTINGS),
            "audit" => Some(LIST_AUDIT_LOGS),
            "backup" => Some(LIST_DATABASE_BACKUPS),
            "runtime" => Some(GET_HEALTH),
            _ => None,
        })
}

#[derive(Default)]
pub struct Workspace {
    pub root: &'static str,
    pub resource: &'static str,
    pub search: String,
    pub page: i64,
    pub payload: Option<Value>,
    pub editor: Option<Editor>,
    pub action: Option<ActionEditor>,
    pub lookups: BTreeMap<String, Vec<(Value, String)>>,
    pub lookup_loaded: bool,
    pub selected: Option<Value>,
    pub loaded: bool,
}
pub struct Editor {
    pub record: Value,
    pub baseline: Value,
    pub buffers: BTreeMap<String, String>,
    pub schema: &'static Value,
    pub id: i64,
    pub invalid: Option<String>,
}
pub struct ActionEditor {
    pub operation: Operation,
    pub title: String,
    pub request: Value,
    pub buffers: BTreeMap<String, String>,
    pub record: Value,
    pub invalid: Option<String>,
}
impl Workspace {
    pub fn open(&mut self, key: &'static str) {
        self.root = key;
        self.resource = tabs(key).first().map(|(key, _)| *key).unwrap_or(key);
        self.page = 1;
        self.search.clear();
        self.payload = None;
        self.editor = None;
        self.action = None;
        self.selected = None;
        self.loaded = false;
    }
    pub fn items(&self) -> Vec<Value> {
        let Some(payload) = &self.payload else {
            return vec![];
        };
        if let Some(items) = payload.as_array() {
            return items.clone();
        }
        if self.resource == "worklist" {
            return payload["page"]["items"]
                .as_array()
                .cloned()
                .unwrap_or_default();
        }
        let field = match self.resource {
            "users" => "users",
            "companies" => "companies",
            "departments" => "departments",
            "container-projects" => "projects",
            _ => "items",
        };
        payload[field].as_array().cloned().unwrap_or_default()
    }
    pub fn edit(&mut self, record: Option<Value>) -> Result<(), String> {
        let resource = catalog::resource(self.resource).ok_or("此页面没有可编辑记录。")?;
        let id = record
            .as_ref()
            .and_then(|record| record["id"].as_i64())
            .unwrap_or(0);
        let operation = if id > 0 {
            resource.update
        } else {
            resource.create
        };
        let mut value = contracts::object(operation.id, true);
        if let Some(record) = record {
            value = contracts::overlay(value, &record);
            value["expectedVersion"] = value["versionNumber"].clone();
        } else {
            if let Some(object) = value.as_object_mut() {
                if object.contains_key("requestKey") {
                    object.insert("requestKey".into(), json!(crate::paths::nonce()?));
                }
                if object.contains_key("isActive") {
                    object.insert("isActive".into(), json!(true));
                }
            }
            for (key, default) in [
                ("departmentId", json!("GENERAL")),
                ("companyCode", json!("DEFAULT")),
                ("companyScope", json!("DEFAULT")),
                ("capacity", json!(10)),
                ("maximumBookingHours", json!(8)),
                ("advanceBookingDays", json!(90)),
                ("requiresKey", json!(true)),
                ("unit", json!("件")),
                (
                    "hireDate",
                    json!(chrono::Local::now().date_naive().to_string()),
                ),
                ("employmentType", json!("FullTime")),
                ("currency", json!("USD")),
                ("role", json!("User")),
            ] {
                if value.get(key).is_some() {
                    value[key] = default;
                }
            }
        }
        self.editor = Some(Editor {
            record: value.clone(),
            baseline: value,
            buffers: BTreeMap::new(),
            schema: contracts::request(operation.id),
            id,
            invalid: None,
        });
        Ok(())
    }
}

pub fn actions(key: &str, record: &Value) -> Vec<(Operation, &'static str)> {
    let state = record["status"].as_str().unwrap_or("");
    match key {
        "invoices" => {
            let mut actions = vec![
                (CLONE_INVOICE, "复制发票"),
                (GET_CUSTOMS_COO_DOCUMENT, "原产地证"),
                (GET_AGENT_CONSIGNMENT_DOCUMENT, "代理委托"),
            ];
            if ["Verified", "Shipped", "Completed"].contains(&state) {
                actions.push((UNVERIFY_INVOICE, "撤销核对"));
            }
            if state != "Cancelled" {
                actions.push((TRANSITION_INVOICE_STATUS, "变更状态"));
            }
            actions
        }
        "crm-customers" => {
            if ["暂停", "已流失"].contains(&state) {
                vec![(RESTORE_CRM_CUSTOMER, "恢复客户")]
            } else {
                vec![(DEACTIVATE_CRM_CUSTOMER, "停用客户")]
            }
        }
        "crm-follow-ups" => {
            let mut actions = if record["isCompleted"] == true {
                vec![(RESTORE_CRM_FOLLOW_UP, "恢复跟进")]
            } else {
                vec![(COMPLETE_CRM_FOLLOW_UP, "完成跟进")]
            };
            actions.push((TRANSFER_CRM_FOLLOW_UP, "转移跟进"));
            actions
        }
        "opportunities" => vec![
            (TRANSITION_SALES_OPPORTUNITY, "变更阶段"),
            (ARCHIVE_SALES_OPPORTUNITY, "归档商机"),
        ],
        "suppliers" => match state {
            "考察中" => vec![
                (ADMIT_SUPPLIER, "供应商准入"),
                (DEACTIVATE_SUPPLIER, "停用"),
            ],
            "停用" => vec![(RESTORE_SUPPLIER, "恢复")],
            _ => vec![(DEACTIVATE_SUPPLIER, "停用")],
        },
        "crm-contacts" => vec![(SET_PRIMARY_CRM_CONTACT, "设为主要联系人")],
        "supplier-contacts" => vec![(SET_PRIMARY_SUPPLIER_CONTACT, "设为主要联系人")],
        "supplier-products" => vec![
            (DEACTIVATE_SUPPLIER_PRODUCT_LINK, "停用"),
            (RESTORE_SUPPLIER_PRODUCT_LINK, "恢复"),
        ],
        "supplier-assessments" => vec![(CONFIRM_SUPPLIER_ASSESSMENT, "确认评价")],
        "people" => match state {
            "Probation" => vec![
                (CONFIRM_PERSONNEL, "办理转正"),
                (TRANSFER_PERSONNEL, "调岗"),
                (DEPART_PERSONNEL, "离职"),
            ],
            "Departed" => vec![(REHIRE_PERSONNEL, "重新入职")],
            _ => vec![(TRANSFER_PERSONNEL, "调岗"), (DEPART_PERSONNEL, "离职")],
        },
        "bookings" => match state {
            "Approved" => vec![
                (ISSUE_MEETING_ROOM_KEY, "领取钥匙"),
                (CANCEL_MEETING_BOOKING, "取消预约"),
            ],
            "InUse" => vec![(RETURN_MEETING_ROOM_KEY, "归还钥匙")],
            "Pending" => vec![
                (APPROVE_MEETING_BOOKING, "批准"),
                (REJECT_MEETING_BOOKING, "拒绝"),
            ],
            _ => vec![],
        },
        "supplies" => vec![
            (RESTOCK_OFFICE_SUPPLY, "补充库存"),
            (STOCKTAKE_OFFICE_SUPPLY, "库存盘点"),
        ],
        "supply-requests" => match state {
            "Approved" => vec![
                (ISSUE_OFFICE_SUPPLY, "发放物品"),
                (CANCEL_OFFICE_SUPPLY_REQUEST, "取消领用"),
            ],
            "Issued" if record["isReturnable"] == true => vec![(RETURN_OFFICE_SUPPLY, "归还物品")],
            "Pending" => vec![
                (APPROVE_OFFICE_SUPPLY_REQUEST, "批准"),
                (REJECT_OFFICE_SUPPLY_REQUEST, "拒绝"),
            ],
            _ => vec![],
        },
        "report-templates" => vec![
            (PUBLISH_USER_REPORT_TEMPLATE, "发布模板"),
            (SHARE_USER_REPORT_TEMPLATE, "共享模板"),
            (CLONE_USER_REPORT_TEMPLATE, "复制模板"),
            (ARCHIVE_USER_REPORT_TEMPLATE, "归档模板"),
        ],
        "email-templates" => vec![
            (PUBLISH_EMAIL_TEMPLATE, "发布模板"),
            (SHARE_EMAIL_TEMPLATE, "共享模板"),
            (ARCHIVE_EMAIL_TEMPLATE, "归档模板"),
        ],
        _ => vec![],
    }
}

pub fn display(value: &Value) -> String {
    match value {
        Value::Null => String::new(),
        Value::String(value) => match value.as_str() {
            "Draft" => "草稿",
            "Verified" => "已核对",
            "Shipped" => "已出运",
            "Completed" => "已完成",
            "Cancelled" => "已取消",
            "Approved" => "已批准",
            "Pending" => "待办理",
            "Rejected" => "已拒绝",
            "InUse" => "使用中",
            "Issued" => "已发放",
            "Returned" => "已归还",
            "Probation" => "试用期",
            "Active" => "在职",
            "Departed" => "已离职",
            "Published" => "已发布",
            "Shared" => "已共享",
            "Disabled" => "已停用",
            "Archived" => "已归档",
            "ReferenceOnly" => "待核实",
            _ => value,
        }
        .into(),
        Value::Bool(value) => if *value { "是" } else { "否" }.into(),
        Value::Number(value) => value.to_string(),
        Value::Array(values) => format!("{} 项", values.len()),
        Value::Object(_) => "详细资料".into(),
    }
}
