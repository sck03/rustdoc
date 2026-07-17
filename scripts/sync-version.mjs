import { access, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptPath = fileURLToPath(import.meta.url);
const repoRoot = path.resolve(path.dirname(scriptPath), "..");
const versionConfig = JSON.parse(await readText("version.json"));
const version = requireSemver(versionConfig.version);
const assemblyVersion = requireAssemblyVersion(versionConfig.assemblyVersion || toAssemblyVersion(version));
const fileVersion = requireAssemblyVersion(versionConfig.fileVersion || assemblyVersion);

await writeDirectoryBuildProps();

for (const file of [
  "apps/export-doc-web/package.json",
  "apps/export-doc-tauri/package.json",
  "apps/license-keygen-tauri/package.json",
  "apps/export-doc-web/package-lock.json",
  "apps/export-doc-tauri/package-lock.json",
  "apps/license-keygen-tauri/package-lock.json",
  "apps/export-doc-tauri/src-tauri/tauri.conf.json",
  "apps/license-keygen-tauri/src-tauri/tauri.conf.json",
]) {
  if (isPrivateToolPath(file) && !(await fileExists(file))) {
    continue;
  }
  await updateJson(file, (json) => {
    json.version = version;
    if (json.packages?.[""]) {
      json.packages[""].version = version;
    }
  });
}

for (const file of [
  "apps/export-doc-tauri/src-tauri/Cargo.toml",
  "apps/license-keygen-tauri/src-tauri/Cargo.toml",
  "tools/excel-analyzer-rs/Cargo.toml",
]) {
  if (isPrivateToolPath(file) && !(await fileExists(file))) {
    continue;
  }
  await updateCargoTomlPackageVersion(file);
}

await updateCargoLockPackageVersion("apps/export-doc-tauri/src-tauri/Cargo.lock", "export-doc-tauri");
if (await fileExists("apps/license-keygen-tauri/src-tauri/Cargo.lock")) {
  await updateCargoLockPackageVersion("apps/license-keygen-tauri/src-tauri/Cargo.lock", "export-doc-license-keygen-tauri");
}
await updateCargoLockPackageVersion("tools/excel-analyzer-rs/Cargo.lock", "exportdoc-excel-analyzer");

console.log(`Synced ExportDocManager version ${version}.`);

async function writeDirectoryBuildProps() {
  const relativePath = "Directory.Build.props";
  let content;
  try {
    content = await readText(relativePath);
  } catch {
    content = "<Project>\n  <PropertyGroup>\n  </PropertyGroup>\n</Project>\n";
  }

  for (const [propertyName, propertyValue] of [
    ["Version", version],
    ["AssemblyVersion", assemblyVersion],
    ["FileVersion", fileVersion],
    ["InformationalVersion", version],
  ]) {
    content = upsertXmlProperty(content, propertyName, propertyValue);
  }

  const requiredBuildProperties = [
    [
      "_ExportDocManagerHostArchitecture",
      "    <_ExportDocManagerHostArchitecture>$([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant())</_ExportDocManagerHostArchitecture>",
    ],
    [
      "_ExportDocManagerHostOs",
      "    <_ExportDocManagerHostOs Condition=\"$([System.OperatingSystem]::IsWindows())\">win</_ExportDocManagerHostOs>\n" +
        "    <_ExportDocManagerHostOs Condition=\"$([System.OperatingSystem]::IsLinux())\">linux</_ExportDocManagerHostOs>\n" +
        "    <_ExportDocManagerHostOs Condition=\"$([System.OperatingSystem]::IsMacOS())\">osx</_ExportDocManagerHostOs>",
    ],
    [
      "RuntimeIdentifier",
      "    <RuntimeIdentifier Condition=\"'$(RuntimeIdentifier)' == '' and '$(_ExportDocManagerHostOs)' != ''\">$(_ExportDocManagerHostOs)-$(_ExportDocManagerHostArchitecture)</RuntimeIdentifier>",
    ],
    [
      "SelfContained",
      "    <SelfContained Condition=\"'$(SelfContained)' == ''\">false</SelfContained>",
    ],
  ];
  const missingBuildProperties = requiredBuildProperties
    .filter(([propertyName]) => !new RegExp(`<${escapeRegExp(propertyName)}(?:\\s|>)`).test(content))
    .map(([, xml]) => xml);
  if (missingBuildProperties.length > 0) {
    content = insertIntoFirstPropertyGroup(content, missingBuildProperties.join("\n"));
  }

  if (!content.endsWith("\n")) {
    content += "\n";
  }

  await writeIfChanged(relativePath, content);
}

function upsertXmlProperty(content, propertyName, propertyValue) {
  const pattern = new RegExp(
    `(<${escapeRegExp(propertyName)}(?:\\s[^>]*)?>)[\\s\\S]*?(</${escapeRegExp(propertyName)}>)`,
  );
  if (pattern.test(content)) {
    return content.replace(pattern, `$1${propertyValue}$2`);
  }

  return insertIntoFirstPropertyGroup(content, `    <${propertyName}>${propertyValue}</${propertyName}>`);
}

function insertIntoFirstPropertyGroup(content, xml) {
  const closingTag = "  </PropertyGroup>";
  const index = content.indexOf(closingTag);
  if (index < 0) {
    throw new Error("Directory.Build.props is missing a PropertyGroup closing tag.");
  }

  const prefix = content.slice(0, index).replace(/\s*$/, "\n");
  return `${prefix}${xml}\n${content.slice(index)}`;
}

async function updateJson(relativePath, mutate) {
  const value = JSON.parse(await readText(relativePath));
  mutate(value);
  await writeIfChanged(relativePath, `${JSON.stringify(value, null, 2)}\n`);
}

async function updateCargoTomlPackageVersion(relativePath) {
  const text = await readText(relativePath);
  let matched = false;
  const updated = text.replace(
    /(\[package\][\s\S]*?\nversion\s*=\s*")[^"]+(")/,
    (_match, prefix, suffix) => {
      matched = true;
      return `${prefix}${version}${suffix}`;
    },
  );
  if (!matched) {
    throw new Error(`Package version was not found in ${relativePath}.`);
  }

  await writeIfChanged(relativePath, updated);
}

async function updateCargoLockPackageVersion(relativePath, packageName) {
  const text = await readText(relativePath);
  const pattern = new RegExp(`(name = "${escapeRegExp(packageName)}"\\r?\\nversion = ")[^"]+(")`, "g");
  let matched = false;
  const updated = text.replace(pattern, (_match, prefix, suffix) => {
    matched = true;
    return `${prefix}${version}${suffix}`;
  });
  if (!matched) {
    throw new Error(`Package ${packageName} was not found in ${relativePath}.`);
  }

  await writeIfChanged(relativePath, updated);
}

async function readText(relativePath) {
  return await readFile(path.join(repoRoot, relativePath), "utf8");
}

async function fileExists(relativePath) {
  try {
    await access(path.join(repoRoot, relativePath));
    return true;
  } catch {
    return false;
  }
}

function isPrivateToolPath(relativePath) {
  return relativePath.replaceAll("\\", "/").startsWith("apps/license-keygen-tauri/");
}

async function writeIfChanged(relativePath, content) {
  const absolutePath = path.join(repoRoot, relativePath);
  let previous = "";
  try {
    previous = await readFile(absolutePath, "utf8");
  } catch {
    previous = "";
  }

  if (previous !== content) {
    await writeFile(absolutePath, content, "utf8");
  }
}

function requireSemver(value) {
  const text = String(value || "").trim();
  if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/.test(text)) {
    throw new Error(`version.json version must be SemVer, got '${value}'.`);
  }

  return text;
}

function requireAssemblyVersion(value) {
  const text = String(value || "").trim();
  if (!/^\d+\.\d+\.\d+\.\d+$/.test(text)) {
    throw new Error(`Assembly/File version must have four numeric parts, got '${value}'.`);
  }

  return text;
}

function toAssemblyVersion(semver) {
  const parts = semver.split(/[+-]/, 1)[0].split(".").map((part) => Number.parseInt(part, 10));
  while (parts.length < 4) {
    parts.push(0);
  }

  return parts.slice(0, 4).join(".");
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
