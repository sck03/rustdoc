//! Decode the string scalars in the model's YAML character dictionary.
use anyhow::{bail, Context, Result};

pub(crate) fn label(value: &str) -> Result<String> {
    if let Some(value) = value.strip_prefix('\'') {
        let value = value
            .strip_suffix('\'')
            .context("unterminated character dictionary scalar")?;
        let mut chars = value.chars();
        let mut decoded = String::new();
        while let Some(character) = chars.next() {
            if character == '\'' && chars.next() != Some('\'') {
                bail!("invalid quoted character dictionary scalar");
            }
            decoded.push(character);
        }
        return Ok(decoded);
    }
    if let Some(value) = value.strip_prefix('"') {
        let value = value
            .strip_suffix('"')
            .context("unterminated character dictionary scalar")?;
        let mut decoded = String::new();
        let mut chars = value.chars();
        while let Some(character) = chars.next() {
            if character != '\\' {
                decoded.push(character);
                continue;
            }
            let escape = chars
                .next()
                .context("incomplete character dictionary escape")?;
            decoded.push(match escape {
                '0' => '\0',
                'a' => '\u{7}',
                'b' => '\u{8}',
                't' => '\t',
                'n' => '\n',
                'v' => '\u{b}',
                'f' => '\u{c}',
                'r' => '\r',
                'e' => '\u{1b}',
                'N' => '\u{85}',
                '_' => '\u{a0}',
                'L' => '\u{2028}',
                'P' => '\u{2029}',
                ' ' | '"' | '/' | '\\' => escape,
                'x' | 'u' | 'U' => {
                    let digits = match escape {
                        'x' => 2,
                        'u' => 4,
                        _ => 8,
                    };
                    let mut code = 0u32;
                    for _ in 0..digits {
                        code = code * 16
                            + chars
                                .next()
                                .and_then(|c| c.to_digit(16))
                                .context("invalid dictionary Unicode escape")?;
                    }
                    char::from_u32(code).context("invalid dictionary Unicode scalar")?
                }
                _ => bail!("unsupported character dictionary escape"),
            });
        }
        return Ok(decoded);
    }
    if value.is_empty() {
        bail!("empty character dictionary scalar");
    }
    Ok(value.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn dictionary_quotes_and_unicode_are_decoded_once() {
        assert_eq!(label("''''").unwrap(), "'");
        assert_eq!(label("'\"'").unwrap(), "\"");
        assert_eq!(label(r#""\u4e2d""#).unwrap(), "中");
        assert_eq!(label(r#""\U00020000""#).unwrap(), "𠀀");
        assert_eq!(label(r#""\\""#).unwrap(), "\\");
        assert!(label("'bad'quote'").is_err());
        assert!(label(r#""\uD800""#).is_err());
        assert!(label(r#""\UFFFFFFFF""#).is_err());
    }
}
