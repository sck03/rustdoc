import assert from "node:assert/strict";
import { createRequire } from "node:module";
import path from "node:path";

const web = path.resolve(import.meta.dirname, "../apps/export-doc-web");
const require = createRequire(path.join(web, "package.json"));
const result = await require("esbuild").build({
  stdin: { resolveDir: web, loader: "ts", contents: `
    export { isConcurrencyConflict, readApiError } from "./src/ui/formUtils.ts";
    export { ApiError } from "./src/api/index.ts";` },
  bundle: true, format: "esm", platform: "node", write: false, logLevel: "silent",
});
const { isConcurrencyConflict, readApiError, ApiError } = await import(
  `data:text/javascript;base64,${Buffer.from(result.outputFiles[0].text).toString("base64")}`,
);
for (const message of [
  "记录已被修改或缺少版本号。请重新读取后核对，当前草稿已保留。",
  "系统设置已被修改，请重新加载后核对。当前草稿已保留。",
  "A translated message without version-related keywords",
]) {
  const error = new ApiError(409, "Conflict", JSON.stringify({ code: "ConcurrencyConflict", message }));
  assert.equal(isConcurrencyConflict(error), true);
  assert.equal(readApiError(error), message);
}
for (const [status, body] of [
  [409, { message: "同一发票号记录已存在。" }],
  [409, { message: "其他用户正在办理，请刷新后重试。" }],
  [409, { code: "BusinessConflict", message: "加载最新资料" }],
  [400, { code: "ConcurrencyConflict", message: "请求无效" }],
]) assert.equal(isConcurrencyConflict(new ApiError(status, "Error", JSON.stringify(body))), false);
for (const text of ["invalid JSON", "null", "[]", "42"]) {
  assert.equal(isConcurrencyConflict(new ApiError(409, "Conflict", text)), false);
}
for (const response of ["plain response", "<html>gateway credentials</html>", "null", "[]", '{"message":42}']) {
  const message = readApiError(new ApiError(503, "Unavailable", response));
  assert.match(message, /HTTP 503/);
  assert.match(message, /先查询处理结果/);
  assert(!message.includes(response), "unrecognized upstream content must not be rendered");
}
const unavailable = readApiError(new ApiError(503, "Unavailable", JSON.stringify({
  message: "文件库离线，无法读取附件。", requestId: "request-fixture", outcome: "Unknown",
  issues: [{ suggestion: "重新连接已登记介质。" }, { suggestion: "重新连接已登记介质。" }],
})));
assert.match(unavailable, /文件库离线/);
assert.equal(unavailable.match(/重新连接已登记介质/g)?.length, 1);
assert.match(unavailable, /先查询处理回执/);
assert.match(unavailable, /追踪编号：request-fixture/);
assert.match(readApiError(new TypeError("fetch failed")), /检查网络和服务状态/);
console.log("frontend structured concurrency feedback tests passed");
