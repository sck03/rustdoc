import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { createHash } from "node:crypto";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const require = createRequire(path.join(root, "apps/export-doc-web/package.json"));
const { decompress } = require("wawoff2");
const fontRoot = path.join(root, "Resources/Fonts/OpenSource");
const manifest = JSON.parse(await readFile(path.join(fontRoot, "font-manifest.json"), "utf8"));
function tables(bytes) {
  assert.equal(bytes.toString("ascii", 0, 4), "OTTO");
  const result = new Map();
  for (let i = 0; i < bytes.readUInt16BE(4); i++) {
    const record = 12 + i * 16;
    const tag = bytes.toString("ascii", record, record + 4);
    const offset = bytes.readUInt32BE(record + 8), length = bytes.readUInt32BE(record + 12);
    assert(offset + length <= bytes.length);
    const value = Buffer.from(bytes.subarray(offset, offset + length));
    if (tag === "DSIG") {
      // WOFF2 omits the original container signature. Pinned Noto fonts have
      // an empty signature table; binary provenance remains SHA-256 verified.
      assert.equal(value.toString("hex"), "0000000100000000");
      continue;
    }
    if (tag === "head") {
      value.fill(0, 8, 12); // container checksum changes with table offsets
      value.writeUInt16BE(value.readUInt16BE(16) & ~0x0800, 16); // WOFF2 lossless-compression flag
    }
    result.set(tag, value);
  }
  return result;
}
let rawBytes = 0, webBytes = 0;
for (const font of manifest.fonts) {
  assert(font.web, `${font.fileName}: declared web representation`);
  const original = await readFile(path.join(fontRoot, font.fileName));
  const compressed = await readFile(path.join(fontRoot, font.web.fileName));
  assert.equal(createHash("sha256").update(compressed).digest("hex").toUpperCase(), font.web.sha256);
  const before = tables(original), after = tables(Buffer.from(await decompress(compressed)));
  assert.deepEqual([...after.keys()].sort(), [...before.keys()].sort());
  for (const [tag, bytes] of before) assert.deepEqual(after.get(tag), bytes, `${font.fileName}: ${tag} preserves all glyphs, shaping, metrics and metadata`);
  rawBytes += original.length; webBytes += compressed.length;
}
assert(webBytes < rawBytes);
console.log(JSON.stringify({ fonts: manifest.fonts.length, rawBytes, webBytes, savedBytes: rawBytes - webBytes, reductionPercent: (100 * (1 - webBytes / rawBytes)).toFixed(2) }));
