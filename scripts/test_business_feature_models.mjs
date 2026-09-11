import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { pathToFileURL } from "node:url";

const repo = path.resolve(import.meta.dirname, "..");
const web = path.join(repo, "apps/export-doc-web");
const require = createRequire(path.join(web, "package.json"));
const output = path.join(repo, ".codex-runtime/business-feature-models");
fs.mkdirSync(output, { recursive: true });
const source = name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/"));
const bundle = path.join(output, "models.mjs");
await require("esbuild").build({ stdin: { loader: "ts", resolveDir: web, contents: `
  export * as attachments from ${source("features/attachments/attachmentModel.ts")};
  export * as worklist from ${source("features/worklist/worklistModel.ts")};
  export { filterWorkspaceNavGroups } from ${source("app/workspaceNavigation.ts")};
  export { isRouteAccessAllowed } from ${source("app/routeAccess.ts")};
  export * as review from ${source("features/invoices/invoiceReviewModel.ts")};
  export { createEmptyInvoice } from ${source("features/invoices/invoiceModel.ts")};
` }, outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
const model = await import(pathToFileURL(bundle).href);
const permissions = [{ resourceKey: "document.invoices", action: "view", dataScope: "own" }];
const capabilities = { productEdition: "Full", enabledModules: ["document.invoices"], permissions, canUseDocumentWorkspace: true };
for (const [feature, pathname] of [["worklist", "/worklist"], ["business-attachments", "/business-attachments"], ["business-attachments", "/invoices/17/attachments"]]) {
  const args = { pathname, user: { capabilities }, canManageSystem: false, isDesktopRuntime: true };
  assert.equal(model.isRouteAccessAllowed(args), false, "missing feature information must deny a deep link");
  assert.equal(model.isRouteAccessAllowed({ ...args, user: { capabilities: { ...capabilities, availableFeatures: [feature] } } }), true);
}
assert(!model.filterWorkspaceNavGroups(capabilities).flatMap(group => group.items).some(item => item.to === "/worklist"));
assert(model.filterWorkspaceNavGroups({ ...capabilities, availableFeatures: ["worklist"] }).flatMap(group => group.items).some(item => item.to === "/worklist"));
const archiveNavigation = model.filterWorkspaceNavGroups({ ...capabilities, availableFeatures: ["business-attachments"] }).flatMap(group => group.items);
assert.deepEqual(archiveNavigation.filter(item => item.isActive("/invoices/17/attachments")).map(item => item.to), ["/business-attachments"]);
assert.equal(model.worklist.worklistTarget({ source: "customer-follow-up", recordId: 79 }), "/crm/follow-ups?followUpId=79");
assert.equal(model.worklist.worklistTarget({ source: "supply-return", recordId: 23 }), "/office/supplies?view=requests&requestId=23");
assert.equal(model.worklist.worklistTarget({ source: "contract-end", recordId: 5 }), "/office/people/5");
assert.equal(model.worklist.worklistTarget({ source: "unknown", recordId: 1 }), null);
assert.equal(model.worklist.worklistDueLabel({}, "Asia/Shanghai"), "未设截止日期");
assert.equal(model.worklist.worklistDueLabel({ dueDate: "2026-09-08" }, "America/New_York"), "2026-09-08", "natural days must not shift with time zone conversion");
assert.equal(model.attachments.canPreviewAttachment("text/html"), false);
assert.equal(model.attachments.canPreviewAttachment("image/svg+xml"), false);
assert.equal(model.attachments.canPreviewAttachment("application/pdf"), true);
assert.match(model.attachments.attachmentVersionLabel({ currentRevision: 1, latestRevision: 2 }), /v1/);
const file = new File(["客户原始资料"], "café.txt", { type: "text/plain" });
const request = model.attachments.attachmentUploadRequest({ invoiceId: 9, uploadKey: "key", title: "客户资料", categoryId: 3, note: "样".repeat(500), file });
assert.equal(request.invoiceId, 9);
assert.equal(request.body.get("file").name, "café.txt");
assert.equal(request.body.get("note"), "样".repeat(500));
assert.equal(request.body.get("categoryId"), "3");
assert.deepEqual(model.attachments.attachmentMetadata({ title: "图纸", categoryId: 7, poNumber: "PO", styleNo: "STYLE", latestRevision: 2 }),
  { title: "图纸", categoryId: 7, poNumber: "PO", styleNo: "STYLE" });
assert(!request.body.has("invoiceId"));
assert(!request.body.has("attachmentId"));
const draft = model.createEmptyInvoice("2026-09-08");
draft.items = [{ styleName: "BOLT", quantity: 1 }, {}, { styleNo: "NEW STYLE", quantity: 0 }];
const review = model.review.prepareInvoiceReview(draft);
assert.deepEqual(review.sourceRows, [1, 3]);
assert.equal(model.review.invoiceReviewIssueLabel({ rowNumber: 2, field: "quantity", message: "数量必须大于 0。" }, review.sourceRows), "第 3 行：数量必须大于 0。");
process.stdout.write("Business feature model contracts passed.\n");
