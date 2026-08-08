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
const sessionStorageSource = fs.readFileSync(
  path.join(repoRoot, "apps", "export-doc-web", "src", "app", "webSessionStorage.ts"),
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

const sessionStateStart = sessionStorageSource.indexOf("export type WebSessionState = {");
const sessionStateEnd = sessionStorageSource.indexOf("};", sessionStateStart);
assert(sessionStateStart >= 0 && sessionStateEnd > sessionStateStart, "persisted web session declaration must remain discoverable");
assert.doesNotMatch(
  sessionStorageSource.slice(sessionStateStart, sessionStateEnd),
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
  "管理员初始化口令",
  "value={bootstrapToken}",
  'type="password"',
  'autoComplete="off"',
  "仅首次建立管理员账号时填写",
]) {
  assert(loginPageSource.includes(contract), `login page is missing bootstrap-token contract: ${contract}`);
}
for (const deploymentHint of [
  "首次登录可使用 admin 空密码",
  "PostgreSQL 团队版首次启用需为 admin 设置强密码",
  "首次启用口令",
  "仅首次启用系统时填写",
]) {
  assert(!loginPageSource.includes(deploymentHint), `login page must not expose deployment guidance: ${deploymentHint}`);
}
assert(!loginPageSource.includes("localStorage"), "login page must not persist the bootstrap token in localStorage");
assert(!loginPageSource.includes("sessionStorage"), "login page must not persist the bootstrap token in sessionStorage");

process.stdout.write("login security contracts passed\n");
