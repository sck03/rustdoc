import assert from "node:assert/strict";
import { isDotnetSdkVersionCompatible } from "./lib/dotnet-sdk-compatibility.mjs";

assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.302", "latestPatch"), true);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.303", "latestPatch"), true);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.301", "latestPatch"), false);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.401", "latestPatch"), false);
assert.equal(isDotnetSdkVersionCompatible("10.0.302", "10.0.303", "patch"), true);
assert.equal(isDotnetSdkVersionCompatible("10.0.302-preview.1", "10.0.303", "latestPatch"), false);
assert.equal(isDotnetSdkVersionCompatible("not-a-version", "10.0.302", "latestPatch"), false);

console.log("dotnet SDK compatibility tests passed");
