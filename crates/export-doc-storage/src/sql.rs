//! Shared SQL primitives; identifiers come from reviewed query models, values bind.
use crate::{Error, Result};
pub(crate) fn field(path: &str, postgres: bool) -> String {
    if postgres && !path.contains('.') {
        format!("r.body->>'{path}'")
    } else if postgres {
        format!("r.body #>> '{{{}}}'", path.replace('.', ","))
    } else {
        format!("json_extract(r.body,'$.{path}')")
    }
}
pub(crate) fn json_field(path: &str, postgres: bool) -> String {
    if postgres {
        format!("r.body #> '{{{}}}'", path.replace('.', ","))
    } else {
        format!("json(r.body -> '$.{path}')")
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
    if postgres {
        format!("strpos({value},{keyword})>0")
    } else {
        format!("instr({value},{keyword})>0")
    }
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
