import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { normalizeReleaseVersion } from "./lib/release-version.mjs";

export async function syncVersion(root, requestedVersion) {
  const changes = new Map();
  const read = file => readFile(path.join(root, file), "utf8");
  const updateJson = async (file, mutate) => {
    const value = JSON.parse(await read(file));
    mutate(value);
    changes.set(file, `${JSON.stringify(value, null, 2)}\n`);
  };
  const config = JSON.parse(await read("version.json"));
  const version = normalizeReleaseVersion(requestedVersion || config.version);
  await updateJson("version.json", value => {
    value.version = version;
    value.assemblyVersion = value.fileVersion = `${version.split("-")[0]}.0`;
  });
  for (const directory of ["apps/export-doc-web", "apps/export-doc-tauri"]) {
    for (const file of ["package.json", "package-lock.json"]) {
      await updateJson(`${directory}/${file}`, value => {
        value.version = version;
        if (value.packages?.[""]) value.packages[""].version = version;
      });
    }
  }
  await updateJson("apps/export-doc-tauri/src-tauri/tauri.conf.json", value => { value.version = version; });

  const workspace = await read("Cargo.toml");
  const members = workspace.match(/^members\s*=\s*\[([^\]]+)\]/mu)?.[1];
  if (!members) throw new Error("Cargo workspace members are missing.");
  changes.set("Cargo.toml", replaceVersion(workspace, "workspace.package", version));
  const packageNames = [];
  for (const [, directory] of members.matchAll(/"([^"]+)"/gu)) {
    const file = `${directory}/Cargo.toml`;
    const manifest = await read(file);
    const name = manifest.match(/^name\s*=\s*"([^"]+)"/mu)?.[1];
    if (!name) throw new Error(`Package name missing in ${file}.`);
    packageNames.push(name);
    if (!/^version\.workspace\s*=\s*true\s*$/mu.test(manifest)) {
      changes.set(file, replaceVersion(manifest, "package", version));
    }
  }
  const rootLock = await read("Cargo.lock");
  for (const directory of ["apps/exportdoc-ocr-rs", "tools/excel-analyzer-rs"]) {
    const manifest = await read(`${directory}/Cargo.toml`);
    const name = manifest.match(/^name\s*=\s*"([^"]+)"/mu)?.[1];
    if (!name) throw new Error(`Package name missing in ${directory}.`);
    if (rootLock.includes(`name = "${name}"`)) packageNames.push(name);
    changes.set(`${directory}/Cargo.toml`, replaceVersion(manifest, "package", version));
    changes.set(`${directory}/Cargo.lock`, updateLock(await read(`${directory}/Cargo.lock`), [name], version));
  }
  changes.set("Cargo.lock", updateLock(rootLock, packageNames, version));
  // Validate every input before writing; missing or stale layouts fail before mutation.
  for (const [file, content] of changes) {
    if (await read(file) !== content) await writeFile(path.join(root, file), content, "utf8");
  }
  return version;
}

function replaceVersion(text, section, version) {
  const escaped = section.replaceAll(".", "\\.");
  const pattern = new RegExp(`(\\[${escaped}\\][^\\[]*?\\nversion\\s*=\\s*")[^"]+(")`, "u");
  if (!pattern.test(text)) throw new Error(`Missing explicit version in [${section}].`);
  return text.replace(pattern, (_match, prefix, suffix) => `${prefix}${version}${suffix}`);
}

function updateLock(text, names, version) {
  const remaining = new Set(names);
  const blocks = text.split(/(?=^\[\[package\]\])/mu).map(block => {
    const name = block.match(/^name = "([^"]+)"/mu)?.[1];
    if (!remaining.has(name) || /^source = /mu.test(block)) return block;
    remaining.delete(name);
    return block.replace(/^version = "[^"]+"/mu, `version = "${version}"`);
  });
  if (remaining.size) throw new Error(`Local packages missing from Cargo lock: ${[...remaining].join(", ")}`);
  return blocks.join("");
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
  console.log(`Synced Rust/React/Tauri version ${await syncVersion(root, process.argv[2])}.`);
}
