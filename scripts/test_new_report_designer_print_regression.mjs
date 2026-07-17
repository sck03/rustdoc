import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { assert, locateChromeForTesting, parsePng, toImportSpecifier } from "./lib/report-regression-common.mjs";
import {
  CdpClient,
  closeChrome,
  delay,
  getPageWebSocketUrl,
  waitForDevToolsUrl,
} from "./lib/chromium-cdp.mjs";

const require = createRequire(import.meta.url);
const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, "..");
const workspaceRoot = path.join(repoRoot, ".codex-runtime", "new-report-designer-print-regression");
const entryPath = path.join(workspaceRoot, "entry.ts");
const bundlePath = path.join(workspaceRoot, "bundle.mjs");
const htmlPath = path.join(workspaceRoot, "new-report-designer-print.html");
const pdfPath = path.join(workspaceRoot, "new-report-designer-print.pdf");
const screenshotPath = path.join(workspaceRoot, "new-report-designer-print.png");
const pointsPerMm = 72 / 25.4;

async function buildBundle() {
  const esbuild = require(path.join(repoRoot, "apps", "export-doc-web", "node_modules", "esbuild"));
  const exporterPath = path.join(
    repoRoot,
    "apps",
    "export-doc-web",
    "src",
    "features",
    "report-designer",
    "reportDesignerHtmlExporter.ts",
  );

  fs.mkdirSync(workspaceRoot, { recursive: true });
  fs.writeFileSync(
    entryPath,
    `
import { exportReportDesignerSchemaToHtml } from "${toImportSpecifier(workspaceRoot, exporterPath)}";

export function buildHtml() {
  const schema = {
    version: 2,
    reportType: "ExportDocument",
    page: {
      size: "A4",
      orientation: "Portrait",
      marginTopMm: 10,
      marginRightMm: 10,
      marginBottomMm: 10,
      marginLeftMm: 10,
      fontFamily: "Arial, sans-serif",
      fontSizePt: 9,
    },
    sections: [
      {
        id: "section-header",
        type: "Header",
        print: {
          repeatOnEveryPage: true,
          keepTogether: true,
          pinToPageBottom: false,
        },
        blocks: [
          {
            id: "block-title",
            type: "Text",
            text: "NINGBO BRIDGE IMP & EXP CO.,LTD",
            style: { fontSizePt: 15, bold: true, align: "Center", marginBottomMm: 4 },
          },
          {
            id: "block-invoice-title",
            type: "Text",
            text: "COMMERCIAL INVOICE",
            style: { fontSizePt: 16, bold: true, align: "Center", marginBottomMm: 2 },
          },
        ],
      },
      {
        id: "section-body",
        type: "Body",
        print: {
          repeatOnEveryPage: false,
          keepTogether: false,
          pinToPageBottom: false,
        },
        blocks: [
          {
            id: "block-party-row",
            type: "Row",
            marginTopMm: 1,
            marginBottomMm: 1,
            columns: [
              {
                id: "party-left",
                contentKind: "Field",
                text: "",
                label: "TO:M/S",
                fieldPath: "Customer.CustomerNameEN",
                fallbackText: "",
                widthPercent: 62,
                style: { fontSizePt: 8, bold: true, align: "Left" },
                border: { color: "#333333", widthPx: 0, style: "None", top: false, right: false, bottom: false, left: false },
              },
              {
                id: "party-right",
                contentKind: "Field",
                text: "",
                label: "Invoice No.",
                fieldPath: "Invoice.InvoiceNo",
                fallbackText: "",
                widthPercent: 38,
                style: { fontSizePt: 8, bold: true, align: "Left" },
                border: { color: "#333333", widthPx: 1, style: "Dashed", top: false, right: false, bottom: true, left: false },
              },
            ],
          },
          {
            id: "block-route-row",
            type: "Row",
            marginTopMm: 0,
            marginBottomMm: 2,
            columns: [
              {
                id: "route-from",
                contentKind: "Field",
                text: "",
                label: "From",
                fieldPath: "Invoice.LoadingPort",
                fallbackText: "",
                widthPercent: 42,
                style: { fontSizePt: 8, align: "Left" },
                border: { color: "#333333", widthPx: 1, style: "Dashed", top: false, right: false, bottom: true, left: false },
              },
              {
                id: "route-to",
                contentKind: "Field",
                text: "",
                label: "To",
                fieldPath: "Invoice.DestinationPort",
                fallbackText: "",
                widthPercent: 58,
                style: { fontSizePt: 8, align: "Left" },
                border: { color: "#333333", widthPx: 1, style: "Dashed", top: false, right: false, bottom: true, left: false },
              },
            ],
          },
          {
            id: "block-detail",
            type: "DetailTable",
            title: "Commercial Invoice Details",
            detailWidthMm: 150,
            sourcePath: "Invoice.Items",
            repeatMode: "ScribanFor",
            print: {
              repeatHeaderOnPageBreak: true,
              keepRowsTogether: true,
            },
            sideBand: {
              title: "唛头 Marks",
              widthMm: 38,
              contentKind: "Text",
              text: "Vendor name:\\nOrder number:\\nDescription:\\nSKU NO:\\nColour:\\nSIZE:\\nCarton Number:\\nDimension(CM):\\nGross Weight:\\nBATCH Number:\\nCountry of Origin: China",
              fieldPath: "",
              style: { fontSizePt: 8, bold: true, align: "Left", marginTopMm: 8 },
            },
            columns: [
              {
                id: "col-desc",
                title: "Description",
                headerGroupTitle: "Quantities and Descriptions",
                headerGroupSpan: 3,
                contentKind: "Composite",
                fieldPath: "Invoice.Items.ProductNameEN",
                content: [
                  { id: "desc-name", kind: "Field", text: "", fieldPath: "Invoice.Items.ProductNameEN" },
                  { id: "desc-break-1", kind: "LineBreak", text: "", fieldPath: "" },
                  { id: "desc-model-label", kind: "Text", text: "MO ", fieldPath: "" },
                  { id: "desc-model", kind: "Field", text: "", fieldPath: "Invoice.Items.Sku" },
                  { id: "desc-qty-gap", kind: "Text", text: "    ", fieldPath: "" },
                  { id: "desc-qty", kind: "Field", text: "", fieldPath: "Invoice.Items.Quantity" },
                  { id: "desc-ctns", kind: "Text", text: "CTNS", fieldPath: "" },
                ],
                widthMm: 76,
                align: "Left",
              },
              {
                id: "col-cartons",
                title: "Cartons",
                contentKind: "Field",
                fieldPath: "Invoice.Items.Cartons",
                widthMm: 24,
                align: "Right",
                border: { color: "#333333", widthPx: 1, style: "None", top: false, right: false, bottom: false, left: false },
              },
              {
                id: "col-price",
                title: "Unit Price",
                contentKind: "Field",
                fieldPath: "Invoice.Items.UnitPrice",
                widthMm: 24,
                align: "Right",
                border: { color: "#333333", widthPx: 1, style: "Dashed", top: false, right: false, bottom: true, left: false },
              },
              {
                id: "col-amount",
                title: "总 值 Amount",
                headerGroupTitle: "总 值 Amount",
                headerGroupSpan: 1,
                contentKind: "Field",
                fieldPath: "Invoice.Items.TotalPrice",
                widthMm: 26,
                align: "Right",
              },
            ],
            summaryRow: {
              label: "TOTAL",
              labelColumnSpan: 3,
              cells: [
                { columnId: "col-amount", contentKind: "Field", text: "", fieldPath: "Invoice.TotalAmount" },
              ],
              style: { fontSizePt: 9, bold: true, align: "Right" },
            },
            headerStyle: { fontSizePt: 9, bold: true, align: "Center" },
            bodyStyle: { fontSizePt: 8, align: "Left" },
            border: { color: "#333333", widthPx: 1, style: "Solid", top: true, right: true, bottom: true, left: true },
          },
          { id: "block-break", type: "PageBreak" },
          {
            id: "block-after-break",
            type: "Text",
            text: "AFTER EXPLICIT PAGE BREAK",
            style: { fontSizePt: 12, bold: true, align: "Center", marginTopMm: 10 },
          },
        ],
      },
      {
        id: "section-footer",
        type: "Footer",
        print: {
          repeatOnEveryPage: true,
          keepTogether: true,
          pinToPageBottom: true,
        },
        blocks: [
          {
            id: "block-footer",
            type: "Text",
            text: "PRINT REGRESSION FOOTER",
            style: { fontSizePt: 8, align: "Center", marginTopMm: 3 },
          },
        ],
      },
    ],
  };

  return renderSampleData(exportReportDesignerSchemaToHtml(schema));
}

export function buildPaperVariantHtmls() {
  return [
    {
      name: "a5-landscape-ticket-grid",
      expectedPageWidthMm: 210,
      expectedPageHeightMm: 148,
      expectedPageCss: "@page { size: 210mm 148mm; margin: 8mm 8mm 8mm 8mm; }",
      html: renderSampleData(exportReportDesignerSchemaToHtml(buildPaperVariantSchema({
        title: "A5 LANDSCAPE PAYMENT STYLE VOUCHER",
        size: "A5",
        orientation: "Landscape",
        marginMm: 8,
      }))),
    },
    {
      name: "custom-landscape-compact-report",
      expectedPageWidthMm: 160,
      expectedPageHeightMm: 100,
      expectedPageCss: "@page { size: 160mm 100mm; margin: 6mm 6mm 6mm 6mm; }",
      html: renderSampleData(exportReportDesignerSchemaToHtml(buildPaperVariantSchema({
        title: "CUSTOM LANDSCAPE COMPACT REPORT",
        size: "Custom",
        orientation: "Landscape",
        widthMm: 100,
        heightMm: 160,
        marginMm: 6,
      }))),
    },
  ];
}

function buildPaperVariantSchema(options) {
  return {
    version: 2,
    reportType: "ExportDocument",
    page: {
      size: options.size,
      orientation: options.orientation,
      widthMm: options.widthMm,
      heightMm: options.heightMm,
      marginTopMm: options.marginMm,
      marginRightMm: options.marginMm,
      marginBottomMm: options.marginMm,
      marginLeftMm: options.marginMm,
      fontFamily: "Arial, sans-serif",
      fontSizePt: 9,
    },
    sections: [
      {
        id: "variant-header",
        type: "Header",
        print: {
          repeatOnEveryPage: true,
          keepTogether: true,
          pinToPageBottom: false,
          minHeightMm: 12,
        },
        blocks: [
          {
            id: "variant-title",
            type: "Text",
            text: options.title,
            style: { fontSizePt: 13, bold: true, align: "Center", marginBottomMm: 3 },
          },
        ],
      },
      {
        id: "variant-body",
        type: "Body",
        print: {
          repeatOnEveryPage: false,
          keepTogether: false,
          pinToPageBottom: false,
        },
        blocks: [
          {
            id: "variant-row",
            type: "Row",
            marginBottomMm: 3,
            columns: [
              {
                id: "variant-row-left",
                contentKind: "Field",
                text: "",
                label: "Invoice",
                fieldPath: "Invoice.InvoiceNo",
                fallbackText: "",
                widthPercent: 40,
                style: { fontSizePt: 9, bold: true, align: "Left" },
                border: { color: "#333333", widthPx: 1, style: "Solid", top: false, right: false, bottom: true, left: false },
              },
              {
                id: "variant-row-right",
                contentKind: "Field",
                text: "",
                label: "Customer",
                fieldPath: "Customer.CustomerNameEN",
                fallbackText: "",
                widthPercent: 60,
                style: { fontSizePt: 9, align: "Right" },
                border: { color: "#333333", widthPx: 1, style: "Dashed", top: false, right: false, bottom: true, left: false },
              },
            ],
          },
          {
            id: "variant-grid",
            type: "Grid",
            columns: [
              { id: "variant-grid-col-1", widthPercent: 24 },
              { id: "variant-grid-col-2", widthPercent: 38 },
              { id: "variant-grid-col-3", widthPercent: 38 },
            ],
            rows: [
              {
                id: "variant-grid-row-1",
                heightMm: 10,
                cells: [
                  { id: "variant-grid-cell-1", contentKind: "Text", text: "项目", fieldPath: "", colSpan: 1, rowSpan: 1, style: { fontSizePt: 9, bold: true, align: "Center" } },
                  { id: "variant-grid-cell-2", contentKind: "Text", text: "报表模型", fieldPath: "", colSpan: 1, rowSpan: 1, style: { fontSizePt: 9, bold: true, align: "Center" } },
                  { id: "variant-grid-cell-3", contentKind: "Text", text: "打印检查", fieldPath: "", colSpan: 1, rowSpan: 1, style: { fontSizePt: 9, bold: true, align: "Center" } },
                ],
              },
              {
                id: "variant-grid-row-2",
                heightMm: 12,
                cells: [
                  { id: "variant-grid-cell-4", contentKind: "Text", text: "纸张", fieldPath: "", colSpan: 1, rowSpan: 1, style: { fontSizePt: 9, align: "Center" } },
                  { id: "variant-grid-cell-5", contentKind: "Text", text: options.size + " / " + options.orientation, fieldPath: "", colSpan: 1, rowSpan: 1, style: { fontSizePt: 9, align: "Center" } },
                  { id: "variant-grid-cell-6", contentKind: "Text", text: "Chrome PDF MediaBox", fieldPath: "", colSpan: 1, rowSpan: 1, style: { fontSizePt: 9, align: "Center" } },
                ],
              },
              {
                id: "variant-grid-row-3",
                heightMm: 12,
                cells: [
                  { id: "variant-grid-cell-7", contentKind: "Text", text: "版区", fieldPath: "", colSpan: 1, rowSpan: 1, style: { fontSizePt: 9, align: "Center" } },
                  { id: "variant-grid-cell-8", contentKind: "Text", text: "Header / Body / Footer", fieldPath: "", colSpan: 1, rowSpan: 1, style: { fontSizePt: 9, align: "Center" } },
                  { id: "variant-grid-cell-9", contentKind: "Text", text: "重复页眉和受控表格", fieldPath: "", colSpan: 1, rowSpan: 1, style: { fontSizePt: 9, align: "Center" } },
                ],
              },
            ],
            defaultCellStyle: { fontSizePt: 9, align: "Center" },
            border: { color: "#333333", widthPx: 1, style: "Solid", top: true, right: true, bottom: true, left: true },
            marginTopMm: 2,
            marginBottomMm: 2,
          },
        ],
      },
      {
        id: "variant-footer",
        type: "Footer",
        print: {
          repeatOnEveryPage: true,
          keepTogether: true,
          pinToPageBottom: false,
        },
        blocks: [
          {
            id: "variant-footer-text",
            type: "Text",
            text: "PAPER SIZE REGRESSION",
            style: { fontSizePt: 8, align: "Center", marginTopMm: 2 },
          },
        ],
      },
    ],
  };
}

function renderSampleData(html) {
  const rows = Array.from({ length: 92 }, (_, index) => ({
    ProductNameEN: "Long product description " + String(index + 1).padStart(2, "0") + " with controlled wrapping for Chromium PDF regression",
    Sku: "SKU-" + String(index + 1).padStart(3, "0") + "-LONG-CONTINUOUS-CODE-" + "X".repeat(24),
    Quantity: String(10 + index),
    Cartons: String(20 + index),
    UnitPrice: "@" + (5.8 + index / 10).toFixed(2),
    TotalPrice: (128.5 + index * 3.25).toFixed(2),
  }));
  const invoiceValues = {
    InvoiceNo: "INV-PRINT-REGRESSION",
    LoadingPort: "NINGBO CHINA",
    DestinationPort: "LE HAVRE",
    ShippingMarks: "PRINT-REGRESSION-MARKS\\\\n" + "MARKS-LONG-CONTINUOUS-" + "Y".repeat(80),
    TotalAmount: "12345.67",
  };
  const customerValues = {
    CustomerNameEN: "EURO DISNEY ASSOCES S.A.S\\\\n1 rond-point d'Isigny /See Trafic\\\\n77700 CHESSY\\\\nFRANCE",
  };

  return html
    .replace(/\\{\\{\\s*for\\s+item\\s+in\\s+Invoice\\.Items\\s*\\}\\}([\\s\\S]*?)\\{\\{\\s*end\\s*\\}\\}/g, (_, rowTemplate) =>
      rows.map((row) =>
        rowTemplate.replace(/\\{\\{\\s*item\\.([A-Za-z_][A-Za-z0-9_]*)\\s*\\}\\}/g, (_match, key) => escapeHtml(row[key] ?? "")),
      ).join(""),
    )
    .replace(/\\{\\{\\s*Invoice\\.([A-Za-z_][A-Za-z0-9_]*)\\s*\\}\\}/g, (_match, key) => escapeHtml(invoiceValues[key] ?? ""))
    .replace(/\\{\\{\\s*Customer\\.([A-Za-z_][A-Za-z0-9_]*)\\s*\\}\\}/g, (_match, key) => escapeHtml(customerValues[key] ?? ""));
}

function escapeHtml(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}
`,
    "utf8",
  );

  await esbuild.build({
    entryPoints: [entryPath],
    outfile: bundlePath,
    bundle: true,
    format: "esm",
    platform: "node",
    target: "node20",
    logLevel: "silent",
  });
}

async function inspectWithChrome(chromePath, target = {}) {
  const targetHtmlPath = target.htmlPath ?? htmlPath;
  const targetPdfPath = target.pdfPath ?? pdfPath;
  const targetScreenshotPath = target.screenshotPath ?? screenshotPath;
  const profilePath = path.join(workspaceRoot, "ChromeProfile");
  fs.rmSync(profilePath, { recursive: true, force: true });
  fs.mkdirSync(profilePath, { recursive: true });

  const child = spawn(
    chromePath,
    [
      "--headless",
      "--disable-gpu",
      "--disable-extensions",
      "--disable-background-networking",
      "--no-first-run",
      "--hide-scrollbars",
      "--force-device-scale-factor=1",
      "--font-render-hinting=none",
      "--remote-debugging-port=0",
      `--user-data-dir=${profilePath}`,
      "--window-size=960,1200",
      "about:blank",
    ],
    {
      stdio: ["ignore", "pipe", "pipe"],
      windowsHide: true,
    },
  );

  let browserWebSocketUrl;
  try {
    browserWebSocketUrl = await waitForDevToolsUrl(child, "new-report-designer-print");
    const pageWebSocketUrl = await getPageWebSocketUrl(browserWebSocketUrl, "new-report-designer-print");
    const page = await CdpClient.connect(pageWebSocketUrl);
    try {
      await page.send("Page.enable");
      await page.send("Runtime.enable");
      await page.send("Emulation.setDeviceMetricsOverride", {
        width: 960,
        height: 1200,
        deviceScaleFactor: 1,
        mobile: false,
      });
      await page.send("Emulation.setEmulatedMedia", { media: "print" });
      const loadEvent = page.waitForEvent("Page.loadEventFired", () => true, 30000);
      await page.send("Page.navigate", { url: pathToFileURL(targetHtmlPath).href });
      await loadEvent;
      await page.send("Runtime.evaluate", {
        expression: "document.fonts && document.fonts.ready ? document.fonts.ready.then(() => true) : true",
        awaitPromise: true,
        returnByValue: true,
      });
      await page.send("Runtime.evaluate", {
        expression: "new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))",
        awaitPromise: true,
        returnByValue: true,
      });

      const domMetrics = await readDomPrintMetrics(page);
      const pdfResult = await page.send("Page.printToPDF", {
        printBackground: true,
        preferCSSPageSize: true,
        displayHeaderFooter: false,
      });
      assert(pdfResult.data, "Chrome did not return PDF data.");
      fs.writeFileSync(targetPdfPath, Buffer.from(pdfResult.data, "base64"));
      const screenshotResult = await page.send("Page.captureScreenshot", {
        format: "png",
        fromSurface: true,
        captureBeyondViewport: true,
      });
      assert(screenshotResult.data, "Chrome did not return screenshot data.");
      fs.writeFileSync(targetScreenshotPath, Buffer.from(screenshotResult.data, "base64"));

      return {
        domMetrics,
        pdfMetrics: analyzePdf(targetPdfPath),
        pixelMetrics: analyzePngPixels(targetScreenshotPath),
      };
    } finally {
      page.close();
    }
  } finally {
    await closeChrome(browserWebSocketUrl, child);
  }
}

async function readDomPrintMetrics(page) {
  const response = await page.send("Runtime.evaluate", {
    expression: `(() => {
      const report = document.querySelector(".edm-report-page-table");
      const reportHead = report ? Array.from(report.children).find((element) => element.tagName === "THEAD") : null;
      const reportFoot = report ? Array.from(report.children).find((element) => element.tagName === "TFOOT") : null;
      const layout = document.querySelector(".edm-detail-layout");
      const layoutHead = layout ? Array.from(layout.children).find((element) => element.tagName === "THEAD") : null;
      const table = document.querySelector(".edm-detail-table");
      const tableHead = table ? table.querySelector("thead") : null;
      const firstRow = table ? table.querySelector("tbody tr") : null;
      const headerGroupRow = table ? table.querySelector(".edm-detail-header-group-row") : null;
      const compositeCell = table ? table.querySelector("tbody tr td") : null;
      const dashedCell = table ? table.querySelector("tbody tr td:nth-child(3)") : null;
      const noBorderCell = table ? table.querySelector("tbody tr td:nth-child(2)") : null;
      const breakRow = document.querySelector(".report-page-break-row");
      const sideBandCell = layout ? layout.querySelector("tbody > tr > td") : null;
      const bodyText = document.body.innerText || "";
      return {
        reportHeaderDisplay: reportHead ? getComputedStyle(reportHead).display : "",
        reportFooterDisplay: reportFoot ? getComputedStyle(reportFoot).display : "",
        reportClass: report ? report.className : "",
        tableHeaderDisplay: tableHead ? getComputedStyle(tableHead).display : "",
        layoutHeaderDisplay: layoutHead ? getComputedStyle(layoutHead).display : "",
        rowBreakInside: firstRow ? getComputedStyle(firstRow).breakInside : "",
        rowPageBreakInside: firstRow ? getComputedStyle(firstRow).pageBreakInside : "",
        breakBefore: breakRow ? getComputedStyle(breakRow).breakBefore : "",
        pageBreakBefore: breakRow ? getComputedStyle(breakRow).pageBreakBefore : "",
        tableWidth: table ? Math.round(table.getBoundingClientRect().width) : 0,
        layoutWidth: layout ? Math.round(layout.getBoundingClientRect().width) : 0,
        sideBandWidth: sideBandCell ? Math.round(sideBandCell.getBoundingClientRect().width) : 0,
        scrollHeight: Math.round(document.documentElement.scrollHeight),
        viewportHeight: window.innerHeight,
        detailHeaderClass: table ? table.className : "",
        layoutHeaderClass: layout ? layout.className : "",
        hasHeaderGroup: Boolean(headerGroupRow),
        headerGroupText: headerGroupRow ? headerGroupRow.textContent : "",
        compositeCellText: compositeCell ? compositeCell.textContent : "",
        compositeCellLineBreaks: compositeCell ? compositeCell.querySelectorAll("br").length : 0,
        dashedCellBorderBottomStyle: dashedCell ? getComputedStyle(dashedCell).borderBottomStyle : "",
        noBorderCellBorderTopWidth: noBorderCell ? getComputedStyle(noBorderCell).borderTopWidth : "",
        noBorderCellBorderRightWidth: noBorderCell ? getComputedStyle(noBorderCell).borderRightWidth : "",
        pageBreakTextLeaked: bodyText.includes("Page Break") || bodyText.includes("分页符"),
      };
    })()`,
    returnByValue: true,
  });
  const value = response?.result?.value;
  assert(value, "Unable to read DOM print metrics.");
  return value;
}

function analyzePngPixels(filePath) {
  assert(fs.existsSync(filePath), `Expected screenshot to exist: ${filePath}`);
  const image = parsePng(fs.readFileSync(filePath));
  const { width, height, pixels } = image;

  let nonWhite = 0;
  let dark = 0;
  let longestHorizontalInkRun = 0;
  const rowInkCounts = new Array(height).fill(0);
  const columnInkCounts = new Array(width).fill(0);
  const total = width * height;
  for (let y = 0; y < height; y += 1) {
    let currentHorizontalInkRun = 0;
    for (let x = 0; x < width; x += 1) {
      const offset = (y * width + x) * 4;
      const red = pixels[offset];
      const green = pixels[offset + 1];
      const blue = pixels[offset + 2];
      const alpha = pixels[offset + 3];
      if (alpha === 0) {
        currentHorizontalInkRun = 0;
        continue;
      }
      if (red < 245 || green < 245 || blue < 245) {
        nonWhite += 1;
      }

      if (red < 90 && green < 90 && blue < 90) {
        dark += 1;
      }

      if (red < 180 && green < 180 && blue < 180) {
        rowInkCounts[y] += 1;
        columnInkCounts[x] += 1;
        currentHorizontalInkRun += 1;
        longestHorizontalInkRun = Math.max(longestHorizontalInkRun, currentHorizontalInkRun);
      } else {
        currentHorizontalInkRun = 0;
      }
    }
  }

  const strongHorizontalRuleCount = rowInkCounts.filter((count) => count >= width * 0.3).length;
  const strongVerticalRuleCount = columnInkCounts.filter((count) => count >= height * 0.2).length;

  return {
    width,
    height,
    nonWhiteRatio: Math.round((nonWhite / total) * 100000) / 100000,
    darkRatio: Math.round((dark / total) * 100000) / 100000,
    strongHorizontalRuleCount,
    strongVerticalRuleCount,
    longestHorizontalInkRun,
  };
}

function analyzePdf(filePath) {
  assert(fs.existsSync(filePath), `Expected PDF to exist: ${filePath}`);
  const buffer = fs.readFileSync(filePath);
  const content = buffer.toString("latin1");
  const mediaBoxes = [...content.matchAll(/\/MediaBox\s*\[\s*([^\]]+?)\s*\]/g)]
    .map((match) => parseMediaBox(match[1]))
    .filter(Boolean);

  assert(content.startsWith("%PDF-"), "Generated PDF is missing PDF header.");
  assert(/%%EOF\s*$/.test(content), "Generated PDF is missing EOF marker.");
  assert(mediaBoxes.length > 0, "Generated PDF is missing MediaBox entries.");

  return {
    bytes: buffer.length,
    pageCount: (content.match(/\/Type\s*\/Page\b/g) || []).length,
    mediaBoxes,
  };
}

function parseMediaBox(value) {
  const numbers = value
    .trim()
    .split(/\s+/)
    .map((part) => Number.parseFloat(part))
    .filter((part) => Number.isFinite(part));

  if (numbers.length < 4) {
    return null;
  }

  const [left, bottom, right, top] = numbers;
  return {
    width: Math.round(Math.abs(right - left) * 1000) / 1000,
    height: Math.round(Math.abs(top - bottom) * 1000) / 1000,
  };
}

function assertMediaBoxMatchesMm(mediaBox, expectedWidthMm, expectedHeightMm, label) {
  const expectedWidth = expectedWidthMm * pointsPerMm;
  const expectedHeight = expectedHeightMm * pointsPerMm;
  const widthDelta = Math.abs(mediaBox.width - expectedWidth);
  const heightDelta = Math.abs(mediaBox.height - expectedHeight);
  assert(
    widthDelta <= 3 && heightDelta <= 3,
    `Expected ${label} PDF MediaBox to be close to ${expectedWidthMm}mm x ${expectedHeightMm}mm, found ${mediaBox.width}pt x ${mediaBox.height}pt.`,
  );
}

async function run() {
  fs.rmSync(workspaceRoot, { recursive: true, force: true });
  fs.mkdirSync(workspaceRoot, { recursive: true });
  await buildBundle();
  const module = await import(pathToFileURL(bundlePath).href);
  const html = module.buildHtml();
  fs.writeFileSync(htmlPath, html, "utf8");

  const chromePath = locateChromeForTesting(repoRoot);
  const { domMetrics, pdfMetrics, pixelMetrics } = await inspectWithChrome(chromePath);
  assert(domMetrics.reportHeaderDisplay === "table-header-group", "Expected report header band to repeat in print media.");
  assert(domMetrics.reportFooterDisplay === "table-footer-group", "Expected report footer band to repeat in print media.");
  assert(domMetrics.reportClass.includes("edm-report-repeat-header"), "Expected report shell repeat-header class.");
  assert(domMetrics.reportClass.includes("edm-report-repeat-footer"), "Expected report shell repeat-footer class.");
  assert(domMetrics.reportClass.includes("edm-report-pin-footer"), "Expected report shell pinned-footer class.");
  assert(domMetrics.tableHeaderDisplay === "table-header-group", "Expected detail table header to repeat in print media.");
  assert(domMetrics.layoutHeaderDisplay === "table-header-group", "Expected side-band layout header to repeat in print media.");
  assert(
    domMetrics.rowBreakInside === "avoid" || domMetrics.rowPageBreakInside === "avoid",
    "Expected detail rows to avoid being split across pages.",
  );
  assert(
    domMetrics.breakBefore === "page" || domMetrics.pageBreakBefore === "always",
    "Expected explicit page-break block to retain print page break CSS.",
  );
  assert(domMetrics.detailHeaderClass.includes("edm-detail-repeat-header"), "Expected detail table repeat-header class.");
  assert(domMetrics.detailHeaderClass.includes("edm-detail-keep-rows"), "Expected detail table keep-rows class.");
  assert(domMetrics.layoutHeaderClass.includes("edm-detail-repeat-header"), "Expected detail layout repeat-header class.");
  assert(domMetrics.hasHeaderGroup, "Expected grouped detail header row to render.");
  assert(domMetrics.headerGroupText.includes("Quantities and Descriptions"), "Expected grouped detail header text.");
  assert(domMetrics.compositeCellText.includes("MO SKU-"), "Expected composite detail cell text to render.");
  assert(domMetrics.compositeCellLineBreaks >= 1, "Expected composite detail cell line break to render.");
  assert(domMetrics.dashedCellBorderBottomStyle === "dashed", "Expected dashed detail cell border in print media.");
  assert(domMetrics.noBorderCellBorderTopWidth === "0px" && domMetrics.noBorderCellBorderRightWidth === "0px", "Expected no-border detail cell override in print media.");
  assert(domMetrics.layoutWidth > 700, `Expected layout width to be stable, found ${domMetrics.layoutWidth}.`);
  assert(domMetrics.tableWidth > 500, `Expected nested detail table width to be stable, found ${domMetrics.tableWidth}.`);
  assert(
    domMetrics.sideBandWidth >= 95 && domMetrics.sideBandWidth <= 260,
    `Expected side-band width to remain close to configured mm width, found ${domMetrics.sideBandWidth}.`,
  );
  assert(domMetrics.scrollHeight > domMetrics.viewportHeight * 2, "Expected rendered report to span multiple screen pages.");
  assert(!domMetrics.pageBreakTextLeaked, "Expected page-break marker text not to leak into printed content.");
  assert(pdfMetrics.bytes > 20000, `Expected non-trivial PDF output, found ${pdfMetrics.bytes} bytes.`);
  assert(pdfMetrics.pageCount >= 3, `Expected multi-page PDF output, found ${pdfMetrics.pageCount} page(s).`);
  assert(pixelMetrics.width >= 900 && pixelMetrics.height >= 1100, `Expected full-page screenshot dimensions, found ${pixelMetrics.width}x${pixelMetrics.height}.`);
  assert(pixelMetrics.nonWhiteRatio > 0.02, `Expected screenshot to contain visible report pixels, found non-white ratio ${pixelMetrics.nonWhiteRatio}.`);
  assert(pixelMetrics.darkRatio > 0.002, `Expected screenshot to contain dark text/grid pixels, found dark ratio ${pixelMetrics.darkRatio}.`);
  assert(
    pixelMetrics.strongHorizontalRuleCount >= 6,
    `Expected screenshot to contain fixed report horizontal rules, found ${pixelMetrics.strongHorizontalRuleCount}.`,
  );
  assert(
    pixelMetrics.strongVerticalRuleCount >= 3,
    `Expected screenshot to contain fixed report vertical rules, found ${pixelMetrics.strongVerticalRuleCount}.`,
  );
  assert(
    pixelMetrics.longestHorizontalInkRun >= 300,
    `Expected screenshot to contain long table/header separators, found longest run ${pixelMetrics.longestHorizontalInkRun}.`,
  );

  const firstPage = pdfMetrics.mediaBoxes[0];
  assert(firstPage.width >= 590 && firstPage.width <= 600, `Expected A4 PDF width, found ${firstPage.width}.`);
  assert(firstPage.height >= 835 && firstPage.height <= 850, `Expected A4 PDF height, found ${firstPage.height}.`);

  const paperVariants = [];
  for (const variant of module.buildPaperVariantHtmls()) {
    const variantHtmlPath = path.join(workspaceRoot, `${variant.name}.html`);
    const variantPdfPath = path.join(workspaceRoot, `${variant.name}.pdf`);
    const variantScreenshotPath = path.join(workspaceRoot, `${variant.name}.png`);
    fs.writeFileSync(variantHtmlPath, variant.html, "utf8");
    assert(
      variant.html.includes(variant.expectedPageCss),
      `Expected ${variant.name} to export CSS page size: ${variant.expectedPageCss}`,
    );

    const variantMetrics = await inspectWithChrome(chromePath, {
      htmlPath: variantHtmlPath,
      pdfPath: variantPdfPath,
      screenshotPath: variantScreenshotPath,
    });
    assert(variantMetrics.pdfMetrics.bytes > 8000, `Expected ${variant.name} to produce a non-trivial PDF.`);
    assert(variantMetrics.pdfMetrics.pageCount >= 1, `Expected ${variant.name} to produce at least one PDF page.`);
    assert(variantMetrics.pixelMetrics.nonWhiteRatio > 0.005, `Expected ${variant.name} screenshot to contain visible report pixels.`);
    assertMediaBoxMatchesMm(
      variantMetrics.pdfMetrics.mediaBoxes[0],
      variant.expectedPageWidthMm,
      variant.expectedPageHeightMm,
      variant.name,
    );
    paperVariants.push({
      ...variantMetrics,
      name: variant.name,
      htmlPath: variantHtmlPath,
      pdfPath: variantPdfPath,
      screenshotPath: variantScreenshotPath,
      expectedPageWidthMm: variant.expectedPageWidthMm,
      expectedPageHeightMm: variant.expectedPageHeightMm,
    });
  }

  const summaryPath = path.join(workspaceRoot, "print-regression-summary.json");
  fs.writeFileSync(
    summaryPath,
    `${JSON.stringify({ browserExecutable: chromePath, htmlPath, pdfPath, screenshotPath, domMetrics, pdfMetrics, pixelMetrics, paperVariants }, null, 2)}\n`,
    "utf8",
  );
  process.stdout.write("new-report-designer-print-regression test passed\n");
  process.stdout.write(`summary: ${summaryPath}\n`);
}

run().catch((error) => {
  process.stderr.write(`${error.stack || error.message}\n`);
  process.exit(1);
});
