//! Setting keys belong to the generated configuration. Only the semantic
//! association between an invoice field and its configurable cell lives here.
pub const HEADERS: &[(&str, &str)] = &[
    ("exporterNameCN", "exporterNameCNCell"),
    ("exporterNameEN", "exporterNameCell"),
    ("exporterCreditCode", "creditCodeCell"),
    ("customerNameEN", "customerNameCell"),
    ("notifyPartyName", "notifyPartyNameCell"),
    ("invoiceDate", "invoiceDateCell"),
    ("contractNo", "contractNoCell"),
    ("issuingBank", "issuingBankCell"),
    ("currency", "currencyCell"),
    ("invoiceNo", "invoiceNoCell"),
    ("supervisionMode", "supervisionModeCell"),
    ("letterOfCreditNo", "letterOfCreditNoCell"),
    ("paymentTerms", "paymentTermsCell"),
    ("transportMode", "transportModeCell"),
    ("tradeTerms", "tradeTermsCell"),
    ("portOfLoading", "portOfLoadingCell"),
    ("portOfDestination", "portOfDestinationCell"),
    ("destinationCountry", "destinationCountryCell"),
    ("shippingMarks", "shippingMarksCell"),
];
pub const MULTILINE: &[(&str, &str, &str)] = &[
    (
        "exporterAddressEN",
        "exporterAddressStartCell",
        "exporterAddressLineCount",
    ),
    (
        "customerAddressEN",
        "customerAddressStartCell",
        "customerAddressLineCount",
    ),
    (
        "notifyPartyAddress",
        "notifyPartyAddressStartCell",
        "notifyPartyAddressLineCount",
    ),
];
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
