import { existsSync, readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

// Native archives hosted on NuGet are governed by the native resource manifest.
const args = process.argv.slice(2);
const index = args.indexOf("--repository-root");
if (index >= 0 && (!args[index + 1] || args[index + 1].startsWith("--"))) throw new Error("--repository-root requires a path");
const root = index < 0 ? fileURLToPath(new URL("../", import.meta.url)) : path.resolve(args[index + 1]);
const ignored = new Set([".git", ".codex-runtime", "artifacts", "bin", "obj", "dist", "target", "node_modules", "Browsers", "App_Data"]);
function files(directory) {
  if (!existsSync(directory)) return [];
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    if (entry.isSymbolicLink()) return [];
    const name = path.join(directory, entry.name);
    return entry.isDirectory() ? ignored.has(entry.name) ? [] : files(name) : [name];
  });
}
const failures = [];
if (!args.includes("--generated-only")) {
  for (const file of files(root)) {
    if (/\.(?:cs|csproj|sln|slnx)$/iu.test(file) || ["Directory.Build.props", "Directory.Packages.props", "global.json", "NuGet.Config", "packages.lock.json"].includes(path.basename(file))) failures.push(`Retired .NET source/configuration: ${path.relative(root, file)}`);
  }
}
for (const name of ["THIRD_PARTY_DEPENDENCIES.md", "THIRD_PARTY_NOTICES.md"]) {
  const file = path.join(root, name);
  if (existsSync(file) && /(?:pkg:nuget\/|\|\s*NPOI\s*\|)/iu.test(readFileSync(file, "utf8"))) failures.push(`Managed dependency in Rust notices: ${name}`);
}
if (failures.length) throw new Error(failures.join("\n"));
console.log("Rust source and delivery dependency boundary passed (no managed .NET projects).");
