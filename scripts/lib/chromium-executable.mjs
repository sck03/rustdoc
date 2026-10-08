import fs from "node:fs";
import path from "node:path";

export function locateChromeForTesting(repoRoot, preference = "headless-shell") {
  const playwrightCacheRoot = path.resolve(process.env.PLAYWRIGHT_BROWSERS_PATH || path.join(repoRoot, "artifacts", "playwright-browsers"));
  const playwrightChromiumRoots = fs.existsSync(playwrightCacheRoot)
    ? fs.readdirSync(playwrightCacheRoot, { withFileTypes: true })
      .filter((entry) => entry.isDirectory() && /^chromium[-_]/u.test(entry.name))
      .map((entry) => path.join(playwrightCacheRoot, entry.name))
    : [];
  const candidates = [];

  for (let rootIndex = 0; rootIndex < playwrightChromiumRoots.length; rootIndex += 1) {
    const root = playwrightChromiumRoots[rootIndex];
    for (const fileName of process.platform === "win32"
      ? ["chrome-headless-shell.exe", "chrome.exe"]
      : ["chrome-headless-shell", "chrome", "Chromium", "Google Chrome for Testing"]) {
      for (const executablePath of findFiles(root, fileName)) {
        candidates.push({ executablePath, isHeadlessShell: fileName.includes("headless"), rootIndex });
      }
    }
  }

  const ordered = candidates.sort((left, right) => {
    const leftRank = rankChromeCandidate(left, preference);
    const rightRank = rankChromeCandidate(right, preference);
    return leftRank - rightRank || left.rootIndex - right.rootIndex || left.executablePath.length - right.executablePath.length;
  });
  const selected = ordered.find((candidate) => preference !== "full-chrome" || !candidate.isHeadlessShell);
  if (selected) {
    return selected.executablePath;
  }

  throw new Error(
    `Test Chromium (${preference}) is missing from ${playwrightCacheRoot}. ` +
    "Set PLAYWRIGHT_BROWSERS_PATH to the repository artifacts/playwright-browsers directory, then run " +
    "node apps/export-doc-web/node_modules/playwright/cli.js install chromium.",
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

function findFiles(root, fileName) {
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
