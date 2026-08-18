import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workflowRoot = path.join(repositoryRoot, ".github", "workflows");
const requiredActions = new Map([
  ["actions/checkout", "fbc6f3992d24b796d5a048ff273f7fcc4a7b6c09"],
  ["actions/setup-dotnet", "26b0ec14cb23fa6904739307f278c14f94c95bf1"],
  ["actions/setup-node", "a0853c24544627f65ddf259abe73b1d18a591444"],
  ["actions/setup-python", "ece7cb06caefa5fff74198d8649806c4678c61a1"],
  ["actions/upload-artifact", "043fb46d1a93c77aae656e7c1c64a875d1fc6a0a"],
  ["actions/download-artifact", "3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c"],
  ["docker/metadata-action", "dc802804100637a589fabce1cb79ff13a1411302"],
  ["docker/build-push-action", "53b7df96c91f9c12dcc8a07bcb9ccacbed38856a"],
  ["docker/setup-qemu-action", "96fe6ef7f33517b61c61be40b68a1882f3264fb8"],
  ["docker/setup-buildx-action", "bb05f3f5519dd87d3ba754cc423b652a5edd6d2c"],
  ["docker/login-action", "dbcb813823bdd20940b903addbd779551569679f"],
  ["dtolnay/rust-toolchain", "4360b52568e2003a75bf9bc1d59f33a8e3fc893c"],
]);
const requiredDotNetSdk = "10.0.302";
const requiredRustToolchain = "1.96.0";
const failures = [];
let actionCount = 0;

const chromeProvisioningPath = path.join(repositoryRoot, "scripts", "provision-chrome-for-testing.ps1");
const chromeProvisioning = readFileSync(chromeProvisioningPath, "utf8");
if (/\bmac-x64\b/iu.test(chromeProvisioning)) {
  failures.push("provision-chrome-for-testing.ps1: retired Intel macOS Chrome payload is still supported.");
}

for (const entry of readdirSync(workflowRoot, { withFileTypes: true })) {
  if (!entry.isFile() || !/\.ya?ml$/iu.test(entry.name)) continue;
  const workflowPath = path.join(workflowRoot, entry.name);
  const lines = readFileSync(workflowPath, "utf8").split(/\r?\n/u);
  for (let index = 0; index < lines.length; index += 1) {
    const match = lines[index].match(/\buses:\s*([^\s#]+)@([^\s#]+)/u);
    if (!match) continue;
    actionCount += 1;
    const [, action, revision] = match;
    const required = requiredActions.get(action);
    if (!required) {
      failures.push(`${entry.name}:${index + 1}: unreviewed third-party action ${action}.`);
    } else if (revision !== required) {
      failures.push(`${entry.name}:${index + 1}: ${action} must use reviewed commit ${required}, found ${revision}.`);
    }
    if (!/^[0-9a-f]{40}$/u.test(revision)) {
      failures.push(`${entry.name}:${index + 1}: ${action} must be pinned to a full commit SHA.`);
    }
    if (action === "actions/setup-node") {
      const localBlock = lines.slice(index + 1, index + 8).join("\n");
      if (!/node-version:\s*["']?24["']?\s*$/mu.test(localBlock)) {
        failures.push(`${entry.name}:${index + 1}: actions/setup-node must explicitly select Node 24.`);
      }
    }
    if (action === "actions/setup-dotnet") {
      const localBlock = lines.slice(index + 1, index + 8).join("\n");
      const escapedSdk = requiredDotNetSdk.replaceAll(".", "\\.");
      if (!new RegExp(`dotnet-version:\\s*["']?${escapedSdk}["']?\\s*$`, "mu").test(localBlock)) {
        failures.push(
          `${entry.name}:${index + 1}: actions/setup-dotnet must explicitly select ${requiredDotNetSdk}.`,
        );
      }
    }
    if (action === "dtolnay/rust-toolchain") {
      const localBlock = lines.slice(index + 1, index + 8).join("\n");
      const escapedToolchain = requiredRustToolchain.replaceAll(".", "\\.");
      if (!new RegExp(`toolchain:\\s*["']?${escapedToolchain}["']?\\s*$`, "mu").test(localBlock)) {
        failures.push(
          `${entry.name}:${index + 1}: Rust setup must explicitly select ${requiredRustToolchain}.`,
        );
      }
    }
  }

  if (/node-version:\s*["']?(?:20|22)["']?\b/mu.test(lines.join("\n"))) {
    failures.push(`${entry.name}: workflow still declares Node 20 or Node 22.`);
  }
  if (/dotnet-version:\s*["']?(?:8|9)(?:\.|["'])/mu.test(lines.join("\n"))) {
    failures.push(`${entry.name}: workflow still declares .NET 8 or .NET 9.`);
  }
  if (/\b(?:osx-x64|mac-x64|macos-[^\s"']*-intel)\b/mu.test(lines.join("\n"))) {
    failures.push(`${entry.name}: workflow still declares the retired Intel macOS desktop target.`);
  }

  let doubleQuotedHereStringStart = -1;
  for (let index = 0; index < lines.length; index += 1) {
    const trimmed = lines[index].trim();
    if (doubleQuotedHereStringStart < 0 && trimmed === '@"') {
      doubleQuotedHereStringStart = index;
      continue;
    }
    if (doubleQuotedHereStringStart < 0) continue;
    if (trimmed.startsWith('"@')) {
      doubleQuotedHereStringStart = -1;
      continue;
    }
    if (lines[index].trimEnd().endsWith("`")) {
      failures.push(
        `${entry.name}:${index + 1}: a line-ending Markdown backtick inside a double-quoted PowerShell here-string escapes the newline; use a literal @' ... '@ here-string.`,
      );
    }
  }
}

const rootToolchain = readFileSync(path.join(repositoryRoot, "rust-toolchain.toml"), "utf8");
if (!new RegExp(`channel\\s*=\\s*["']${requiredRustToolchain.replaceAll(".", "\\.")}["']`, "u").test(rootToolchain)) {
  failures.push(`rust-toolchain.toml must pin Rust ${requiredRustToolchain}.`);
}

const dependencyWorkflowPath = path.join(workflowRoot, "dependency-governance.yml");
const dependencyWorkflow = readFileSync(dependencyWorkflowPath, "utf8");
if (!dependencyWorkflow.includes("generate-dependency-governance.mjs artifacts/dependency-governance --release --verify-repository")) {
  failures.push("dependency-governance.yml: dependency inventory must enforce release licenses and repository notice consistency.");
}
const dependencyGenerator = readFileSync(
  path.join(repositoryRoot, "scripts", "generate-dependency-governance.mjs"),
  "utf8",
);
if (!dependencyGenerator.includes('"--no-restore"')) {
  failures.push("generate-dependency-governance.mjs: NuGet inventory must consume the dependency graph restored by the workflow.");
}
const cargoAuditInvocations = dependencyWorkflow
  .split(/\r?\n/u)
  .filter((line) => line.includes('cargo-audit"') && line.includes("--file"));
if (cargoAuditInvocations.length !== 3) {
  failures.push(`dependency-governance.yml: expected 3 cargo-audit lock-file invocations, found ${cargoAuditInvocations.length}.`);
}
for (const invocation of cargoAuditInvocations) {
  if (!/cargo-audit"\s+audit\b/u.test(invocation)) {
    failures.push("dependency-governance.yml: direct cargo-audit execution must include the audit subcommand.");
  }
  if (!invocation.includes("--deny unsound") || !invocation.includes("--deny yanked")) {
    failures.push("dependency-governance.yml: RustSec invocations must reject new unsound and yanked dependencies.");
  }
}
const reviewedRustSecExceptions = dependencyWorkflow.match(/--ignore\s+RUSTSEC-[0-9-]+/gu) ?? [];
if (reviewedRustSecExceptions.length !== 1 || reviewedRustSecExceptions[0] !== "--ignore RUSTSEC-2024-0429") {
  failures.push("dependency-governance.yml: only the reviewed RUSTSEC-2024-0429 exception is permitted.");
}

for (const workflowName of ["windows-desktop-package.yml", "windows-browser-server-package.yml"]) {
  const workflow = readFileSync(path.join(workflowRoot, workflowName), "utf8");
  if (!workflow.includes("rust_target: x86_64-pc-windows-msvc")) {
    failures.push(`${workflowName}: Windows release Rust target must use the GitHub runner MSVC toolchain.`);
  }
  if (workflow.includes("rust_target: x86_64-pc-windows-gnu")) {
    failures.push(`${workflowName}: Windows release workflow must not replace the requested MSVC target with the local GNU target.`);
  }
}

if (failures.length > 0) {
  process.stderr.write(`${failures.join("\n")}\n`);
  process.exit(1);
}

process.stdout.write(`GitHub workflow action governance passed (${actionCount} action references).\n`);
