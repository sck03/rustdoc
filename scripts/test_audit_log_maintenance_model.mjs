import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "audit-log-maintenance-model-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.cjs");
fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });

const modelPath = path
  .join(repoRoot, "apps", "export-doc-web", "src", "features", "audit-logs", "auditLogMaintenanceModel.ts")
  .replaceAll("\\", "/");
const panelPath = path
  .join(repoRoot, "apps", "export-doc-web", "src", "features", "audit-logs", "AuditLogMaintenancePanel.tsx")
  .replaceAll("\\", "/");
fs.writeFileSync(
  entry,
  `import React from "react";
   import { renderToStaticMarkup } from "react-dom/server";
   import * as model from ${JSON.stringify(modelPath)};
   import { AuditLogMaintenancePanel } from ${JSON.stringify(panelPath)};
   const shared = {
     currentResultCount: 12,
     filterSummary: ["实体为“发票”"],
     exportPath: "E:/Exports/AuditLogs.xlsx",
     retentionDays: "180",
     isBusy: false,
     canExport: true,
     canDownload: true,
     canDeleteFiltered: true,
     canCleanup: true,
     onExportPathChange() {}, onSelectExportPath() {}, onExport() {}, onDownload() {}, onRetentionDaysChange() {},
     async onDeleteFiltered() {}, async onCleanup() {}, onActionError() {}
   };
   globalThis.__auditLogMaintenanceModel = model;
   globalThis.__auditLogMaintenanceMarkup = {
     browser: renderToStaticMarkup(React.createElement(AuditLogMaintenancePanel, { ...shared, isDesktopRuntime: false })),
     desktop: renderToStaticMarkup(React.createElement(AuditLogMaintenancePanel, { ...shared, isDesktopRuntime: true }))
   };`,
  "utf8",
);

const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({
  entryPoints: [entry],
  outfile: bundle,
  bundle: true,
  format: "cjs",
  platform: "node",
  nodePaths: [path.join(repoRoot, "apps", "export-doc-web", "node_modules")],
  logLevel: "silent",
});
await import(pathToFileURL(bundle).href);

const model = globalThis.__auditLogMaintenanceModel;
const markup = globalThis.__auditLogMaintenanceMarkup;
const assert = (condition, message) => {
  if (!condition) throw new Error(message);
};
const emptyFilters = {
  invoiceKeyword: "",
  entityName: "",
  action: "",
  userId: "",
  keyword: "",
  startTime: "",
  endTime: "",
};

assert(!model.hasActiveAuditLogFilters(emptyFilters), "empty filters must not enable targeted deletion");
assert(model.hasActiveAuditLogFilters({ ...emptyFilters, userId: " admin " }), "operator filter enables targeted deletion");
assert(model.hasActiveAuditLogFilters({ ...emptyFilters, startTime: "2026-07-01T00:00:00.000Z" }), "date filter enables targeted deletion");

const descriptions = model.describeAuditLogFilters(
  {
    ...emptyFilters,
    entityName: "Invoice",
    action: "Deleted",
    keyword: "INV-100",
  },
  {
    entityLabels: { Invoice: "发票" },
    actionLabels: { Deleted: "删除" },
  },
);
assert(descriptions.includes("实体为“发票”"), "entity description uses business label");
assert(descriptions.includes("动作为“删除”"), "action description uses business label");
assert(descriptions.includes("关键字包含“INV-100”"), "keyword description is explicit");
assert(markup.browser.includes("下载 Excel"), "browser mode exposes a download action");
assert(markup.browser.includes("浏览器的默认下载目录"), "browser mode explains the download destination");
assert(!markup.browser.includes('placeholder="AuditLogs.xlsx"'), "browser mode never renders a server path input");
assert(!markup.browser.includes("选择位置"), "browser mode hides desktop path selection");
assert(markup.desktop.includes('placeholder="AuditLogs.xlsx"'), "desktop mode keeps the explicit path field");
assert(markup.desktop.includes("选择位置"), "desktop mode keeps the native save dialog action");
assert(markup.desktop.includes("导出到文件"), "desktop mode labels path export clearly");

process.stdout.write("audit-log-maintenance-model tests passed\n");
