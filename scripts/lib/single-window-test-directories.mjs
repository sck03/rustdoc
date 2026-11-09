import fs from 'node:fs';
import path from 'node:path';

export function createSingleWindowTestDirectories(root) {
  return Object.fromEntries(['OutBox', 'SentBox', 'InBox', 'FailBox'].map(name => {
    const directory = root ? path.resolve(root, name) : '';
    if (directory) fs.mkdirSync(directory, { recursive: true });
    return [name[0].toLowerCase() + name.slice(1), directory];
  }));
}
