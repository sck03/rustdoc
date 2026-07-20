import { spawnSync } from "node:child_process";
import { cp, mkdir, readFile, readdir, rm, stat, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptPath = fileURLToPath(import.meta.url);
const repoRoot = path.resolve(path.dirname(scriptPath), "..");
const localRuntimeRoot = path.join(repoRoot, ".codex-runtime");
const apiProject = path.join(repoRoot, "src", "ExportDocManager.Api", "ExportDocManager.Api.csproj");
const bundleRoot = path.join(repoRoot, "artifacts", "tauri-bundle");
const publishRoot = path.join(bundleRoot, "publish");
const resourcesRoot = path.join(bundleRoot, "resources");
const sidecarRoot = path.join(resourcesRoot, "sidecar");
const ocrSidecarRoot = path.join(sidecarRoot, "ocr");
const toolsRoot = path.join(resourcesRoot, "Tools");
const runtimeLayoutManifestFileName = "runtime-layout.json";
const productEditionManifestFileName = "product-edition.json";

const stableResourceDirs = new Set(["Templates", "OcrModels", "Resources", "Browsers"]);
const sidecarExcludedExtensions = new Set([".pdb", ".xml"]);
const rootConfigFiles = ["appsettings.json", "local-preferences.json"];
const runtimeDataDirectories = ["Database", "Files", "Exports", "SingleWindow", "Backups", "Cache", "Config", "Security", "WebView", "Logs"];

const rid = process.env.EXPORTDOCMANAGER_TAURI_RID || detectRuntimeIdentifier();
const selfContained = (process.env.EXPORTDOCMANAGER_TAURI_SELF_CONTAINED || "true").toLowerCase() !== "false";
const productEdition = normalizeProductEdition(process.env.EXPORTDOCMANAGER_PRODUCT_EDITION);
const allowMissingBrowser = (process.env.EXPORTDOCMANAGER_ALLOW_MISSING_BROWSER || "").toLowerCase() === "true";

run("node", [path.join(repoRoot, "scripts", "sync-version.mjs")], process.env);

assertInsideRepo(bundleRoot, "Tauri bundle output");

await rm(bundleRoot, { recursive: true, force: true });
await mkdir(publishRoot, { recursive: true });
await mkdir(sidecarRoot, { recursive: true });
await mkdir(toolsRoot, { recursive: true });

const env = {
  ...process.env,
  DOTNET_CLI_HOME: resolveLocalBuildPath("DOTNET_CLI_HOME", "dotnet-cli"),
  DOTNET_CLI_TELEMETRY_OPTOUT: process.env.DOTNET_CLI_TELEMETRY_OPTOUT || "1",
  DOTNET_NOLOGO: process.env.DOTNET_NOLOGO || "1",
  NUGET_PACKAGES: resolveLocalBuildPath("NUGET_PACKAGES", "nuget-packages"),
  NUGET_HTTP_CACHE_PATH: resolveLocalBuildPath("NUGET_HTTP_CACHE_PATH", "nuget-http-cache"),
  TEMP: resolveLocalBuildPath("TEMP", "temp"),
  TMP: resolveLocalBuildPath("TMP", "temp"),
  CARGO_TARGET_DIR: process.env.CARGO_TARGET_DIR || path.join(bundleRoot, "cargo-ocr-target"),
};
await mkdir(env.DOTNET_CLI_HOME, { recursive: true });
await mkdir(env.NUGET_PACKAGES, { recursive: true });
await mkdir(env.NUGET_HTTP_CACHE_PATH, { recursive: true });
await mkdir(env.TEMP, { recursive: true });
await mkdir(env.TMP, { recursive: true });

const args = [
  "publish",
  apiProject,
  "-c",
  "Release",
  "-r",
  rid,
  "--self-contained",
  String(selfContained),
  "-o",
  publishRoot,
  "/p:PublishSingleFile=false",
  "/p:PublishReadyToRun=false",
  "/p:DebugType=None",
  "/p:DebugSymbols=false",
];

console.log(`Publishing API sidecar for Tauri bundle (${rid}, self-contained=${selfContained})...`);
run("dotnet", args, env);
await ensureMacOsX64OnnxRuntime(env);
await buildRustOcrSidecar(env);
await buildRustExcelAnalyzer(env);

const entries = await readdir(publishRoot, { withFileTypes: true });
for (const entry of entries) {
  const source = path.join(publishRoot, entry.name);
  if (entry.isDirectory() && stableResourceDirs.has(entry.name)) {
    if (entry.name === "Browsers") {
      await copyBrowserRuntimeResources(source, path.join(resourcesRoot, entry.name));
    } else {
      await cp(source, path.join(resourcesRoot, entry.name), { recursive: true, force: true });
    }
    continue;
  }

  if (entry.isFile() && sidecarExcludedExtensions.has(path.extname(entry.name).toLowerCase())) {
    continue;
  }

  await cp(source, path.join(sidecarRoot, entry.name), { recursive: true, force: true });
}

for (const fileName of rootConfigFiles) {
  const source = path.join(repoRoot, fileName);
  try {
    await cp(source, path.join(resourcesRoot, fileName), { force: true });
  } catch (error) {
    if (error?.code !== "ENOENT") {
      throw error;
    }
  }
}

await copyWindowsWebView2LoaderIfNeeded();
await copyExcelAnalyzerIfAvailable();
const productEditionManifest = await createProductEditionManifest();
await writeFile(
  path.join(resourcesRoot, productEditionManifestFileName),
  `${JSON.stringify(productEditionManifest, null, 2)}\n`,
  "utf8",
);
const runtimeLayoutManifest = await createRuntimeLayoutManifest();
await writeFile(
  path.join(resourcesRoot, runtimeLayoutManifestFileName),
  `${JSON.stringify(runtimeLayoutManifest, null, 2)}\n`,
  "utf8",
);
await validateRuntimeLayoutManifest(runtimeLayoutManifest);

console.log("Prepared Tauri resources:");
console.log(`  ${resourcesRoot}`);
console.log("Runtime layout:");
console.log("  resources/sidecar/        API executable and self-contained runtime");
console.log("  resources/sidecar/ocr/    Rust PP-OCRv6 sidecar without OpenCV");
console.log("  resources/Templates/      report templates");
console.log("  resources/OcrModels/      OCR models");
console.log("  resources/Resources/      Excel and Single Window built-in resources");
console.log("  resources/Browsers/       optional current-platform Chrome Headless Shell renderer");
console.log("  resources/Tools/          optional platform-native helper tools such as Excel analyzer");
if (rid.startsWith("win-")) {
  console.log("  resources/WebView2Loader.dll  Tauri WebView2 loader beside the desktop exe");
}
console.log(`  resources/${runtimeLayoutManifestFileName}  machine-readable runtime layout manifest`);
console.log(`  resources/${productEditionManifestFileName}  build-time product edition manifest`);

async function buildRustOcrSidecar(buildEnv) {
  const target = rustTargetTripleFromRid(rid);
  if (!target) {
    throw new Error(`No Rust OCR target mapping is defined for ${rid}.`);
  }
  const manifest = path.join(repoRoot, "apps", "exportdoc-ocr-rs", "Cargo.toml");
  console.log(`Building Rust OCR sidecar for ${target}...`);
  run("cargo", ["build", "--manifest-path", manifest, "--release", "--target", target], buildEnv);
  const fileName = rid.startsWith("win-") ? "exportdoc-ocr.exe" : "exportdoc-ocr";
  const binary = path.join(buildEnv.CARGO_TARGET_DIR, target, "release", fileName);
  await mkdir(ocrSidecarRoot, { recursive: true });
  await cp(binary, path.join(ocrSidecarRoot, fileName), { force: true });
  await cp(path.join(repoRoot, "apps", "exportdoc-ocr-rs", "README.md"), path.join(ocrSidecarRoot, "README.md"), { force: true });
}

async function buildRustExcelAnalyzer(buildEnv) {
  const target = rustTargetTripleFromRid(rid);
  const manifest = path.join(repoRoot, "tools", "excel-analyzer-rs", "Cargo.toml");
  console.log(`Building Rust Excel analyzer for ${target}...`);
  run("cargo", ["build", "--manifest-path", manifest, "--release", "--locked", "--target", target], buildEnv);
}

async function ensureMacOsX64OnnxRuntime(buildEnv) {
  if (rid !== "osx-x64") return;
  const nativeLibrary = path.join(publishRoot, "libonnxruntime.dylib");
  if (await tryStat(nativeLibrary)) return;

  const version = "1.27.1";
  const archiveRoot = path.join(localRuntimeRoot, "onnxruntime", `osx-x64-${version}`);
  const archive = path.join(archiveRoot, `onnxruntime-osx-x86_64-${version}.tgz`);
  const extracted = path.join(archiveRoot, `onnxruntime-osx-x86_64-${version}`);
  await mkdir(archiveRoot, { recursive: true });
  if (!(await tryStat(archive))) {
    run("curl", ["-fL", "--retry", "3", "-o", archive, `https://github.com/microsoft/onnxruntime/releases/download/v${version}/onnxruntime-osx-x86_64-${version}.tgz`], buildEnv);
  }
  if (!(await tryStat(extracted))) {
    run("tar", ["-xzf", archive, "-C", archiveRoot], buildEnv);
  }
  const libRoot = path.join(extracted, "lib");
  const libraries = (await readdir(libRoot)).filter((name) => name.startsWith("libonnxruntime") && name.endsWith(".dylib"));
  if (!libraries.includes("libonnxruntime.dylib")) throw new Error(`ONNX Runtime macOS x64 native library was not found after extracting ${archive}.`);
  for (const name of libraries) await cp(path.join(libRoot, name), path.join(publishRoot, name), { force: true, dereference: true });
}

async function createProductEditionManifest() {
  const versionManifest = JSON.parse(await readFile(path.join(repoRoot, "version.json"), "utf8"));
  const metadata = {
    Document: { displayName: "单证员版", enabledWorkspaces: ["document"] },
    Sales: { displayName: "业务员版", enabledWorkspaces: ["sales"] },
    Full: { displayName: "全功能版", enabledWorkspaces: ["document", "sales"] },
  }[productEdition];

  return {
    schemaVersion: 1,
    product: "ExportDocManager",
    productVersion: String(versionManifest.version || ""),
    edition: productEdition,
    displayName: metadata.displayName,
    enabledWorkspaces: metadata.enabledWorkspaces,
    generatedAt: new Date().toISOString(),
    runtimeDataPolicy: "Runtime business data defaults to App_Data beside the installed program directory.",
  };
}

function normalizeProductEdition(value) {
  const normalized = String(value || "Full").trim().toLowerCase();
  if (normalized === "document") return "Document";
  if (normalized === "sales") return "Sales";
  return "Full";
}

function run(command, args, env) {
  const result = spawnSync(command, args, {
    cwd: repoRoot,
    env,
    shell: false,
    stdio: "inherit",
  });

  if (result.error) {
    throw result.error;
  }

  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(" ")} failed with exit code ${result.status}.`);
  }
}

function detectRuntimeIdentifier() {
  const platformMap = new Map([
    ["win32", "win"],
    ["linux", "linux"],
    ["darwin", "osx"],
  ]);
  const archMap = new Map([
    ["x64", "x64"],
    ["arm64", "arm64"],
  ]);

  const platform = platformMap.get(process.platform);
  const arch = archMap.get(process.arch);
  if (!platform || !arch) {
    throw new Error(`Unsupported Tauri sidecar platform: ${process.platform}/${process.arch}. Set EXPORTDOCMANAGER_TAURI_RID explicitly.`);
  }

  return `${platform}-${arch}`;
}

function assertInsideRepo(targetPath, purpose) {
  const fullPath = path.resolve(targetPath);
  const relative = path.relative(repoRoot, fullPath);
  if (relative === "" || relative.startsWith("..") || path.isAbsolute(relative)) {
    throw new Error(`Refusing to change ${purpose} outside repo: ${fullPath}`);
  }
}

async function createRuntimeLayoutManifest() {
  const programRootResources = [
    await inspectProgramRootEntry("api-sidecar", path.join(sidecarRoot, sidecarFileName()), "file", true),
  ];

  for (const directoryName of stableResourceDirs) {
    programRootResources.push(
      await inspectProgramRootEntry(
        directoryName,
        path.join(resourcesRoot, directoryName),
        "directory",
        true,
      ),
    );
  }

  for (const fileName of rootConfigFiles) {
    const absolutePath = path.join(resourcesRoot, fileName);
    const entry = await inspectProgramRootEntry(fileName, absolutePath, "file", false);
    if (entry.exists) {
      programRootResources.push(entry);
    }
  }

  const excelAnalyzerEntry = await inspectProgramRootEntry(
    "excel-analyzer",
    path.join(toolsRoot, excelAnalyzerFileName()),
    "file",
    false,
  );
  if (excelAnalyzerEntry.exists) {
    programRootResources.push(excelAnalyzerEntry);
  }

  if (rid.startsWith("win-")) {
    programRootResources.push(
      await inspectProgramRootEntry("WebView2Loader.dll", path.join(resourcesRoot, "WebView2Loader.dll"), "file", true),
    );
  }

  programRootResources.push(
    await inspectProgramRootEntry(
      "product-edition.json",
      path.join(resourcesRoot, productEditionManifestFileName),
      "file",
      true,
    ),
  );

  return {
    schemaVersion: 1,
    product: "ExportDocManager",
    target: "tauri-desktop",
    generatedAt: new Date().toISOString(),
    runtimeIdentifier: rid,
    selfContained,
    storagePolicy: {
      programRoot: "Program root carries the Tauri executable, API sidecar, stable templates, OCR models, browser renderer assets and built-in resources.",
      dataRoot: "Runtime business data and operational logs default to App_Data beside the program root, or an explicit --data-root / environment / runtime-paths.json value.",
      userSelectedOutput: "User-selected import and export paths remain outside the managed data root by explicit request.",
    },
    programRootResources,
    runtimeDataDirectories: runtimeDataDirectories.map((directoryName) => ({
      name: directoryName,
      storage: "runtime-data-root",
      kind: "directory",
      required: true,
      relativePath: directoryName,
      createdBy: "tauri-runtime-and-api-startup",
    })),
    knownExternalConventions: [
      {
        name: "single-window-client-import-directory",
        storage: "external-client-convention",
        reason: "Retained only for the external Single Window client handoff convention.",
      },
    ],
  };
}

async function inspectProgramRootEntry(name, absolutePath, kind, required) {
  const relativePath = toResourcesRelativePath(absolutePath);
  let exists = false;
  let fileCount = 0;
  let bytes = 0;

  try {
    const entryStat = await stat(absolutePath);
    exists = kind === "directory" ? entryStat.isDirectory() : entryStat.isFile();
    if (exists && kind === "directory") {
      const summary = await summarizeDirectory(absolutePath);
      fileCount = summary.fileCount;
      bytes = summary.bytes;
    } else if (exists) {
      fileCount = 1;
      bytes = entryStat.size;
    }
  } catch (error) {
    if (error?.code !== "ENOENT") {
      throw error;
    }
  }

  if (required && !exists) {
    throw new Error(`Required Tauri runtime layout entry is missing: ${relativePath}`);
  }

  return {
    name,
    storage: "program-root",
    kind,
    required,
    relativePath,
    exists,
    fileCount,
    bytes,
  };
}

async function summarizeDirectory(directoryPath) {
  let fileCount = 0;
  let bytes = 0;
  for (const entry of await readdir(directoryPath, { withFileTypes: true })) {
    const absolutePath = path.join(directoryPath, entry.name);
    if (entry.isDirectory()) {
      const child = await summarizeDirectory(absolutePath);
      fileCount += child.fileCount;
      bytes += child.bytes;
      continue;
    }

    if (entry.isFile()) {
      const entryStat = await stat(absolutePath);
      fileCount += 1;
      bytes += entryStat.size;
    }
  }

  return { fileCount, bytes };
}

async function validateRuntimeLayoutManifest(manifest) {
  if (manifest.schemaVersion !== 1) {
    throw new Error(`Unsupported runtime layout manifest schema: ${manifest.schemaVersion}`);
  }

  for (const entry of manifest.programRootResources) {
    const absolutePath = resolveResourcesRelativePath(entry.relativePath);
    const entryStat = await stat(absolutePath);
    if (entry.kind === "directory" && !entryStat.isDirectory()) {
      throw new Error(`Runtime layout entry should be a directory: ${entry.relativePath}`);
    }

    if (entry.kind === "file" && !entryStat.isFile()) {
      throw new Error(`Runtime layout entry should be a file: ${entry.relativePath}`);
    }
  }

  for (const directoryName of stableResourceDirs) {
    const misplacedPath = path.join(sidecarRoot, directoryName);
    try {
      await stat(misplacedPath);
      throw new Error(`Stable program resource '${directoryName}' must stay outside resources/sidecar.`);
    } catch (error) {
      if (error?.code !== "ENOENT") {
        throw error;
      }
    }
  }
}

function sidecarFileName() {
  return rid.startsWith("win-") ? "ExportDocManager.Api.exe" : "ExportDocManager.Api";
}

function excelAnalyzerFileName() {
  return rid.startsWith("win-") ? "exportdoc-excel-analyzer.exe" : "exportdoc-excel-analyzer";
}

function toResourcesRelativePath(targetPath) {
  const fullPath = path.resolve(targetPath);
  const relative = path.relative(resourcesRoot, fullPath);
  if (relative === "" || relative.startsWith("..") || path.isAbsolute(relative)) {
    throw new Error(`Runtime layout path must stay under resources root: ${fullPath}`);
  }

  return relative.split(path.sep).join("/");
}

function resolveResourcesRelativePath(relativePath) {
  if (!relativePath || path.isAbsolute(relativePath) || relativePath.split(/[\\/]/).includes("..")) {
    throw new Error(`Runtime layout path must be a relative resources path: ${relativePath}`);
  }

  return path.join(resourcesRoot, relativePath);
}

async function copyWindowsWebView2LoaderIfNeeded() {
  if (!rid.startsWith("win-")) {
    return;
  }

  const arch = rid.endsWith("-arm64") ? "arm64" : "x64";
  const source = await findWindowsWebView2Loader(arch);
  await cp(source, path.join(resourcesRoot, "WebView2Loader.dll"), { force: true });
}

async function copyExcelAnalyzerIfAvailable() {
  const source = await findExcelAnalyzerBinary();
  if (!source) {
    console.warn(
      `Rust Excel analyzer binary was not found for ${rid}; packaged app will fall back to the built-in .NET module.`,
    );
    return;
  }

  await cp(source, path.join(toolsRoot, excelAnalyzerFileName()), { force: true });
  await cp(path.join(repoRoot, "tools", "excel-analyzer-rs", "THIRD_PARTY_NOTICES.md"), path.join(toolsRoot, "EXCEL_ANALYZER_NOTICES.md"), { force: true });
}

async function findExcelAnalyzerBinary() {
  const candidates = [];
  for (const variableName of ["EXPORTDOCMANAGER_EXCEL_ANALYZER_BINARY", "EXPORTDOCMANAGER_EXCEL_ANALYZER"]) {
    const configuredPath = process.env[variableName];
    if (configuredPath) {
      candidates.push(path.resolve(configuredPath));
    }
  }

  const fileName = excelAnalyzerFileName();
  const targetRoots = [
    process.env.EXPORTDOCMANAGER_EXCEL_ANALYZER_TARGET_DIR,
    process.env.CARGO_TARGET_DIR,
    path.join(repoRoot, "artifacts", "cargo-target-excel-analyzer"),
    path.join(repoRoot, "tools", "excel-analyzer-rs", "target"),
  ].filter(Boolean);

  for (const targetRoot of targetRoots) {
    candidates.push(path.join(targetRoot, "release", fileName));
    const rustTarget = process.env.CARGO_BUILD_TARGET || rustTargetTripleFromRid(rid);
    if (rustTarget) {
      candidates.push(path.join(targetRoot, rustTarget, "release", fileName));
    }
  }

  return findFirstExistingFile(candidates);
}

function rustTargetTripleFromRid(runtimeIdentifier) {
  const map = new Map([
    ["win-x64", "x86_64-pc-windows-msvc"],
    ["win-arm64", "aarch64-pc-windows-msvc"],
    ["linux-x64", "x86_64-unknown-linux-gnu"],
    ["linux-arm64", "aarch64-unknown-linux-gnu"],
    ["osx-x64", "x86_64-apple-darwin"],
    ["osx-arm64", "aarch64-apple-darwin"],
  ]);

  return map.get(runtimeIdentifier) || "";
}

async function copyBrowserRuntimeResources(sourceRoot, destinationRoot) {
  await rm(destinationRoot, { recursive: true, force: true });
  await mkdir(destinationRoot, { recursive: true });

  await copyIfExists(path.join(sourceRoot, "README.md"), path.join(destinationRoot, "README.md"));

  if (rid === "linux-arm64") {
    const chromiumArm64Source = path.join(sourceRoot, "ChromiumArm64");
    const chromiumArm64Stat = await tryStat(chromiumArm64Source);
    if (chromiumArm64Stat?.isDirectory()) {
      await cp(chromiumArm64Source, path.join(destinationRoot, "ChromiumArm64"), { recursive: true, force: true });
      return;
    }
    if (!allowMissingBrowser) {
      throw new Error(`Chromium ARM64 was not found under ${sourceRoot}. Run scripts/provision-playwright-chromium-arm64.ps1 on Linux ARM64 before building.`);
    }
    return;
  }

  let platform;
  try {
    platform = chromeForTestingPlatform();
  } catch (error) {
    if (!allowMissingBrowser) {
      throw error;
    }

    console.warn(`${error.message} Continuing without a bundled browser because EXPORTDOCMANAGER_ALLOW_MISSING_BROWSER=true.`);
    return;
  }
  const platformSource = path.join(sourceRoot, "ChromeForTesting", platform);
  const platformDestination = path.join(destinationRoot, "ChromeForTesting", platform);
  const headlessShellSource = path.join(platformSource, "ChromeHeadlessShell");
  const headlessShellStat = await tryStat(headlessShellSource);

  if (headlessShellStat?.isDirectory()) {
    await mkdir(platformDestination, { recursive: true });
    await copyLooseFiles(platformSource, platformDestination);
    await cp(headlessShellSource, path.join(platformDestination, "ChromeHeadlessShell"), {
      recursive: true,
      force: true,
    });
    return;
  }

  const directExecutable = await findFirstExistingFile([
    path.join(sourceRoot, "chrome-headless-shell.exe"),
    path.join(sourceRoot, "chrome-headless-shell"),
    path.join(sourceRoot, "ChromeHeadlessShell", "chrome-headless-shell.exe"),
    path.join(sourceRoot, "ChromeHeadlessShell", "chrome-headless-shell"),
  ]);

  if (directExecutable) {
    const relative = path.relative(sourceRoot, directExecutable);
    const directRoot = relative.includes(path.sep) ? relative.split(path.sep)[0] : relative;
    const directSource = path.join(sourceRoot, directRoot);
    await cp(directSource, path.join(destinationRoot, directRoot), { recursive: true, force: true });
    return;
  }

  if (allowMissingBrowser) {
    console.warn(
      `Chrome Headless Shell for ${platform} was not found under ${sourceRoot}; continuing with an empty optional browser resource directory because EXPORTDOCMANAGER_ALLOW_MISSING_BROWSER=true.`,
    );
    return;
  }

  throw new Error(
    `Chrome Headless Shell for ${platform} was not found under ${sourceRoot}. Run scripts/provision-chrome-for-testing.ps1 with -Product ChromeHeadlessShell before building the desktop bundle.`,
  );
}

function chromeForTestingPlatform() {
  if (rid.startsWith("win-")) {
    return "win64";
  }

  if (rid === "linux-x64") {
    return "linux64";
  }

  if (rid === "osx-arm64") {
    return "mac-arm64";
  }

  if (rid === "osx-x64") {
    return "mac-x64";
  }

  throw new Error(`No Chrome for Testing platform mapping is defined for ${rid}.`);
}

async function copyLooseFiles(sourceRoot, destinationRoot) {
  for (const entry of await readdir(sourceRoot, { withFileTypes: true })) {
    if (entry.isFile()) {
      await cp(path.join(sourceRoot, entry.name), path.join(destinationRoot, entry.name), { force: true });
    }
  }
}

async function copyIfExists(source, destination) {
  const entryStat = await tryStat(source);
  if (entryStat?.isFile() || entryStat?.isDirectory()) {
    await cp(source, destination, { recursive: entryStat.isDirectory(), force: true });
  }
}

async function findFirstExistingFile(candidates) {
  for (const candidate of candidates) {
    const candidateStat = await tryStat(candidate);
    if (candidateStat?.isFile()) {
      return candidate;
    }
  }

  return null;
}

async function tryStat(targetPath) {
  try {
    return await stat(targetPath);
  } catch (error) {
    if (error?.code !== "ENOENT") {
      throw error;
    }
  }

  return null;
}

async function findWindowsWebView2Loader(arch) {
  const cargoHome = resolveCargoHome();
  const registrySrcRoot = path.join(cargoHome, "registry", "src");
  const candidates = [];

  try {
    for (const registryEntry of await readdir(registrySrcRoot, { withFileTypes: true })) {
      if (!registryEntry.isDirectory()) {
        continue;
      }

      const registryRoot = path.join(registrySrcRoot, registryEntry.name);
      for (const crateEntry of await readdir(registryRoot, { withFileTypes: true })) {
        if (crateEntry.isDirectory() && crateEntry.name.startsWith("webview2-com-sys-")) {
          candidates.push(path.join(registryRoot, crateEntry.name, arch, "WebView2Loader.dll"));
        }
      }
    }
  } catch (error) {
    if (error?.code !== "ENOENT") {
      throw error;
    }
  }

  if (process.env.CARGO_TARGET_DIR) {
    candidates.push(path.join(process.env.CARGO_TARGET_DIR, "release", "WebView2Loader.dll"));
    candidates.push(path.join(process.env.CARGO_TARGET_DIR, "debug", "WebView2Loader.dll"));
  }

  for (const candidate of candidates) {
    try {
      const candidateStat = await stat(candidate);
      if (candidateStat.isFile()) {
        return candidate;
      }
    } catch (error) {
      if (error?.code !== "ENOENT") {
        throw error;
      }
    }
  }

  throw new Error(
    `WebView2Loader.dll for ${arch} was not found under '${registrySrcRoot}'. Run npm --prefix apps/export-doc-tauri run tauri:check:local once with CARGO_HOME set to the non-system-drive Rust toolchain.`
  );
}

function resolveCargoHome() {
  if (process.env.CARGO_HOME) {
    return path.resolve(process.env.CARGO_HOME);
  }

  const locator = process.platform === "win32" ? "where.exe" : "which";
  const result = spawnSync(locator, [process.platform === "win32" ? "cargo.exe" : "cargo"], {
    encoding: "utf8",
    windowsHide: true,
  });
  const cargoPath = result.status === 0
    ? result.stdout.split(/\r?\n/u).map((value) => value.trim()).find(Boolean)
    : "";
  if (!cargoPath) {
    throw new Error("CARGO_HOME is not set and cargo could not be located on PATH. Use scripts/run-tauri-local.ps1 so the non-system-drive Rust toolchain is selected explicitly.");
  }

  const cargoBinDir = path.dirname(path.resolve(cargoPath));
  const cargoHome = path.basename(cargoBinDir).toLowerCase() === "bin"
    ? path.dirname(cargoBinDir)
    : cargoBinDir;
  if (isWindowsSystemDrivePath(cargoHome)) {
    throw new Error(`Cargo home resolved to the system drive: ${cargoHome}. Configure CARGO_HOME or use scripts/run-tauri-local.ps1 with a non-system-drive Rust toolchain.`);
  }
  return cargoHome;
}

function resolveLocalBuildPath(environmentName, relativePath) {
  const configuredPath = process.env[environmentName];
  if (configuredPath && !isWindowsSystemDrivePath(configuredPath)) {
    return path.resolve(configuredPath);
  }
  return path.join(localRuntimeRoot, relativePath);
}

function isWindowsSystemDrivePath(candidatePath) {
  if (process.platform !== "win32" || !process.env.SystemDrive || !candidatePath) {
    return false;
  }
  const systemRoot = `${process.env.SystemDrive}${path.sep}`.toLowerCase();
  return path.resolve(candidatePath).toLowerCase().startsWith(systemRoot);
}
