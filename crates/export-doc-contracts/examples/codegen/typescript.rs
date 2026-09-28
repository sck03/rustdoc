use super::{Operation, array, ordered, quoted, required, text};
use serde_json::Value;
use std::fmt::Write;

pub fn identifier(value: &str, lower_first: bool) -> String {
    let mut result = String::new();
    let mut upper_next = !lower_first;
    for ch in value.chars() {
        if ch.is_alphanumeric() {
            if result.is_empty() && lower_first {
                result.extend(ch.to_lowercase());
            } else if upper_next {
                result.extend(ch.to_uppercase());
            } else {
                result.push(ch);
            }
            upper_next = false;
        } else {
            upper_next = true;
        }
    }
    if result.is_empty() {
        return if lower_first { "unnamed" } else { "Unnamed" }.into();
    }
    if result.starts_with(char::is_numeric) {
        result.insert_str(0, if lower_first { "value" } else { "Value" });
    }
    result
}
fn property(name: &str) -> String {
    if name
        .chars()
        .enumerate()
        .all(|(index, c)| c == '$' || c == '_' || c.is_alphabetic() || index > 0 && c.is_numeric())
        && !name.is_empty()
    {
        name.into()
    } else {
        quoted(name)
    }
}
fn access(name: &str) -> String {
    if property(name) == name {
        format!("request.{name}")
    } else {
        format!("request[{}]", quoted(name))
    }
}
fn joined(values: impl Iterator<Item = String>, separator: &str) -> String {
    let mut unique = Vec::new();
    for value in values {
        if !unique.contains(&value) {
            unique.push(value);
        }
    }
    unique.join(separator)
}
fn enumeration(schema: &Value) -> String {
    joined(
        array(&schema["enum"])
            .iter()
            .filter(|v| !v.is_null())
            .map(|v| v.to_string()),
        " | ",
    )
}
fn has_properties(schema: &Value) -> bool {
    schema["properties"]
        .as_object()
        .is_some_and(|p| !p.is_empty())
}
pub fn schema_type(schema: &Value) -> String {
    if !schema.is_object() {
        return "unknown".into();
    }
    let enumeration = enumeration(schema);
    if !enumeration.is_empty() {
        return enumeration;
    }
    if let Some(reference) = schema["$ref"].as_str() {
        return identifier(reference.rsplit('/').next().unwrap(), false);
    }
    for (key, separator) in [("oneOf", " | "), ("anyOf", " | "), ("allOf", " & ")] {
        if let Some(values) = schema[key].as_array() {
            let result = joined(values.iter().map(schema_type), separator);
            return if result.is_empty() {
                "unknown".into()
            } else {
                result
            };
        }
    }
    let types: Vec<&str> = match schema["type"].as_array() {
        Some(types) => types.iter().map(text).collect(),
        None => vec![text(&schema["type"])],
    };
    if types == ["null"] {
        return "null".into();
    }
    let kind = types.iter().find(|t| **t != "null").copied().unwrap_or("");
    let result = match kind {
        "array" => {
            let item = schema_type(&schema["items"]);
            if item.contains(['|', '&']) {
                format!("({item})[]")
            } else {
                format!("{item}[]")
            }
        }
        "integer" | "number" => "number".into(),
        "string" | "boolean" => kind.into(),
        _ if has_properties(schema) => format!(
            "{{ {} }}",
            ordered(&schema["properties"])
                .iter()
                .map(|(name, value)| format!(
                    "{}{}: {}",
                    property(name),
                    if required(schema, name) { "" } else { "?" },
                    schema_type(value)
                ))
                .collect::<Vec<_>>()
                .join("; ")
        ),
        "object" => "Record<string, unknown>".into(),
        _ => "unknown".into(),
    };
    if types.contains(&"null") && result != "unknown" {
        format!("{result} | null")
    } else {
        result
    }
}
fn content_type(content: &Value) -> Option<String> {
    let object = content.as_object()?;
    if object.contains_key("application/octet-stream")
        || object
            .values()
            .any(|v| matches!(text(&v["schema"]["format"]), "binary" | "byte"))
    {
        return Some("Blob".into());
    }
    if object.contains_key("multipart/form-data") {
        return Some("FormData".into());
    }
    let json = object.get("application/json").or_else(|| {
        object
            .iter()
            .find(|(key, _)| key.ends_with("+json"))
            .map(|(_, v)| v)
    });
    if let Some(schema) = json.and_then(|v| v.get("schema")) {
        return Some(schema_type(schema));
    }
    object
        .keys()
        .any(|key| key.starts_with("text/"))
        .then(|| "string".into())
}
fn response_type(operation: &Value) -> String {
    let responses = &operation["responses"];
    let mut codes = vec!["200", "201", "202", "203", "206", "204"];
    for (code, _) in ordered(responses) {
        if code.len() == 3 && code.starts_with('2') && !codes.contains(&code) {
            codes.push(code);
        }
    }
    codes
        .iter()
        .find_map(|code| content_type(&responses[*code]["content"]))
        .unwrap_or_else(|| "void".into())
}
pub fn generate(document: &Value, operations: &[Operation<'_>]) -> String {
    let mut output = "/* eslint-disable */\n// <auto-generated />\n// Generated by export-doc-contracts Rust generate_clients from Rust OpenAPI metadata.\n// Do not edit this file by hand. Run scripts/generate-api-client.ps1 instead.\n\n".to_string();
    for (key, schema) in ordered(&document["components"]["schemas"]) {
        let name = identifier(key, false);
        if has_properties(schema) && enumeration(schema).is_empty() {
            writeln!(output, "export interface {name} {{").unwrap();
            for (key, value) in ordered(&schema["properties"]) {
                writeln!(
                    output,
                    "  {}{}: {};",
                    property(key),
                    if required(schema, key) { "" } else { "?" },
                    schema_type(value)
                )
                .unwrap();
            }
            output.push_str("}\n\n");
        } else {
            writeln!(output, "export type {name} = {};\n", schema_type(schema)).unwrap();
        }
    }
    for (key, schema) in ordered(&document["components"]["schemas"]) {
        let defaults: Vec<_> = ordered(&schema["properties"])
            .into_iter()
            .filter(|(_, v)| v.get("default").is_some())
            .collect();
        if defaults.is_empty() {
            continue;
        }
        let name = identifier(key, false);
        writeln!(output, "export const {name}Defaults = {{").unwrap();
        for (key, value) in defaults {
            writeln!(output, "  {}: {},", property(key), value["default"]).unwrap();
        }
        writeln!(output, "}} as const satisfies Partial<{name}>;\n").unwrap();
    }
    let mut methods = String::new();
    let mut sorted: Vec<_> = operations.iter().collect();
    sorted.sort_by_key(|op| op.id);
    for op in sorted {
        let params = array(&op.value["parameters"]);
        let body = content_type(&op.value["requestBody"]["content"]);
        let body_required = op.value["requestBody"]["required"] == true;
        let request_name = format!("{}Request", identifier(op.id, false));
        let has_request = !params.is_empty() || body.is_some();
        if has_request {
            writeln!(output, "export interface {request_name} {{").unwrap();
            for location in ["path", "query", "header"] {
                for p in params.iter().filter(|p| p["in"] == location) {
                    writeln!(
                        output,
                        "  {}{}: {};",
                        property(text(&p["name"])),
                        if location == "path" || p["required"] == true {
                            ""
                        } else {
                            "?"
                        },
                        schema_type(&p["schema"])
                    )
                    .unwrap();
                }
            }
            if let Some(body) = &body {
                writeln!(
                    output,
                    "  body{}: {body};",
                    if body_required { "" } else { "?" }
                )
                .unwrap();
            }
            output.push_str("}\n\n");
        }
        let required = params
            .iter()
            .any(|p| p["in"] == "path" || p["required"] == true)
            || body_required;
        let request_arg = if has_request {
            format!(
                "request: {request_name}{}, ",
                if required { "" } else { " = {}" }
            )
        } else {
            String::new()
        };
        let response = response_type(op.value);
        writeln!(
            methods,
            "  public {}({request_arg}init?: ApiRequestInit): Promise<{response}> {{",
            identifier(op.id, true)
        )
        .unwrap();
        let mut path = op.path.to_string();
        let paths: Vec<_> = params.iter().filter(|p| p["in"] == "path").collect();
        for p in &paths {
            path = path.replace(
                &format!("{{{}}}", text(&p["name"])),
                &format!("${{encodePath({})}}", access(text(&p["name"]))),
            );
        }
        let expression = if paths.is_empty() {
            quoted(&path)
        } else {
            format!("`{path}`")
        };
        writeln!(methods, "    const path = {expression};").unwrap();
        write!(
            methods,
            "    return this.request<{response}>({}, path, {{",
            quoted(&op.method.to_uppercase())
        )
        .unwrap();
        let mut options = Vec::new();
        let query: Vec<_> = params.iter().filter(|p| p["in"] == "query").collect();
        if !query.is_empty() {
            options.push(format!(
                "      query: {{\n{}\n      }}",
                query
                    .iter()
                    .map(|p| format!(
                        "        {}: {},",
                        quoted(text(&p["name"])),
                        access(text(&p["name"]))
                    ))
                    .collect::<Vec<_>>()
                    .join("\n")
            ));
        }
        if body.is_some() {
            options.push("      body: request.body".into());
        }
        let headers: Vec<_> = params.iter().filter(|p| p["in"] == "header").collect();
        if !headers.is_empty() {
            options.push(format!("      init: {{\n        ...init,\n        headers: mergeHeaders(init?.headers, {{\n{}\n        }}),\n      }}", headers.iter().map(|p| { let a = access(text(&p["name"])); format!("          {}: {},", quoted(text(&p["name"])), if p["required"] == true { format!("String({a})") } else { format!("{a} === undefined ? undefined : String({a})") }) }).collect::<Vec<_>>().join("\n")));
        }
        if options.is_empty() {
            methods.push_str(" init });\n");
        } else {
            if headers.is_empty() {
                options.push("      init".into());
            }
            writeln!(methods, "\n{},\n    }});", options.join(",\n")).unwrap();
        }
        methods.push_str("  }\n\n");
    }
    output.push_str(
        &include_str!("api-client-runtime.ts.txt")
            .replace("\r\n", "\n")
            .replace("/* GENERATED_METHODS */\n", &methods),
    );
    output
}
