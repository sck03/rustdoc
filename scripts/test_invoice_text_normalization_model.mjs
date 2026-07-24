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
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modelPath)}; import * as hsModel from ${JSON.stringify(hsModelPath)}; globalThis.__model = model; globalThis.__hsModel = hsModel;`, "utf8");
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
await import(pathToFileURL(bundle).href);

const model = globalThis.__model;
const hsModel = globalThis.__hsModel;
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

const imported = model.readRouteInvoiceDraft({ invoiceDraft: { ...draft, customerNameEN: "mixed Case buyer" } });
assert(imported?.customerNameEN === "MIXED CASE BUYER", "routed Excel draft uppercased automatically");
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
process.stdout.write("invoice-text-normalization model tests passed\n");
