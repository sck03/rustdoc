import { normalizeReleaseVersion } from "./release-version.mjs";

const targets = [
  { os: "windows", architecture: "x64", runner: "windows-latest", target: "x86_64-pc-windows-msvc", bundles: "nsis" },
  { os: "windows", architecture: "arm64", runner: "windows-11-arm", target: "aarch64-pc-windows-msvc", bundles: "nsis" },
  { os: "linux", architecture: "x64", runner: "ubuntu-24.04", target: "x86_64-unknown-linux-gnu", bundles: "deb,appimage" },
  { os: "linux", architecture: "arm64", runner: "ubuntu-24.04-arm", target: "aarch64-unknown-linux-gnu", bundles: "deb,appimage" },
  { os: "macos", architecture: "arm64", runner: "macos-15", target: "aarch64-apple-darwin", bundles: "app,dmg" },
];

export function createReleasePlan({ product, os, architecture, version, repository }) {
  if (!["desktop", "web", "container"].includes(product)) throw new Error("Unknown release product.");
  if (!["windows", "linux", "macos", "all"].includes(os)) throw new Error("Unknown release OS.");
  if (!["x64", "arm64", "all"].includes(architecture)) throw new Error("Unknown release architecture.");
  if (product === "container" && os !== "linux") throw new Error("Containers require Linux.");
  if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/u.test(repository)) throw new Error("Invalid GitHub repository.");
  const include = targets.filter(target => (os === "all" || target.os === os)
    && (architecture === "all" || target.architecture === architecture)
    // The governed PostgreSQL Windows client is x64 only.
    && !(product === "web" && target.os === "windows" && target.architecture === "arm64"))
    .map(target => ({ ...target, artifact: `${target.os}-${target.architecture}`,
      ...(product === "container" ? { platform: `linux/${target.architecture === "x64" ? "amd64" : "arm64"}` } : {}) }));
  if (!include.length) throw new Error("该产品没有支持的 OS/架构组合；macOS 仅 ARM64，Windows 网页服务仅 x64。");
  return { version: normalizeReleaseVersion(version), matrix: { include },
    image: `ghcr.io/${repository.split("/")[0].toLowerCase()}/exportdoc-rust-native` };
}
