import { createHash, randomUUID } from "node:crypto";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputRoot = path.resolve(repositoryRoot, process.argv[2] || "artifacts/dependency-governance");
mkdirSync(outputRoot, { recursive: true });

const components = new Map();
collectNpmLock("web", "apps/export-doc-web/package-lock.json");
collectNpmLock("tauri-build", "apps/export-doc-tauri/package-lock.json");
collectCargoLock("tauri", "apps/export-doc-tauri/src-tauri/Cargo.lock");
collectCargoLock("ocr", "apps/exportdoc-ocr-rs/Cargo.lock");
collectCargoLock("excel-analyzer", "tools/excel-analyzer-rs/Cargo.lock");
collectNuGet();

const ordered = [...components.values()].sort((left, right) =>
  left.ecosystem.localeCompare(right.ecosystem)
  || left.name.localeCompare(right.name)
  || left.version.localeCompare(right.version));
const generatedAt = new Date().toISOString();

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
writeFileSync(
  path.join(outputRoot, "THIRD_PARTY_DEPENDENCIES.md"),
  buildMarkdown(ordered, generatedAt),
  "utf8",
);

process.stdout.write(`Dependency governance artifacts generated: ${ordered.length} components in ${outputRoot}\n`);

function collectNpmLock(scope, relativePath) {
  const lock = JSON.parse(readFileSync(path.join(repositoryRoot, relativePath), "utf8"));
  for (const [packagePath, entry] of Object.entries(lock.packages || {})) {
    if (!packagePath || !entry.version) continue;
    const name = npmPackageName(packagePath);
    if (!name) continue;
    addComponent({
      ecosystem: "npm",
      scope,
      name,
      version: String(entry.version),
      license: normalizeLicense(entry.license),
      downloadLocation: entry.resolved || `https://registry.npmjs.org/${name}/-/${name.split("/").at(-1)}-${entry.version}.tgz`,
      purl: npmPurl(name, entry.version),
    });
  }
}

function collectCargoLock(scope, relativePath) {
  const content = readFileSync(path.join(repositoryRoot, relativePath), "utf8");
  for (const block of content.split("[[package]]").slice(1)) {
    const name = readTomlString(block, "name");
    const version = readTomlString(block, "version");
    const source = readTomlString(block, "source");
    if (!name || !version || !source) continue;
    addComponent({
      ecosystem: "cargo",
      scope,
      name,
      version,
      license: "NOASSERTION",
      downloadLocation: source,
      purl: `pkg:cargo/${encodeURIComponent(name)}@${encodeURIComponent(version)}`,
    });
  }
}

function collectNuGet() {
  const result = spawnSync(
    "dotnet",
    ["list", "ExportDocManager.sln", "package", "--include-transitive", "--format", "json"],
    { cwd: repositoryRoot, encoding: "utf8", windowsHide: true },
  );
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(`dotnet package inventory failed:\n${result.stdout || ""}\n${result.stderr || ""}`);
  }
  const raw = String(result.stdout || "");
  const report = JSON.parse(raw.slice(raw.indexOf("{")));
  for (const project of report.projects || []) {
    const projectName = path.basename(project.path || "dotnet-project");
    for (const framework of project.frameworks || []) {
      for (const groupName of ["topLevelPackages", "transitivePackages"]) {
        for (const item of framework[groupName] || []) {
          addComponent({
            ecosystem: "nuget",
            scope: projectName,
            name: item.id,
            version: item.resolvedVersion || item.requestedVersion || "unknown",
            license: "NOASSERTION",
            downloadLocation: `https://www.nuget.org/packages/${item.id}/${item.resolvedVersion || item.requestedVersion || ""}`,
            purl: `pkg:nuget/${encodeURIComponent(item.id)}@${encodeURIComponent(item.resolvedVersion || item.requestedVersion || "unknown")}`,
          });
        }
      }
    }
  }
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
    licenseConcluded: "NOASSERTION",
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
      tools: [{ vendor: "ExportDocManager", name: "dependency-governance", version: "1" }],
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

function buildMarkdown(items, generatedAt) {
  const lines = [
    "# ExportDocManager third-party dependency inventory",
    "",
    `Generated: ${generatedAt}`,
    "",
    "This inventory is a review aid generated from committed lock files and the restored NuGet graph. " +
      "`NOASSERTION` means the lock format does not carry authoritative license metadata; release review must consult the package source before redistribution.",
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
  return `${lines.join("\n")}\n`;
}

function npmPackageName(packagePath) {
  const marker = "node_modules/";
  const index = packagePath.lastIndexOf(marker);
  if (index < 0) return "";
  const remaining = packagePath.slice(index + marker.length);
  const segments = remaining.split("/");
  return remaining.startsWith("@") ? segments.slice(0, 2).join("/") : segments[0];
}

function readTomlString(block, key) {
  return block.match(new RegExp(`^${key}\\s*=\\s*"([^"]+)"`, "mu"))?.[1] || "";
}

function normalizeLicense(value) {
  if (typeof value === "string" && value.trim() && !/SEE LICENSE/iu.test(value)) return value.trim();
  if (Array.isArray(value) && value.length) return value.join(" AND ");
  return "NOASSERTION";
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

function escapeMarkdown(value) {
  return String(value).replaceAll("|", "\\|").replaceAll("\n", " ");
}
