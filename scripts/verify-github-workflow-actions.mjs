import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workflowRoot = path.join(repositoryRoot, ".github", "workflows");
const requiredVersions = new Map([
  ["actions/checkout", "v5"],
  ["actions/setup-dotnet", "v5"],
  ["actions/setup-node", "v5"],
  ["actions/setup-python", "v6"],
  ["actions/upload-artifact", "v7"],
  ["actions/download-artifact", "v8"],
]);
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
    const [, action, version] = match;
    const required = requiredVersions.get(action);
    if (required && version !== required) {
      failures.push(`${entry.name}:${index + 1}: ${action} must use @${required}, found @${version}.`);
    }
    if (action === "actions/setup-node") {
      const localBlock = lines.slice(index + 1, index + 8).join("\n");
      if (!/node-version:\s*["']?24["']?\s*$/mu.test(localBlock)) {
        failures.push(`${entry.name}:${index + 1}: actions/setup-node must explicitly select Node 24.`);
      }
    }
  }

  if (/node-version:\s*["']?(?:20|22)["']?\b/mu.test(lines.join("\n"))) {
    failures.push(`${entry.name}: workflow still declares Node 20 or Node 22.`);
  }
}

const dependencyWorkflowPath = path.join(workflowRoot, "dependency-governance.yml");
const dependencyWorkflow = readFileSync(dependencyWorkflowPath, "utf8");
const cargoAuditInvocations = dependencyWorkflow
  .split(/\r?\n/u)
  .filter((line) => line.includes("cargo-audit\"") && line.includes("--file"));
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

if (failures.length > 0) {
  process.stderr.write(`${failures.join("\n")}\n`);
  process.exit(1);
}

process.stdout.write(`GitHub workflow action governance passed (${actionCount} action references).\n`);
