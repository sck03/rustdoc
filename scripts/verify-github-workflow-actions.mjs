import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workflowRoot = path.join(repositoryRoot, ".github", "workflows");
const requiredActions = new Map([
  ["actions/checkout", "fbc6f3992d24b796d5a048ff273f7fcc4a7b6c09"],
  ["actions/setup-node", "a0853c24544627f65ddf259abe73b1d18a591444"],
  ["actions/setup-python", "ece7cb06caefa5fff74198d8649806c4678c61a1"],
  ["actions/cache", "caa296126883cff596d87d8935842f9db880ef25"],
  ["actions/upload-artifact", "043fb46d1a93c77aae656e7c1c64a875d1fc6a0a"],
  ["actions/download-artifact", "3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c"],
  ["docker/metadata-action", "dc802804100637a589fabce1cb79ff13a1411302"],
  ["docker/build-push-action", "53b7df96c91f9c12dcc8a07bcb9ccacbed38856a"],
  ["docker/setup-qemu-action", "96fe6ef7f33517b61c61be40b68a1882f3264fb8"],
  ["docker/setup-buildx-action", "bb05f3f5519dd87d3ba754cc423b652a5edd6d2c"],
  ["docker/login-action", "dbcb813823bdd20940b903addbd779551569679f"],
  ["dtolnay/rust-toolchain", "4360b52568e2003a75bf9bc1d59f33a8e3fc893c"],
]);
const requiredRustToolchain = "1.98.1";
const failures = [];
let actionCount = 0;

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
      if (!/node-version:\s*["']?26["']?\s*$/mu.test(localBlock)) {
        failures.push(`${entry.name}:${index + 1}: actions/setup-node must explicitly select Node 26.`);
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

  if (/node-version:\s*["']?(?:20|22|24)["']?\b/mu.test(lines.join("\n"))) {
    failures.push(`${entry.name}: workflow still declares an older Node runtime.`);
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
const dependencyPolicyChecks = dependencyWorkflow.match(/node scripts\/verify-dependency-policy\.mjs/gu) ?? [];
if (dependencyPolicyChecks.length < 2) {
  failures.push("dependency-governance.yml: exact dependency policy must run before and after generating dependency evidence.");
}
const cargoAuditInvocations = dependencyWorkflow
  .split(/\r?\n/u)
  .filter((line) => line.includes('cargo-audit"') && line.includes("--file"));
if (cargoAuditInvocations.length === 0) {
  failures.push(
    "dependency-governance.yml: expected at least one workspace cargo-audit invocation.",
  );
}
for (const invocation of cargoAuditInvocations) {
  if (!/cargo-audit"\s+audit\b/u.test(invocation)) {
    failures.push("dependency-governance.yml: direct cargo-audit execution must include the audit subcommand.");
  }
  if (!invocation.includes("--deny unsound") || !invocation.includes("--deny yanked")) {
    failures.push("dependency-governance.yml: RustSec invocations must reject new unsound and yanked dependencies.");
  }
}
if (failures.length > 0) {
  process.stderr.write(`${failures.join("\n")}\n`);
  process.exit(1);
}

process.stdout.write(`GitHub workflow action governance passed (${actionCount} action references).\n`);
