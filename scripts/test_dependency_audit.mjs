import assert from "node:assert/strict";
import { isApprovedLicenseExpression, classifyNpmAudit } from "./lib/dependency-audit.mjs";

const approved = new Set(["MIT", "Apache-2.0", "LicenseRef-SQLite-Public-Domain"]);
for (const value of ["MIT", "MIT OR Apache-2.0", "(MIT AND Apache-2.0)", "Apache-2.0 WITH LLVM-exception", "LicenseRef-SQLite-Public-Domain"]) assert.equal(isApprovedLicenseExpression(value, approved), true, value);
for (const value of ["", "OR", "MIT AND", "MIT OR Proprietary", "MIT WITH LLVM-exception", "(MIT", "MIT +", "NOASSERTION"]) assert.equal(isApprovedLicenseExpression(value, approved), false, value);
const audit = (severity, status = 0) => ({ status, stdout: JSON.stringify({ vulnerabilities: {}, metadata: { vulnerabilities: { info: 0, low: 0, moderate: 0, high: 0, critical: 0, ...severity } } }) });
assert.equal(classifyNpmAudit(audit({})).state, "passed");
assert.equal(classifyNpmAudit(audit({ moderate: 1 })).state, "warning");
assert.equal(classifyNpmAudit(audit({ high: 1 }, 1)).exitCode, 1);
for (const value of [audit({ high: 1 }), audit({}, 1), audit({ low: -1 }), { status: 0, stdout: "{}" }, { status: 0, stdout: "" }, { status: null, error: new Error("timeout") }, { status: 1, stdout: '{"error":{"code":"ENOTFOUND"}}' }]) assert.throws(() => classifyNpmAudit(value));
console.log("Dependency audit regression checks passed.");
