import { readFileSync, writeFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { promoteContainerRelease } from "../lib/container-release.mjs";

const selection = process.env.RELEASE_ARCHITECTURE;
if (!["x64", "arm64", "all"].includes(selection)) throw new Error("Invalid architecture.");
const architectures = selection === "all" ? ["x64", "arm64"] : [selection];
const records = architectures.map(architecture => JSON.parse(readFileSync(`artifacts/container-digests/${architecture}.json`, "utf8")));
const release = promoteContainerRelease({ requestedVersion: process.env.RELEASE_VERSION, image: process.env.IMAGE,
  selection, revision: process.env.GITHUB_SHA, publishLatest: process.env.PUBLISH_LATEST === "true", records, docker });
writeFileSync("artifacts/container-release.json", `${JSON.stringify(release, null, 2)}\n`);
console.log(`Published ${release.reference} (${architectures.join(", ")}).`);

function docker(args, missing = false) {
  const result = spawnSync("docker", args, { encoding: "utf8", timeout: 120_000, windowsHide: true });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    if (missing && /manifest unknown|not found/iu.test(result.stderr)) return null;
    throw new Error(`Docker ${args.slice(0, 3).join(" ")} failed: ${result.stderr}`);
  }
  return result.stdout;
}
