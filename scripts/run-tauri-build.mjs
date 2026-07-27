import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const tauriRoot = path.join(repositoryRoot, "apps", "export-doc-tauri");
const generatedRoot = path.join(repositoryRoot, "artifacts", "tauri-updater-config");
const buildArguments = process.argv.slice(2);
const endpoint = String(process.env.EXPORTDOCMANAGER_UPDATER_ENDPOINT || "").trim();
const publicKey = String(process.env.EXPORTDOCMANAGER_UPDATER_PUBLIC_KEY || "").trim();
const privateKey = String(process.env.TAURI_SIGNING_PRIVATE_KEY || "").trim();
const privateKeyPassword = String(process.env.TAURI_SIGNING_PRIVATE_KEY_PASSWORD || "");
const requireSignedUpdater = /^(?:1|true|yes)$/iu.test(
  String(process.env.EXPORTDOCMANAGER_REQUIRE_SIGNED_UPDATER || "").trim(),
);

if (Boolean(endpoint) !== Boolean(publicKey)) {
  throw new Error("Updater endpoint and public key must be configured together.");
}

if (endpoint) {
  const parsedEndpoint = new URL(endpoint);
  if (parsedEndpoint.protocol !== "https:") {
    throw new Error(`Release updater endpoint must use HTTPS: ${endpoint}`);
  }
}

if (requireSignedUpdater) {
  if (!endpoint || !publicKey || !privateKey || !privateKeyPassword) {
    throw new Error(
      "A release build requires EXPORTDOCMANAGER_UPDATER_ENDPOINT, " +
      "EXPORTDOCMANAGER_UPDATER_PUBLIC_KEY, TAURI_SIGNING_PRIVATE_KEY and " +
      "TAURI_SIGNING_PRIVATE_KEY_PASSWORD.",
    );
  }
  if (buildArguments.includes("--no-sign")) {
    throw new Error("A release updater build cannot use --no-sign.");
  }
}

const configIndex = buildArguments.findIndex((argument) => argument === "--config");
let baseConfig = {};
if (configIndex >= 0) {
  const configuredPath = buildArguments[configIndex + 1];
  if (!configuredPath) {
    throw new Error("--config requires a JSON file path.");
  }
  baseConfig = JSON.parse(readFileSync(path.resolve(tauriRoot, configuredPath), "utf8"));
  buildArguments.splice(configIndex, 2);
}

if (endpoint && publicKey) {
  const releaseConfig = deepMerge(baseConfig, {
    bundle: { createUpdaterArtifacts: true },
    plugins: {
      updater: {
        endpoints: [endpoint],
        pubkey: publicKey,
      },
    },
  });
  mkdirSync(generatedRoot, { recursive: true });
  const configPath = path.join(generatedRoot, "tauri.release.conf.json");
  writeFileSync(configPath, `${JSON.stringify(releaseConfig, null, 2)}\n`, "utf8");
  buildArguments.push("--config", configPath);
} else if (Object.keys(baseConfig).length > 0) {
  mkdirSync(generatedRoot, { recursive: true });
  const configPath = path.join(generatedRoot, "tauri.local.conf.json");
  writeFileSync(configPath, `${JSON.stringify(baseConfig, null, 2)}\n`, "utf8");
  buildArguments.push("--config", configPath);
}

const npmCommand = process.platform === "win32" ? "npm.cmd" : "npm";
const result = spawnSync(npmCommand, ["exec", "--", "tauri", "build", ...buildArguments], {
  cwd: tauriRoot,
  env: process.env,
  stdio: "inherit",
  windowsHide: true,
});
if (result.error) throw result.error;
process.exit(result.status ?? 1);

function deepMerge(base, overlay) {
  if (!isObject(base) || !isObject(overlay)) return overlay;
  const merged = { ...base };
  for (const [key, value] of Object.entries(overlay)) {
    merged[key] = isObject(value) && isObject(merged[key])
      ? deepMerge(merged[key], value)
      : value;
  }
  return merged;
}

function isObject(value) {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}
