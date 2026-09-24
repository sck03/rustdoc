import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

// RUSTSEC-2024-0429 affects VariantStrIter, not all GLib operations. Tauri 2's
// GTK3 stack is pinned to glib 0.18.5; fixed glib >=0.20 is API-incompatible.
// This conservative source check is an exception precondition, not proof that
// upstream memory unsafety is fixed. See the workflow manual's risk decision.
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const result = spawnSync("cargo", ["metadata", "--locked", "--format-version", "1"], {
  cwd: root, encoding: "utf8", maxBuffer: 32 * 1024 * 1024, timeout: 120_000, windowsHide: true,
});
if (result.error) throw result.error;
if (result.status !== 0) throw new Error(`Cannot inspect locked dependency sources: ${result.stderr}`);
const packages = JSON.parse(result.stdout).packages;
const glib = packages.filter(item => item.name === "glib");
const tauri = packages.filter(item => item.name === "tauri");
if (glib.length !== 1 || glib[0].version !== "0.18.5" || tauri.length !== 1 || tauri[0].version !== "2.11.6") {
  throw new Error("Tauri/GLib versions changed; remove or reassess RUSTSEC-2024-0429 exception.");
}
const symbols = /\b(?:VariantStrIter|array_iter_str)\b/u;
let scanned = 0;
for (const item of packages) {
  if (item.id === glib[0].id) continue; // Reviewed upstream definitions and own tests.
  const sourceRoot = path.dirname(item.manifest_path);
  for (const file of rustFiles(sourceRoot)) {
    scanned++;
    if (symbols.test(readFileSync(file, "utf8"))) {
      throw new Error(`Affected GLib string iterator referenced by ${item.name}: ${path.relative(sourceRoot, file)}. Exception no longer applies.`);
    }
  }
}
console.log(`RUSTSEC-2024-0429 exception preconditions checked: glib 0.18.5 / tauri 2.11.6, ${scanned} Rust files. Upstream issue remains unfixed.`);

function* rustFiles(directory) {
  for (const item of readdirSync(directory, { withFileTypes: true })) {
    if (item.isSymbolicLink()) throw new Error(`Cannot audit linked dependency source: ${path.join(directory, item.name)}`);
    if (item.isDirectory()) {
      if (![".git", ".codex-runtime", "artifacts", "target", "node_modules", "dist"].includes(item.name)) yield* rustFiles(path.join(directory, item.name));
    } else if (item.isFile() && item.name.endsWith(".rs")) yield path.join(directory, item.name);
  }
}
