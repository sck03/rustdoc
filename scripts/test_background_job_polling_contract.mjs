import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const sourcePath = path.join(
  repositoryRoot,
  "apps",
  "export-doc-web",
  "src",
  "ui",
  "downloadJobResult.ts",
);
const source = fs.readFileSync(sourcePath, "utf8");
const assert = (condition, message) => {
  if (!condition) throw new Error(message);
};

assert(source.includes("options.pollIntervalMs ?? 1_000"), "polling should default to one second");
assert(source.includes("options.maxPollIntervalMs ?? 5_000"), "polling should expose a bounded maximum interval");
assert(source.includes("Math.ceil(currentPollIntervalMs * 1.5)"), "polling should back off between requests");
assert(source.includes("document.hidden"), "polling should pause while the document is hidden");
assert(source.includes("visibilitychange"), "polling should resume when the document becomes visible");
const hiddenChecks = source.match(/if \(typeof document !== "undefined" && document\.hidden\)/gu) ?? [];
assert(hiddenChecks.length >= 2, "polling should recheck visibility after a delay");
process.stdout.write("background job polling contract passed\n");
