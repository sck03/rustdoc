import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "reference-catalog-model-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.mjs");
fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });
const modelPath = path.join(repoRoot, "apps", "export-doc-web", "src", "features", "single-window", "referenceCatalogModel.ts").replaceAll("\\", "/");
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modelPath)}; globalThis.__model = model;`, "utf8");
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
await import(pathToFileURL(bundle).href);

const m = globalThis.__model;
const assert = (condition, message) => { if (!condition) throw new Error(message); };

assert(JSON.stringify(m.normalizeAliases([" China ", "china", "中国", ""])) === JSON.stringify(["China", "中国"]), "alias normalization");
assert(JSON.stringify(m.parsePastedTableRows("A\tB\r\nC\tD\r\n")) === JSON.stringify([["A", "B"], ["C", "D"]]), "pasted table parsing");

const catalog = m.normalizeCatalog({
  countries: [
    { code: "CN", englishName: "China", chineseName: "中国", aliases: ["PRC"] },
    { code: "cn", englishName: "", chineseName: "中华人民共和国", aliases: ["China"] },
  ],
  currencies: [
    { code: "156", acdCode: "CNY", alphaCode: "CNY", aliases: [] },
    { code: "840", acdCode: "cny", alphaCode: "USD", aliases: [] },
  ],
});
const errors = m.validateCatalog(catalog);
assert(errors.some((item) => item.includes("COO国家/地区代码存在重复值")), "country duplicate validation");
assert(errors.some((item) => item.includes("ACD海关币制码存在重复值")), "ACD currency duplicate validation");

const countriesPage = m.catalogPages.find((page) => page.key === "countries");
const deduplicated = m.deduplicatePageRows(catalog.countries, countriesPage);
assert(deduplicated.length === 1, "duplicate rows merged");
assert(deduplicated[0].chineseName === "中国", "existing non-empty field preserved");
assert(JSON.stringify(deduplicated[0].aliases) === JSON.stringify(["PRC", "China"]), "aliases merged");

process.stdout.write("reference catalog model tests passed\n");
