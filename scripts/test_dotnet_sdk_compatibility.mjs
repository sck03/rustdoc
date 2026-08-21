import assert from "node:assert/strict";
import { isDotnetSdkVersionCompatible } from "./lib/dotnet-sdk-compatibility.mjs";

assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.302", "latestPatch"), true);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.303", "latestPatch"), true);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.301", "latestPatch"), false);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.401", "latestPatch"), false);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.303", "patch"), true);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.401", "feature"), true);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.401", "latestFeature"), true);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.301", "feature"), false);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.201", "feature"), false);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.1.100", "latestMinor"), true);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "11.0.100", "latestMinor"), false);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.302", "disable"), true);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.303", "disable"), false);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.303", "unknown"), false);
assert.equal(isDotnetSdkVersionCompatible("10.0.302-preview.1", "10.0.303", "latestPatch"), false);
assert.equal(isDotnetSdkVersionCompatible("not-a-version", "10.0.302", "latestPatch"), false);

console.log("dotnet SDK compatibility tests passed");
