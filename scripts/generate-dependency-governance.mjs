import { createHash, randomUUID } from "node:crypto";
import {
  existsSync,
  mkdirSync,
  readFileSync,
  writeFileSync,
} from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const argumentsList = process.argv.slice(2);
const outputArgument = argumentsList.find((argument) => !argument.startsWith("--"));
const outputRoot = path.resolve(repositoryRoot, outputArgument || "artifacts/dependency-governance");
const releaseMode = argumentsList.includes("--release");
const writeRepository = argumentsList.includes("--write-repository");
const verifyRepository = argumentsList.includes("--verify-repository");
mkdirSync(outputRoot, { recursive: true });

const approvedLicenseIdentifiers = new Set([
  "0BSD",
  "Apache-2.0",
  "BlueOak-1.0.0",
  "BSD-2-Clause",
  "BSD-3-Clause",
  "BSL-1.0",
  "CC-BY-4.0",
  "CC0-1.0",
  "CDLA-Permissive-2.0",
  "EPL-2.0",
  "ISC",
  "LGPL-2.1-or-later",
  "LGPL-3.0-only",
  "LLVM-exception",
  "MIT",
  "MIT-0",
  "MPL-2.0",
  "MS-PL",
  "OpenSSL",
  "PostgreSQL",
  "Unicode-3.0",
  "Unicode-DFS-2016",
  "Unlicense",
  "Zlib",
]);
const components = new Map();

collectNpmLock("web", "apps/export-doc-web/package-lock.json");
collectNpmLock("tauri-build", "apps/export-doc-tauri/package-lock.json");
collectCargoMetadata("tauri", "apps/export-doc-tauri/src-tauri/Cargo.toml");
collectCargoMetadata("ocr", "apps/exportdoc-ocr-rs/Cargo.toml");
collectCargoMetadata("excel-analyzer", "tools/excel-analyzer-rs/Cargo.toml");
collectNuGet();

const ordered = [...components.values()].sort((left, right) =>
  left.ecosystem.localeCompare(right.ecosystem)
  || left.name.localeCompare(right.name)
  || left.version.localeCompare(right.version));
const generatedAt = new Date().toISOString();
const notices = buildNotices(ordered);
const inventory = buildInventory(ordered);

writeFileSync(
  path.join(outputRoot, "exportdocmanager.spdx.json"),
  `${JSON.stringify(buildSpdx(ordered, generatedAt), null, 2)}\n`,
  "utf8",
);
writeFileSync(
  path.join(outputRoot, "exportdocmanager.cyclonedx.json"),
  `${JSON.stringify(buildCycloneDx(ordered, generatedAt), null, 2)}\n`,
  "utf8",
);
writeFileSync(path.join(outputRoot, "THIRD_PARTY_NOTICES.md"), notices, "utf8");
writeFileSync(path.join(outputRoot, "THIRD_PARTY_DEPENDENCIES.md"), inventory, "utf8");

if (writeRepository) {
  writeFileSync(path.join(repositoryRoot, "THIRD_PARTY_NOTICES.md"), notices, "utf8");
  writeFileSync(path.join(repositoryRoot, "THIRD_PARTY_DEPENDENCIES.md"), inventory, "utf8");
}
if (verifyRepository) {
  verifyGeneratedFile("THIRD_PARTY_NOTICES.md", notices);
  verifyGeneratedFile("THIRD_PARTY_DEPENDENCIES.md", inventory);
}

const unresolved = ordered.filter((item) => item.license === "NOASSERTION");
const disallowed = ordered.filter((item) => !isApprovedLicenseExpression(item.license));
if (releaseMode && (unresolved.length > 0 || disallowed.length > 0)) {
  const details = [
    ...unresolved.map((item) => `${item.ecosystem}:${item.name}@${item.version}=NOASSERTION`),
    ...disallowed
      .filter((item) => item.license !== "NOASSERTION")
      .map((item) => `${item.ecosystem}:${item.name}@${item.version}=${item.license}`),
  ];
  throw new Error(
    `Release dependency license policy failed for ${details.length} component(s):\n${details.join("\n")}`,
  );
}

process.stdout.write(
  `Dependency governance artifacts generated: ${ordered.length} components, ` +
  `unresolved=${unresolved.length}, disallowed=${disallowed.length}, release=${releaseMode}, root=${outputRoot}\n`,
);

function collectNpmLock(scope, relativePath) {
  const lockPath = path.join(repositoryRoot, relativePath);
  const lock = JSON.parse(readFileSync(lockPath, "utf8"));
  for (const [packagePath, entry] of Object.entries(lock.packages || {})) {
    if (!packagePath || !entry.version) continue;
    const name = npmPackageName(packagePath);
    if (!name) continue;
    const installedPackageJson = path.join(path.dirname(lockPath), packagePath, "package.json");
    let installedLicense = "";
    if (existsSync(installedPackageJson)) {
      try {
        installedLicense = JSON.parse(readFileSync(installedPackageJson, "utf8")).license;
      } catch {
        installedLicense = "";
      }
    }
    addComponent({
      ecosystem: "npm",
      scope,
      name,
      version: String(entry.version),
      license: normalizeLicense(installedLicense || entry.license),
      downloadLocation: entry.resolved || `https://registry.npmjs.org/${name}/-/${name.split("/").at(-1)}-${entry.version}.tgz`,
      purl: npmPurl(name, entry.version),
    });
  }
}

function collectCargoMetadata(scope, relativeManifestPath) {
  const result = spawnSync(
    "cargo",
    ["metadata", "--locked", "--format-version", "1", "--manifest-path", relativeManifestPath],
    { cwd: repositoryRoot, encoding: "utf8", windowsHide: true, maxBuffer: 64 * 1024 * 1024 },
  );
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(`cargo metadata failed for ${relativeManifestPath}:\n${result.stdout || ""}\n${result.stderr || ""}`);
  }
  const metadata = JSON.parse(result.stdout);
  for (const item of metadata.packages || []) {
    if (!item.source) continue;
    addComponent({
      ecosystem: "cargo",
      scope,
      name: item.name,
      version: String(item.version),
      license: normalizeLicense(item.license),
      downloadLocation: item.repository || item.homepage || item.source,
      purl: `pkg:cargo/${encodeURIComponent(item.name)}@${encodeURIComponent(item.version)}`,
    });
  }
}

function collectNuGet() {
  const result = spawnSync(
    "dotnet",
    ["list", "ExportDocManager.sln", "package", "--include-transitive", "--format", "json"],
    { cwd: repositoryRoot, encoding: "utf8", windowsHide: true, maxBuffer: 64 * 1024 * 1024 },
  );
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(`dotnet package inventory failed:\n${result.stdout || ""}\n${result.stderr || ""}`);
  }
  const raw = String(result.stdout || "");
  const report = JSON.parse(raw.slice(raw.indexOf("{")));
  const packagesRoot = resolveNuGetPackagesRoot();
  for (const project of report.projects || []) {
    const projectName = path.basename(project.path || "dotnet-project");
    for (const framework of project.frameworks || []) {
      for (const groupName of ["topLevelPackages", "transitivePackages"]) {
        for (const item of framework[groupName] || []) {
          const version = item.resolvedVersion || item.requestedVersion || "unknown";
          addComponent({
            ecosystem: "nuget",
            scope: projectName,
            name: item.id,
            version,
            license: readNuGetLicense(packagesRoot, item.id, version),
            downloadLocation: `https://www.nuget.org/packages/${item.id}/${version}`,
            purl: `pkg:nuget/${encodeURIComponent(item.id)}@${encodeURIComponent(version)}`,
          });
        }
      }
    }
  }
}

function resolveNuGetPackagesRoot() {
  const candidates = [
    process.env.NUGET_PACKAGES,
    path.join(repositoryRoot, ".codex-runtime", "nuget-packages"),
  ].filter(Boolean);
  for (const candidate of candidates) {
    if (existsSync(candidate)) return path.resolve(candidate);
  }

  const result = spawnSync(
    "dotnet",
    ["nuget", "locals", "global-packages", "--list", "--force-english-output"],
    { cwd: repositoryRoot, encoding: "utf8", windowsHide: true },
  );
  if (result.status === 0) {
    const resolved = String(result.stdout || "").match(/global-packages:\s*(.+)/iu)?.[1]?.trim();
    if (resolved && existsSync(resolved)) return path.resolve(resolved);
  }
  return "";
}

function readNuGetLicense(packagesRoot, packageId, version) {
  if (!packagesRoot) return "NOASSERTION";
  const packageRoot = path.join(packagesRoot, packageId.toLowerCase(), String(version).toLowerCase());
  const nuspecCandidates = [
    path.join(packageRoot, `${packageId.toLowerCase()}.nuspec`),
    path.join(packageRoot, `${packageId}.nuspec`),
  ];
  const nuspecPath = nuspecCandidates.find(existsSync);
  if (!nuspecPath) return "NOASSERTION";
  const nuspec = readFileSync(nuspecPath, "utf8");
  const expression = nuspec.match(/<license\b[^>]*type=["']expression["'][^>]*>([\s\S]*?)<\/license>/iu)?.[1];
  if (expression) return normalizeLicense(decodeXml(expression));

  const licenseFileName = decodeXml(
    nuspec.match(/<license\b[^>]*type=["']file["'][^>]*>([\s\S]*?)<\/license>/iu)?.[1] || "",
  ).trim();
  if (licenseFileName) {
    const licensePath = path.resolve(packageRoot, licenseFileName.replaceAll("/", path.sep));
    if (licensePath.startsWith(path.resolve(packageRoot) + path.sep) && existsSync(licensePath)) {
      const detected = detectLicenseText(readFileSync(licensePath, "utf8"));
      if (detected) return detected;
    }
  }

  const licenseUrl = decodeXml(nuspec.match(/<licenseUrl>([\s\S]*?)<\/licenseUrl>/iu)?.[1] || "").trim();
  return normalizeLicense(mapKnownLicenseUrl(licenseUrl));
}

function mapKnownLicenseUrl(value) {
  const normalized = String(value || "").toLowerCase();
  if (normalized.includes("apache.org/licenses/license-2.0")) return "Apache-2.0";
  if (normalized.includes("opensource.org/licenses/mit") || normalized.endsWith("/license/mit")) return "MIT";
  if (normalized.includes("licenses.nuget.org/mit")) return "MIT";
  if (normalized.includes("licenses.nuget.org/apache-2.0")) return "Apache-2.0";
  if (normalized.includes("github.com/dotnet/corefx") && normalized.includes("license")) return "MIT";
  if (normalized.includes("xunit/xunit") && normalized.includes("license")) return "Apache-2.0";
  if (normalized.includes("microsoft.com/web/webpi/eula/net_library_eula_enu.htm")) return "MS-PL";
  return "";
}

function detectLicenseText(value) {
  const normalized = String(value || "");
  if (/Permission is hereby granted, free of charge/iu.test(normalized)) return "MIT";
  if (/Apache License\s+Version 2\.0/iu.test(normalized)) return "Apache-2.0";
  if (/Redistribution and use in source and binary forms/iu.test(normalized)) {
    return /Neither the name of/iu.test(normalized) ? "BSD-3-Clause" : "BSD-2-Clause";
  }
  return "";
}

function addComponent(component) {
  const key = `${component.ecosystem}:${component.name.toLowerCase()}:${component.version}`;
  const existing = components.get(key);
  if (existing) {
    existing.scopes = [...new Set([...existing.scopes, component.scope])].sort();
    if (existing.license === "NOASSERTION" && component.license !== "NOASSERTION") existing.license = component.license;
    return;
  }
  components.set(key, { ...component, scopes: [component.scope] });
}

function buildSpdx(items, generatedAt) {
  const packages = items.map((item) => ({
    SPDXID: spdxId(item),
    name: item.name,
    versionInfo: item.version,
    downloadLocation: item.downloadLocation || "NOASSERTION",
    filesAnalyzed: false,
    licenseConcluded: item.license,
    licenseDeclared: item.license,
    supplier: "NOASSERTION",
    externalRefs: [{
      referenceCategory: "PACKAGE-MANAGER",
      referenceType: "purl",
      referenceLocator: item.purl,
    }],
    comment: `Used by: ${item.scopes.join(", ")}`,
  }));
  return {
    spdxVersion: "SPDX-2.3",
    dataLicense: "CC0-1.0",
    SPDXID: "SPDXRef-DOCUMENT",
    name: "ExportDocManager dependency SBOM",
    documentNamespace: `https://github.com/sck03/rustdoc/sbom/${randomUUID()}`,
    creationInfo: { created: generatedAt, creators: ["Tool: ExportDocManager dependency governance script"] },
    packages,
    relationships: packages.map((item) => ({
      spdxElementId: "SPDXRef-DOCUMENT",
      relationshipType: "DESCRIBES",
      relatedSpdxElement: item.SPDXID,
    })),
  };
}

function buildCycloneDx(items, generatedAt) {
  return {
    bomFormat: "CycloneDX",
    specVersion: "1.6",
    serialNumber: `urn:uuid:${randomUUID()}`,
    version: 1,
    metadata: {
      timestamp: generatedAt,
      tools: [{ vendor: "ExportDocManager", name: "dependency-governance", version: "2" }],
      component: { type: "application", name: "ExportDocManager", version: readProjectVersion() },
    },
    components: items.map((item) => ({
      type: "library",
      "bom-ref": item.purl,
      group: item.ecosystem,
      name: item.name,
      version: item.version,
      purl: item.purl,
      licenses: item.license === "NOASSERTION" ? undefined : [{ expression: item.license }],
      properties: [{ name: "exportdocmanager:used-by", value: item.scopes.join(", ") }],
    })),
  };
}

function buildNotices(items) {
  const lines = [
    "# ExportDocManager third-party notices",
    "",
    `Project version: ${readProjectVersion()}`,
    "",
    "This file is the unified redistribution notice for package-manager dependencies and bundled runtime assets. " +
      "The machine-readable SPDX and CycloneDX documents shipped beside it contain the same dependency inventory.",
    "",
    "## Package dependencies",
    "",
    "| Ecosystem | Package | Version | Declared license | Used by |",
    "|---|---|---:|---|---|",
  ];
  for (const item of items) {
    lines.push(
      `| ${escapeMarkdown(item.ecosystem)} | ${escapeMarkdown(item.name)} | ${escapeMarkdown(item.version)} | ` +
      `${escapeMarkdown(item.license)} | ${escapeMarkdown(item.scopes.join(", "))} |`,
    );
  }

  lines.push(
    "",
    "## Bundled runtime assets",
    "",
    "- Noto CJK report fonts are redistributed under the SIL Open Font License. The complete text is included below and is also shipped at `Resources/Fonts/OpenSource/OFL-Noto-CJK.txt`.",
    "- PaddleOCR/PP-OCRv6 model provenance and notices are shipped at `OcrModels/PaddleOCR/V6/THIRD_PARTY_NOTICES.md`.",
    "- The Rust Excel analyzer notice is shipped at `Tools/EXCEL_ANALYZER_NOTICES.md`.",
    "- Chrome Headless Shell or the reviewed Playwright Chromium ARM64 build is shipped with its upstream license/notice file under `Browsers/`; the clean-package gate rejects browser payloads without a corresponding notice.",
    "",
    "### Noto CJK font license",
    "",
    readRequiredText("Resources/Fonts/OpenSource/OFL-Noto-CJK.txt"),
    "",
    "### PP-OCRv6 notice",
    "",
    readRequiredText("OcrModels/PaddleOCR/V6/THIRD_PARTY_NOTICES.md"),
    "",
    "### Excel analyzer notice",
    "",
    readRequiredText("tools/excel-analyzer-rs/THIRD_PARTY_NOTICES.md"),
    "",
  );
  return `${lines.join("\n").trimEnd()}\n`;
}

function buildInventory(items) {
  const lines = [
    "# ExportDocManager third-party dependency inventory",
    "",
    `Project version: ${readProjectVersion()}`,
    "",
    "Generated from committed npm/Cargo lock files, Cargo package metadata, restored NuGet package metadata, and the solution package graph.",
    "",
  ];
  for (const ecosystem of ["npm", "nuget", "cargo"]) {
    const group = items.filter((item) => item.ecosystem === ecosystem);
    lines.push(`## ${ecosystem} (${group.length})`, "", "| Package | Version | Declared license | Used by |", "|---|---:|---|---|");
    for (const item of group) {
      lines.push(`| ${escapeMarkdown(item.name)} | ${escapeMarkdown(item.version)} | ${escapeMarkdown(item.license)} | ${escapeMarkdown(item.scopes.join(", "))} |`);
    }
    lines.push("");
  }
  return `${lines.join("\n").trimEnd()}\n`;
}

function npmPackageName(packagePath) {
  const marker = "node_modules/";
  const index = packagePath.lastIndexOf(marker);
  if (index < 0) return "";
  const remaining = packagePath.slice(index + marker.length);
  const segments = remaining.split("/");
  return remaining.startsWith("@") ? segments.slice(0, 2).join("/") : segments[0];
}

function normalizeLicense(value) {
  if (Array.isArray(value) && value.length) return value.map(normalizeLicense).join(" AND ");
  if (typeof value !== "string" || !value.trim()) return "NOASSERTION";
  let normalized = decodeXml(value).replaceAll(/\s+/gu, " ").trim();
  if (/^MIT\s+OR\s+SEE LICENSE/iu.test(normalized)) return "MIT";
  if (/SEE LICENSE/iu.test(normalized)) return "NOASSERTION";
  normalized = normalized
    .replaceAll("Apache 2.0", "Apache-2.0")
    .replaceAll(/\bBSD-3\b(?!-Clause)/gu, "BSD-3-Clause")
    .replaceAll(/\bBSD-2\b(?!-Clause)/gu, "BSD-2-Clause")
    .replaceAll(/\s*\/\s*/gu, " OR ");
  return normalized || "NOASSERTION";
}

function isApprovedLicenseExpression(expression) {
  if (expression === "NOASSERTION") return false;
  const identifiers = String(expression).match(/[A-Za-z0-9][A-Za-z0-9.+-]*/gu) || [];
  return identifiers.length > 0 && identifiers.every((identifier) =>
    ["AND", "OR", "WITH"].includes(identifier.toUpperCase()) || approvedLicenseIdentifiers.has(identifier));
}

function npmPurl(name, version) {
  if (name.startsWith("@") && name.includes("/")) {
    const [scope, packageName] = name.slice(1).split("/", 2);
    return `pkg:npm/%40${encodeURIComponent(scope)}/${encodeURIComponent(packageName)}@${encodeURIComponent(version)}`;
  }
  return `pkg:npm/${encodeURIComponent(name)}@${encodeURIComponent(version)}`;
}

function spdxId(item) {
  const digest = createHash("sha256").update(item.purl).digest("hex").slice(0, 16);
  return `SPDXRef-Package-${digest}`;
}

function readProjectVersion() {
  const version = JSON.parse(readFileSync(path.join(repositoryRoot, "version.json"), "utf8"));
  return version.version || version.productVersion || "unknown";
}

function readRequiredText(relativePath) {
  const absolutePath = path.join(repositoryRoot, relativePath);
  if (!existsSync(absolutePath)) throw new Error(`Required third-party notice is missing: ${relativePath}`);
  return readFileSync(absolutePath, "utf8").trim();
}

function verifyGeneratedFile(relativePath, expected) {
  const absolutePath = path.join(repositoryRoot, relativePath);
  if (!existsSync(absolutePath) || readFileSync(absolutePath, "utf8") !== expected) {
    throw new Error(`${relativePath} is stale. Run generate-dependency-governance.mjs with --write-repository.`);
  }
}

function decodeXml(value) {
  return String(value || "")
    .replaceAll("&amp;", "&")
    .replaceAll("&lt;", "<")
    .replaceAll("&gt;", ">")
    .replaceAll("&quot;", '"')
    .replaceAll("&apos;", "'");
}

function escapeMarkdown(value) {
  return String(value).replaceAll("|", "\\|").replaceAll("\n", " ");
}
