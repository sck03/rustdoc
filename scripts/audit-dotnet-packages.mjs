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
  if (!isNuGetVulnerabilityMetadataUnavailable(result)) {
    writeCommandFailure(result);
    process.exit(result.status ?? 1);
  }
  await auditWithOsvFallback(result);
  process.exit(0);
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

async function auditWithOsvFallback(sourceAudit) {
  const packageList = spawnSync(
    resolveDotnetCommand(),
    ["list", "ExportDocManager.sln", "package", "--include-transitive", "--format", "json", "--no-restore"],
    { cwd: repositoryRoot, encoding: "utf8", windowsHide: true },
  );
  if (packageList.error) throw packageList.error;
  if (packageList.status !== 0) {
    writeCommandFailure(sourceAudit);
    writeCommandFailure(packageList);
    process.exit(packageList.status ?? sourceAudit.status ?? 1);
  }

  const packages = collectResolvedPackages(parseDotnetJson(packageList.stdout));
  if (packages.length === 0) {
    throw new Error("The restored NuGet dependency graph did not contain any resolved packages.");
  }

  process.stderr.write(
    "NuGet source vulnerability metadata was unavailable; " +
    "falling back to OSV exact-version auditing for the restored dependency graph.\n",
  );
  const vulnerabilities = await queryOsv(packages);
  if (vulnerabilities.length > 0) {
    process.stderr.write("NuGet OSV fallback audit failed:\n");
    for (const item of vulnerabilities) {
      process.stderr.write(`- ${item.id}@${item.version}: ${item.advisories.join(", ")}\n`);
    }
    process.exit(1);
  }

  process.stdout.write(
    `NuGet vulnerability audit passed via OSV fallback (${packages.length} exact package versions).\n`,
  );
}

function isNuGetVulnerabilityMetadataUnavailable(commandResult) {
  const output = `${commandResult.stdout || ""}\n${commandResult.stderr || ""}`;
  const hasMetadataFailure = [
    /Unable to load the service index/iu,
    /SSL connection could not be established/iu,
    /SEC_E_NO_CREDENTIALS/iu,
    /\bNU1301\b/iu,
    /(?:package )?vulnerabilit(?:y|ies).*(?:data|metadata).*(?:unavailable|could not|failed|error)/iu,
    /(?:could not|failed|error).*(?:retrieve|download|load|get).*(?:package )?vulnerabilit(?:y|ies)/iu,
  ].some((pattern) => pattern.test(output));
  if (!hasMetadataFailure) return false;

  const nuGetCodes = [...output.matchAll(/\bNU\d{4}\b/giu)].map((match) => match[0].toUpperCase());
  if (nuGetCodes.some((code) => code !== "NU1301" && code !== "NU1900")) {
    return false;
  }

  return ![
    /\b(?:MSB|NETSDK)\d{4}\b/iu,
    /(?:unknown|unrecognized|invalid)\s+(?:command|option|argument)/iu,
    /(?:command|option|argument).*(?:is required|requires a value|was not expected)/iu,
    /(?:project|solution).*(?:does not exist|not found|could not be found)/iu,
    /project\.assets\.json/iu,
  ].some((pattern) => pattern.test(output));
}

function writeCommandFailure(commandResult) {
  process.stderr.write(commandResult.stdout || "");
  process.stderr.write(commandResult.stderr || "");
}

function parseDotnetJson(output) {
  const text = String(output || "");
  const jsonStart = text.indexOf("{");
  if (jsonStart < 0) throw new Error(`dotnet package list did not return JSON: ${text}`);
  return JSON.parse(text.slice(jsonStart));
}

function collectResolvedPackages(document) {
  const packages = new Map();
  for (const project of document.projects || []) {
    for (const framework of project.frameworks || []) {
      for (const group of [framework.topLevelPackages, framework.transitivePackages]) {
        for (const item of group || []) {
          const id = String(item.id || "").trim();
          const version = String(item.resolvedVersion || item.version || "").trim();
          if (id && version) packages.set(`${id.toLowerCase()}@${version.toLowerCase()}`, { id, version });
        }
      }
    }
  }
  return [...packages.values()].sort((left, right) =>
    left.id.localeCompare(right.id, "en", { sensitivity: "base" }) || left.version.localeCompare(right.version, "en"));
}

async function queryOsv(packages) {
  const vulnerabilities = [];
  for (let offset = 0; offset < packages.length; offset += 500) {
    const batch = packages.slice(offset, offset + 500);
    const response = await fetch("https://api.osv.dev/v1/querybatch", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        queries: batch.map((item) => ({
          package: { ecosystem: "NuGet", name: item.id },
          version: item.version,
        })),
      }),
    });
    if (!response.ok) {
      throw new Error(`OSV NuGet audit request failed: HTTP ${response.status} ${response.statusText}`);
    }

    const report = await response.json();
    if (!Array.isArray(report.results) || report.results.length !== batch.length) {
      throw new Error("OSV NuGet audit returned an incomplete batch response.");
    }
    for (let index = 0; index < batch.length; index += 1) {
      const advisories = (report.results[index]?.vulns || []).map((item) => item.id).filter(Boolean);
      if (advisories.length > 0) vulnerabilities.push({ ...batch[index], advisories });
    }
  }
  return vulnerabilities;
}

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
