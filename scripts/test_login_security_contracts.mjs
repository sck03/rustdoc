import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const appSource = fs.readFileSync(path.join(repoRoot, "apps", "export-doc-web", "src", "App.tsx"), "utf8");
const loginPageSource = fs.readFileSync(
  path.join(repoRoot, "apps", "export-doc-web", "src", "features", "auth", "LoginPage.tsx"),
  "utf8",
);

assert.match(
  appSource,
  /const \[bootstrapToken, setBootstrapToken\] = useState\(""\);/,
  "bootstrap token must remain transient React state",
);
assert.match(
  appSource,
  /"X-ExportDocManager-Bootstrap-Token": bootstrapToken\.trim\(\)/,
  "login requests must send the bootstrap token through the dedicated header",
);
assert.match(
  appSource,
  /setBootstrapToken\(""\);/,
  "successful login must clear the bootstrap token from memory",
);

const sessionStateStart = appSource.indexOf("type SessionState = {");
const sessionStateEnd = appSource.indexOf("type LoadState", sessionStateStart);
assert(sessionStateStart >= 0 && sessionStateEnd > sessionStateStart, "SessionState declaration must remain discoverable");
assert.doesNotMatch(
  appSource.slice(sessionStateStart, sessionStateEnd),
  /bootstrapToken/i,
  "bootstrap token must never become part of the persisted browser session",
);
assert.doesNotMatch(
  appSource,
  /(?:writeStoredJson|localStorage|sessionStorage)[^\n;]*bootstrapToken/i,
  "bootstrap token must not be written to browser storage",
);
assert.match(
  appSource,
  /isDesktopRuntime=\{isDesktopRuntime\}/,
  "the login page must receive the actual desktop runtime state",
);
assert.match(
  loginPageSource,
  /isDesktopRuntime: boolean;/,
  "the login page must model desktop and browser deployment modes explicitly",
);
assert.match(
  loginPageSource,
  /\{!isDesktopRuntime \? \(\s*<details className="login-connection-settings">/s,
  "desktop SQLite login must hide server connection and bootstrap-token settings",
);

for (const contract of [
  "首次部署令牌",
  'type="password"',
  'autoComplete="off"',
  "登录成功后立即从页面内存清除",
  "仅网络版空库首次建立管理员时填写",
]) {
  assert(loginPageSource.includes(contract), `login page is missing bootstrap-token contract: ${contract}`);
}
assert(!loginPageSource.includes("localStorage"), "login page must not persist the bootstrap token in localStorage");
assert(!loginPageSource.includes("sessionStorage"), "login page must not persist the bootstrap token in sessionStorage");

process.stdout.write("login security contracts passed\n");
