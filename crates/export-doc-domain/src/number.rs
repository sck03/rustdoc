/// Ordinary signed decimal text; precision is never silently rounded.
pub fn parse_decimal_text(text: &str) -> Option<rust_decimal::Decimal> {
    let unsigned = text.strip_prefix(['-', '+']).unwrap_or(text);
    let (whole, fraction) = unsigned
        .split_once('.')
        .map_or((unsigned, None), |(whole, fraction)| {
            (whole, Some(fraction))
        });
    if whole.is_empty()
        || !whole.bytes().all(|b| b.is_ascii_digit())
        || fraction.is_some_and(|v| v.is_empty() || !v.bytes().all(|b| b.is_ascii_digit()))
    {
        return None;
    }
    rust_decimal::Decimal::from_str_exact(text).ok()
}

/// Decimal display rounds explicitly before applying a fixed-width format.
pub fn format_decimal(value: rust_decimal::Decimal, precision: u32) -> String {
    format!(
        "{:.*}",
        precision as usize,
        value.round_dp_with_strategy(
            precision,
            rust_decimal::RoundingStrategy::MidpointAwayFromZero
        )
    )
}

const ONES: [&str; 20] = [
    "",
    "ONE",
    "TWO",
    "THREE",
    "FOUR",
    "FIVE",
    "SIX",
    "SEVEN",
    "EIGHT",
    "NINE",
    "TEN",
    "ELEVEN",
    "TWELVE",
    "THIRTEEN",
    "FOURTEEN",
    "FIFTEEN",
    "SIXTEEN",
    "SEVENTEEN",
    "EIGHTEEN",
    "NINETEEN",
];
const TENS: [&str; 10] = [
    "", "", "TWENTY", "THIRTY", "FORTY", "FIFTY", "SIXTY", "SEVENTY", "EIGHTY", "NINETY",
];
pub fn english_integer(value: u64) -> String {
    if value < 20 {
        return ONES[value as usize].into();
    }
    if value < 100 {
        return format!(
            "{}{}",
            TENS[(value / 10) as usize],
            if value % 10 == 0 {
                String::new()
            } else {
                format!("-{}", ONES[(value % 10) as usize])
            }
        );
    }
    for (unit, label, separator) in [
        (1_000_000_000, "BILLION", " "),
        (1_000_000, "MILLION", " "),
        (1000, "THOUSAND", " "),
        (100, "HUNDRED", " AND "),
    ] {
        if value >= unit {
            return format!(
                "{} {label}{}",
                english_integer(value / unit),
                if value % unit == 0 {
                    String::new()
                } else {
                    format!("{separator}{}", english_integer(value % unit))
                }
            );
        }
    }
    unreachable!()
}
