//! Shared SQL primitives; identifiers come from reviewed query models, values bind.
use crate::{Error, Result};
mod statement;
pub(crate) use statement::Statement;
pub(crate) fn object(pg: bool) -> &'static str {
    if pg {
        "jsonb_build_object"
    } else {
        "json_object"
    }
}
pub(crate) fn field(path: &str, postgres: bool) -> String {
    field_from("r.body", path, postgres)
}
pub(crate) fn field_from(source: &'static str, path: &str, postgres: bool) -> String {
    if postgres && !path.contains('.') {
        format!("{source}->>'{path}'")
    } else if postgres {
        format!("{source} #>> '{{{}}}'", path.replace('.', ","))
    } else {
        format!("json_extract({source},'$.{path}')")
    }
}
pub(crate) fn json_field(path: &str, postgres: bool) -> String {
    json_field_from("r.body", path, postgres)
}
pub(crate) fn json_field_from(source: &'static str, path: &str, postgres: bool) -> String {
    if postgres {
        format!("{source} #> '{{{}}}'", path.replace('.', ","))
    } else {
        format!("json({source} -> '$.{path}')")
    }
}
pub(crate) fn bind(values: &mut Vec<String>, value: impl ToString, postgres: bool) -> String {
    values.push(value.to_string());
    if postgres {
        format!("${}::text", values.len())
    } else {
        format!("?{}", values.len())
    }
}
/// Strict RFC 3339 instants used by sorting and retention, expressed as epoch milliseconds.
pub(crate) fn timestamp(value: &str, postgres: bool) -> String {
    if postgres {
        format!(
            "CASE WHEN {value} ~ '^[0-9]{{4}}-[0-9]{{2}}-[0-9]{{2}}[Tt][0-9]{{2}}:[0-9]{{2}}:[0-9]{{2}}(\\.[0-9]+)?([Zz]|[+-][0-9]{{2}}:[0-9]{{2}})$' THEN (EXTRACT(EPOCH FROM ({value})::timestamptz)*1000)::bigint ELSE ('invalid timestamp: '||COALESCE({value},''))::bigint END"
        )
    } else {
        format!("job_time({value})")
    }
}
/// Chronological ordering keeps RFC 3339 offsets and the original nanosecond precision.
pub(crate) fn instant_order(value: &str, postgres: bool) -> (String, String) {
    if postgres {
        let seconds = format!("regexp_replace({value},'\\.[0-9]+','')");
        let nanos =
            format!("rpad(COALESCE(substring({value} from '\\.([0-9]+)'),'0'),9,'0')::bigint");
        (format!("({})/1000", timestamp(&seconds, true)), nanos)
    } else {
        (
            format!("record_time_seconds({value})"),
            format!("record_time_nanos({value})"),
        )
    }
}
pub(crate) fn normalized(value: &str, postgres: bool) -> String {
    if postgres {
        format!(
            "lower(btrim(normalize({value}, NFC), U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000') COLLATE pg_unicode_fast)"
        )
    } else {
        format!("template_normalize({value})")
    }
}
pub(crate) fn contains(value: &str, keyword: &str, postgres: bool) -> String {
    let (value, keyword) = (normalized(value, postgres), normalized(keyword, postgres));
    substring(&value, &keyword, postgres)
}
pub(crate) fn substring(value: &str, keyword: &str, postgres: bool) -> String {
    if postgres {
        format!("strpos({value},{keyword})>0")
    } else {
        format!("instr({value},{keyword})>0")
    }
}
/// A binary range preserves literal prefix matching and allows a B-tree seek.
pub(crate) fn prefix(value: &str, prefix: &str, values: &mut Vec<String>, pg: bool) -> String {
    let lower = bind(values, prefix, pg);
    let collation = if pg { "\"C\"" } else { "BINARY" };
    let value = format!("({value}) COLLATE {collation}");
    let mut upper = prefix.to_owned();
    while let Some(last) = upper.pop() {
        if let Some(next) = (u32::from(last) + 1..=0x10ffff).find_map(char::from_u32) {
            upper.push(next);
            let upper = bind(values, upper, pg);
            return format!("({value}>={lower} AND {value}<{upper})");
        }
    }
    format!("{value}>={lower}")
}
pub(crate) fn valid_path(path: &str) -> Result<()> {
    if path.is_empty()
        || !path.split('.').all(|part| {
            !part.is_empty() && part.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'_')
        })
    {
        return Err(Error::unavailable("查询字段不符合受控模型。"));
    }
    Ok(())
}
pub(crate) fn projection(fields: &[(&str, &str)], postgres: bool) -> Result<String> {
    if fields.is_empty() {
        return Ok(if postgres { "r.body::text" } else { "r.body" }.into());
    }
    projected(
        fields.iter().map(|(name, source)| {
            valid_path(source)?;
            Ok((*name, json_field(source, postgres)))
        }),
        postgres,
    )
}
/// Expressions are produced only by reviewed query builders; names stay validated.
pub(crate) fn projected<'a>(
    fields: impl IntoIterator<Item = Result<(&'a str, String)>>,
    postgres: bool,
) -> Result<String> {
    let pairs = fields
        .into_iter()
        .map(|item| {
            let (name, expression) = item?;
            valid_path(name)?;
            Ok(format!("('{name}',{expression})"))
        })
        .collect::<Result<Vec<_>>>()?;
    Ok(if postgres {
        format!(
            "(SELECT COALESCE(jsonb_object_agg(name,body_value),'{{}}'::jsonb)::text FROM (VALUES {}) AS projected(name,body_value) WHERE body_value IS NOT NULL)",
            pairs.join(",")
        )
    } else {
        format!(
            "(SELECT json_group_object(column1,json(column2)) FROM (VALUES {}) WHERE column2 IS NOT NULL)",
            pairs.join(",")
        )
    })
}
