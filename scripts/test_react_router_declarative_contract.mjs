import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const webRoot = path.join(repoRoot, "apps", "export-doc-web");
const packageJson = JSON.parse(fs.readFileSync(path.join(webRoot, "package.json"), "utf8"));
const packageLock = JSON.parse(fs.readFileSync(path.join(webRoot, "package-lock.json"), "utf8"));

const expectedVersion = "7.18.1";
assert.equal(
  packageJson.dependencies?.["react-router-dom"],
  expectedVersion,
  "react-router-dom must remain pinned to the reviewed v7 release",
);
for (const packageName of ["react-router", "react-router-dom"]) {
  const packageEntry = packageLock.packages?.[`node_modules/${packageName}`];
  assert.equal(
    packageEntry?.version,
    expectedVersion,
    `${packageName} must resolve to the same reviewed version in package-lock.json`,
  );
}

function collectSourceFiles(directory) {
  const files = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const entryPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...collectSourceFiles(entryPath));
    } else if (/\.(?:ts|tsx)$/.test(entry.name)) {
      files.push(entryPath);
    }
  }
  return files;
}

const source = collectSourceFiles(path.join(webRoot, "src"))
  .map((filePath) => fs.readFileSync(filePath, "utf8"))
  .join("\n");

assert.match(source, /\bHashRouter\b/, "the Web shell must keep the declarative HashRouter entry point");
assert.match(source, /\bRoutes\b/, "the Web shell must keep declarative Routes");
assert.match(source, /\bRoute\b/, "the Web shell must keep declarative Route definitions");

for (const forbiddenPattern of [
  /\b(?:createBrowserRouter|createHashRouter|createMemoryRouter|RouterProvider)\b/,
  /\b(?:HydratedRouter|ServerRouter|StaticRouter|createStaticRouter)\b/,
  /\b(?:singleFetch|serverAction|unstable_RSC)\b/i,
  /@react-router\/(?:dev|node|serve)\b/,
  /react-server-dom/i,
]) {
  assert.doesNotMatch(
    source,
    forbiddenPattern,
    `the Web client must not introduce React Router server/RSC APIs: ${forbiddenPattern}`,
  );
}

process.stdout.write("react-router declarative contracts passed\n");
