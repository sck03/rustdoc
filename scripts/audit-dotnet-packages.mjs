import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { resolveDotnetCommand } from "./lib/dotnet-command.mjs";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const result = spawnSync(
  resolveDotnetCommand(),
  ["list", "ExportDocManager.sln", "package", "--vulnerable", "--include-transitive", "--format", "json"],
  { cwd: repositoryRoot, encoding: "utf8", windowsHide: true },
);

if (result.error) throw result.error;
if (result.status !== 0) {
  process.stderr.write(result.stdout || "");
  process.stderr.write(result.stderr || "");
  process.exit(result.status ?? 1);
}

const output = String(result.stdout || "");
const vulnerabilities = [];
try {
  const document = JSON.parse(output.slice(output.indexOf("{")));
  collectVulnerabilities(document, vulnerabilities);
} catch (error) {
  if (/has the following vulnerable packages/iu.test(output)) {
    process.stderr.write(output);
    process.exit(1);
  }
  throw new Error(`Unable to parse dotnet vulnerability report: ${error.message}`);
}

if (vulnerabilities.length > 0) {
  process.stderr.write("NuGet vulnerability audit failed:\n");
  for (const item of vulnerabilities) {
    process.stderr.write(`- ${item.id}@${item.version}: ${item.advisories.join(", ")}\n`);
  }
  process.exit(1);
}

process.stdout.write("NuGet vulnerability audit passed (including transitive packages).\n");

function collectVulnerabilities(value, target) {
  if (!value || typeof value !== "object") return;
  if (Array.isArray(value)) {
    for (const item of value) collectVulnerabilities(item, target);
    return;
  }

  if (Array.isArray(value.vulnerabilities) && value.vulnerabilities.length > 0) {
    target.push({
      id: value.id || value.name || "unknown-package",
      version: value.resolvedVersion || value.version || "unknown-version",
      advisories: value.vulnerabilities.map((item) => item.advisoryUrl || item.url || item.severity || JSON.stringify(item)),
    });
  }
  for (const nested of Object.values(value)) collectVulnerabilities(nested, target);
}
