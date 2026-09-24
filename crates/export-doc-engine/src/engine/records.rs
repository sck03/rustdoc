use super::{
    auth,
    catalog::{RESOURCES, Resource},
    error::{Result, conflict, error, invalid, unavailable},
    store::{self, Actor, Store},
};
use crate::{contracts, generated_api::Operation, invoice};
use export_doc_storage::{AuditWrite, Connection};
use serde_json::{Value, json};
use unicode_normalization::UnicodeNormalization;

pub fn find(operation: Operation) -> Option<&'static Resource> {
    RESOURCES.iter().find(|resource| {
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
pub fn id(parameters: &[(&str, String)]) -> Result<i64> {
    parameters
        .iter()
        .find(|(key, _)| *key == "id")
        .or_else(|| {
            parameters.iter().find(|(key, _)| {
                [
                    "id",
                    "customerId",
                    "supplierId",
                    "employeeId",
                    "roomId",
                    "supplyId",
                    "invoiceId",
                    "paymentId",
                ]
                .contains(key)
            })
        })
        .and_then(|(_, value)| value.parse::<i64>().ok())
        .filter(|id| *id > 0)
        .ok_or_else(|| invalid("记录编号必须大于零。"))
}

fn addressed_id(
    store: &Store,
    resource: &Resource,
    operation: Operation,
    parameters: &[(&str, String)],
) -> Result<i64> {
    if operation.path.contains("{code}") {
        let code = parameters
            .iter()
            .find(|(key, _)| *key == "code")
            .map(|(_, value)| store::normalize(value))
            .filter(|value| !value.is_empty())
            .ok_or_else(|| invalid("缺少记录代码。"))?;
        return store
            .all(resource.key)?
            .iter()
            .find(|record| store::normalize(&text(record, resource.identity)) == code)
            .and_then(|record| record["id"].as_i64())
            .ok_or_else(|| error(404, "记录不存在。"));
    }
    id(parameters)
}
pub fn field<'a>(body: &'a Value, path: &str) -> &'a Value {
    path.split('.').fold(body, |value, key| &value[key])
}
pub fn text(body: &Value, path: &str) -> String {
    field(body, path).as_str().unwrap_or("").trim().to_owned()
}
pub fn required(body: &Value, path: &str, label: &str, max: usize) -> Result<()> {
    let text = text(body, path);
    if text.is_empty() || text.chars().count() > max {
        Err(invalid(format!("{label}须为 1–{max} 字。")))
    } else {
        Ok(())
    }
}
pub fn positive(body: &Value, key: &str, label: &str) -> Result<i64> {
    body[key]
        .as_i64()
        .filter(|value| *value > 0)
        .ok_or_else(|| invalid(format!("{label}必须大于零。")))
}
pub fn parent(connection: &Connection, actor: &Actor, kind: &str, id: i64) -> Result<Value> {
    let record = store::get(connection, kind, id)?;
    if !actor.admin && record["companyScope"] != actor.company {
        return Err(error(403, "不能引用其他公司的记录。"));
    }
    Ok(record)
}
fn read_permission(resource: &Resource) -> &str {
    if resource.permission == "document.master-data" {
        if resource.key == "products" {
            "common.product-reference"
        } else {
            "document.reference-data"
        }
    } else {
        resource.permission
    }
}

pub fn list(
    store: &Store,
    actor: &Actor,
    resource: &Resource,
    operation: Operation,
    query: &[(&str, String)],
    parameters: &[(&str, String)],
) -> Result<Value> {
    let action = "view";
    let permission = read_permission(resource);
    auth::authorize(actor, permission, action)?;
    if matches!(
        resource.key,
        "rooms" | "supplies" | "bookings" | "supply-requests"
    ) {
        return super::office_queries::list(store, actor, resource.key, query);
    }
    let mut records = if resource.key == "departments" {
        super::organization::departments(store)?
    } else {
        store.all(resource.key)?
    };
    records.retain(|record| auth::visible(actor, permission, action, record));
    for (key, value) in parameters.iter().chain(query.iter()) {
        if [
            "crmCustomerId",
            "customerId",
            "supplierId",
            "employeeId",
            "meetingRoomId",
            "officeSupplyId",
            "departmentId",
        ]
        .contains(key)
            && !value.is_empty()
        {
            let field = if *key == "customerId" {
                "crmCustomerId"
            } else {
                key
            };
            records.retain(|record| {
                record.get(field).is_some_and(|actual| {
                    actual.as_str().is_some_and(|actual| actual == value)
                        || actual
                            .as_i64()
                            .is_some_and(|actual| actual.to_string() == *value)
                })
            });
        }
    }
    if resource.key == "invoices" {
        for record in &mut records {
            record["customerName"] = record["customerNameEN"].clone();
            record["exporterName"] = record["exporterNameEN"].clone();
        }
    }
    if resource.key == "people" {
        records = records
            .into_iter()
            .map(|record| {
                let mut employee = record["employee"].clone();
                employee["canViewDetails"] = json!(auth::visible(
                    actor,
                    resource.permission,
                    "view-details",
                    &record
                ));
                employee
            })
            .collect();
    }
    let shape = contracts::resolve(contracts::response(operation.id));
    if contracts::kind(shape) == "array" {
        return Ok(json!(records));
    }
    let catalog_records =
        matches!(resource.key, "users" | "permission-templates").then(|| records.clone());
    let page = store::paged(records, query);
    if shape["properties"].get("items").is_some() {
        return Ok(page);
    }
    let mut result = contracts::object(operation.id, false);
    let field = if resource.key == "users" {
        "users"
    } else if resource.key == "container-projects" {
        "projects"
    } else if resource.key == "permission-templates" {
        "templates"
    } else {
        "items"
    };
    result[field] = catalog_records
        .map(Value::Array)
        .unwrap_or_else(|| page["items"].clone());
    if resource.key == "users" {
        result["roles"] = json!(
            export_doc_domain::permissions::catalog()
                .roles
                .iter()
                .map(|role| role.code.clone())
                .collect::<Vec<_>>()
        );
        result["companies"] = json!(store.all("companies")?);
        result["departments"] = json!(super::organization::departments(store)?);
        result["permissionTemplates"] = json!(store.all("permission-templates")?);
        let templates = store.all("permission-templates")?;
        for user in result["users"].as_array_mut().into_iter().flatten() {
            let template = user["permissionTemplateId"]
                .as_i64()
                .and_then(|id| templates.iter().find(|template| template["id"] == id));
            user["permissionTemplateCode"] = template
                .map(|value| value["code"].clone())
                .unwrap_or(json!(""));
            user["permissionTemplateName"] = template
                .map(|value| value["name"].clone())
                .unwrap_or(json!(""));
        }
    }
    if resource.key == "permission-templates" {
        result["templates"] = Value::Array(
            result["templates"]
                .as_array()
                .into_iter()
                .flatten()
                .cloned()
                .map(super::permission_templates::project)
                .collect::<Result<Vec<_>>>()?,
        );
        result["resources"] = contracts::contract()["permissions"]["resources"].clone();
        for resource in result["resources"].as_array_mut().into_iter().flatten() {
            for action in resource["actions"].as_array_mut().into_iter().flatten() {
                action["presetLevel"] = action["navigationAccessLevel"].clone();
            }
        }
        result["dataScopes"] = json!(["own", "department", "company", "all"]);
        result["accessLevels"] = json!(["view", "operate", "manage"]);
        result["applyPolicy"] = json!("权限变更后原会话失效；依赖动作由服务端计算。");
    }
    Ok(result)
}

pub fn handle(
    clock: &crate::clock::BusinessClock,
    store: &Store,
    actor: &Actor,
    resource: &Resource,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    body: Option<Value>,
) -> Result<Value> {
    if operation == resource.list {
        return list(store, actor, resource, operation, query, parameters);
    }
    if resource.get == Some(operation) {
        let permission = read_permission(resource);
        auth::authorize(
            actor,
            permission,
            if resource.key == "people" {
                "view-details"
            } else {
                "view"
            },
        )?;
        let value = store.get(
            resource.key,
            addressed_id(store, resource, operation, parameters)?,
        )?;
        if !auth::visible(
            actor,
            permission,
            if resource.key == "people" {
                "view-details"
            } else {
                "view"
            },
            &value,
        ) {
            return Err(error(403, "没有查看此记录的权限。"));
        }
        return Ok(value);
    }
    if resource.delete == Some(operation) {
        return delete(
            store,
            actor,
            resource,
            parameters,
            body.unwrap_or_else(|| json!({})),
            query,
        );
    }
    let mut request = body.ok_or_else(|| invalid("请求内容不能为空。"))?;
    if !request.is_object() {
        return Err(invalid("请求内容须为业务对象。"));
    }
    let record_id = if operation == resource.update && operation != resource.create {
        addressed_id(store, resource, operation, parameters)?
    } else {
        request["id"].as_i64().unwrap_or(0)
    };
    let action = if record_id > 0 { "edit" } else { "create" };
    auth::authorize(actor, resource.permission, action)?;
    if record_id > 0 {
        let previous = store.get(resource.key, record_id)?;
        if !auth::visible(actor, resource.permission, action, &previous) {
            return Err(error(403, "没有修改此记录的权限。"));
        }
    }
    for (key, value) in parameters {
        let foreign = match *key {
            "customerId" => Some("crmCustomerId"),
            "supplierId" => Some("supplierId"),
            _ => None,
        };
        if let Some(foreign) = foreign {
            request[foreign] = json!(
                value
                    .parse::<i64>()
                    .map_err(|_| invalid("关联编号无效。"))?
            );
        }
    }
    if resource.key == "users" {
        return super::accounts::save(store, actor, record_id, request);
    }
    let business_date = clock.now().map_err(invalid)?.today;
    let value = store.transaction(|transaction| {
        if transaction.provider() == "PostgreSQL"
            && matches!(resource.key, "bookings" | "supply-requests")
            && request["employeeId"].as_i64().is_some()
        {
            return Err(invalid("团队模式不能代其他人员申请。"));
        }
        if record_id == 0 && matches!(resource.key, "bookings" | "supply-requests") {
            let key = text(&request, "requestKey");
            if key.is_empty() || key.len() > 100 {
                return Err(invalid("登记请求缺少有效的幂等编号。"));
            }
            if let Some(existing) = store::all(transaction, resource.key)?
                .into_iter()
                .find(|record| record["requestKey"] == key)
            {
                let fields: &[&str] = if resource.key == "bookings" {
                    &[
                        "meetingRoomId",
                        "title",
                        "attendeeCount",
                        "startsAt",
                        "endsAt",
                    ]
                } else {
                    &["officeSupplyId", "quantity", "purpose", "returnDueDate"]
                };
                if existing["ownerUserId"] != actor.id
                    || existing["companyScope"] != actor.company
                    || fields
                        .iter()
                        .any(|field| existing[*field] != request[*field])
                    || (transaction.provider() == "SQLite"
                        && existing["employeeId"] != request["employeeId"])
                {
                    return Err(conflict("同一请求标识不能用于不同的登记内容。"));
                }
                return Ok(existing);
            }
        }
        save_in_transaction(
            transaction,
            actor,
            resource,
            record_id,
            &request,
            business_date,
        )
    })?;
    let value = if resource.key == "permission-templates" {
        super::permission_templates::project(value)?
    } else {
        value
    };
    if resource.result_field.is_empty() {
        Ok(value)
    } else {
        let mut response = contracts::object(operation.id, false);
        response["success"] = json!(true);
        response["message"] = json!("已保存");
        if response.get("id").is_some() {
            response["id"] = value["id"].clone();
        }
        response[resource.result_field] = value;
        Ok(response)
    }
}

/// Reuse the normal save invariants inside multi-record import transactions.
pub(super) fn save_in_transaction(
    transaction: &Connection,
    actor: &Actor,
    resource: &Resource,
    record_id: i64,
    request: &Value,
    business_date: chrono::NaiveDate,
) -> Result<Value> {
    let action = if record_id > 0 { "edit" } else { "create" };
    auth::authorize(actor, resource.permission, action)?;
    if record_id > 0
        && !auth::visible(
            actor,
            resource.permission,
            action,
            &store::get(transaction, resource.key, record_id)?,
        )
    {
        return Err(error(403, "记录不在当前账号的操作范围内。"));
    }
    let previous = if record_id > 0 {
        store::get(transaction, resource.key, record_id)?
    } else {
        contracts::initial(contracts::schema(resource.schema))
    };
    if record_id > 0 {
        store::check_version(&previous, store::expected(&request))?;
    }
    let mut value = contracts::overlay(previous.clone(), &request);
    if !matches!(resource.key, "users" | "people") {
        value["ownerUserId"] = if record_id > 0 {
            previous["ownerUserId"].clone()
        } else {
            json!(actor.id)
        };
        value["companyScope"] = if record_id > 0 {
            previous["companyScope"].clone()
        } else {
            json!(actor.company)
        };
    }
    normalize(&mut value, 0)?;
    validate(
        transaction,
        actor,
        resource,
        record_id,
        &previous,
        &mut value,
        business_date,
    )?;
    if resource.key == "people" {
        return super::personnel::save(transaction, actor, record_id, value, action, "");
    }
    let identity = if resource.identity.is_empty() {
        None
    } else {
        Some(text(&value, resource.identity))
    };
    let saved = store::save(
        transaction,
        resource.key,
        record_id,
        value,
        identity,
        actor,
        action,
    )?;
    if matches!(resource.key, "bookings" | "supply-requests") {
        let event = if record_id > 0 {
            "Edit"
        } else if transaction.provider() == "SQLite" {
            "Register"
        } else {
            "Submit"
        };
        super::office_events::append(
            transaction,
            actor,
            resource.key,
            &saved,
            event,
            saved["quantity"].as_i64().unwrap_or(0),
            "",
        )?;
    }
    Ok(saved)
}

fn normalize(value: &mut Value, depth: usize) -> Result<()> {
    if depth > 20 {
        return Err(invalid("内容嵌套过深。"));
    }
    match value {
        Value::String(text) => {
            if text.len() > 2 * 1024 * 1024 {
                return Err(invalid("单个字段超出容量。"));
            }
            *text = text.nfc().collect::<String>().trim().to_owned();
        }
        Value::Array(values) => {
            if values.len() > 5000 {
                return Err(invalid("明细行超出上限。"));
            }
            for value in values {
                normalize(value, depth + 1)?;
            }
        }
        Value::Object(values) => {
            for value in values.values_mut() {
                normalize(value, depth + 1)?;
            }
        }
        _ => {}
    }
    Ok(())
}

fn validate(
    connection: &Connection,
    actor: &Actor,
    resource: &Resource,
    id: i64,
    previous: &Value,
    value: &mut Value,
    business_date: chrono::NaiveDate,
) -> Result<()> {
    match resource.key {
        "invoices" => {
            if id > 0 && previous["status"] != "Draft" {
                return Err(conflict(
                    "已核对、出运、结汇或作废的发票不能编辑，请先按业务流程撤销核对。",
                ));
            }
            let mut dto: crate::generated_api::ApiInvoiceDetailDto =
                serde_json::from_value(value.clone())
                    .map_err(|error| invalid(format!("发票字段无效：{error}")))?;
            let draft = invoice::InvoiceDraft::from_dto(dto);
            dto = draft.build().map_err(invalid)?;
            dto.status = "Draft".into();
            let existing: std::collections::BTreeSet<i64> = previous["items"]
                .as_array()
                .into_iter()
                .flatten()
                .filter_map(|row| row["id"].as_i64())
                .collect();
            let mut next = previous["_nextItemId"]
                .as_i64()
                .unwrap_or(1)
                .max(existing.iter().next_back().copied().unwrap_or(0) + 1);
            let mut assigned = std::collections::BTreeSet::new();
            for item in &mut dto.items {
                if id == 0 || item.id == 0 {
                    item.id = next;
                    next = next
                        .checked_add(1)
                        .ok_or_else(|| unavailable("发票货项编号超出范围。"))?;
                } else if !existing.contains(&item.id) {
                    return Err(invalid("货项编号不属于当前发票；新货项编号须为 0。"));
                }
                if !assigned.insert(item.id) {
                    return Err(invalid("发票货项编号不能重复。"));
                }
                item.invoice_id = id;
            }
            value["_nextItemId"] = json!(next);
            *value = contracts::overlay(value.clone(), &serde_json::to_value(dto)?);
            super::report_assets::validate_invoice(connection, actor, value)?;
        }
        "exporters" => super::report_assets::validate_exporter(connection, actor, value)?,
        "payments" => {
            let payment: crate::generated_api::ApiPaymentDto =
                serde_json::from_value(value.clone())
                    .map_err(|cause| invalid(format!("付款内容无效：{cause}")))?;
            export_doc_domain::payment::validate(&payment)
                .map_err(|cause| invalid(cause.to_string()))?;
            if payment.payee_id > 0 {
                let payee = store::get(connection, "payees", payment.payee_id)?;
                if !auth::visible(actor, "document.reference-data", "view", &payee)
                    && !auth::visible(actor, "document.master-data", "view", &payee)
                {
                    return Err(error(403, "收款对象不在当前账号的数据范围内。"));
                }
            }
        }
        "crm-customers" => {
            required(value, "name", "客户名称", 200)?;
            if id == 0 {
                value["status"] = json!("潜在客户");
            } else {
                value["status"] = previous["status"].clone();
            }
            if let Some(linked) = value["linkedDocumentCustomerId"]
                .as_i64()
                .filter(|id| *id > 0)
            {
                let customer = store::get(connection, "customers", linked)?;
                if !auth::visible(actor, "document.reference-data", "view", &customer) {
                    return Err(error(403, "关联单证客户不在当前账号的数据范围内。"));
                }
            } else {
                value["linkedDocumentCustomerId"] = Value::Null;
            }
        }
        "suppliers" => {
            required(value, "name", "供应商名称", 200)?;
            if id == 0 {
                value["status"] = json!("考察中");
            } else {
                value["status"] = previous["status"].clone();
            }
        }
        "people" | "rooms" | "bookings" | "supplies" | "supply-requests" => {
            super::office::validate(
                connection,
                actor,
                resource.key,
                id,
                previous,
                value,
                business_date,
            )?
        }
        "companies" | "departments" => {
            super::organization::validate(connection, actor, resource.key, id, previous, value)?
        }
        "permission-templates" => {
            super::permission_templates::validate(id, previous, value)?;
        }
        "email-templates" | "report-templates" => {
            required(value, "name", "模板名称", 160)?;
            if id == 0 {
                value["status"] = json!("Draft");
            } else {
                value["status"] = previous["status"].clone();
            }
            if resource.key == "report-templates" {
                let content = text(value, "contentHtml");
                crate::designer::Design::from_source(&content).map_err(invalid)?;
                super::report_assets::validate_template(connection, actor, &content, None)?;
            }
        }
        "hs-codes" => {
            let code = text(value, "code");
            if !code.chars().all(|character| character.is_ascii_digit())
                || !(4..=13).contains(&code.len())
            {
                return Err(invalid("HS 编码应为 4–13 位数字。"));
            }
            required(value, "name", "商品名称", 500)?;
            value["normalizedCode"] = json!(code);
            if id == 0 {
                value["status"] = json!("ReferenceOnly");
            }
        }
        "container-projects" => required(value, "name", "方案名称", 160)?,
        _ => {
            let name_fields: Vec<_> = resource
                .columns
                .iter()
                .take(2)
                .map(|(key, _)| *key)
                .collect();
            if !name_fields.iter().any(|key| !text(value, key).is_empty()) {
                return Err(invalid("请填写名称或编号。"));
            }
        }
    }
    Ok(())
}

pub fn delete(
    store: &Store,
    actor: &Actor,
    resource: &Resource,
    parameters: &[(&str, String)],
    mut body: Value,
    query: &[(&str, String)],
) -> Result<Value> {
    auth::authorize(actor, resource.permission, "delete")?;
    let record_id = addressed_id(
        store,
        resource,
        resource
            .delete
            .ok_or_else(|| invalid("此记录不支持删除。"))?,
        parameters,
    )?;
    for (key, value) in query {
        if *key == "rowVersion" {
            body["rowVersion"] = json!(value);
        }
        if *key == "expectedVersion" {
            body["expectedVersion"] =
                json!(value.parse::<i64>().map_err(|_| invalid("版本号无效。"))?);
        }
    }
    store.transaction(|transaction| {
        let previous = store::get(transaction, resource.key, record_id)?;
        if !auth::visible(actor, resource.permission, "delete", &previous) {
            return Err(error(403, "没有删除此记录的权限。"));
        }
        store::check_version(&previous, store::expected(&body))?;
        if resource.key == "users" && record_id == actor.id {
            return Err(conflict("不能删除当前登录账号。"));
        }
        if resource.key == "permission-templates" && previous["isSystem"] == true {
            return Err(conflict("系统内置权限方案不可删除。"));
        }
        if resource.key == "invoices" && previous["status"] != "Draft" {
            return Err(conflict("只允许删除未核对的草稿发票。"));
        }
        if ["bookings", "supply-requests"].contains(&resource.key) {
            return Err(conflict("交接记录必须通过取消流程处理。"));
        }
        check_references(transaction, resource.key, &previous)?;
        if resource.key == "people" && !super::office::can_correct(transaction, record_id)? {
            return Err(conflict(
                "档案已有账号、业务或任职历史，请通过离职流程处理。",
            ));
        }
        if ["rooms", "supplies"].contains(&resource.key)
            && store::history(transaction, resource.key, record_id)?
                .iter()
                .any(|event| event["action"] != "create" && event["action"] != "edit")
        {
            return Err(conflict("记录已有正式业务历史，请使用停用或离职流程。"));
        }
        transaction.append_audit_details(
            &AuditWrite {
                kind: resource.key,
                record_id,
                version: previous["versionNumber"].as_i64().unwrap_or(0),
                action: "delete",
                actor_id: actor.id,
                occurred_at: &store::timestamp(),
                note: &text(&body, "reason"),
            },
            &super::audit_values::changes(Some(&previous), None),
        )?;
        if resource.key == "people" {
            for event in store::all(transaction, "personnel-events")?
                .into_iter()
                .filter(|event| event["employeeId"] == record_id)
            {
                if !transaction.delete(
                    "personnel-events",
                    event["id"]
                        .as_i64()
                        .ok_or_else(|| invalid("人员历史编号无效。"))?,
                    event["versionNumber"]
                        .as_i64()
                        .ok_or_else(|| invalid("人员历史版本无效。"))?,
                )? {
                    return Err(conflict("人员历史已变化，请刷新后重试。"));
                }
            }
        }
        if !transaction.delete(
            resource.key,
            record_id,
            previous["versionNumber"].as_i64().unwrap_or(0),
        )? {
            return Err(conflict("记录版本已变化，删除已取消。"));
        }
        Ok(json!({"success":true,"message":"已删除"}))
    })
}

pub fn check_references(connection: &Connection, kind: &str, record: &Value) -> Result<()> {
    const REFS: &[(&str, &str, &str)] = &[
        ("customers", "invoices", "customerId"),
        ("exporters", "invoices", "exporterId"),
        ("payees", "payments", "payeeId"),
        ("crm-customers", "crm-contacts", "crmCustomerId"),
        ("crm-customers", "crm-follow-ups", "crmCustomerId"),
        ("crm-customers", "opportunities", "crmCustomerId"),
        ("suppliers", "supplier-contacts", "supplierCompanyId"),
        ("suppliers", "supplier-products", "supplierCompanyId"),
        ("suppliers", "supplier-assessments", "supplierCompanyId"),
        ("products", "supplier-products", "productId"),
        ("rooms", "bookings", "meetingRoomId"),
        ("supplies", "supply-requests", "officeSupplyId"),
        ("people", "bookings", "employeeId"),
        ("people", "supply-requests", "employeeId"),
        ("permission-templates", "users", "permissionTemplateId"),
        ("invoices", "attachments", "invoiceId"),
        ("attachment-categories", "attachments", "categoryId"),
    ];
    for (parent, child, field) in REFS.iter().filter(|(parent, _, _)| *parent == kind) {
        let _ = parent;
        if store::all(connection, child)?
            .iter()
            .any(|item| item[*field] == record["id"])
        {
            return Err(conflict("该记录已被业务引用，不能删除。"));
        }
    }
    if ["companies", "departments"].contains(&kind) {
        let code = &record["code"];
        for resource in RESOURCES {
            for item in store::all(connection, resource.key)? {
                if item["id"] != record["id"]
                    && (if kind == "companies" {
                        item["companyScope"] == *code || item["companyCode"] == *code
                    } else {
                        item["departmentId"] == *code || item["parentCode"] == *code
                    })
                {
                    return Err(conflict("目录存在下级、账号或业务引用，不能删除。"));
                }
            }
        }
    }
    Ok(())
}
