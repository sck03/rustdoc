//! Coordinates of the bundled invoice template, shared by import and export.
pub const ITEMS_START_ROW: u32 = 20;
pub const HEADERS: &[(&str, u32, u32)] = &[
    ("exporterNameCN", 1, 1),
    ("exporterNameEN", 3, 2),
    ("exporterCreditCode", 4, 15),
    ("customerNameEN", 8, 2),
    ("notifyPartyName", 13, 2),
    ("invoiceDate", 3, 15),
    ("contractNo", 5, 15),
    ("issuingBank", 7, 15),
    ("currency", 8, 15),
    ("invoiceNo", 9, 15),
    ("supervisionMode", 10, 15),
    ("letterOfCreditNo", 6, 15),
    ("paymentTerms", 11, 15),
    ("transportMode", 12, 15),
    ("tradeTerms", 14, 15),
    ("portOfLoading", 15, 15),
    ("portOfDestination", 16, 15),
    ("destinationCountry", 17, 15),
    ("shippingMarks", 20, 1),
    ("shipmentDate", 13, 15),
];
pub const MULTILINE: &[(&str, u32)] = &[
    ("exporterAddressEN", 4),
    ("customerAddressEN", 9),
    ("notifyPartyAddress", 14),
];
pub fn item_column(key: &str) -> Option<usize> {
    Some(match key {
        "poNumber" => 2,
        "styleNo" => 3,
        "styleName" => 4,
        "fabricComposition" => 5,
        "styleNameCN" => 6,
        "brand" => 7,
        "hsCode" => 8,
        "origin" => 9,
        "quantity" => 10,
        "unitEN" => 11,
        "unitCN" => 12,
        "cartons" => 13,
        "ctnUnitEN" => 14,
        "length" => 15,
        "width" => 16,
        "height" => 17,
        "volume" => 18,
        "gwPerCtn" => 19,
        "gwTotal" => 20,
        "nwPerCtn" => 21,
        "nwTotal" => 22,
        "unitPrice" => 23,
        "totalPrice" => 24,
        _ => return None,
    })
}
pub fn cell(address: &str) -> Option<(u32, u32)> {
    let address = address.replace('$', "");
    let letters = address
        .chars()
        .take_while(char::is_ascii_alphabetic)
        .count();
    if letters == 0 || letters > 3 {
        return None;
    }
    let mut col = 0_u32;
    for byte in address[..letters].bytes() {
        col = col * 26 + u32::from(byte.to_ascii_uppercase() - b'A' + 1);
    }
    let row = address[letters..].parse::<u32>().ok()?;
    if !(1..=1_048_576).contains(&row) || !(1..=16384).contains(&col) {
        return None;
    }
    Some((row, col))
}
pub fn address(row: u32, mut column: u32) -> String {
    let mut letters = Vec::new();
    while column > 0 {
        column -= 1;
        letters.push((b'A' + (column % 26) as u8) as char);
        column /= 26;
    }
    format!("{}{row}", letters.into_iter().rev().collect::<String>())
}
