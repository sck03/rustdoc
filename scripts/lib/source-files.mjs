import { readdirSync } from "node:fs";
import path from "node:path";

export function walkSourceFiles(directory, ignoredDirectories = new Set()) {
  const files = [];
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory() && !ignoredDirectories.has(entry.name)) files.push(...walkSourceFiles(fullPath, ignoredDirectories));
    else if (entry.isFile()) files.push(fullPath);
    else if (entry.isSymbolicLink()) throw new Error(`Governed source must not be linked: ${fullPath}`);
  }
  return files;
}
