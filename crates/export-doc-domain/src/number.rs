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
