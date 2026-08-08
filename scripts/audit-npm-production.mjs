import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const packageRoot = process.argv[2]
  ? path.resolve(process.cwd(), process.argv[2])
  : path.join(repositoryRoot, "apps", "export-doc-web");
const npmExecPath = String(process.env.npm_execpath || "").trim();
const npmCommand = npmExecPath ? process.execPath : "npm";
const npmArguments = [
  ...(npmExecPath ? [npmExecPath] : []),
  "audit",
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
  },
);

if (audit.error) throw audit.error;
const report = parseAuditReport(audit.stdout);
const vulnerabilities = Object.values(report.vulnerabilities || {});
if ((audit.status ?? 1) === 0 && vulnerabilities.length === 0) {
  process.stdout.write(`npm production audit passed: ${path.relative(repositoryRoot, packageRoot)}.\n`);
  process.exit(0);
}

process.stderr.write(`${JSON.stringify(report, null, 2)}\n`);
process.stderr.write(`npm production audit failed: ${path.relative(repositoryRoot, packageRoot)}.\n`);
process.exit(audit.status ?? 1);

function parseAuditReport(output) {
  const text = String(output || "").trim();
  if (!text) return {};
  const jsonStart = text.indexOf("{");
  if (jsonStart < 0) throw new Error(`npm audit did not return JSON: ${text}`);
  return JSON.parse(text.slice(jsonStart));
}
