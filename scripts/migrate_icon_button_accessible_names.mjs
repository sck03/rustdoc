import fs from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "../apps/export-doc-web/src");
let changedFiles = 0;

for (const file of walk(root)) {
  if (!file.endsWith(".tsx")) continue;
  const source = fs.readFileSync(file, "utf8");
  const next = source.replace(/<button\b([\s\S]*?)>/g, (opening, attributes) => {
    if (!/className\s*=\s*(?:"[^"]*icon-button[^"]*"|\{[^}]*icon-button[^}]*\})/.test(attributes)) return opening;
    if (/aria-label\s*=/.test(attributes)) return opening;
    const title = attributes.match(/title\s*=\s*("[^"]*"|\{[^}]+\})/);
    if (!title) return opening;
    return `<button${attributes.replace(title[0], `${title[0]} aria-label=${title[1]}`)}>`;
  });
  if (next !== source) {
    fs.writeFileSync(file, next, "utf8");
    changedFiles += 1;
  }
}

process.stdout.write(`icon button accessible-name migration updated ${changedFiles} files\n`);

function* walk(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) yield* walk(fullPath);
    else yield fullPath;
  }
}
