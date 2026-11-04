import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
const root = path.resolve(import.meta.dirname, "..");
const read = file => readFileSync(path.join(root, file), "utf8");
const manual = JSON.parse(read("scripts/lib/manual-validation.json")).tests;
const tests = readdirSync(path.join(root, "scripts")).filter(name => /^test_.*\.mjs$/u.test(name));
const sources = [read("apps/export-doc-web/package.json")];
function collect(directory) {
  for (const entry of readdirSync(path.join(root, directory), { withFileTypes: true })) {
    const file = `${directory}/${entry.name}`;
    if (entry.isDirectory()) collect(file);
    else if (/\.(?:ps1|cmd|ya?ml)$/u.test(entry.name)) sources.push(read(file));
  }
}
collect("scripts"); collect(".github/workflows");
const automated = sources.join("\n");
for (const name of tests) {
  if (automated.includes(name)) continue;
  const record = manual[name];
  assert(record?.environment && record.commands?.some(command => command.includes(`scripts/${name}`)), `${name}: missing executable entry or documented manual environment/command`);
}
for (const name of Object.keys(manual)) assert(tests.includes(name), `${name}: stale manual entry`);
console.log(`Validation registry: ${tests.length} executable tests; ${Object.keys(manual).length} platform/manual entries. Syntax checks are not execution evidence.`);
