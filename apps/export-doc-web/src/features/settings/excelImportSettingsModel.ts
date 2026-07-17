import {
  isRecord,
  readFiniteNumber,
  readNestedValue,
  readRecordString,
  readRecordValue,
  toPascalCase,
} from "./settingsValueUtils.ts";

export type SettingsRecord = Record<string, unknown>;

export const defaultExcelImportSettings: SettingsRecord = {
  schemeName: "Default",
  exporterNameCNCell: "A1",
  exporterNameCell: "B3",
  exporterAddressStartCell: "B4",
  exporterAddressLineCount: 4,
  creditCodeCell: "O4",
  customerNameCell: "B8",
  customerAddressStartCell: "B9",
  customerAddressLineCount: 4,
  notifyPartyNameCell: "B13",
  notifyPartyAddressStartCell: "B14",
  notifyPartyAddressLineCount: 4,
  invoiceDateCell: "O3",
  contractNoCell: "O5",
  issuingBankCell: "O7",
  currencyCell: "O8",
  invoiceNoCell: "O9",
  supervisionModeCell: "O10",
  letterOfCreditNoCell: "O6",
  paymentTermsCell: "O11",
  transportModeCell: "O12",
  tradeTermsCell: "O14",
  portOfLoadingCell: "O15",
  portOfDestinationCell: "O16",
  destinationCountryCell: "O17",
  shippingMarksCell: "A20",
  itemsStartRow: 20,
  itemsEndRow: 0,
  poNumberCol: 2,
  styleNoCol: 3,
  styleNameCol: 4,
  fabricCompositionCol: 5,
  styleNameCNCol: 6,
  brandCol: 7,
  hsCodeCol: 8,
  originCol: 9,
  quantityCol: 10,
  unitENCol: 11,
  unitCNCol: 12,
  cartonsCol: 13,
  ctnUnitENCol: 14,
  lengthCol: 15,
  widthCol: 16,
  heightCol: 17,
  volumeCol: 18,
  gwPerCtnCol: 19,
  gwTotalCol: 20,
  nwPerCtnCol: 21,
  nwTotalCol: 22,
  unitPriceCol: 23,
  totalPriceCol: 24,
};

export function readExcelImportSettingsForSettings(settings: SettingsRecord) {
  return normalizeExcelImportSettings(readNestedValue(settings, ["excelImport"]));
}

export function readExcelImportSchemesForSettings(settings: SettingsRecord) {
  const value = readNestedValue(settings, ["excelImportSchemes"]);
  if (!Array.isArray(value)) return [];

  const schemes: SettingsRecord[] = [];
  const seen = new Set<string>();
  for (const rawScheme of value) {
    const scheme = normalizeExcelImportSettings(rawScheme);
    const schemeName = readRecordString(scheme, "schemeName");
    if (!schemeName || seen.has(schemeName)) continue;
    schemes.push(scheme);
    seen.add(schemeName);
  }
  return schemes;
}

export function normalizeExcelImportSettings(value: unknown) {
  const source = isRecord(value) ? value : {};
  const result: SettingsRecord = {};
  for (const [key, defaultValue] of Object.entries(defaultExcelImportSettings)) {
    const sourceValue = readRecordValue(source, key, toPascalCase(key));
    result[key] = typeof defaultValue === "number"
      ? readFiniteNumber(sourceValue, defaultValue)
      : sourceValue == null ? defaultValue : String(sourceValue);
  }
  return result;
}

export function createDefaultExcelImportSettings(schemeName: string) {
  return { ...defaultExcelImportSettings, schemeName: schemeName.trim() || "Default" };
}

export function buildExcelSchemeOptions(schemes: SettingsRecord[]) {
  return schemes.map((scheme) => {
    const schemeName = readRecordString(scheme, "schemeName");
    return { value: schemeName, label: schemeName };
  });
}

export function readExcelImportRecordNumber(record: SettingsRecord, key: string) {
  const defaultValue = defaultExcelImportSettings[key];
  return readFiniteNumber(
    readRecordValue(record, key, toPascalCase(key)),
    typeof defaultValue === "number" ? defaultValue : 0,
  );
}
