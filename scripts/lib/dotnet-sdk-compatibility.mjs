const sdkVersionPattern = /^(?<major>\d+)\.(?<minor>\d+)\.(?<featureBandAndPatch>\d+)(?:-(?<prerelease>[0-9A-Za-z.-]+))?$/u;

export function isDotnetSdkVersionCompatible(requiredVersion, actualVersion, rollForward = "patch", allowPrerelease = false) {
  const required = parseSdkVersion(requiredVersion);
  const actual = parseSdkVersion(actualVersion);
  if (!required || !actual) return false;
  if (!allowPrerelease && (required.prerelease || actual.prerelease)) return false;
  if (required.major === actual.major && required.minor === actual.minor && required.featureBandAndPatch === actual.featureBandAndPatch) {
    return true;
  }

  if (rollForward !== "patch" && rollForward !== "latestPatch") return false;

  const requiredFeatureBand = Math.floor(required.featureBandAndPatch / 100) * 100;
  const actualFeatureBand = Math.floor(actual.featureBandAndPatch / 100) * 100;
  return required.major === actual.major
    && required.minor === actual.minor
    && requiredFeatureBand === actualFeatureBand
    && actual.featureBandAndPatch > required.featureBandAndPatch;
}

function parseSdkVersion(value) {
  const match = String(value ?? "").trim().match(sdkVersionPattern);
  if (!match?.groups) return null;
  return {
    major: Number.parseInt(match.groups.major, 10),
    minor: Number.parseInt(match.groups.minor, 10),
    featureBandAndPatch: Number.parseInt(match.groups.featureBandAndPatch, 10),
    prerelease: match.groups.prerelease || "",
  };
}
