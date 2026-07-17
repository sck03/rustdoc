import { desktopAccessHeaders, ensureTrailingSlash } from "./web-runtime-smoke-common.mjs";

export async function loginToApi(options) {
  const response = await fetch(new URL("/api/auth/login", ensureTrailingSlash(options.apiBaseUrl)), {
    method: "POST",
    headers: { "Content-Type": "application/json", ...desktopAccessHeaders(options) },
    body: JSON.stringify({ username: options.username, password: options.password }),
  });
  if (!response.ok) {
    throw new Error(`API login failed with HTTP ${response.status}: ${await response.text()}`);
  }

  const login = await response.json();
  if (!login.accessToken || !login.expiresAt || !login.user) {
    throw new Error("API login response did not include accessToken, expiresAt and user.");
  }

  return login;
}

export async function logoutFromApi(options, accessToken, tokenType = "Bearer") {
  await fetch(new URL("/api/auth/logout", ensureTrailingSlash(options.apiBaseUrl)), {
    method: "POST",
    headers: { Authorization: `${tokenType || "Bearer"} ${accessToken}`, ...desktopAccessHeaders(options) },
  }).catch(() => undefined);
}
