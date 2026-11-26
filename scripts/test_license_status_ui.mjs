import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { createReactUiFixture } from "./lib/react-ui-fixture.mjs";

const { web, output, require, bundle, source, launch } = createReactUiFixture("license-status-ui");
await require("esbuild").build({ stdin: { loader: "tsx", resolveDir: web, contents: `
  import React from "react";
  import {createRoot} from "react-dom/client";
  import {QueryClient,QueryClientProvider} from "@tanstack/react-query";
  import {ExportDocManagerApiClient} from ${source("api/index.ts")};
  import {LicensePage} from ${source("features/system/LicensePage.tsx")};
  import ${source("styles/index.css")};
  import ${source("styles/workspaces.css")};
  import ${source("styles/responsive.css")};
  const queries=new QueryClient({defaultOptions:{queries:{retry:false,refetchOnWindowFocus:false}}});
  const client=new ExportDocManagerApiClient({baseUrl:"http://license.test",accessToken:"fixture"});
  createRoot(document.getElementById("root")).render(<QueryClientProvider client={queries}><LicensePage client={client}/></QueryClientProvider>);
` }, outfile: bundle, bundle: true, platform: "browser", format: "iife", jsx: "automatic", logLevel: "silent" });

const base = { isRegistered: false, isTrialExpired: false, trialDays: 7, daysRemaining: 7,
  expireDate: "2026-12-02", licenseState: "Trial", machineId: "a".repeat(64), message: "试用期剩余 7 天。",
  licenseStoragePath: "fixture", storagePolicy: "fixture" };
const lifetime = { ...base, isRegistered: true, licenseState: "Lifetime", daysRemaining: null, expireDate: null, message: "已注册 (终身授权)" };
const cases = [
  ["终身授权", lifetime, "已注册（终身授权）", "永久有效", "永不过期"],
  ["一年授权", { ...base, isRegistered: true, licenseState: "FixedTerm", daysRemaining: 366, expireDate: "2027-11-26" }, "已注册（定期授权）", "366 天", "2027-11-26"],
  ["三年授权", { ...base, isRegistered: true, licenseState: "FixedTerm", daysRemaining: 1097, expireDate: "2029-11-26" }, "已注册（定期授权）", "1097 天", "2029-11-26"],
  ["到期当天", { ...base, isRegistered: true, licenseState: "FixedTerm", daysRemaining: 1, expireDate: "2026-11-26" }, "已注册（定期授权）", "1 天", "2026-11-26"],
  ["授权过期", { ...base, isTrialExpired: true, licenseState: "Expired", daysRemaining: 0, expireDate: "2026-11-25" }, "授权已过期", "0 天", "2026-11-25"],
  ["未注册试用", base, "未注册（试用中）", "7 天", "2026-12-02"],
  ["试用最后一天", { ...base, daysRemaining: 1, expireDate: "2026-11-26" }, "未注册（试用中）", "1 天", "2026-11-26"],
  ["试用过期", { ...base, isTrialExpired: true, licenseState: "TrialExpired", daysRemaining: 0, expireDate: "2026-11-25" }, "未注册（试用已到期）", "0 天", "2026-11-25"],
  ["授权无效", { ...base, isTrialExpired: true, licenseState: "Invalid", daysRemaining: null, expireDate: null, message: "注册码无效或机器码已变更。" }, "授权无效", "—", "—"],
];
const results = [], context = await launch();
const detail = (page, label) => page.getByText(label, { exact: true }).locator("..").locator("strong");
async function scenario(name, initial, check, width = 1280) {
  const page = await context.newPage(), errors = [];
  let status = initial;
  page.setDefaultTimeout(10000);
  page.on("pageerror", error => errors.push(error.message));
  try {
    await page.setViewportSize({ width, height: 900 });
    await page.route("http://license.test/**", async route => {
      const request = route.request();
      if (request.url().endsWith("/register")) {
        assert.deepEqual(request.postDataJSON(), { licenseKey: "EDM2-fixture" });
        status = lifetime;
        return route.fulfill({ json: { success: true, message: "注册成功。", status } });
      }
      if (request.url().endsWith("/api/system/license")) {
        return status ? route.fulfill({ json: status }) : route.fulfill({ status: 503, json: { message: "授权服务暂不可用。" } });
      }
      return route.fulfill({ contentType: "text/html", body: '<!doctype html><html lang="zh-CN"><title>授权状态验收</title><div id="root"></div></html>' });
    });
    await page.goto("http://license.test/");
    await page.addStyleTag({ path: path.join(output, "app.css") });
    await page.addScriptTag({ path: bundle });
    await check(page);
    assert.deepEqual(errors, []);
    assert.doesNotMatch(await page.locator("body").innerText(), /922337203685477|262142|undefined|NaN/);
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false);
    results.push(name);
  } finally { await page.close(); }
}
try {
  for (const [name, status, state, remaining, expires] of cases) {
    await scenario(name, status, async page => {
      await detail(page, "状态").getByText(state, { exact: true }).waitFor();
      assert.equal(await detail(page, "剩余天数").innerText(), remaining);
      assert.equal(await detail(page, "到期日期").innerText(), expires);
      assert.equal(await page.getByLabel("注册码", { exact: true }).isDisabled(), status.isRegistered);
      assert.equal(await page.getByText("试用期限", { exact: true }).count(), status.licenseState.startsWith("Trial") ? 1 : 0);
      if (status.licenseState === "Invalid") await page.getByText(status.message, { exact: true }).waitFor();
      if (name === "终身授权") await page.screenshot({ path: path.join(output, "lifetime.png"), fullPage: true });
    });
  }
  await scenario("窄屏注册与刷新", base, async page => {
    await detail(page, "状态").getByText("未注册（试用中）", { exact: true }).waitFor();
    await page.getByLabel("注册码", { exact: true }).fill("EDM2-fixture");
    await page.getByRole("button", { name: "注册授权", exact: true }).click();
    await detail(page, "剩余天数").getByText("永久有效", { exact: true }).waitFor();
    await page.getByRole("button", { name: "刷新授权状态", exact: true }).click();
    await detail(page, "到期日期").getByText("永不过期", { exact: true }).waitFor();
    assert.equal(await page.getByLabel("注册码", { exact: true }).inputValue(), "");
    assert.equal(await page.getByRole("button", { name: "注册", exact: true }).isDisabled(), true);
    await page.screenshot({ path: path.join(output, "lifetime-narrow.png"), fullPage: true });
  }, 600);
  await scenario("读取失败", null, async page => {
    await detail(page, "状态").getByText("读取失败", { exact: true }).waitFor();
    assert.equal(await detail(page, "剩余天数").innerText(), "—");
    await page.getByText("授权服务暂不可用。", { exact: true }).waitFor();
  });
  fs.writeFileSync(path.join(output, "result.json"), JSON.stringify({ passed: results }, null, 2));
  console.log(JSON.stringify({ passed: results.length, output }));
} finally { await context.close(); }
