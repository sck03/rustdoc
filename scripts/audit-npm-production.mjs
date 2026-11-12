import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { classifyNpmAudit } from "./lib/dependency-audit.mjs";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const packageRoot = process.argv[2]
  ? path.resolve(process.cwd(), process.argv[2])
  : path.join(repositoryRoot, "apps", "export-doc-web");
const npmExecPath = String(process.env.npm_execpath || "").trim();
const npmCommand = npmExecPath ? process.execPath : "npm";
const npmArguments = [
  ...(npmExecPath ? [npmExecPath] : []),
  "audit",
  "--package-lock-only",
  "--omit=dev",
  "--audit-level=high",
  "--json",
  "--registry=https://registry.npmjs.org",
];
const audit = spawnSync(
  npmCommand,
  npmArguments,
  {
    cwd: packageRoot,
    encoding: "utf8",
    windowsHide: true,
    shell: !npmExecPath && process.platform === "win32",
    timeout: 120_000,
    maxBuffer: 8 * 1024 * 1024,
  },
);

try {
  const result = classifyNpmAudit(audit);
  if (result.state !== "passed") process.stderr.write(`${JSON.stringify(result.report, null, 2)}\n`);
  process.stdout.write(`npm production audit ${result.state}: ${path.relative(repositoryRoot, packageRoot)} (blocks high/critical).\n`);
  process.exitCode = result.exitCode;
} catch (error) {
  process.stderr.write(`npm production audit execution failed: ${error.message}\n`);
  process.exitCode = 2;
}
