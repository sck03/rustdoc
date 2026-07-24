import { existsSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";

const root = path.resolve(process.argv[2] || "artifacts/cross-platform-report-metrics");
const expectedPlatforms = Number(process.env.EXPORTDOCMANAGER_EXPECTED_REPORT_PLATFORMS || "3");
if (!existsSync(root)) {
  throw new Error(`Cross-platform report metric root does not exist: ${root}`);
}

const metricFiles = walk(root).filter((file) => file.endsWith(".metrics.json"));
if (metricFiles.length === 0) {
  throw new Error(`No report PDF metric files were found under ${root}.`);
}
const layoutFiles = walk(root).filter((file) => file.endsWith(".layout.json"));
if (layoutFiles.length === 0) {
  throw new Error(`No report PDF layout files were found under ${root}.`);
}

const groups = new Map();
for (const file of metricFiles) {
  const metric = JSON.parse(readFileSync(file, "utf8"));
  const slug = String(metric.Slug || metric.slug || "").trim();
  if (!slug) throw new Error(`Metric file does not contain a report slug: ${file}`);
  const entries = groups.get(slug) ?? [];
  entries.push({ file, ...metric });
  groups.set(slug, entries);
}

const layoutGroups = new Map();
for (const file of layoutFiles) {
  const layout = JSON.parse(readFileSync(file, "utf8"));
  const slug = String(layout.slug || layout.Slug || "").trim();
  if (!slug) throw new Error(`Layout file does not contain a report slug: ${file}`);
  const entries = layoutGroups.get(slug) ?? [];
  entries.push({ file, ...layout });
  layoutGroups.set(slug, entries);
}

const failures = [];
const comparisons = [];
for (const [slug, entries] of [...groups.entries()].sort(([left], [right]) => left.localeCompare(right))) {
  const layoutEntries = layoutGroups.get(slug) ?? [];
  const pageCounts = unique(entries.map((entry) => Number(entry.PageCount)));
  const orientations = unique(entries.map((entry) => String(entry.FirstPageOrientation)));
  const widths = entries.map((entry) => Number(entry.FirstPageWidth));
  const heights = entries.map((entry) => Number(entry.FirstPageHeight));
  const streamCounts = entries.map((entry) => Number(entry.StreamCount));
  const byteCounts = entries.map((entry) => Number(entry.Bytes));
  const streamRatio = ratio(streamCounts);
  const byteRatio = ratio(byteCounts);

  if (entries.length !== expectedPlatforms) {
    failures.push(`${slug}: expected ${expectedPlatforms} platform results, received ${entries.length}`);
  }
  if (layoutEntries.length !== expectedPlatforms) {
    failures.push(`${slug}: expected ${expectedPlatforms} platform layout results, received ${layoutEntries.length}`);
  }
  if (pageCounts.length !== 1) failures.push(`${slug}: page counts differ (${pageCounts.join(", ")})`);
  if (orientations.length !== 1) failures.push(`${slug}: page orientations differ (${orientations.join(", ")})`);
  if (spread(widths) > 0.75 || spread(heights) > 0.75) {
    failures.push(`${slug}: first-page media boxes differ beyond tolerance`);
  }
  if (streamRatio > 1.25) failures.push(`${slug}: PDF stream counts differ by more than 25%`);
  if (byteRatio > 1.5) failures.push(`${slug}: PDF byte sizes differ by more than 50%`);

  const layoutPageCounts = unique(layoutEntries.map((entry) => Number(entry.pageCount)));
  const layoutLineCounts = unique(layoutEntries.map((entry) => Number(entry.lineCount)));
  const layoutHashes = unique(layoutEntries.map((entry) => String(entry.layoutHash || "")));
  const overlapCounts = layoutEntries.map((entry) => Number(entry.overlapCount));
  const maximumLineTopSpread = calculateMaximumLineTopSpread(layoutEntries);
  if (layoutPageCounts.length !== 1) failures.push(`${slug}: extracted layout page counts differ (${layoutPageCounts.join(", ")})`);
  if (layoutLineCounts.length !== 1) failures.push(`${slug}: extracted line counts differ (${layoutLineCounts.join(", ")})`);
  if (layoutHashes.length !== 1 || !layoutHashes[0]) failures.push(`${slug}: line wrapping signatures differ across platforms`);
  if (layoutHashes.length !== 1 || !layoutHashes[0]) {
    const mismatch = describeFirstLayoutMismatch(layoutEntries);
    if (mismatch) failures.push(`${slug}: ${mismatch}`);
  }
  if (overlapCounts.some((count) => count !== 0)) failures.push(`${slug}: at least one platform contains overlapping PDF text`);
  if (maximumLineTopSpread > 2.5) failures.push(`${slug}: equivalent text lines move vertically by more than 2.5pt across platforms`);
  if (layoutEntries.length > 0 && pageCounts.length === 1 && layoutPageCounts.some((count) => count !== pageCounts[0])) {
    failures.push(`${slug}: PDF structure page count and extracted layout page count disagree`);
  }

  comparisons.push({
    slug,
    platforms: entries.map((entry) => ({
      operatingSystem: entry.OperatingSystem,
      architecture: entry.Architecture,
      pageCount: entry.PageCount,
      orientation: entry.FirstPageOrientation,
      streamCount: entry.StreamCount,
      bytes: entry.Bytes,
      firstPageWidth: entry.FirstPageWidth,
      firstPageHeight: entry.FirstPageHeight,
      file: path.relative(root, entry.file).replaceAll(path.sep, "/"),
    })),
    pageCountConsistent: pageCounts.length === 1,
    orientationConsistent: orientations.length === 1,
    streamRatio,
    byteRatio,
    layout: {
      platforms: layoutEntries.map((entry) => ({
        operatingSystem: entry.operatingSystem,
        architecture: entry.architecture,
        pageCount: entry.pageCount,
        lineCount: entry.lineCount,
        layoutHash: entry.layoutHash,
        overlapCount: entry.overlapCount,
        file: path.relative(root, entry.file).replaceAll(path.sep, "/"),
      })),
      pageCountConsistent: layoutPageCounts.length === 1,
      lineCountConsistent: layoutLineCounts.length === 1,
      wrappingConsistent: layoutHashes.length === 1 && Boolean(layoutHashes[0]),
      maximumLineTopSpread,
    },
  });
}

for (const slug of layoutGroups.keys()) {
  if (!groups.has(slug)) failures.push(`${slug}: layout evidence exists without matching PDF metrics`);
}

const summary = {
  generatedAt: new Date().toISOString(),
  expectedPlatforms,
  metricFiles: metricFiles.length,
  layoutFiles: layoutFiles.length,
  passed: failures.length === 0,
  failures,
  comparisons,
};
writeFileSync(path.join(root, "cross-platform-report-summary.json"), `${JSON.stringify(summary, null, 2)}\n`, "utf8");

if (failures.length > 0) {
  if (process.env.GITHUB_ACTIONS === "true") {
    for (const failure of failures) {
      process.stderr.write(`::error title=Cross-platform report mismatch::${escapeWorkflowCommand(failure)}\n`);
    }
  }
  throw new Error(`Cross-platform report comparison failed:\n${failures.join("\n")}`);
}

process.stdout.write(`Cross-platform report metrics passed (${comparisons.length} report cases across ${expectedPlatforms} platforms).\n`);

function walk(directory) {
  const files = [];
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const absolutePath = path.join(directory, entry.name);
    if (entry.isDirectory()) files.push(...walk(absolutePath));
    else if (entry.isFile()) files.push(absolutePath);
  }
  return files;
}

function unique(values) {
  return [...new Set(values)];
}

function spread(values) {
  return Math.max(...values) - Math.min(...values);
}

function ratio(values) {
  const minimum = Math.min(...values);
  return minimum > 0 ? Math.max(...values) / minimum : Number.POSITIVE_INFINITY;
}

function calculateMaximumLineTopSpread(entries) {
  if (entries.length <= 1) return 0;
  const pageCounts = unique(entries.map((entry) => entry.pages?.length ?? 0));
  if (pageCounts.length !== 1) return Number.POSITIVE_INFINITY;

  let maximum = 0;
  for (let pageIndex = 0; pageIndex < pageCounts[0]; pageIndex += 1) {
    const topsByPlatform = entries.map((entry) => entry.pages?.[pageIndex]?.lineTops ?? []);
    const lineCounts = unique(topsByPlatform.map((tops) => tops.length));
    if (lineCounts.length !== 1) return Number.POSITIVE_INFINITY;
    for (let lineIndex = 0; lineIndex < lineCounts[0]; lineIndex += 1) {
      maximum = Math.max(maximum, spread(topsByPlatform.map((tops) => Number(tops[lineIndex]))));
    }
  }
  return maximum;
}

function describeFirstLayoutMismatch(entries) {
  if (entries.length <= 1) return "layout mismatch cannot be localized without multiple platform results";
  const maximumPages = Math.max(...entries.map((entry) => entry.pages?.length ?? 0));
  for (let pageIndex = 0; pageIndex < maximumPages; pageIndex += 1) {
    const maximumLines = Math.max(...entries.map((entry) => entry.pages?.[pageIndex]?.lineHashes?.length ?? 0));
    for (let lineIndex = 0; lineIndex < maximumLines; lineIndex += 1) {
      const hashes = entries.map((entry) => entry.pages?.[pageIndex]?.lineHashes?.[lineIndex] ?? "<missing>");
      if (unique(hashes).length === 1) continue;

      const platforms = entries.map((entry) => {
        const page = entry.pages?.[pageIndex];
        const operatingSystem = String(entry.operatingSystem || "unknown").split("-")[0];
        const hash = String(page?.lineHashes?.[lineIndex] ?? "missing").slice(0, 12);
        const top = page?.lineTops?.[lineIndex] ?? "missing";
        const left = page?.lineLefts?.[lineIndex] ?? "missing";
        const right = page?.lineRights?.[lineIndex] ?? "missing";
        return `${operatingSystem}[hash=${hash},top=${top},left=${left},right=${right}]`;
      });
      return `first differing line is page ${pageIndex + 1}, line ${lineIndex + 1}: ${platforms.join("; ")}`;
    }
  }
  return "layout hashes differ although all extracted line hashes match";
}

function escapeWorkflowCommand(value) {
  return String(value)
    .replaceAll("%", "%25")
    .replaceAll("\r", "%0D")
    .replaceAll("\n", "%0A");
}
