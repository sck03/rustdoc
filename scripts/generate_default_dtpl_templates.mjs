#!/usr/bin/env node
// Generates the six built-in .dtpl templates from the canonical V3 shapes.
// The generator mirrors the Rust container contract for release assets.
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import crypto from "node:crypto";
import { defaultReportDesigns } from "./lib/default-report-designs.mjs";

const root = path.resolve(process.argv[2] ?? ".");
const MAGIC = Buffer.from("EXPORTDOCDT", "ascii");
const VERSION = 1;
const DOCUMENT = "document.v3.json";

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit += 1) crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1));
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function zipDeflate(name, raw) {
  const compressed = zlib.deflateRawSync(raw, { level: 9 });
  const nameBytes = Buffer.from(name, "utf8");
  const local = Buffer.alloc(30);
  local.writeUInt32LE(0x04034b50, 0);
  local.writeUInt16LE(20, 4);
  local.writeUInt16LE(8, 8);
  local.writeUInt32LE(crc32(raw), 14);
  local.writeUInt32LE(compressed.length, 18);
  local.writeUInt32LE(raw.length, 22);
  local.writeUInt16LE(nameBytes.length, 26);
  const central = Buffer.alloc(46);
  central.writeUInt32LE(0x02014b50, 0);
  central.writeUInt16LE(20, 4);
  central.writeUInt16LE(20, 6);
  central.writeUInt16LE(8, 10);
  central.writeUInt32LE(crc32(raw), 16);
  central.writeUInt32LE(compressed.length, 20);
  central.writeUInt32LE(raw.length, 24);
  central.writeUInt16LE(nameBytes.length, 28);
  central.writeUInt32LE(0o600 << 16, 38);
  const centralOffset = local.length + nameBytes.length + compressed.length;
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(1, 8);
  end.writeUInt16LE(1, 10);
  end.writeUInt32LE(central.length + nameBytes.length, 12);
  end.writeUInt32LE(centralOffset, 16);
  return Buffer.concat([local, nameBytes, compressed, central, nameBytes, end]);
}

function encode(design) {
  const payload = zipDeflate(DOCUMENT, Buffer.from(JSON.stringify(design), "utf8"));
  const header = Buffer.alloc(MAGIC.length + 2 + 4 + 32);
  MAGIC.copy(header, 0);
  header.writeUInt16LE(VERSION, MAGIC.length);
  header.writeUInt32LE(payload.length, MAGIC.length + 2);
  crypto.createHash("sha256").update(payload).digest().copy(header, MAGIC.length + 6);
  return Buffer.concat([header, payload]);
}

const templates = defaultReportDesigns();
for (const [relative, design] of templates) {
  const target = path.join(root, relative);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, encode(design));
  console.log(`${relative} ${fs.statSync(target).size} bytes`);
}
