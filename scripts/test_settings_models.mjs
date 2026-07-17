import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "settings-model-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.mjs");
fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });
const modelPath = path.join(repoRoot, "apps", "export-doc-web", "src", "features", "settings", "excelImportSettingsModel.ts").replaceAll("\\", "/");
const navigationPath = path.join(repoRoot, "apps", "export-doc-web", "src", "features", "settings", "settingsNavigationModel.ts").replaceAll("\\", "/");
const categoryCatalogPath = path.join(repoRoot, "apps", "export-doc-web", "src", "features", "settings", "settingsCategoryCatalog.ts").replaceAll("\\", "/");
const runtimeDiagnosticsPath = path.join(repoRoot, "apps", "export-doc-web", "src", "features", "settings", "runtimeDiagnosticsModel.ts").replaceAll("\\", "/");
const runtimeDependencyDiagnosticsPath = path.join(repoRoot, "apps", "export-doc-web", "src", "features", "settings", "runtimeDependencyDiagnosticsModel.ts").replaceAll("\\", "/");
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modelPath)}; import * as navigation from ${JSON.stringify(navigationPath)}; import * as categoryCatalog from ${JSON.stringify(categoryCatalogPath)}; import * as runtimeDiagnostics from ${JSON.stringify(runtimeDiagnosticsPath)}; import * as runtimeDependencyDiagnostics from ${JSON.stringify(runtimeDependencyDiagnosticsPath)}; globalThis.__model = model; globalThis.__navigation = navigation; globalThis.__categoryCatalog = categoryCatalog; globalThis.__runtimeDiagnostics = runtimeDiagnostics; globalThis.__runtimeDependencyDiagnostics = runtimeDependencyDiagnostics;`, "utf8");
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
await import(pathToFileURL(bundle).href);
const m = globalThis.__model;
const navigation = globalThis.__navigation;
const categoryCatalog = globalThis.__categoryCatalog;
const runtimeDiagnostics = globalThis.__runtimeDiagnostics;
const runtimeDependencyDiagnostics = globalThis.__runtimeDependencyDiagnostics;
const assert = (condition, message) => { if (!condition) throw new Error(message); };

const normalized = m.normalizeExcelImportSettings({ SchemeName: "Legacy", ItemsStartRow: "25", invoiceNoCell: null });
assert(normalized.schemeName === "Legacy", "PascalCase scheme name");
assert(normalized.itemsStartRow === 25, "numeric string conversion");
assert(normalized.invoiceNoCell === "O9", "null restores default");
assert(m.createDefaultExcelImportSettings(" ").schemeName === "Default", "blank default scheme name");
assert(m.createDefaultExcelImportSettings(" Custom ").schemeName === "Custom", "trim scheme name");

const settings = { excelImportSchemes: [
  { schemeName: "A", itemsStartRow: 21 },
  { SchemeName: "A", ItemsStartRow: 99 },
  { schemeName: "", itemsStartRow: 30 },
  { schemeName: "B", itemsStartRow: "31" },
] };
const schemes = m.readExcelImportSchemesForSettings(settings);
assert(schemes.length === 2 && schemes[0].itemsStartRow === 21 && schemes[1].itemsStartRow === 31, "scheme filtering and normalization");
assert(JSON.stringify(m.buildExcelSchemeOptions(schemes)) === JSON.stringify([{ value: "A", label: "A" }, { value: "B", label: "B" }]), "scheme options");
assert(m.readExcelImportRecordNumber({ ItemsStartRow: "42" }, "itemsStartRow") === 42, "record number PascalCase");
assert(m.readExcelImportRecordNumber({}, "itemsStartRow") === 20, "record number default");
assert(navigation.readSettingsCategoryFromSearch("?section=paymentTemplates") === "document-templates", "payment category");
assert(navigation.readSettingsCategoryFromSearch("?section=users") === "runtime", "users moved to independent access-control page");
assert(navigation.readSettingsCategoryFromSearch("?section=%20diagnostics%20") === "maintenance", "trimmed category");
assert(navigation.readSettingsPanelLabelFromSearch("?section=backup") === "数据备份与还原", "backup panel");
assert(navigation.readSettingsPanelLabelFromSearch("?section=diagnostics") === "运行诊断", "diagnostics panel");
assert(navigation.readSettingsCategoryFromSearch("?section=ownership") === "maintenance", "ownership maintenance category");
assert(navigation.readSettingsPanelLabelFromSearch("?section=unknown") === null, "unknown panel");
const salesEditionCategories = categoryCatalog.filterSettingsCategories({ canUseDocumentWorkspace: false });
assert(JSON.stringify(salesEditionCategories.map((item) => item.key)) === JSON.stringify(["runtime", "exchange-rate", "communication", "maintenance"]), "sales edition settings are focused on common runtime tasks");
const documentEditionCategories = categoryCatalog.filterSettingsCategories({ canUseDocumentWorkspace: true });
assert(documentEditionCategories.some((item) => item.key === "document-templates"), "document edition keeps document settings");
assert(!documentEditionCategories.some((item) => item.key === "users"), "single-role edition hides user management");
assert(navigation.readSettingsCategoryFromSearch("?section=singleWindow", salesEditionCategories.map((item) => item.key)) === "runtime", "sales edition deep link falls back from document settings");

const runtimeGroups = runtimeDiagnostics.buildRuntimePathGroups({
  appRoot: "E:/App",
  checkedAt: "2026-07-13T00:00:00Z",
  dataRoot: "E:/App/App_Data",
  databaseRoot: "E:/App/App_Data/Database",
  informationalVersion: "1.0.0+test",
  productVersion: "1.0.0",
  runtimePaths: [
    { key: "template-root", label: "报表模板", path: "E:/App/Templates", storageClass: "program-resource", accessMode: "managed", requirement: "feature", exists: true, description: "模板" },
    { key: "log-root", label: "日志目录", path: "E:/App/App_Data/Logs", storageClass: "runtime-data", accessMode: "read-write", requirement: "core", exists: true, description: "日志" },
    { key: "tool-root", label: "工具运行包", path: "E:/App/Tools", storageClass: "program-resource", accessMode: "read-only", requirement: "optional", exists: false, description: "工具" },
  ],
  runtimeDependencies: [
    { key: "report-renderer", label: "报表 PDF 浏览器", requirement: "feature", status: "ready", ready: true, resolvedPath: "E:/App/Browsers/chrome.exe", message: "ready" },
    { key: "ocr-runtime", label: "智能 OCR", requirement: "optional", status: "missing", ready: false, resolvedPath: "E:/App/OcrModels/PaddleOCR/V6", message: "missing" },
  ],
  status: "ok",
});
assert(runtimeGroups.length === 2, "runtime path grouping");
assert(runtimeGroups[0].items[0].availability === "available", "runtime available status");
assert(runtimeGroups[0].items[1].availability === "missing", "runtime missing status");
assert(runtimeDiagnostics.runtimePathAccessModeLabel("managed") === "功能维护", "managed access label");
const runtimeSummary = runtimeDiagnostics.summarizeRuntimePathGroups(runtimeGroups);
assert(runtimeSummary.total === 3, "runtime path summary");
assert(runtimeSummary.coreTotal === 1 && runtimeSummary.coreMissing === 0, "optional dependency does not fail core readiness");
assert(runtimeSummary.featureMissing === 0, "feature dependency summary");
assert(runtimeDiagnostics.runtimePathRequirementLabel("feature") === "按功能需要", "runtime requirement label");
const runtimeDependencies = runtimeDependencyDiagnostics.buildRuntimeDependencyItems({
  appRoot: "E:/App",
  checkedAt: "2026-07-13T00:00:00Z",
  dataRoot: "E:/App/App_Data",
  databaseRoot: "E:/App/App_Data/Database",
  informationalVersion: "1.0.0+test",
  productVersion: "1.0.0",
  runtimeDependencies: [
    { key: "report-renderer", label: "报表 PDF 浏览器", requirement: "feature", status: "ready", ready: true, resolvedPath: "E:/App/Browsers/chrome.exe", message: "ready" },
    { key: "ocr-runtime", label: "智能 OCR", requirement: "optional", status: "disabled", ready: false, resolvedPath: "E:/App/OcrModels/PaddleOCR/V6", message: "disabled" },
  ],
  runtimePaths: [],
  status: "ok",
});
const runtimeDependencySummary = runtimeDependencyDiagnostics.summarizeRuntimeDependencies(runtimeDependencies);
assert(runtimeDependencySummary.total === 2 && runtimeDependencySummary.ready === 1, "runtime dependency summary");
assert(runtimeDependencySummary.featureUnavailable === 0 && runtimeDependencySummary.optionalUnavailable === 1, "optional runtime dependency summary");
assert(runtimeDependencyDiagnostics.runtimeDependencyStatusLabel("incomplete") === "文件不完整", "runtime dependency status label");
process.stdout.write("settings-model tests passed\n");
