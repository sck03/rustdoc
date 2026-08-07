import { existsSync, readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const baselinePath = path.join(repositoryRoot, "scripts", "baselines", "source-size-governance.json");
if (!existsSync(baselinePath)) throw new Error(`Source-size baseline was not found: ${baselinePath}`);

const baseline = JSON.parse(readFileSync(baselinePath, "utf8"));
const failures = [];
const report = [];
for (const rule of baseline.rules ?? []) {
  const directory = path.join(repositoryRoot, ...rule.directory.split("/"));
  const regex = new RegExp(rule.filePattern, "u");
  const files = walk(directory).filter((file) => regex.test(path.basename(file)));
  const entries = files.map((file) => ({
    file: path.relative(repositoryRoot, file).replaceAll(path.sep, "/"),
    lines: readFileSync(file, "utf8").split(/\r?\n/u).length,
  }));
  for (const entry of entries) {
    if (entry.lines > Number(rule.maximumLines)) {
      failures.push(`${rule.name}: ${entry.file} has ${entry.lines} lines; maximum is ${rule.maximumLines}.`);
    }
  }
  const aggregate = entries.reduce((sum, entry) => sum + entry.lines, 0);
  if (rule.maximumAggregateLines !== undefined && aggregate > Number(rule.maximumAggregateLines)) {
    failures.push(`${rule.name}: aggregate is ${aggregate} lines; maximum is ${rule.maximumAggregateLines}.`);
  }
  report.push({ name: rule.name, files: entries, aggregateLines: aggregate });
}

if (failures.length > 0) {
  process.stderr.write(`Source-size governance failed. Split the affected responsibility before adding more logic.\n${failures.join("\n")}\n`);
  process.exit(1);
}

process.stdout.write(`Source-size governance passed: ${JSON.stringify(report)}\n`);

function walk(directory) {
  if (!existsSync(directory)) throw new Error(`Governed directory was not found: ${directory}`);
  const files = [];
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) files.push(...walk(fullPath));
    else if (entry.isFile()) files.push(fullPath);
  }
  return files;
}
