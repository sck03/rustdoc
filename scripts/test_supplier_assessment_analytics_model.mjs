import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "supplier-assessment-analytics-model-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.mjs");
fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });

const modelPath = path.join(repoRoot, "apps", "export-doc-web", "src", "features", "suppliers", "supplierAssessmentAnalyticsModel.ts").replaceAll("\\", "/");
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modelPath)}; globalThis.__model = model;`, "utf8");
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
await import(pathToFileURL(bundle).href);

const { buildSupplierAssessmentAnalytics } = globalThis.__model;
const assert = (condition, message) => { if (!condition) throw new Error(message); };
const empty = buildSupplierAssessmentAnalytics([]);
assert(empty.totalCount === 0 && empty.latestAverage === null && empty.dimensions.length === 0, "empty analytics");

const rows = [
  { id: 1, assessedAt: "2026-01-10T12:00:00Z", qualityScore: 4, deliveryScore: 2, serviceScore: 3, priceScore: 5, averageScore: 3.5, conclusion: "观察" },
  { id: 3, assessedAt: "2026-03-10T12:00:00Z", qualityScore: 5, deliveryScore: 4, serviceScore: 5, priceScore: 4, averageScore: 4.5, conclusion: "优先合作" },
  { id: 2, assessedAt: "2026-02-10T12:00:00Z", qualityScore: 5, deliveryScore: 3, serviceScore: 4, priceScore: 3, averageScore: 3.75, conclusion: "合格" },
];
const analytics = buildSupplierAssessmentAnalytics(rows);
assert(analytics.totalCount === 3 && analytics.latestAverage === 4.5, "latest score and count");
assert(analytics.changeFromPrevious === 0.75, "change from previous assessment");
assert(analytics.strongestDimension.label === "质量" && analytics.strongestDimension.average === 4.67, "strongest dimension");
assert(analytics.weakestDimension.label === "交期" && analytics.weakestDimension.average === 3, "weakest dimension");
assert(analytics.conclusions.reduce((sum, item) => sum + item.count, 0) === 3, "conclusion distribution count");
assert(analytics.trend.map((item) => item.id).join(",") === "1,2,3", "trend chronological order");
process.stdout.write("supplier-assessment-analytics-model tests passed\n");
