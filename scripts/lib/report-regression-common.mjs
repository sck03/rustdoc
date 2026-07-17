import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";

export function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

export function toImportSpecifier(workspaceRoot, filePath) {
  return path.relative(workspaceRoot, filePath).replaceAll(path.sep, "/");
}

export function locateChromeForTesting(repoRoot, preference = "headless-shell") {
  const manifestRoots = [
    path.join(repoRoot, "Browsers", "ChromeForTesting"),
    path.join(repoRoot, "ExportDocManager", "Browsers", "ChromeForTesting"),
    path.join(repoRoot, "src", "ExportDocManager.Api", "bin"),
  ];

  const candidates = [];
  const seenExecutablePaths = new Set();

  function addCandidate(manifestPath, executablePath, isHeadlessShell) {
    if (!executablePath || !fs.existsSync(executablePath)) {
      return;
    }

    const normalizedPath = path.resolve(executablePath).toLowerCase();
    if (seenExecutablePaths.has(normalizedPath)) {
      return;
    }

    seenExecutablePaths.add(normalizedPath);
    candidates.push({ manifestPath, executablePath, isHeadlessShell });
  }

  for (const root of manifestRoots) {
    if (!fs.existsSync(root)) {
      continue;
    }

    for (const manifestPath of findFiles(root, "chrome-for-testing.manifest.json")) {
      try {
        const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
        const isHeadlessShell =
          manifest.product === "ChromeHeadlessShell" ||
          String(manifest.executablePath || manifestPath).toLowerCase().includes("headless");
        addCandidate(manifestPath, manifest.executablePath, isHeadlessShell);

        const expectedFileName = isHeadlessShell
          ? process.platform === "win32"
            ? "chrome-headless-shell.exe"
            : "chrome-headless-shell"
          : process.platform === "win32"
            ? "chrome.exe"
            : "chrome";
        for (const executablePath of findFiles(path.dirname(manifestPath), expectedFileName)) {
          addCandidate(manifestPath, executablePath, isHeadlessShell);
        }
      } catch {
        // Try the next manifest.
      }
    }

    for (const fileName of process.platform === "win32"
      ? ["chrome-headless-shell.exe", "chrome.exe"]
      : ["chrome-headless-shell", "chrome"]) {
      for (const executablePath of findFiles(root, fileName)) {
        addCandidate(executablePath, executablePath, fileName.includes("headless"));
      }
    }
  }

  const ordered = candidates.sort((left, right) => {
    const leftRank = rankChromeCandidate(left, preference);
    const rightRank = rankChromeCandidate(right, preference);
    return leftRank - rightRank || left.manifestPath.length - right.manifestPath.length;
  });
  const selected = ordered.find((candidate) => preference !== "full-chrome" || !candidate.isHeadlessShell);
  if (selected) {
    return selected.executablePath;
  }

  throw new Error(
    preference === "full-chrome"
      ? "Full Chrome for Testing was not found. PDF viewer pixel regression needs chrome.exe under program-root Browsers."
      : "Chrome for Testing was not found. Run scripts/provision-chrome-for-testing.ps1 first.",
  );
}

function rankChromeCandidate(candidate, preference) {
  if (preference === "headless-shell") {
    return candidate.isHeadlessShell ? 0 : 1;
  }

  if (preference === "full-chrome") {
    return candidate.isHeadlessShell ? 1 : 0;
  }

  return 0;
}

export function findFiles(root, fileName) {
  const result = [];
  const stack = [root];
  while (stack.length > 0) {
    const current = stack.pop();
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      const fullPath = path.join(current, entry.name);
      if (entry.isDirectory()) {
        stack.push(fullPath);
      } else if (entry.isFile() && entry.name === fileName) {
        result.push(fullPath);
      }
    }
  }

  return result;
}

export function assertTemplateSourcePageOrientation(testCase, htmlPath) {
  if (!testCase.expectedTemplatePageOrientation) {
    return;
  }

  const html = fs.readFileSync(htmlPath, "utf8");
  const pattern = new RegExp(
    `@page\\s*\\{[^}]*size\\s*:\\s*A4\\s+${escapeRegExp(testCase.expectedTemplatePageOrientation)}\\b`,
    "is",
  );
  assert(
    pattern.test(html),
    `${testCase.slug}: expected template @page size A4 ${testCase.expectedTemplatePageOrientation}.`,
  );
}

export function escapeRegExp(value) {
  return String(value).replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

export function parseColor(value) {
  const match = /^#?([0-9a-f]{6})$/i.exec(value);
  assert(match, `Invalid color sample: ${value}`);
  const hex = match[1];
  return {
    r: Number.parseInt(hex.slice(0, 2), 16),
    g: Number.parseInt(hex.slice(2, 4), 16),
    b: Number.parseInt(hex.slice(4, 6), 16),
  };
}

export function colorDistance(left, right) {
  return Math.abs(left.r - right.r) + Math.abs(left.g - right.g) + Math.abs(left.b - right.b);
}

export function roundRatio(value) {
  return Math.round(value * 1000000) / 1000000;
}

export function analyzeReportScreenshot(filePath, expectedColorSamples = [], options = {}) {
  const image = parsePng(fs.readFileSync(filePath));
  const pixels = image.pixels;
  const totalPixels = image.width * image.height;
  const colorBuckets = new Set();
  const colorDistanceTolerance = options.colorDistanceTolerance ?? 24;
  const whiteThreshold = options.whiteThreshold ?? 248;
  const darkLumaThreshold = options.darkLumaThreshold ?? 145;
  const colorSamples = expectedColorSamples.map((sample) => ({
    ...sample,
    rgb: parseColor(sample.color),
    pixels: 0,
  }));

  let nonWhitePixels = 0;
  let darkPixels = 0;
  let minX = image.width;
  let minY = image.height;
  let maxX = -1;
  let maxY = -1;

  for (let y = 0; y < image.height; y += 1) {
    for (let x = 0; x < image.width; x += 1) {
      const offset = (y * image.width + x) * 4;
      const r = pixels[offset];
      const g = pixels[offset + 1];
      const b = pixels[offset + 2];
      const a = pixels[offset + 3];
      if (a === 0) {
        continue;
      }

      const isWhite = r >= whiteThreshold && g >= whiteThreshold && b >= whiteThreshold;
      if (!isWhite) {
        nonWhitePixels += 1;
        minX = Math.min(minX, x);
        minY = Math.min(minY, y);
        maxX = Math.max(maxX, x);
        maxY = Math.max(maxY, y);
        colorBuckets.add(`${r >> 4}:${g >> 4}:${b >> 4}`);
      }

      const luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
      if (luma < darkLumaThreshold) {
        darkPixels += 1;
      }

      for (const sample of colorSamples) {
        if (colorDistance({ r, g, b }, sample.rgb) <= colorDistanceTolerance) {
          sample.pixels += 1;
        }
      }
    }
  }

  const metrics = {
    width: image.width,
    height: image.height,
    totalPixels,
    nonWhitePixels,
    nonWhiteRatio: roundRatio(nonWhitePixels / totalPixels),
    darkPixels,
    darkRatio: roundRatio(darkPixels / totalPixels),
    colorBucketCount: colorBuckets.size,
    contentBounds:
      maxX >= 0
        ? {
            left: minX,
            top: minY,
            right: maxX,
            bottom: maxY,
            width: maxX - minX + 1,
            height: maxY - minY + 1,
          }
        : null,
    colorSamples: colorSamples.map(({ color, minPixels, pixels: samplePixels }) => ({
      color,
      minPixels,
      pixels: samplePixels,
    })),
  };

  if (options.fingerprintGrid) {
    metrics.layoutFingerprint = buildNonWhiteLayoutFingerprint(image, options.fingerprintGrid, whiteThreshold);
  }

  return metrics;
}

export function buildNonWhiteLayoutFingerprint(image, grid, whiteThreshold = 248) {
  const bits = [];
  let occupiedCells = 0;

  for (let row = 0; row < grid.rows; row += 1) {
    const yStart = Math.floor((row * image.height) / grid.rows);
    const yEnd = Math.max(yStart, Math.floor(((row + 1) * image.height) / grid.rows) - 1);

    for (let column = 0; column < grid.columns; column += 1) {
      const xStart = Math.floor((column * image.width) / grid.columns);
      const xEnd = Math.max(xStart, Math.floor(((column + 1) * image.width) / grid.columns) - 1);
      let cellPixels = 0;
      let nonWhitePixels = 0;

      for (let y = yStart; y <= yEnd; y += 1) {
        for (let x = xStart; x <= xEnd; x += 1) {
          const offset = (y * image.width + x) * 4;
          if (image.pixels[offset + 3] === 0) {
            continue;
          }

          cellPixels += 1;
          const r = image.pixels[offset];
          const g = image.pixels[offset + 1];
          const b = image.pixels[offset + 2];
          if (!(r >= whiteThreshold && g >= whiteThreshold && b >= whiteThreshold)) {
            nonWhitePixels += 1;
          }
        }
      }

      const minNonWhitePixels = Math.max(4, Math.ceil(cellPixels * grid.minNonWhiteRatio));
      const isOccupied = nonWhitePixels >= minNonWhitePixels;
      bits.push(isOccupied ? 1 : 0);
      if (isOccupied) {
        occupiedCells += 1;
      }
    }
  }

  return {
    columns: grid.columns,
    rows: grid.rows,
    minNonWhiteRatio: grid.minNonWhiteRatio,
    occupiedCells,
    hex: bitsToHex(bits),
  };
}

export function parsePng(buffer) {
  const signature = buffer.subarray(0, 8);
  assert(signature.equals(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])), "Invalid PNG signature.");

  let offset = 8;
  let width = 0;
  let height = 0;
  let bitDepth = 0;
  let colorType = 0;
  const idatChunks = [];

  while (offset < buffer.length) {
    const length = buffer.readUInt32BE(offset);
    const type = buffer.toString("ascii", offset + 4, offset + 8);
    const dataStart = offset + 8;
    const dataEnd = dataStart + length;
    const data = buffer.subarray(dataStart, dataEnd);
    offset = dataEnd + 4;

    if (type === "IHDR") {
      width = data.readUInt32BE(0);
      height = data.readUInt32BE(4);
      bitDepth = data[8];
      colorType = data[9];
    } else if (type === "IDAT") {
      idatChunks.push(data);
    } else if (type === "IEND") {
      break;
    }
  }

  assert(width > 0 && height > 0, "PNG is missing IHDR dimensions.");
  assert(bitDepth === 8, `Unsupported PNG bit depth: ${bitDepth}`);
  assert(colorType === 2 || colorType === 6, `Unsupported PNG color type: ${colorType}`);

  const sourceBytesPerPixel = colorType === 6 ? 4 : 3;
  const stride = width * sourceBytesPerPixel;
  const raw = zlib.inflateSync(Buffer.concat(idatChunks));
  const output = Buffer.alloc(width * height * 4);
  let sourceOffset = 0;
  let outputOffset = 0;
  let previousRow = Buffer.alloc(stride);

  for (let row = 0; row < height; row += 1) {
    const filterType = raw[sourceOffset];
    sourceOffset += 1;
    const encodedRow = raw.subarray(sourceOffset, sourceOffset + stride);
    sourceOffset += stride;
    const currentRow = Buffer.alloc(stride);

    for (let index = 0; index < stride; index += 1) {
      const left = index >= sourceBytesPerPixel ? currentRow[index - sourceBytesPerPixel] : 0;
      const up = previousRow[index] ?? 0;
      const upLeft = index >= sourceBytesPerPixel ? previousRow[index - sourceBytesPerPixel] : 0;
      const value = encodedRow[index];
      currentRow[index] = unfilterByte(filterType, value, left, up, upLeft);
    }

    for (let x = 0; x < width; x += 1) {
      const sourcePixel = x * sourceBytesPerPixel;
      output[outputOffset] = currentRow[sourcePixel];
      output[outputOffset + 1] = currentRow[sourcePixel + 1];
      output[outputOffset + 2] = currentRow[sourcePixel + 2];
      output[outputOffset + 3] = colorType === 6 ? currentRow[sourcePixel + 3] : 255;
      outputOffset += 4;
    }

    previousRow = currentRow;
  }

  return { width, height, pixels: output };
}

function unfilterByte(filterType, value, left, up, upLeft) {
  switch (filterType) {
    case 0:
      return value;
    case 1:
      return (value + left) & 0xff;
    case 2:
      return (value + up) & 0xff;
    case 3:
      return (value + Math.floor((left + up) / 2)) & 0xff;
    case 4:
      return (value + paeth(left, up, upLeft)) & 0xff;
    default:
      throw new Error(`Unsupported PNG filter type: ${filterType}`);
  }
}

function paeth(left, up, upLeft) {
  const estimate = left + up - upLeft;
  const leftDistance = Math.abs(estimate - left);
  const upDistance = Math.abs(estimate - up);
  const upLeftDistance = Math.abs(estimate - upLeft);
  if (leftDistance <= upDistance && leftDistance <= upLeftDistance) {
    return left;
  }

  return upDistance <= upLeftDistance ? up : upLeft;
}

export function bitsToHex(bits) {
  let output = "";
  for (let index = 0; index < bits.length; index += 4) {
    const value =
      ((bits[index] || 0) << 3) |
      ((bits[index + 1] || 0) << 2) |
      ((bits[index + 2] || 0) << 1) |
      (bits[index + 3] || 0);
    output += value.toString(16);
  }

  return output;
}

export function pickBounds(bounds) {
  if (!bounds) {
    return null;
  }

  return {
    left: bounds.left,
    top: bounds.top,
    width: bounds.width,
    height: bounds.height,
  };
}

export function assertBoundsWithinTolerance(label, actual, expected, tolerance) {
  assert(actual && expected, `${label}: expected both actual and baseline bounds.`);
  for (const property of ["left", "top", "width", "height"]) {
    const delta = Math.abs(Number(actual[property]) - Number(expected[property]));
    assert(delta <= tolerance, `${label}: ${property} changed by ${delta}px, allowed ${tolerance}px.`);
  }
}

export function hexHammingDistance(actual, expected) {
  assert(typeof actual === "string" && typeof expected === "string", "Fingerprint values must be hex strings.");
  assert(actual.length === expected.length, "Fingerprint lengths differ.");
  let distance = 0;
  for (let index = 0; index < actual.length; index += 1) {
    const left = Number.parseInt(actual[index], 16);
    const right = Number.parseInt(expected[index], 16);
    assert(Number.isFinite(left) && Number.isFinite(right), "Fingerprint contains a non-hex character.");
    distance += bitCount4(left ^ right);
  }

  return distance;
}

function bitCount4(value) {
  return ((value >> 3) & 1) + ((value >> 2) & 1) + ((value >> 1) & 1) + (value & 1);
}
