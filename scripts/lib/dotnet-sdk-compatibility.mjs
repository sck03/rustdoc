const sdkVersionPattern = /^(?<major>\d+)\.(?<minor>\d+)\.(?<featureBandAndPatch>\d+)(?:-(?<prerelease>[0-9A-Za-z.-]+))?$/u;

export function isDotnetSdkVersionCompatible(requiredVersion, actualVersion, rollForward = "patch", allowPrerelease = false) {
  const required = parseSdkVersion(requiredVersion);
  const actual = parseSdkVersion(actualVersion);
  if (!required || !actual) return false;
  if (!allowPrerelease && (required.prerelease || actual.prerelease)) return false;
  if (!rollForwardModes.has(rollForward)) return false;
  if (rollForward === "disable") return sameSdkVersion(required, actual);

  if (rollForward === "patch" || rollForward === "latestPatch") {
    return sameSdkMinor(required, actual)
      && actual.featureBand === required.featureBand
      && actual.patch >= required.patch;
  }

  if (rollForward === "feature" || rollForward === "latestFeature") {
    return sameSdkMinor(required, actual)
      && actual.featureBand >= required.featureBand
      && (actual.featureBand > required.featureBand || actual.patch >= required.patch);
  }

  if (rollForward === "minor" || rollForward === "latestMinor") {
    return actual.major === required.major
      && (actual.minor > required.minor || (actual.minor === required.minor && isFeatureBandAtLeast(required, actual)));
  }

  return actual.major > required.major
    || (actual.major === required.major
      && (actual.minor > required.minor || (actual.minor === required.minor && isFeatureBandAtLeast(required, actual))));
}

export function getStableDotnetSdkChannel(version) {
  const parsed = parseSdkVersion(version);
  if (!parsed || parsed.prerelease) return null;
  return `${parsed.major}.${parsed.minor}.x`;
}

function parseSdkVersion(value) {
  const match = String(value ?? "").trim().match(sdkVersionPattern);
  if (!match?.groups) return null;
  return {
    major: Number.parseInt(match.groups.major, 10),
    minor: Number.parseInt(match.groups.minor, 10),
    featureBand: Math.floor(Number.parseInt(match.groups.featureBandAndPatch, 10) / 100) * 100,
    patch: Number.parseInt(match.groups.featureBandAndPatch, 10) % 100,
    prerelease: match.groups.prerelease || "",
  };
}

const rollForwardModes = new Set([
  "disable",
  "patch",
  "feature",
  "minor",
  "major",
  "latestPatch",
  "latestFeature",
  "latestMinor",
  "latestMajor",
]);

function sameSdkVersion(required, actual) {
  return sameSdkMinor(required, actual)
    && actual.featureBand === required.featureBand
    && actual.patch === required.patch;
}

function sameSdkMinor(required, actual) {
  return required.major === actual.major && required.minor === actual.minor;
}

function isFeatureBandAtLeast(required, actual) {
  return actual.featureBand >= required.featureBand
    && (actual.featureBand > required.featureBand || actual.patch >= required.patch);
}
