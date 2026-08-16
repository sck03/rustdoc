import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "invoice-text-normalization-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.mjs");
fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });

const modelPath = path
  .join(repoRoot, "apps", "export-doc-web", "src", "features", "invoices", "invoiceModel.ts")
  .replaceAll("\\", "/");
const hsModelPath = path
  .join(repoRoot, "apps", "export-doc-web", "src", "features", "invoices", "invoiceHsKnowledgeModel.ts")
  .replaceAll("\\", "/");
const itemModelPath = path
  .join(repoRoot, "apps", "export-doc-web", "src", "features", "invoices", "invoiceItemsEditorModel.ts")
  .replaceAll("\\", "/");
const productLibraryPath = path
  .join(repoRoot, "apps", "export-doc-web", "src", "features", "invoices", "invoiceProductLibrary.ts")
  .replaceAll("\\", "/");
const formUtilsPath = path
  .join(repoRoot, "apps", "export-doc-web", "src", "ui", "formUtils.ts")
  .replaceAll("\\", "/");
const draftEqualityPath = path
  .join(repoRoot, "apps", "export-doc-web", "src", "features", "invoices", "invoiceDraftEquality.ts")
  .replaceAll("\\", "/");
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modelPath)}; import * as hsModel from ${JSON.stringify(hsModelPath)}; import * as itemModel from ${JSON.stringify(itemModelPath)}; import * as productLibrary from ${JSON.stringify(productLibraryPath)}; import * as formUtils from ${JSON.stringify(formUtilsPath)}; import * as draftEquality from ${JSON.stringify(draftEqualityPath)}; globalThis.__model = model; globalThis.__hsModel = hsModel; globalThis.__itemModel = itemModel; globalThis.__productLibrary = productLibrary; globalThis.__formUtils = formUtils; globalThis.__draftEquality = draftEquality;`, "utf8");
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
await import(pathToFileURL(bundle).href);

const model = globalThis.__model;
const hsModel = globalThis.__hsModel;
const itemModel = globalThis.__itemModel;
const productLibrary = globalThis.__productLibrary;
const formUtils = globalThis.__formUtils;
const draftEquality = globalThis.__draftEquality;
const assert = (condition, message) => { if (!condition) throw new Error(message); };
const draft = model.uppercaseInvoiceEnglishText({
  ...model.createEmptyInvoice(),
  invoiceNo: "2026yh024",
  customerNameEN: "Peak Marketing",
  customerAddressEN: "1/40 Yarraman Place, Brisbane, Australia",
  exporterNameCN: "宁波布利杰进出口有限公司",
  destinationCountry: "australia",
  tradeTerms: "fob",
  items: [{
    id: 0,
    invoiceId: 0,
    styleNo: "tee-a1",
    styleName: "men's cotton t-shirt",
    styleNameCN: "棉制男式T恤衫",
    fabricComposition: "100% cotton",
    unitEN: "pcs",
    unitCN: "件",
    quantity: 10,
    unitPrice: 2,
    totalPrice: 20,
  }],
});
assert(draft.invoiceNo === "2026YH024", "invoice number uppercased");
assert(draft.customerNameEN === "PEAK MARKETING", "customer name uppercased");
assert(draft.customerAddressEN === "1/40 YARRAMAN PLACE, BRISBANE, AUSTRALIA", "address uppercased");
assert(draft.destinationCountry === "AUSTRALIA" && draft.tradeTerms === "FOB", "shipping fields uppercased");
assert(draft.exporterNameCN === "宁波布利杰进出口有限公司", "Chinese exporter name preserved");
assert(draft.items[0].styleName === "MEN'S COTTON T-SHIRT", "item description uppercased");
assert(draft.items[0].fabricComposition === "100% COTTON", "item composition uppercased");
assert(draft.items[0].styleNameCN === "棉制男式T恤衫" && draft.items[0].unitCN === "件", "Chinese item fields preserved");
const persistedDraft = model.normalizeInvoiceForSave(draft, 7);
const changedDraft = { ...persistedDraft, customerNameEN: "TEMPORARY CUSTOMER" };
assert(!draftEquality.areInvoiceDraftsEqual(changedDraft, persistedDraft), "invoice dirty comparison detects a changed field");
const restoredDraft = { ...changedDraft, customerNameEN: persistedDraft.customerNameEN };
assert(draftEquality.areInvoiceDraftsEqual(restoredDraft, persistedDraft), "invoice dirty comparison clears after restoring the saved value");
assert(!draftEquality.areInvoiceDraftsEqual({ ...persistedDraft, items: [{ ...persistedDraft.items[0], quantity: 11 }] }, persistedDraft), "invoice dirty comparison detects nested item changes");
assert(formUtils.toDateInputValue("2026-07-29") === "2026-07-29", "business date keeps the DateOnly wire format");
assert(formUtils.toDateInputValue("2026-07-29T00:00:00") === "", "business date rejects date-time compatibility values");
assert(formUtils.dateInputToApiDate("2026-07-29") === "2026-07-29", "date inputs submit the DateOnly wire format");
assert(formUtils.currentLocalDateInputValue(new Date(2026, 6, 29, 0, 30, 0)) === "2026-07-29", "local date input uses the operator calendar day instead of UTC");

const imported = model.readRouteInvoiceDraft({ invoiceDraft: { ...draft, customerNameEN: "mixed Case buyer" } });
assert(imported?.customerNameEN === "MIXED CASE BUYER", "routed Excel draft uppercased automatically");
assert(model.canDeleteInvoiceStatus("Draft") === true, "draft invoice can be physically deleted");
assert(model.canDeleteInvoiceStatus("Verified") === false, "verified invoice cannot be physically deleted");
assert(model.canDeleteInvoiceStatus("Shipped") === false, "shipped invoice cannot be physically deleted");
assert(model.canDeleteInvoiceStatus("Completed") === false, "completed invoice cannot be physically deleted");
assert(model.canDeleteInvoiceStatus("Cancelled") === false, "cancelled invoice remains for audit");
assert(model.canUnverifyInvoiceStatus("Verified") === true, "verified invoice can return to draft through managed unverify");
assert(model.canUnverifyInvoiceStatus("Cancelled") === false, "cancelled invoice cannot return to draft and bypass audit retention");
assert(model.isInvoiceEditableStatus("Cancelled") === false, "cancelled invoice remains locked");
assert(hsModel.buildInvoiceHsQuery({ hsCode: "6110", styleNameCN: "化纤制套头衫" }) === "6110", "HS code prefix takes priority");
assert(hsModel.buildInvoiceHsQuery({ hsCode: "61", styleNameCN: "化纤制套头衫", styleName: "PULLOVER" }) === "化纤制套头衫 PULLOVER", "short HS code falls back to product names");
const feedbackContext = hsModel.buildInvoiceHsFeedbackContext({
  styleNo: "YLAW1320-2",
  styleNameCN: "化纤制针织女式非起绒套头衫",
  styleName: "LADIES PULLOVER",
  fabricComposition: "51%涤44%棉5%氨纶",
  brand: "PETROL INDUSTRIES",
}, "候选标准名称", "候选规格");
assert(feedbackContext.productName === "化纤制针织女式非起绒套头衫", "HS feedback learns the current invoice product name");
assert(feedbackContext.specification.includes("LADIES PULLOVER") && feedbackContext.specification.includes("51%涤44%棉5%氨纶") && feedbackContext.specification.includes("PETROL INDUSTRIES"), "HS feedback keeps reusable product attributes");
assert(!feedbackContext.specification.includes("YLAW1320-2"), "HS feedback does not split identical products by style number");
assert(itemModel.invoiceItemUnitPriceDisplayValue(12) === "12.00", "ordinary unit price uses two decimals");
assert(itemModel.invoiceItemUnitPriceDisplayValue(12.3) === "12.30", "ordinary fractional unit price uses two decimals");
assert(itemModel.invoiceItemUnitPriceDisplayValue(100 / 3) === "33.33333", "derived unit price uses five decimals");
assert(itemModel.invoiceItemUnitPriceDisplayValue(14.2857) === "14.2857", "extended unit price trims insignificant trailing zeroes");
assert(itemModel.invoiceItemUnitPriceDisplayValue(14.225) === "14.225", "extended unit price keeps meaningful three decimals");
assert(itemModel.invoiceItemWeightDisplayValue(1.236) === "1.24", "gross/net weight uses two decimals");
assert(itemModel.invoiceItemVolumeDisplayValue(1.23456) === "1.235", "volume uses three decimals");
const productSourceItem = {
  ...itemModel.createEmptyInvoiceItem(7),
  styleNo: " SKU-001 ",
  styleName: "Existing product",
  styleNameCN: "已有商品",
};
const existingProduct = {
  id: 42,
  productCode: "SKU-001",
  nameEN: "Old product",
  nameCN: "旧商品",
  description: "Preserved description",
  rowVersion: "concurrency-token-42",
};
const updatedProductDraft = productLibrary.createProductDraftFromInvoiceItem(productSourceItem, existingProduct);
assert(updatedProductDraft.id === 42, "existing product update preserves id");
assert(updatedProductDraft.rowVersion === "concurrency-token-42", "existing product update preserves row version");
const newProductDraft = productLibrary.createProductDraftFromInvoiceItem(productSourceItem);
assert(newProductDraft.id === 0, "new product draft uses zero id");
assert(newProductDraft.rowVersion === "", "new product draft starts without a row version");
process.stdout.write("invoice-text-normalization model tests passed\n");
