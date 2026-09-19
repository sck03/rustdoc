use export_doc_engine::{api::ApiClient, generated_api::Operation};
use serde_json::Value;

/// Read the public page contract until complete. A limit violation is visible,
/// never a silently truncated drop-down that can lose a selected relationship.
pub fn rows(
    client: &ApiClient,
    operation: Operation,
    parameters: &[(&str, String)],
    query: &[(&str, String)],
    field: &str,
) -> Result<Vec<Value>, String> {
    let mut output = vec![];
    for page_number in 1..=250 {
        export_doc_engine::operation::check().map_err(|cause| cause.to_string())?;
        let mut query: Vec<_> = query
            .iter()
            .filter(|(key, _)| !["pageNumber", "pageSize"].contains(key))
            .cloned()
            .collect();
        query.extend([
            ("pageNumber", page_number.to_string()),
            ("pageSize", "200".into()),
        ]);
        let payload: Value = client
            .json(operation, parameters, &query, None)
            .map_err(|cause| cause.to_string())?;
        if let Some(rows) = payload.as_array() {
            return Ok(rows.clone());
        }
        let page = payload
            .get("page")
            .filter(|value| value.is_object())
            .unwrap_or(&payload);
        let rows = page[field]
            .as_array()
            .ok_or_else(|| format!("{} 返回的资料目录格式无效。", operation.id))?;
        if page["hasNextPage"] == true && rows.is_empty() {
            return Err("目录分页未返回数据，已停止继续查询。".into());
        }
        output.extend(rows.iter().cloned());
        if page["hasNextPage"] != true {
            return Ok(output);
        }
    }
    Err("资料目录超过 50000 项，请先缩小查询范围。".into())
}
