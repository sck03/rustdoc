// Common subset supported by Cargo, npm, Tauri and OCI tags.
export function normalizeReleaseVersion(value) {
  const version = String(value ?? "").trim().replace(/^v/u, "");
  const match = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/u.exec(version);
  if (!match || version.length > 80 || match.slice(1, 4).some(part => Number(part) > 65535)
      || match[4]?.split(".").some(part => /^0\d+$/u.test(part))) {
    throw new Error("版本格式无效，请使用 0.1.2、v0.1.2 或 0.1.2-beta.1；不支持构建元数据，数字段须在 0–65535 内。");
  }
  return version;
}
