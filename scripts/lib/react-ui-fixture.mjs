import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { locateChromeForTesting } from "./chromium-executable.mjs";

export function createReactUiFixture(name) {
  const root = path.resolve(import.meta.dirname, "../.."), web = path.join(root, "apps/export-doc-web");
  const output = path.join(root, "artifacts", name, String(Date.now())), temp = path.join(output, "Temp");
  fs.mkdirSync(temp, { recursive: true });
  Object.assign(process.env, { TEMP: temp, TMP: temp });
  const require = createRequire(path.join(web, "package.json"));
  return { root, web, output, require, bundle: path.join(output, "app.js"),
    source: name => JSON.stringify(path.join(web, "src", name).replaceAll("\\", "/")),
    launch: (options = {}) => require("playwright").chromium.launchPersistentContext(path.join(output, "Chrome"),
      { ...options, executablePath: locateChromeForTesting(root), headless: true }) };
}
