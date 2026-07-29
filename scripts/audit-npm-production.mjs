import { readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const packageRoot = process.argv[2]
  ? path.resolve(process.cwd(), process.argv[2])
  : path.join(repositoryRoot, "apps", "export-doc-web");
const allowReviewedRouterRsc = process.argv.includes("--allow-reviewed-react-router-rsc");
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

if (allowReviewedRouterRsc && isReviewedReactRouterRscOnly(report)) {
  const contract = spawnSync(
    process.execPath,
    [path.join(repositoryRoot, "scripts", "test_react_router_declarative_contract.mjs")],
    { cwd: repositoryRoot, encoding: "utf8", windowsHide: true },
  );
  if (contract.error) throw contract.error;
  if (contract.status !== 0) {
    process.stderr.write(contract.stdout || "");
    process.stderr.write(contract.stderr || "");
    process.exit(contract.status ?? 1);
  }
  process.stdout.write(contract.stdout || "");
  process.stdout.write(
    "npm production audit passed with the reviewed GHSA-qwww-vcr4-c8h2 exception: " +
    "the application is pinned to react-router/react-router-dom 7.18.1 and does not expose RSC/server APIs.\n",
  );
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

function isReviewedReactRouterRscOnly(report) {
  const entries = Object.entries(report.vulnerabilities || {});
  if (entries.length === 0) return false;
  const allowedPackages = new Set(["react-router", "react-router-dom"]);
  if (entries.some(([name]) => !allowedPackages.has(name))) return false;

  const lock = JSON.parse(readFileSync(path.join(packageRoot, "package-lock.json"), "utf8"));
  for (const packageName of allowedPackages) {
    if (lock.packages?.[`node_modules/${packageName}`]?.version !== "7.18.1") return false;
  }

  let advisorySeen = false;
  for (const [, vulnerability] of entries) {
    if (vulnerability.severity !== "high") return false;
    for (const via of vulnerability.via || []) {
      if (typeof via === "string") {
        if (!allowedPackages.has(via)) return false;
        continue;
      }
      const advisoryIdentity = `${via.url || ""} ${via.title || ""} ${via.source || ""}`;
      if (!advisoryIdentity.includes("GHSA-qwww-vcr4-c8h2")) return false;
      advisorySeen = true;
    }
  }
  return advisorySeen;
}
