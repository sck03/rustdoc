import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const require = createRequire(import.meta.url);
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const workspace = path.join(repoRoot, ".codex-runtime", "authentication-failure-event-tests");
const entry = path.join(workspace, "entry.ts");
const bundle = path.join(workspace, "bundle.mjs");
fs.rmSync(workspace, { recursive: true, force: true });
fs.mkdirSync(workspace, { recursive: true });

const modelPath = path.join(repoRoot, "apps", "export-doc-web", "src", "api", "authenticationFailureEvents.ts").replaceAll("\\", "/");
const apiPath = path.join(repoRoot, "apps", "export-doc-web", "src", "api", "index.ts").replaceAll("\\", "/");
fs.writeFileSync(entry, `import * as model from ${JSON.stringify(modelPath)}; import { ApiError } from ${JSON.stringify(apiPath)}; globalThis.__model = { ...model, ApiError };`, "utf8");
const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
await esbuild.build({ entryPoints: [entry], outfile: bundle, bundle: true, format: "esm", platform: "node", logLevel: "silent" });
await import(pathToFileURL(bundle).href);

const { ApiError, notifyAuthenticationFailure, subscribeToAuthenticationFailure } = globalThis.__model;
const assert = (condition, message) => { if (!condition) throw new Error(message); };
let notifications = 0;
const unsubscribe = subscribeToAuthenticationFailure(() => { notifications += 1; });

assert(!notifyAuthenticationFailure(new ApiError(403, "Forbidden", "")), "403 must remain a permission error");
assert(!notifyAuthenticationFailure(new ApiError(409, "Conflict", "")), "409 must remain a business conflict");
assert(notifyAuthenticationFailure(new ApiError(401, "Unauthorized", "")), "401 should schedule logout");
assert(!notifyAuthenticationFailure(new ApiError(401, "Unauthorized", "")), "simultaneous 401 responses should be coalesced");
await new Promise((resolve) => setTimeout(resolve, 0));
assert(notifications === 1, "coalesced 401 responses should notify once");
unsubscribe();
assert(notifyAuthenticationFailure(new ApiError(401, "Unauthorized", "")), "401 should still be recognized without listeners");
await new Promise((resolve) => setTimeout(resolve, 0));
assert(notifications === 1, "unsubscribed listener must not run");
process.stdout.write("authentication failure event tests passed\n");
