//! Business resources and their operations. Routes and schemas remain generated.
use crate::generated_api::*;

#[derive(Clone, Copy, Debug)]
pub struct Resource {
    pub key: &'static str,
    pub title: &'static str,
    pub permission: &'static str,
    pub list: Operation,
    pub create: Operation,
    pub update: Operation,
    pub get: Option<Operation>,
    pub delete: Option<Operation>,
    pub identity: &'static str,
    pub schema: &'static str,
    pub result_field: &'static str,
    pub columns: &'static [(&'static str, &'static str)],
}

macro_rules! resource {
    ($key:literal,$title:literal,$permission:literal,$list:ident,$create:ident,$update:ident,$get:expr,$delete:expr,$identity:literal,$schema:literal,$result:literal,[$(($field:literal,$label:literal)),*]) => {
        Resource { key:$key,title:$title,permission:$permission,list:$list,create:$create,update:$update,get:$get,delete:$delete,identity:$identity,schema:$schema,result_field:$result,columns:&[$(($field,$label)),*] }
    };
}

pub const RESOURCES: &[Resource] = &[
    resource!(
        "invoices",
        "发票管理",
        "document.invoices",
        LIST_INVOICES,
        CREATE_INVOICE,
        UPDATE_INVOICE,
        Some(GET_INVOICE),
        Some(DELETE_INVOICE),
        "invoiceNo",
        "ApiInvoiceDetailDto",
        "invoice",
        [
            ("invoiceNo", "发票号"),
            ("invoiceDate", "发票日期"),
            ("customerNameEN", "客户"),
            ("currency", "币种"),
            ("totalAmount", "金额"),
            ("status", "状态")
        ]
    ),
    resource!(
        "payments",
        "付款报销",
        "document.payments",
        LIST_PAYMENTS,
        CREATE_PAYMENT,
        UPDATE_PAYMENT,
        Some(GET_PAYMENT),
        Some(DELETE_PAYMENT),
        "",
        "ApiPaymentDto",
        "payment",
        [
            ("voucherNo", "凭证号"),
            ("invoiceNo", "发票号"),
            ("payeeName", "收款人"),
            ("paymentDate", "付款日期"),
            ("usdAmount", "美元金额"),
            ("cnyAmount", "人民币金额")
        ]
    ),
    resource!(
        "customers",
        "客户",
        "document.master-data",
        LIST_CUSTOMERS_PAGE,
        CREATE_CUSTOMER,
        UPDATE_CUSTOMER,
        Some(GET_CUSTOMER),
        Some(DELETE_CUSTOMER),
        "customerNameEN",
        "ApiCustomerDto",
        "",
        [
            ("customerNameEN", "英文名称"),
            ("customerNameCN", "中文名称"),
            ("country", "国家地区"),
            ("contactPerson", "联系人"),
            ("email", "邮箱")
        ]
    ),
    resource!(
        "exporters",
        "出口商",
        "document.master-data",
        LIST_EXPORTERS_PAGE,
        CREATE_EXPORTER,
        UPDATE_EXPORTER,
        Some(GET_EXPORTER),
        Some(DELETE_EXPORTER),
        "exporterNameEN",
        "ApiExporterDto",
        "",
        [
            ("exporterNameEN", "英文名称"),
            ("exporterNameCN", "中文名称"),
            ("creditCode", "信用代码"),
            ("addressEN", "地址")
        ]
    ),
    resource!(
        "payees",
        "收款人",
        "document.master-data",
        LIST_PAYEES_PAGE,
        CREATE_PAYEE,
        UPDATE_PAYEE,
        Some(GET_PAYEE),
        Some(DELETE_PAYEE),
        "",
        "ApiPayeeDto",
        "",
        [
            ("name", "名称"),
            ("bankName", "银行"),
            ("accountNo", "账号")
        ]
    ),
    resource!(
        "units",
        "单位",
        "document.master-data",
        LIST_UNITS_PAGE,
        CREATE_UNIT,
        UPDATE_UNIT,
        Some(GET_UNIT),
        Some(DELETE_UNIT),
        "nameEN",
        "ApiUnitDto",
        "",
        [("nameEN", "英文单位"), ("nameCN", "中文单位")]
    ),
    resource!(
        "products",
        "商品",
        "document.master-data",
        LIST_PRODUCTS,
        CREATE_PRODUCT,
        UPDATE_PRODUCT,
        Some(GET_PRODUCT),
        Some(DELETE_PRODUCT),
        "productCode",
        "ApiProductDto",
        "",
        [
            ("productCode", "商品编码（款号）"),
            ("nameEN", "英文品名"),
            ("nameCN", "中文品名"),
            ("hsCode", "HS 编码"),
            ("brand", "品牌")
        ]
    ),
    resource!(
        "ports",
        "港口",
        "document.master-data",
        LIST_PORTS_PAGE,
        CREATE_PORT,
        UPDATE_PORT,
        Some(GET_PORT),
        Some(DELETE_PORT),
        "nameEN",
        "ApiPortDto",
        "",
        [
            ("nameEN", "英文港口"),
            ("nameCN", "中文港口"),
            ("country", "国家地区")
        ]
    ),
    resource!(
        "hs-codes",
        "HS 编码知识",
        "document.hs-knowledge",
        LIST_HS_CODES,
        CREATE_HS_CODE,
        UPDATE_HS_CODE,
        Some(GET_HS_CODE),
        Some(DELETE_HS_CODE),
        "code",
        "ApiHsCodeDto",
        "",
        [
            ("code", "HS 编码"),
            ("name", "商品名称"),
            ("unit", "计量单位"),
            ("status", "状态"),
            ("effectiveYear", "生效年份")
        ]
    ),
    resource!(
        "crm-customers",
        "客户与跟进",
        "sales.customers",
        QUERY_CRM_CUSTOMERS,
        CREATE_CRM_CUSTOMER,
        UPDATE_CRM_CUSTOMER,
        Some(GET_CRM_CUSTOMER),
        Some(DELETE_CRM_CUSTOMER),
        "",
        "ApiCrmCustomerDto",
        "",
        [
            ("name", "客户名称"),
            ("countryRegion", "国家地区"),
            ("source", "客户来源"),
            ("status", "状态"),
            ("nextFollowUpAt", "下次跟进")
        ]
    ),
    resource!(
        "crm-contacts",
        "客户联系人",
        "sales.contacts",
        QUERY_CRM_CONTACTS,
        CREATE_CRM_CONTACT,
        UPDATE_CRM_CONTACT,
        None,
        Some(DELETE_CRM_CONTACT),
        "",
        "ApiCrmContactDto",
        "",
        [
            ("name", "姓名"),
            ("title", "职务"),
            ("email", "邮箱"),
            ("phone", "电话"),
            ("isPrimary", "主要联系人")
        ]
    ),
    resource!(
        "crm-follow-ups",
        "跟进记录",
        "sales.follow-ups",
        QUERY_CRM_FOLLOW_UPS,
        CREATE_CRM_FOLLOW_UP,
        UPDATE_CRM_FOLLOW_UP,
        None,
        Some(DELETE_CRM_FOLLOW_UP),
        "",
        "ApiCrmFollowUpDto",
        "",
        [
            ("summary", "沟通摘要"),
            ("type", "跟进方式"),
            ("followedUpAt", "跟进时间"),
            ("nextAction", "下一步"),
            ("isCompleted", "已完成")
        ]
    ),
    resource!(
        "opportunities",
        "商机与报价",
        "sales.opportunities",
        QUERY_SALES_OPPORTUNITIES,
        CREATE_SALES_OPPORTUNITY,
        UPDATE_SALES_OPPORTUNITY,
        Some(GET_SALES_OPPORTUNITY),
        None,
        "",
        "ApiSalesOpportunityDto",
        "",
        [
            ("title", "商机名称"),
            ("quotationNo", "报价单号"),
            ("stage", "销售阶段"),
            ("currency", "币种"),
            ("estimatedAmount", "预计金额"),
            ("expectedCloseDate", "预计成交")
        ]
    ),
    resource!(
        "suppliers",
        "供应商管理",
        "sales.suppliers",
        QUERY_SUPPLIERS,
        CREATE_SUPPLIER,
        UPDATE_SUPPLIER,
        Some(GET_SUPPLIER),
        Some(DELETE_SUPPLIER),
        "",
        "ApiSupplierDto",
        "",
        [
            ("name", "供应商名称"),
            ("countryRegion", "国家地区"),
            ("category", "分类"),
            ("mainProducts", "主营产品"),
            ("status", "状态")
        ]
    ),
    resource!(
        "supplier-contacts",
        "供应商联系人",
        "sales.supplier-contacts",
        QUERY_SUPPLIER_CONTACTS,
        CREATE_SUPPLIER_CONTACT,
        UPDATE_SUPPLIER_CONTACT,
        None,
        Some(DELETE_SUPPLIER_CONTACT),
        "",
        "ApiSupplierContactDto",
        "",
        [
            ("name", "姓名"),
            ("title", "职务"),
            ("email", "邮箱"),
            ("phone", "电话")
        ]
    ),
    resource!(
        "supplier-products",
        "供应产品",
        "sales.supplier-product-links",
        QUERY_SUPPLIER_PRODUCT_LINKS,
        CREATE_SUPPLIER_PRODUCT_LINK,
        UPDATE_SUPPLIER_PRODUCT_LINK,
        None,
        Some(DELETE_SUPPLIER_PRODUCT_LINK),
        "",
        "ApiSupplierProductLinkDto",
        "",
        [
            ("productCode", "商品"),
            ("supplierProductCode", "供应商款号"),
            ("referencePrice", "参考单价"),
            ("currency", "币种"),
            ("status", "状态")
        ]
    ),
    resource!(
        "supplier-assessments",
        "供应商评价",
        "sales.supplier-assessments",
        LIST_SUPPLIER_ASSESSMENTS,
        CREATE_SUPPLIER_ASSESSMENT,
        UPDATE_SUPPLIER_ASSESSMENT,
        None,
        Some(DELETE_SUPPLIER_ASSESSMENT),
        "",
        "ApiSupplierAssessmentDto",
        "",
        [
            ("assessmentDate", "评价日期"),
            ("qualityScore", "质量评分"),
            ("deliveryScore", "交期评分"),
            ("notes", "评价说明"),
            ("status", "状态")
        ]
    ),
    resource!(
        "people",
        "人员档案",
        "office.people",
        LIST_PERSONNEL,
        CREATE_PERSONNEL,
        UPDATE_PERSONNEL,
        Some(GET_PERSONNEL),
        Some(DELETE_PERSONNEL),
        "employeeNumber",
        "PersonnelRecord",
        "",
        [
            ("employeeNumber", "工号"),
            ("fullName", "姓名"),
            ("departmentId", "部门"),
            ("jobTitle", "岗位"),
            ("status", "任职状态")
        ]
    ),
    resource!(
        "rooms",
        "会议室管理",
        "office.rooms",
        LIST_MEETING_ROOMS,
        CREATE_MEETING_ROOM,
        UPDATE_MEETING_ROOM,
        None,
        Some(DELETE_MEETING_ROOM),
        "name",
        "MeetingRoomRecord",
        "",
        [
            ("name", "会议室"),
            ("location", "位置"),
            ("capacity", "容量"),
            ("equipment", "设备"),
            ("isActive", "启用")
        ]
    ),
    resource!(
        "bookings",
        "会议室预约",
        "office.rooms",
        LIST_MEETING_BOOKINGS,
        CREATE_MEETING_BOOKING,
        UPDATE_MEETING_BOOKING,
        None,
        None,
        "requestKey",
        "MeetingBookingRecord",
        "",
        [
            ("title", "会议主题"),
            ("meetingRoomName", "会议室"),
            ("applicantName", "申请人"),
            ("startsAt", "开始时间"),
            ("endsAt", "结束时间"),
            ("status", "状态")
        ]
    ),
    resource!(
        "supplies",
        "物品管理",
        "office.supplies",
        LIST_OFFICE_SUPPLIES,
        CREATE_OFFICE_SUPPLY,
        UPDATE_OFFICE_SUPPLY,
        None,
        Some(DELETE_OFFICE_SUPPLY),
        "name",
        "OfficeSupplyRecord",
        "",
        [
            ("name", "物品名称"),
            ("unit", "单位"),
            ("stockQuantity", "库存"),
            ("reservedQuantity", "已预留"),
            ("minimumStock", "最低库存")
        ]
    ),
    resource!(
        "supply-requests",
        "物品领用",
        "office.supplies",
        LIST_OFFICE_SUPPLY_REQUESTS,
        CREATE_OFFICE_SUPPLY_REQUEST,
        UPDATE_OFFICE_SUPPLY_REQUEST,
        None,
        None,
        "requestKey",
        "OfficeSupplyRequestRecord",
        "",
        [
            ("officeSupplyName", "物品"),
            ("applicantName", "领用人"),
            ("quantity", "数量"),
            ("purpose", "用途"),
            ("returnDueDate", "应还日期"),
            ("status", "状态")
        ]
    ),
    resource!(
        "email-templates",
        "邮件模板",
        "sales.email-templates",
        LIST_EMAIL_TEMPLATES,
        CREATE_EMAIL_TEMPLATE,
        SAVE_EMAIL_TEMPLATE_DRAFT,
        None,
        None,
        "",
        "ApiEmailTemplateDto",
        "",
        [
            ("name", "模板名称"),
            ("category", "分类"),
            ("subject", "邮件主题"),
            ("status", "状态")
        ]
    ),
    resource!(
        "report-templates",
        "报表模板管理",
        "document.report-templates",
        LIST_USER_REPORT_TEMPLATES,
        CREATE_USER_REPORT_TEMPLATE,
        SAVE_USER_REPORT_TEMPLATE_DRAFT,
        Some(GET_USER_REPORT_TEMPLATE),
        None,
        "",
        "ApiUserReportTemplateDto",
        "",
        [
            ("name", "模板名称"),
            ("reportType", "报表类型"),
            ("status", "状态"),
            ("versionNumber", "版本")
        ]
    ),
    resource!(
        "container-projects",
        "装柜方案",
        "document.container-packing",
        LIST_CONTAINER_PACKING_PROJECTS,
        SAVE_CONTAINER_PACKING_PROJECT,
        SAVE_CONTAINER_PACKING_PROJECT,
        Some(GET_CONTAINER_PACKING_PROJECT),
        Some(DELETE_CONTAINER_PACKING_PROJECT),
        "",
        "ApiContainerPackingProjectDto",
        "project",
        [
            ("name", "方案名称"),
            ("containerType", "柜型"),
            ("updatedAt", "更新时间")
        ]
    ),
    resource!(
        "users",
        "账号与权限",
        "system.users",
        LIST_USERS,
        CREATE_USER_ACCOUNT,
        UPDATE_USER_ACCOUNT,
        None,
        Some(DELETE_USER_ACCOUNT),
        "username",
        "ApiUserAccountDto",
        "user",
        [
            ("username", "账号"),
            ("fullName", "姓名"),
            ("role", "角色"),
            ("departmentId", "部门"),
            ("isActive", "启用")
        ]
    ),
    resource!(
        "permission-templates",
        "权限方案",
        "system.permissions",
        LIST_PERMISSION_TEMPLATES,
        CREATE_PERMISSION_TEMPLATE,
        UPDATE_PERMISSION_TEMPLATE,
        None,
        Some(DELETE_PERMISSION_TEMPLATE),
        "code",
        "ApiPermissionTemplateDto",
        "",
        [
            ("code", "方案代码"),
            ("name", "方案名称"),
            ("description", "说明"),
            ("isActive", "启用")
        ]
    ),
    resource!(
        "companies",
        "公司目录",
        "system.users",
        GET_ORGANIZATION_DIRECTORY,
        CREATE_ORGANIZATION_COMPANY,
        UPDATE_ORGANIZATION_COMPANY,
        None,
        Some(DELETE_ORGANIZATION_COMPANY),
        "code",
        "ApiOrganizationCompanyDto",
        "",
        [
            ("code", "公司代码"),
            ("name", "公司名称"),
            ("isActive", "启用")
        ]
    ),
    resource!(
        "departments",
        "部门目录",
        "system.users",
        GET_ORGANIZATION_DIRECTORY,
        CREATE_ORGANIZATION_DEPARTMENT,
        UPDATE_ORGANIZATION_DEPARTMENT,
        None,
        Some(DELETE_ORGANIZATION_DEPARTMENT),
        "code",
        "ApiOrganizationDepartmentDto",
        "",
        [
            ("code", "部门代码"),
            ("name", "部门名称"),
            ("parentCode", "上级部门"),
            ("companyCode", "所属公司"),
            ("isActive", "启用")
        ]
    ),
    resource!(
        "attachment-categories",
        "资料分类",
        "document.invoices",
        LIST_BUSINESS_ATTACHMENT_CATEGORIES,
        CREATE_BUSINESS_ATTACHMENT_CATEGORY,
        UPDATE_BUSINESS_ATTACHMENT_CATEGORY,
        None,
        Some(DELETE_BUSINESS_ATTACHMENT_CATEGORY),
        "name",
        "BusinessAttachmentCategoryRecord",
        "",
        [("name", "分类名称"), ("versionNumber", "版本")]
    ),
];

pub fn resource(key: &str) -> Option<&'static Resource> {
    RESOURCES.iter().find(|resource| resource.key == key)
}
pub fn operation(id: &str) -> Option<Operation> {
    ALL_OPERATIONS
        .iter()
        .find(|operation| operation.id == id)
        .copied()
}
pub fn find_resource_operation(operation: Operation) -> bool {
    RESOURCES.iter().any(|resource| {
        [
            Some(resource.list),
            Some(resource.create),
            Some(resource.update),
            resource.get,
            resource.delete,
        ]
        .contains(&Some(operation))
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn all_native_resources_use_generated_schemas() {
        for resource in RESOURCES {
            assert!(
                crate::contracts::schema(resource.schema).is_object(),
                "{}: {}",
                resource.key,
                resource.schema
            );
            assert!(
                !crate::contracts::request(resource.create.id).is_null(),
                "{}",
                resource.create.id
            );
        }
    }
}
