import {
  existsSync,
  readFileSync,
  readdirSync,
} from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const expectedNpoiVersion = "2.7.6";
const ignoredDirectoryNames = new Set([
  ".git",
  ".codex-runtime",
  "artifacts",
  "bin",
  "dist",
  "node_modules",
  "obj",
  "target",
]);
const scriptRoot = path.dirname(fileURLToPath(import.meta.url));
const defaultRepositoryRoot = path.resolve(scriptRoot, "..");
const argumentsList = process.argv.slice(2);
const generatedOnly = argumentsList.includes("--generated-only");
const repositoryRoot = resolveRepositoryRoot(argumentsList);
const failures = [];

let lockFiles = [];
let npoiLockEntryCount = 0;
if (!generatedOnly) {
  const centralPackageVersions = readCentralPackageVersions();
  const npoiCentralVersions = centralPackageVersions.filter((item) => item.id.toLowerCase() === "npoi");
  if (npoiCentralVersions.length !== 1) {
    failures.push(
      `Directory.Packages.props must contain exactly one central NPOI version; found ${npoiCentralVersions.length}.`,
    );
  } else if (npoiCentralVersions[0].version !== expectedNpoiVersion) {
    failures.push(
      `Directory.Packages.props pins NPOI ${npoiCentralVersions[0].version}; `
      + `the approved commercial dependency policy requires NPOI ${expectedNpoiVersion}.`,
    );
  }

  const repositoryDependencyFiles = collectFiles(
    repositoryRoot,
    (fileName) => fileName === "packages.lock.json" || /\.(?:csproj|props|targets)$/iu.test(fileName),
  );
  const projectFiles = repositoryDependencyFiles.filter((filePath) => /\.(?:csproj|props|targets)$/iu.test(filePath));
  for (const projectFile of projectFiles) {
    const content = readFileSync(projectFile, "utf8");
    for (const match of content.matchAll(/<(?:PackageReference|PackageVersion)\b([^>]*)>/giu)) {
      const attributes = parseXmlAttributes(match[1]);
      if (String(attributes.Include || "").toLowerCase() !== "npoi" || !attributes.Version) continue;
      if (attributes.Version === expectedNpoiVersion) continue;
      failures.push(
        `${relative(projectFile)} declares NPOI ${attributes.Version}; `
        + `project-level overrides must retain NPOI ${expectedNpoiVersion}.`,
      );
    }
  }

  lockFiles = repositoryDependencyFiles.filter((filePath) => path.basename(filePath) === "packages.lock.json");
  for (const lockFile of lockFiles) {
    let lock;
    try {
      lock = JSON.parse(readFileSync(lockFile, "utf8"));
    } catch (error) {
      failures.push(`${relative(lockFile)} is not valid JSON: ${error.message}`);
      continue;
    }

    for (const entry of findPackageEntries(lock, "NPOI")) {
      npoiLockEntryCount += 1;
      const resolved = typeof entry.value?.resolved === "string" ? entry.value.resolved : "";
      if (resolved !== expectedNpoiVersion) {
        failures.push(
          `${relative(lockFile)}:${entry.path} resolves NPOI ${resolved || "<missing>"}; `
          + `the approved version is ${expectedNpoiVersion}.`,
        );
      }
      const requested = typeof entry.value?.requested === "string" ? entry.value.requested : "";
      if (requested && !requested.includes(expectedNpoiVersion)) {
        failures.push(
          `${relative(lockFile)}:${entry.path} requests NPOI ${requested}; `
          + `the central version is ${expectedNpoiVersion}.`,
        );
      }
    }
  }

  if (npoiLockEntryCount === 0) {
    failures.push("No NuGet packages.lock.json contains NPOI; the central pin is not represented in the locked dependency graph.");
  }
}

for (const generatedFile of collectGeneratedDependencyFiles()) {
  const content = readFileSync(generatedFile, "utf8");
  let versions;
  try {
    versions = findNpoiVersions(generatedFile, content);
  } catch (error) {
    failures.push(`${relative(generatedFile)} is not valid dependency evidence: ${error.message}`);
    continue;
  }
  for (const version of versions) {
    if (version === expectedNpoiVersion) continue;
    failures.push(
      `${relative(generatedFile)} contains NPOI ${version}; `
      + `generated dependency evidence must retain NPOI ${expectedNpoiVersion}.`,
    );
  }
}

if (failures.length > 0) {
  process.stderr.write(`Dependency version policy failed:\n${failures.map((item) => `- ${item}`).join("\n")}\n`);
  process.exit(1);
}

process.stdout.write(generatedOnly
  ? "Generated dependency evidence policy passed.\n"
  : `Dependency version policy passed: NPOI ${expectedNpoiVersion}; `
    + `checked ${lockFiles.length} NuGet lock file(s) and ${npoiLockEntryCount} NPOI graph entr${npoiLockEntryCount === 1 ? "y" : "ies"}.\n`);

function resolveRepositoryRoot(argumentsList) {
  const optionIndex = argumentsList.indexOf("--repository-root");
  if (optionIndex < 0) return defaultRepositoryRoot;
  const value = argumentsList[optionIndex + 1];
  if (!value || value.startsWith("--")) throw new Error("--repository-root requires a directory path.");
  return path.resolve(value);
}

function readCentralPackageVersions() {
  const propsPath = path.join(repositoryRoot, "Directory.Packages.props");
  if (!existsSync(propsPath)) {
    failures.push("Directory.Packages.props is missing.");
    return [];
  }

  const content = readFileSync(propsPath, "utf8");
  if (!/<ManagePackageVersionsCentrally>\s*true\s*<\/ManagePackageVersionsCentrally>/iu.test(content)) {
    failures.push("Directory.Packages.props must keep ManagePackageVersionsCentrally=true.");
  }
  return [...content.matchAll(/<PackageVersion\b([^>]*)>/giu)].map((match) => {
    const attributes = parseXmlAttributes(match[1]);
    return { id: attributes.Include || "", version: attributes.Version || "" };
  });
}

function parseXmlAttributes(rawAttributes) {
  return Object.fromEntries(
    [...rawAttributes.matchAll(/\b(Include|Version)\s*=\s*(?:"([^"]*)"|'([^']*)')/giu)]
      .map((attribute) => [attribute[1], attribute[2] ?? attribute[3] ?? ""]),
  );
}

function collectFiles(directory, predicate, files = []) {
  if (!existsSync(directory)) return files;
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const absolutePath = path.join(directory, entry.name);
    if (entry.isSymbolicLink()) continue;
    if (entry.isDirectory()) {
      if (ignoredDirectoryNames.has(entry.name)) continue;
      collectFiles(absolutePath, predicate, files);
      continue;
    }
    if (entry.isFile() && predicate(entry.name, absolutePath)) files.push(absolutePath);
  }
  return files.sort((left, right) => left.localeCompare(right, "en", { sensitivity: "base" }));
}

function findPackageEntries(value, packageId, segments = [], entries = []) {
  if (!value || typeof value !== "object") return entries;
  if (Array.isArray(value)) {
    value.forEach((item, index) => findPackageEntries(item, packageId, [...segments, String(index)], entries));
    return entries;
  }

  for (const [key, nested] of Object.entries(value)) {
    const nestedSegments = [...segments, key];
    if (key.toLowerCase() === packageId.toLowerCase()) entries.push({ path: nestedSegments.join("."), value: nested });
    findPackageEntries(nested, packageId, nestedSegments, entries);
  }
  return entries;
}

function collectGeneratedDependencyFiles() {
  const files = [];
  for (const relativePath of ["THIRD_PARTY_DEPENDENCIES.md", "THIRD_PARTY_NOTICES.md"]) {
    const absolutePath = path.join(repositoryRoot, relativePath);
    if (existsSync(absolutePath)) files.push(absolutePath);
  }
  const governanceRoot = path.join(repositoryRoot, "artifacts", "dependency-governance");
  return collectFiles(
    governanceRoot,
    (fileName) => /\.(?:json|md)$/iu.test(fileName),
    files,
  );
}

function findNpoiVersions(filePath, content) {
  if (path.extname(filePath).toLowerCase() === ".json") {
    return findJsonPackageVersions(JSON.parse(content), "NPOI");
  }
  return content
    .split(/\r?\n/u)
    .filter((line) => /\bNPOI\b/iu.test(line))
    .flatMap((line) => line.match(/\b\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?\b/gu) || []);
}

function findJsonPackageVersions(value, packageId) {
  if (!value || typeof value !== "object") return [];
  if (Array.isArray(value)) return value.flatMap((item) => findJsonPackageVersions(item, packageId));

  const name = Object.entries(value).find(([key]) => key.toLowerCase() === "name")?.[1];
  const versionValue = Object.entries(value).find(([key]) => ["version", "versioninfo"].includes(key.toLowerCase()))?.[1];
  const current = String(name || "").toLowerCase() === packageId.toLowerCase() && versionValue
    ? [String(versionValue)]
    : [];
  return current.concat(Object.values(value).flatMap((nested) => findJsonPackageVersions(nested, packageId)));
}

function relative(absolutePath) {
  return path.relative(repositoryRoot, absolutePath) || path.basename(absolutePath);
}
