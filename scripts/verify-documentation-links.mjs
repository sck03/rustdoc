import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
function markdownFiles(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const file = path.join(directory, entry.name);
    return entry.isDirectory() ? markdownFiles(file) : entry.name.endsWith(".md") ? [file] : [];
  });
}
const files = [...markdownFiles(path.join(root, "docs")), path.join(root, "README.md"), path.join(root, "scripts/README.md")];
const failures = [];
let links = 0;
for (const file of files) {
  const content = fs.readFileSync(file, "utf8").replace(/^```[^\n]*\n[\s\S]*?^```[^\n]*$/gm, "");
  for (const match of content.matchAll(/\[[^\]\n]*\]\(([^)\n]+)\)/g)) {
    const href = match[1].trim().replace(/^<|>$/g, "");
    if (/^(?:[a-z][a-z\d+.-]*:|#)/i.test(href)) continue;
    const relative = decodeURIComponent(href.split("#")[0]);
    if (!relative) continue;
    links += 1;
    const target = path.resolve(path.dirname(file), relative);
    if (!fs.existsSync(target)) failures.push(`${path.relative(root, file)}: missing ${relative}`);
  }
}
if (failures.length) {
  console.error(failures.join("\n"));
  process.exitCode = 1;
} else {
  console.log(`Documentation links passed (${files.length} files, ${links} local links).`);
}
