import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const policyScript = path.join(repositoryRoot, "scripts", "verify-dependency-policy.mjs");
mkdirSync(path.join(repositoryRoot, ".codex-runtime"), { recursive: true });
const fixtureRoot = mkdtempSync(path.join(repositoryRoot, ".codex-runtime", "dependency-policy-"));
const propsPath = path.join(fixtureRoot, "Directory.Packages.props");
const lockPath = path.join(fixtureRoot, "tests", "Sample.Tests", "packages.lock.json");
const projectPath = path.join(fixtureRoot, "tests", "Sample.Tests", "Sample.Tests.csproj");
mkdirSync(path.dirname(lockPath), { recursive: true });

try {
  writeValidFixture();
  assertPolicyPasses("the approved NPOI pin");

  writeFileSync(propsPath, readFileSync(propsPath, "utf8").replace('Version="2.7.6"', 'Version="2.8.0"'));
  assertPolicyFails("central NPOI pin", "approved commercial dependency policy");

  writeValidFixture();
  writeFileSync(lockPath, readFileSync(lockPath, "utf8").replace('"resolved": "2.7.6"', '"resolved": "2.8.0"'));
  assertPolicyFails("locked NPOI version", "resolves NPOI 2.8.0");

  writeValidFixture();
  writeFileSync(projectPath, '<Project><PackageReference Version="2.8.0" Include="NPOI" /></Project>\n');
  assertPolicyFails("project NPOI override", "project-level overrides");

  writeValidFixture();
  writeFileSync(lockPath, JSON.stringify({ version: 2, dependencies: { net10: {} } }, null, 2) + "\n");
  assertPolicyFails("missing NPOI lock entry", "No NuGet packages.lock.json contains NPOI");

  writeValidFixture();
  const evidencePath = path.join(fixtureRoot, "artifacts", "dependency-governance", "THIRD_PARTY_DEPENDENCIES.md");
  mkdirSync(path.dirname(evidencePath), { recursive: true });
  writeFileSync(evidencePath, "| nuget | NPOI | 2.8.0 | Apache-2.0 |\n");
  assertPolicyFails("generated dependency evidence", "generated dependency evidence");

  writeValidFixture();
  writeFileSync(evidencePath, "| nuget | NPOI | 2.7.6 | Apache-2.0 |\n");
  assertPolicyPassesGeneratedOnly();
} finally {
  rmSync(fixtureRoot, { recursive: true, force: true });
}

console.log("dependency version policy tests passed");

function writeValidFixture() {
  writeFileSync(
    propsPath,
    `<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>`
      + `<ItemGroup><PackageVersion Include="NPOI" Version="2.7.6" /></ItemGroup></Project>\n`,
  );
  writeFileSync(
    lockPath,
    JSON.stringify({
      version: 2,
      dependencies: {
        net10: {
          NPOI: {
            type: "Direct",
            requested: "[2.7.6, )",
            resolved: "2.7.6",
          },
        },
      },
    }, null, 2) + "\n",
  );
  writeFileSync(projectPath, '<Project><PackageReference Include="NPOI" /></Project>\n');
}

function runPolicy(extraArguments = []) {
  return spawnSync(
    process.execPath,
    [policyScript, "--repository-root", fixtureRoot, ...extraArguments],
    { cwd: repositoryRoot, encoding: "utf8", windowsHide: true },
  );
}

function assertPolicyPasses(label) {
  const result = runPolicy();
  assert.equal(result.status, 0, `${label} should pass:\n${result.stdout}\n${result.stderr}`);
}

function assertPolicyFails(label, expectedMessage) {
  const result = runPolicy();
  assert.notEqual(result.status, 0, `${label} should fail`);
  assert.match(`${result.stdout}\n${result.stderr}`, new RegExp(expectedMessage, "iu"));
}

function assertPolicyPassesGeneratedOnly() {
  const result = runPolicy(["--generated-only"]);
  assert.equal(result.status, 0, `generated-only evidence should pass:\n${result.stdout}\n${result.stderr}`);
}
