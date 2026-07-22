import fs from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const sourcePath = path.join(root, "apps/export-doc-web/src/styles.css");
const targetPath = path.join(root, "apps/export-doc-web/src/features/master-data/hsKnowledge.css");
const marker = ".hs-knowledge-entry,";
const source = fs.readFileSync(sourcePath, "utf8");
const index = source.indexOf(marker);

if (index < 0) throw new Error(`Unable to find ${marker} in styles.css`);
if (fs.existsSync(targetPath)) throw new Error(`${targetPath} already exists`);

const shared = source.slice(0, index).trimEnd();
const feature = source.slice(index).trim();
fs.writeFileSync(sourcePath, `${shared}\n`, "utf8");
fs.writeFileSync(targetPath, `/* HS knowledge center and invoice-side HS matching. */\n${feature}\n`, "utf8");
process.stdout.write(`moved ${feature.split(/\r?\n/).length} lines from styles.css to hsKnowledge.css\n`);
