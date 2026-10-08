# 报表对照与模型夹具

- `designer-margin-cases.json`、`raster-images.json` 仍由当前 V3 模型和界面测试读取。
- 三份 `customer_custom_*_legacy.html` 保留为原版版式对照；不是当前可导入模板，也不能作为 Rust PDF 已通过的证据。
- 旧 Chromium HTML 打印脚本及其 PDF/print 像素阈值已移除：它们把二进制 .dtpl 当成 HTML，无法验证当前报表。

默认模板源在 [default-report-designs.mjs](../../scripts/lib/default-report-designs.mjs)，运行资产在 Templates。当前验证使用 `test:report-designer-v3`、`test:payment-printing-ui`、`test:invoice-report-ui` 与 Rust 报表测试；真实 HTTP 回归先构建 React 和 Rust office_review 示例。实际 PDF 还须渲染检查，详见[验收方案](../../docs/Rust统一V3模板与原版PDF验收方案.md)。
