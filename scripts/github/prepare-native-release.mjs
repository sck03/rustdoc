import { appendFileSync } from "node:fs";
import { createReleasePlan } from "../lib/native-release-plan.mjs";

const plan = createReleasePlan({ product: process.env.RELEASE_PRODUCT, os: process.env.RELEASE_OS,
  architecture: process.env.RELEASE_ARCHITECTURE, version: process.env.RELEASE_VERSION,
  repository: process.env.GITHUB_REPOSITORY });
if (process.env.PUBLISH_LATEST === "true" && (process.env.PUBLISH !== "true"
    || process.env.RELEASE_ARCHITECTURE !== "all" || plan.version.includes("-"))) {
  throw new Error("latest 仅允许在发布全部架构的稳定版本时更新。");
}
if (!process.env.GITHUB_OUTPUT) throw new Error("GITHUB_OUTPUT is required.");
for (const [key, value] of Object.entries(plan)) {
  appendFileSync(process.env.GITHUB_OUTPUT, `${key}=${typeof value === "string" ? value : JSON.stringify(value)}\n`);
}
console.log(JSON.stringify(plan, null, 2));
