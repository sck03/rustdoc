// Extract first-party reference constants; API schemas still come from OpenAPI.
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { createHash } from "node:crypto";
import path from "node:path";
import { fileURLToPath } from "node:url";
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const application = "src/ExportDocManager.Application/Services/SingleWindow/";
const infrastructure = "src/ExportDocManager.Infrastructure/Services/SingleWindow/";
const sources = {};
const read = (name) => { const text = readFileSync(path.join(root, name), "utf8"); sources[name] = createHash("sha256").update(text).digest("hex"); return text; };
const contract = JSON.parse(readFileSync(path.join(root, "crates/export-doc-contracts/src/generated_contract.json"), "utf8"));
const schemas = { coo: "ApiCustomsCooDocumentDto", goods: "ApiCustomsCooItemDto", acd: "ApiAgentConsignmentDocumentDto", corp: "ApiCustomsCooNonpartyCorpDto" };
const key = (scope, name) => {
  const match = Object.keys(contract.schemas[schemas[scope]].properties).find((field) => field.toLowerCase() === name.toLowerCase());
  if (!match) throw new Error(`Unknown ${scope} property ${name}`);
  return match;
};
const section = (text, start, end) => {
  const first = text.indexOf(start); const last = end ? text.indexOf(end, first + start.length) : text.length;
  if (first < 0 || last < 0) throw new Error(`Reference boundary changed: ${start}`);
  return text.slice(first, last);
};
const quoted = (text) => [...text.matchAll(/"((?:[^"\\]|\\.)*)"/gu)].map((item) => JSON.parse(`"${item[1]}"`));
const constants = { "string.Empty": "" };
for (const name of ["CustomsCooGoodsItemFlagCatalog.cs", "CustomsCooPackTypeCatalog.cs"]) {
  const text = read(application + name);
  for (const [, field, value] of text.matchAll(/const string (\w+) = "([^"]*)"/gu)) constants[name.replace(".cs", "") + "." + field] = value;
}
function arrays(text) {
  const result = {};
  for (const [, name, body] of text.matchAll(/(?:\[\]|IReadOnlyList<SelectionOption<string>>)\s+(\w+)(?:\s*\{ get; \})?\s*=\s*\[([\s\S]*?)\];/gu)) {
    result[name] = [...body.matchAll(/new\(([^,]+),\s*("(?:[^"\\]|\\.)*"|string.Empty)\)/gu)].map(([, value, label]) => ({ value: value.trim().startsWith('"') ? JSON.parse(value.trim()) : constants[value.trim()], label: label === "string.Empty" ? "" : JSON.parse(label) }));
    if (result[name].length === 0 || result[name].some((item) => item.value === undefined)) throw new Error(`Unparsed reference options ${name}`);
  }
  return result;
}
const editor = arrays(read(application + "CustomsCooEditorCatalog.cs"));
if (Object.keys(editor).length !== 13) throw new Error("Editor option catalog changed.");
editor.PackUnitOptions = arrays(read(application + "CustomsCooPackUnitCatalog.cs")).CommonOptions;
const origin = arrays(read(application + "CustomsCooOriginCriteriaCatalog.cs"));
const options = Object.fromEntries(Object.entries(editor).map(([name, value]) => [name[0].toLowerCase() + name.slice(1), value]));
const validator = read(application + "SingleWindowXmlValidator.cs");
const coo = section(validator, "private static void ValidateCustomsCoo", "private static void ValidateAgentConsignment");
const acd = section(validator, "private static void ValidateAgentConsignment", "private static bool RequiresModificationFields");
const base = section(coo, "RequireValue(document.ApplyType", "if (CustomsCooRuleCatalog.UsesRcepInvoiceInfo");
const rules = []; const labels = {};
for (const [scope, source, owner] of [["coo", base, "document"], ["goods", coo, "item"], ["acd", acd, "document"]]) {
  for (const [, method, property, argumentsText] of source.matchAll(new RegExp(`(RequireValue|ValidateMaxLength|ValidateDigits|ValidateExactDate|ValidateDecimal|ValidateAllowedValues)\\(${owner}\\.(\\w+), ([^;]+)\\);`, "gu"))) {
    const values = quoted(argumentsText); const label = values.at(-1)?.split("(")[0] ?? property;
    const propertyKey = key(scope, property); labels[`${scope}.${propertyKey}`] ??= label;
    if (method === "RequireValue" && scope === "goods") continue; // conditional goods requirements are Rust rules
    const numbers = [...argumentsText.matchAll(/(?:^|, )([0-9]+)(?=,)/gu)].map((item) => Number(item[1]));
    let allowed = values.slice(0, -1);
    if (method === "ValidateAllowedValues" && allowed.length === 0) {
      allowed = property === "TradeModeCode" ? editor.CooTradeModeOptions.map((o) => o.value).filter(Boolean) : property === "PackType" ? ["1", "2"] : property === "GoodsItemFlag" ? ["N", "Y"] : [];
      if (allowed.length === 0) throw new Error(`Unknown allowed-value rule ${property}`);
    }
    rules.push({ scope, key: propertyKey, method, numbers, allowed, format: method === "ValidateExactDate" ? values[0] : "", label });
  }
}
if (rules.length < 140) throw new Error("Incomplete single-window validation reference.");
const payload = read(infrastructure + "SingleWindowPayloadGenerators.cs");
function fields(source, scope, owner) {
  return [...source.matchAll(new RegExp(`new XElement\\((?:ns \\+ )?"([^"]+)", (CustomsCooTextFormatter\\.EncodeXmlMultiline\\()?${owner}\\.(\\w+)`, "gu"))].map(([, tag, multiline, property]) => ({ tag, key: key(scope, property), multiline: Boolean(multiline) }));
}
const xml = {
  coo: fields(section(payload, "private static XElement CreateCertificateHead", "private static XElement? CreateOptionalModCertificate"), "coo", "document"),
  goods: fields(section(payload, "private static XElement CreateGoods", "private static XElement? CreateOptionalElement"), "goods", "item"),
  acd: fields(section(payload, "public sealed class AgentConsignmentXmlPayloadGenerator"), "acd", "document"),
};
if (xml.coo.length !== 63 || xml.goods.length !== 39 || xml.acd.length !== 24) throw new Error(`Incomplete XML field reference: ${JSON.stringify(Object.fromEntries(Object.entries(xml).map(([k,v])=>[k,v.length])))}`);
const state = read(application + "SingleWindowDraftStateHelper.Catalog.cs");
const sets = {};
for (const [, name, body] of state.matchAll(/HashSet<string> (\w+)[^{]+\{([^}]+)\}/gu)) {
  sets[name] = [...body.matchAll(/nameof\(\w+\.(\w+)\)/gu)].map((item) => item[1]);
}
const unitText = read(application + "SingleWindowUnitNormalizer.cs");
const units = {};
for (const [, name, body] of unitText.matchAll(/Dictionary<string, string> (\w+)[^{]+\{([^}]+)\}/gu)) {
  units[name] = Object.fromEntries([...body.matchAll(/\["([^"]+)"\] = "([^"]+)"/gu)].map(([, k, v]) => [k, v]));
}
const tradeText = read(application + "CustomsCooTradeModeCatalog.cs");
const cooTradeModes = Object.fromEntries([...tradeText.matchAll(/\["([^"]+)"\] = "([^"]+)"/gu)].map(([, k, v]) => [k, v]));
const output = JSON.stringify({ sources, options, origin, rules, labels, xml, sets, units, cooTradeModes }, null, 2) + "\n";
const target = path.join(root, "crates/export-doc-domain/resources/single-window-reference.json");
if (process.argv.includes("--check")) {
  if (readFileSync(target, "utf8") !== output) throw new Error("Native single-window reference data is stale.");
} else { mkdirSync(path.dirname(target), { recursive: true }); writeFileSync(target, output); }
process.stdout.write(`Single-window reference: ${rules.length} field rules, ${xml.coo.length + xml.goods.length + xml.acd.length} XML fields.\n`);
