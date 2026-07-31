import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const tauriRoot = path.join(repositoryRoot, "apps", "export-doc-tauri");
const generatedRoot = path.join(repositoryRoot, "artifacts", "tauri-updater-config");
const editionCatalog = JSON.parse(
  readFileSync(path.join(repositoryRoot, "scripts", "product-editions.json"), "utf8"),
);
const buildArguments = process.argv.slice(2);
const productEdition = normalizeProductEdition(process.env.EXPORTDOCMANAGER_PRODUCT_EDITION);
const editionMetadata = editionCatalog.editions?.[productEdition];
if (!editionMetadata) {
  throw new Error(`Product edition metadata is missing for ${productEdition}.`);
}
const projectVersion = String(
  JSON.parse(readFileSync(path.join(repositoryRoot, "version.json"), "utf8")).version || "",
).trim();
const requireSignedUpdater = /^(?:1|true|yes)$/iu.test(
  String(process.env.EXPORTDOCMANAGER_REQUIRE_SIGNED_UPDATER || "").trim(),
);
const endpoint = resolveUpdaterEndpoint(
  String(process.env.EXPORTDOCMANAGER_UPDATER_ENDPOINT || "").trim(),
  editionMetadata,
  projectVersion,
);
const publicKey = String(process.env.EXPORTDOCMANAGER_UPDATER_PUBLIC_KEY || "").trim();
const privateKey = String(process.env.TAURI_SIGNING_PRIVATE_KEY || "").trim();
const privateKeyPassword = String(process.env.TAURI_SIGNING_PRIVATE_KEY_PASSWORD || "");
const allowInsecureUpdaterEndpoint = /^(?:1|true|yes)$/iu.test(
  String(process.env.EXPORTDOCMANAGER_ALLOW_INSECURE_UPDATER_ENDPOINT || "").trim(),
);

if (endpoint && !publicKey) {
  throw new Error("A packaged updater endpoint requires EXPORTDOCMANAGER_UPDATER_PUBLIC_KEY.");
}

if (endpoint) {
  if (endpoint.length > 2048 || /[\u0000-\u001f\u007f\\]/u.test(endpoint)) {
    throw new Error("Release updater endpoint contains an invalid character or exceeds 2048 characters.");
  }
  const parsedEndpoint = new URL(endpoint);
  if (!['http:', 'https:'].includes(parsedEndpoint.protocol)) {
    throw new Error(`Release updater endpoint must use HTTP or HTTPS: ${endpoint}`);
  }
  if (parsedEndpoint.username || parsedEndpoint.password || parsedEndpoint.hash) {
    throw new Error("Release updater endpoint must not contain credentials or a URL fragment.");
  }
  if (parsedEndpoint.protocol === "http:" && !allowInsecureUpdaterEndpoint) {
    throw new Error(
      "An HTTP updater endpoint is only allowed for a controlled intranet build. " +
      "Set EXPORTDOCMANAGER_ALLOW_INSECURE_UPDATER_ENDPOINT=true explicitly, or use HTTPS.",
    );
  }
}

if (requireSignedUpdater) {
  if (!publicKey || !privateKey || !privateKeyPassword) {
    throw new Error(
      "A release build requires EXPORTDOCMANAGER_UPDATER_PUBLIC_KEY, " +
      "TAURI_SIGNING_PRIVATE_KEY and " +
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

baseConfig = deepMerge(baseConfig, {
  productName: editionMetadata.productName,
  identifier: editionMetadata.identifier,
});

if (publicKey) {
  const releaseConfig = deepMerge(baseConfig, {
    bundle: { createUpdaterArtifacts: true },
    plugins: {
      updater: {
        endpoints: endpoint ? [endpoint] : [],
        pubkey: publicKey,
        dangerousInsecureTransportProtocol: endpoint.startsWith("http:"),
      },
    },
  });
  mkdirSync(generatedRoot, { recursive: true });
  const configPath = path.join(generatedRoot, `tauri.${editionMetadata.slug}.release.conf.json`);
  writeFileSync(configPath, `${JSON.stringify(releaseConfig, null, 2)}\n`, "utf8");
  buildArguments.push("--config", configPath);
} else {
  mkdirSync(generatedRoot, { recursive: true });
  const configPath = path.join(generatedRoot, `tauri.${editionMetadata.slug}.local.conf.json`);
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

function normalizeProductEdition(value) {
  const normalized = String(value || "Full").trim().toLowerCase();
  if (normalized === "document") return "Document";
  if (normalized === "sales") return "Sales";
  if (normalized === "full") return "Full";
  throw new Error(`Unsupported product edition: ${value}`);
}

function resolveUpdaterEndpoint(configuredEndpoint, metadata, version) {
  if (configuredEndpoint) return configuredEndpoint;
  if (!requireSignedUpdater) return "";

  const repository = String(process.env.GITHUB_REPOSITORY || "").trim();
  if (!/^[^/]+\/[^/]+$/u.test(repository)) {
    return "";
  }

  const prerelease = version.includes("-");
  const channelTag = prerelease ? metadata.prereleaseChannelTag : metadata.stableChannelTag;
  const manifestAsset = prerelease ? metadata.prereleaseManifestAsset : metadata.stableManifestAsset;
  return `https://github.com/${repository}/releases/download/${channelTag}/${manifestAsset}`;
}
