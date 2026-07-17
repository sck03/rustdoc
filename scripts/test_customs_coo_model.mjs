import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "customs-coo-model-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.mjs");
fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });
const modelPath = path.join(repoRoot, "apps", "export-doc-web", "src", "features", "single-window", "customsCooModel.ts").replaceAll("\\", "/");
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modelPath)}; globalThis.__model = model;`, "utf8");
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
await import(pathToFileURL(bundle).href);

const m = globalThis.__model;
const assert = (condition, message) => { if (!condition) throw new Error(message); };

assert(m.shouldShowCooHeaderField({ certType: "H" }, "ExporterEmail"), "H certificate contact field");
assert(!m.shouldShowCooHeaderField({ certType: "A" }, "ExporterEmail"), "non-H certificate contact field");
assert(m.shouldShowCooGoodsOriginCriteriaRef("G", "W"), "G/W origin reference");
assert(!m.shouldShowCooGoodsOriginCriteriaRef("G", "P"), "G/P origin reference hidden");
assert(m.buildCooGoodsDescription({ packQty: "2", packUnit: "CTN", goodsNameE: "shirts" }) === "TWO (2) CARTONS OF SHIRTS", "goods description generation");
assert(m.buildCooPackingSummary("1", "PCS") === "ONE (1) PIECE", "singular packing summary");
assert(m.normalizeCooEnglishUnit("cartons") === "CTN", "packing unit normalization");
assert(m.resolveCooAttachmentFileType("COMMERCIAL INVOICE.pdf") === "1", "invoice attachment type");
assert(m.resolveCooAttachmentFileType("customs declaration.pdf") === "4", "declaration attachment type");
assert(m.fileNameFromPath("E:\\Docs\\invoice.pdf") === "invoice.pdf", "Windows file name extraction");

const normalized = m.normalizeCooDocumentForSave({
  id: undefined,
  items: [
    { gNo: 0, goodsNameE: " SHIRTS ", packQty: "2", packUnit: "ctn" },
    { gNo: 0 },
  ],
  nonpartyCorps: [{ sortNo: 0, entName: " Factory " }, { sortNo: 0 }],
  attachments: [{ fileName: " invoice.pdf ", filePath: " E:/Docs/invoice.pdf " }],
}, 19);
assert(normalized.sourceInvoiceId === 19, "source invoice normalization");
assert(normalized.items.length === 1 && normalized.items[0].gNo === 1, "empty COO item filtering");
assert(normalized.nonpartyCorps.length === 1 && normalized.nonpartyCorps[0].sortNo === 1, "empty nonparty filtering");
assert(normalized.attachments[0].id === 0 && normalized.attachments[0].sortOrder === 0, "attachment numeric normalization");

process.stdout.write("customs-coo model tests passed\n");
