const templateCatalog = [
  {
    slug: "customer-custom-invoice",
    relativePath: "tests/ReportTemplateFixtures/customer_custom_invoice_legacy.html",
    profiles: {
      visual: {
        viewport: { width: 900, height: 1270 },
        minNonWhiteRatio: 0.008,
        minDarkRatio: 0.003,
        minContentWidthRatio: 0.72,
        minContentHeight: 115,
        minColorBuckets: 8,
        expectedColorSamples: [
          { color: "#333333", minPixels: 40 },
          { color: "#f4f4f4", minPixels: 80 },
        ],
      },
      printPixel: {
        viewport: { width: 900, height: 1270 },
        minNonWhiteRatio: 0.007,
        minDarkRatio: 0.0025,
        minContentWidthRatio: 0.7,
        minContentHeight: 110,
        minColorBuckets: 8,
        expectedColorSamples: [
          { color: "#333333", minPixels: 30 },
          { color: "#f4f4f4", minPixels: 60 },
        ],
      },
      pdf: {
        expectedPages: 1,
        expectedOrientation: "portrait",
        minBytes: 30000,
        minFontReferences: 3,
        minInflatedStreamBytes: 60000,
        minTextOperatorCount: 180,
        minDrawingOperatorCount: 40,
      },
      pdfPixel: {
        expectedPages: 1,
        viewport: { width: 900, height: 1270 },
        minPdfBytes: 30000,
        minPaperWhiteRatio: 0.28,
        minPaperWidthRatio: 0.45,
        minPaperHeightRatio: 0.55,
        minDarkPixelsInsidePaper: 900,
        minNonWhitePixelsInsidePaper: 1200,
        minColorBucketsInsidePaper: 12,
      },
    },
  },
  {
    slug: "customer-payment-voucher",
    relativePath: "tests/ReportTemplateFixtures/customer_custom_payment_voucher_legacy.html",
    profiles: {
      visual: {
        viewport: { width: 1270, height: 900 },
        minNonWhiteRatio: 0.007,
        minDarkRatio: 0.0025,
        minContentWidthRatio: 0.78,
        minContentHeight: 130,
        minColorBuckets: 8,
        expectedColorSamples: [
          { color: "#222222", minPixels: 40 },
          { color: "#f3f6f8", minPixels: 80 },
        ],
      },
      printPixel: {
        viewport: { width: 1270, height: 900 },
        minNonWhiteRatio: 0.006,
        minDarkRatio: 0.002,
        minContentWidthRatio: 0.76,
        minContentHeight: 120,
        minColorBuckets: 8,
        expectedColorSamples: [
          { color: "#222222", minPixels: 30 },
          { color: "#f3f6f8", minPixels: 60 },
        ],
      },
      pdf: {
        expectedPages: 1,
        expectedOrientation: "landscape",
        minBytes: 30000,
        minFontReferences: 3,
        minInflatedStreamBytes: 60000,
        minTextOperatorCount: 180,
        minDrawingOperatorCount: 40,
      },
      pdfPixel: {
        expectedPages: 1,
        viewport: { width: 1270, height: 900 },
        minPdfBytes: 30000,
        minPaperWhiteRatio: 0.28,
        minPaperWidthRatio: 0.48,
        minPaperHeightRatio: 0.52,
        minDarkPixelsInsidePaper: 900,
        minNonWhitePixelsInsidePaper: 1200,
        minColorBucketsInsidePaper: 12,
      },
    },
  },
  {
    slug: "customer-compound-selector-invoice",
    relativePath: "tests/ReportTemplateFixtures/customer_custom_compound_selector_invoice_legacy.html",
    profiles: {
      visual: {
        viewport: { width: 900, height: 1270 },
        minNonWhiteRatio: 0.009,
        minDarkRatio: 0.003,
        minContentWidthRatio: 0.72,
        minContentHeight: 115,
        minColorBuckets: 10,
        expectedColorSamples: [
          { color: "#e8eef7", minPixels: 80 },
          { color: "#eef9ef", minPixels: 80 },
          { color: "#fff6d8", minPixels: 80 },
          { color: "#884422", minPixels: 8 },
        ],
      },
      printPixel: {
        viewport: { width: 900, height: 1270 },
        minNonWhiteRatio: 0.008,
        minDarkRatio: 0.0025,
        minContentWidthRatio: 0.7,
        minContentHeight: 110,
        minColorBuckets: 10,
        expectedColorSamples: [
          { color: "#e8eef7", minPixels: 60 },
          { color: "#eef9ef", minPixels: 60 },
          { color: "#fff6d8", minPixels: 60 },
          { color: "#884422", minPixels: 6 },
        ],
      },
      pdf: {
        expectedPages: 1,
        expectedOrientation: "portrait",
        minBytes: 30000,
        minFontReferences: 3,
        minInflatedStreamBytes: 60000,
        minTextOperatorCount: 180,
        minDrawingOperatorCount: 40,
      },
      pdfPixel: {
        expectedPages: 1,
        viewport: { width: 900, height: 1270 },
        minPdfBytes: 30000,
        minPaperWhiteRatio: 0.28,
        minPaperWidthRatio: 0.45,
        minPaperHeightRatio: 0.55,
        minDarkPixelsInsidePaper: 900,
        minNonWhitePixelsInsidePaper: 1200,
        minColorBucketsInsidePaper: 12,
      },
    },
  },
  {
    slug: "built-in-invoice-template",
    relativePath: "Templates/Export/invoice_template.html",
    expectedTemplatePageOrientation: "portrait",
    profiles: {
      visual: {
        viewport: { width: 900, height: 1270 },
        minNonWhiteRatio: 0.012,
        minDarkRatio: 0.004,
        minContentWidthRatio: 0.72,
        minContentHeight: 620,
        minColorBuckets: 8,
        expectedColorSamples: [
          { color: "#000000", minPixels: 120 },
          { color: "#f2f2f2", minPixels: 80 },
        ],
      },
      printPixel: {
        viewport: { width: 900, height: 1270 },
        minNonWhiteRatio: 0.011,
        minDarkRatio: 0.0035,
        minContentWidthRatio: 0.7,
        minContentHeight: 600,
        minColorBuckets: 8,
        expectedColorSamples: [
          { color: "#000000", minPixels: 100 },
          { color: "#f2f2f2", minPixels: 60 },
        ],
      },
      pdf: {
        expectedPages: 2,
        expectedOrientation: "portrait",
        minBytes: 30000,
        minFontReferences: 3,
        minInflatedStreamBytes: 50000,
        minTextOperatorCount: 150,
        minDrawingOperatorCount: 40,
      },
      pdfPixel: {
        expectedPages: 2,
        viewport: { width: 900, height: 1270 },
        minPdfBytes: 30000,
        minPaperWhiteRatio: 0.28,
        minPaperWidthRatio: 0.45,
        minPaperHeightRatio: 0.55,
        minDarkPixelsInsidePaper: 1500,
        minNonWhitePixelsInsidePaper: 5000,
        minColorBucketsInsidePaper: 12,
      },
    },
  },
  {
    slug: "built-in-packing-list-template",
    relativePath: "Templates/Export/packing_list_template.html",
    expectedTemplatePageOrientation: "portrait",
    profiles: {
      visual: {
        viewport: { width: 900, height: 1270 },
        minNonWhiteRatio: 0.01,
        minDarkRatio: 0.0035,
        minContentWidthRatio: 0.72,
        minContentHeight: 620,
        minColorBuckets: 8,
        expectedColorSamples: [
          { color: "#000000", minPixels: 120 },
          { color: "#f2f2f2", minPixels: 80 },
        ],
      },
      printPixel: {
        viewport: { width: 900, height: 1270 },
        minNonWhiteRatio: 0.009,
        minDarkRatio: 0.003,
        minContentWidthRatio: 0.7,
        minContentHeight: 600,
        minColorBuckets: 8,
        expectedColorSamples: [
          { color: "#000000", minPixels: 100 },
          { color: "#f2f2f2", minPixels: 60 },
        ],
      },
      pdf: {
        expectedPages: 2,
        expectedOrientation: "portrait",
        minBytes: 30000,
        minFontReferences: 3,
        minInflatedStreamBytes: 50000,
        minTextOperatorCount: 150,
        minDrawingOperatorCount: 40,
      },
      pdfPixel: {
        expectedPages: 2,
        viewport: { width: 900, height: 1270 },
        minPdfBytes: 30000,
        minPaperWhiteRatio: 0.28,
        minPaperWidthRatio: 0.45,
        minPaperHeightRatio: 0.55,
        minDarkPixelsInsidePaper: 2500,
        minNonWhitePixelsInsidePaper: 7000,
        minColorBucketsInsidePaper: 12,
      },
    },
  },
  {
    slug: "built-in-contract-template",
    relativePath: "Templates/Export/contract_template.html",
    expectedTemplatePageOrientation: "portrait",
    profiles: {
      visual: {
        viewport: { width: 900, height: 1270 },
        minNonWhiteRatio: 0.009,
        minDarkRatio: 0.003,
        minContentWidthRatio: 0.72,
        minContentHeight: 320,
        minColorBuckets: 8,
        expectedColorSamples: [
          { color: "#000000", minPixels: 120 },
          { color: "#f2f2f2", minPixels: 80 },
        ],
      },
      printPixel: {
        viewport: { width: 900, height: 1270 },
        minNonWhiteRatio: 0.008,
        minDarkRatio: 0.0025,
        minContentWidthRatio: 0.7,
        minContentHeight: 300,
        minColorBuckets: 8,
        expectedColorSamples: [
          { color: "#000000", minPixels: 100 },
          { color: "#f2f2f2", minPixels: 60 },
        ],
      },
      pdf: {
        expectedPages: 1,
        expectedOrientation: "portrait",
        minBytes: 30000,
        minFontReferences: 3,
        minInflatedStreamBytes: 45000,
        minTextOperatorCount: 150,
        minDrawingOperatorCount: 35,
      },
      pdfPixel: {
        expectedPages: 1,
        viewport: { width: 900, height: 1270 },
        minPdfBytes: 30000,
        minPaperWhiteRatio: 0.28,
        minPaperWidthRatio: 0.45,
        minPaperHeightRatio: 0.55,
        minDarkPixelsInsidePaper: 3000,
        minNonWhitePixelsInsidePaper: 4000,
        minColorBucketsInsidePaper: 12,
      },
    },
  },
  {
    slug: "built-in-customs-declaration-template",
    relativePath: "Templates/Export/customs_declaration_template.html",
    expectedTemplatePageOrientation: "landscape",
    profiles: {
      visual: {
        viewport: { width: 1440, height: 1100 },
        minNonWhiteRatio: 0.004,
        minDarkRatio: 0.002,
        minContentWidthRatio: 0.7,
        minContentHeight: 520,
        minColorBuckets: 6,
        expectedColorSamples: [{ color: "#000000", minPixels: 120 }],
      },
      printPixel: {
        viewport: { width: 1440, height: 1100 },
        minNonWhiteRatio: 0.0035,
        minDarkRatio: 0.0018,
        minContentWidthRatio: 0.68,
        minContentHeight: 500,
        minColorBuckets: 6,
        expectedColorSamples: [{ color: "#000000", minPixels: 100 }],
      },
      pdf: {
        expectedPages: 3,
        expectedOrientation: "landscape",
        minBytes: 50000,
        minFontReferences: 2,
        minInflatedStreamBytes: 50000,
        minTextOperatorCount: 180,
        minDrawingOperatorCount: 50,
      },
      pdfPixel: {
        expectedPages: 3,
        viewport: { width: 1270, height: 900 },
        minPdfBytes: 50000,
        minPaperWhiteRatio: 0.26,
        minPaperWidthRatio: 0.48,
        minPaperHeightRatio: 0.5,
        minDarkPixelsInsidePaper: 4500,
        minNonWhitePixelsInsidePaper: 6000,
        minColorBucketsInsidePaper: 20,
      },
    },
  },
  {
    slug: "built-in-payment-voucher-template",
    relativePath: "Templates/Internal/payment_voucher_template.html",
    profiles: {
      visual: {
        viewport: { width: 900, height: 1270 },
        minNonWhiteRatio: 0.01,
        minDarkRatio: 0.003,
        minContentWidthRatio: 0.82,
        minContentHeight: 350,
        minColorBuckets: 8,
        expectedColorSamples: [
          { color: "#000000", minPixels: 80 },
          { color: "#474747", minPixels: 120 },
        ],
      },
      printPixel: {
        viewport: { width: 900, height: 1270 },
        minNonWhiteRatio: 0.009,
        minDarkRatio: 0.0025,
        minContentWidthRatio: 0.8,
        minContentHeight: 330,
        minColorBuckets: 8,
        expectedColorSamples: [
          { color: "#000000", minPixels: 60 },
          { color: "#474747", minPixels: 100 },
        ],
      },
      pdf: {
        expectedPages: 1,
        expectedOrientation: "portrait",
        minBytes: 15000,
        minFontReferences: 2,
        minInflatedStreamBytes: 25000,
        minTextOperatorCount: 80,
        minDrawingOperatorCount: 20,
      },
      pdfPixel: {
        expectedPages: 1,
        viewport: { width: 900, height: 1270 },
        minPdfBytes: 15000,
        minPaperWhiteRatio: 0.28,
        minPaperWidthRatio: 0.45,
        minPaperHeightRatio: 0.55,
        minDarkPixelsInsidePaper: 2200,
        minNonWhitePixelsInsidePaper: 3000,
        minColorBucketsInsidePaper: 12,
      },
    },
  },
  {
    slug: "built-in-expense-reimbursement-template",
    relativePath: "Templates/Internal/expense_reimbursement_template.html",
    profiles: {
      visual: {
        viewport: { width: 900, height: 1270 },
        minNonWhiteRatio: 0.01,
        minDarkRatio: 0.003,
        minContentWidthRatio: 0.82,
        minContentHeight: 280,
        minColorBuckets: 8,
        expectedColorSamples: [
          { color: "#000000", minPixels: 80 },
          { color: "#474747", minPixels: 120 },
        ],
      },
      printPixel: {
        viewport: { width: 900, height: 1270 },
        minNonWhiteRatio: 0.009,
        minDarkRatio: 0.0025,
        minContentWidthRatio: 0.8,
        minContentHeight: 260,
        minColorBuckets: 8,
        expectedColorSamples: [
          { color: "#000000", minPixels: 60 },
          { color: "#474747", minPixels: 100 },
        ],
      },
      pdf: {
        expectedPages: 1,
        expectedOrientation: "portrait",
        minBytes: 15000,
        minFontReferences: 2,
        minInflatedStreamBytes: 25000,
        minTextOperatorCount: 80,
        minDrawingOperatorCount: 20,
      },
      pdfPixel: {
        expectedPages: 1,
        viewport: { width: 900, height: 1270 },
        minPdfBytes: 15000,
        minPaperWhiteRatio: 0.22,
        minPaperWidthRatio: 0.45,
        minPaperHeightRatio: 0.55,
        minDarkPixelsInsidePaper: 1800,
        minNonWhitePixelsInsidePaper: 2500,
        minColorBucketsInsidePaper: 12,
      },
    },
  },
  {
    slug: "structured-multi-table-page-break",
    html: `<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <style>
    @page { size: A4 portrait; margin: 12mm; }
    body { font-family: Arial, sans-serif; font-size: 11pt; }
    table { width: 100%; border-collapse: collapse; margin-bottom: 8mm; }
    td { border: 1px solid #333; padding: 6mm; }
    .report-page-break-row { page-break-before: always; break-before: page; }
  </style>
</head>
<body>
  <table><tr><td>STATIC-MULTI-TABLE-FIRST</td></tr></table>
  <div class="report-page-break-row"></div>
  <table><tr><td>STATIC-MULTI-TABLE-SECOND</td></tr></table>
  <table><tr><td>STATIC-MULTI-TABLE-SECOND-DETAIL</td></tr></table>
  <div class="report-page-break-row"></div>
  <table><tr><td>STATIC-MULTI-TABLE-THIRD</td></tr></table>
  <table><tr><td>STATIC-MULTI-TABLE-THIRD-SUMMARY</td></tr></table>
</body>
</html>`,
    profiles: {
      pdf: {
        expectedPages: 3,
        expectedOrientation: "portrait",
        minBytes: 12000,
        minFontReferences: 2,
        minInflatedStreamBytes: 18000,
        minTextOperatorCount: 45,
        minDrawingOperatorCount: 18,
      },
      pdfPixel: {
        expectedPages: 3,
        viewport: { width: 900, height: 1270 },
        minPdfBytes: 12000,
        minPaperWhiteRatio: 0.28,
        minPaperWidthRatio: 0.45,
        minPaperHeightRatio: 0.55,
        minDarkPixelsInsidePaper: 900,
        minNonWhitePixelsInsidePaper: 2500,
        minColorBucketsInsidePaper: 12,
      },
    },
  },
  {
    slug: "synthetic-page-break",
    html: `<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <style>
    @page { size: A4 portrait; margin: 12mm; }
    body { font-family: Arial, sans-serif; font-size: 12pt; }
    .box { border: 1px solid #333; min-height: 180px; padding: 10px; }
    .page-break { page-break-before: always; break-before: page; }
  </style>
</head>
<body>
  <div class="box">STATIC-BEFORE-PAGE</div>
  <div class="page-break"></div>
  <div class="box">STATIC-AFTER-PAGE</div>
</body>
</html>`,
    profiles: {
      pdf: {
        expectedPages: 2,
        expectedOrientation: "portrait",
        minBytes: 10000,
        minFontReferences: 2,
        minInflatedStreamBytes: 25000,
        minTextOperatorCount: 40,
        minDrawingOperatorCount: 20,
      },
      pdfPixel: {
        expectedPages: 2,
        viewport: { width: 900, height: 1270 },
        minPdfBytes: 10000,
        minPaperWhiteRatio: 0.28,
        minPaperWidthRatio: 0.45,
        minPaperHeightRatio: 0.55,
        minDarkPixelsInsidePaper: 350,
        minNonWhitePixelsInsidePaper: 500,
        minColorBucketsInsidePaper: 12,
      },
    },
  },
  {
    slug: "synthetic-print-media-sentinel",
    html: `<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <style>
    body { margin: 0; background: #ffffff; font-family: Arial, sans-serif; }
    .sheet { width: 420px; min-height: 260px; margin: 40px; padding: 28px; background: #ff0000; color: #ffffff; }
    .print-only { display: none; }
    @media print {
      .sheet { background: #0b5a9d; color: #111111; border: 8px solid #000000; }
      .screen-only { display: none; }
      .print-only { display: block; font-size: 28px; font-weight: 700; }
    }
  </style>
</head>
<body>
  <main class="sheet">
    <div class="screen-only">SCREEN-MEDIA-SHOULD-NOT-RENDER</div>
    <div class="print-only">PRINT-MEDIA-PIXEL-SENTINEL</div>
  </main>
</body>
</html>`,
    profiles: {
      printPixel: {
        viewport: { width: 640, height: 480 },
        minNonWhiteRatio: 0.36,
        minDarkRatio: 0.06,
        minContentWidthRatio: 0.72,
        minContentHeight: 300,
        minColorBuckets: 3,
        expectedColorSamples: [
          { color: "#0b5a9d", minPixels: 90000 },
          { color: "#000000", minPixels: 9000 },
        ],
      },
    },
  },
  {
    slug: "synthetic-print-full-page-sentinel",
    html: `<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <style>
    body { margin: 0; background: #ffffff; font-family: Arial, sans-serif; }
    .page { width: 640px; height: 480px; box-sizing: border-box; padding: 36px; color: #111111; font-size: 28px; font-weight: 700; }
    .page:nth-child(1) { background: #dd3333; }
    .page:nth-child(2) { background: #dd3333; }
    .page:nth-child(3) { background: #dd3333; }
    @media print {
      .page:nth-child(1) { background: #145a32; }
      .page:nth-child(2) { background: #6f1d8f; }
      .page:nth-child(3) { background: #1f4e79; }
    }
  </style>
</head>
<body>
  <section class="page">PRINT-FULL-PAGE-TOP</section>
  <section class="page">PRINT-FULL-PAGE-MIDDLE</section>
  <section class="page">PRINT-FULL-PAGE-BOTTOM</section>
</body>
</html>`,
    profiles: {
      printPixel: {
        viewport: { width: 640, height: 480 },
        minNonWhiteRatio: 0.95,
        minDarkRatio: 0.95,
        minContentWidthRatio: 0.99,
        minContentHeight: 470,
        minColorBuckets: 3,
        minFullPageHeight: 1400,
        expectedColorSamples: [{ color: "#145a32", minPixels: 100000 }],
        expectedFullPageColorSamples: [
          { color: "#145a32", minPixels: 100000 },
          { color: "#6f1d8f", minPixels: 100000 },
          { color: "#1f4e79", minPixels: 100000 },
        ],
      },
    },
  },
];

const supportedProfiles = new Set(["visual", "printPixel", "pdf", "pdfPixel"]);

export function createReportRegressionTemplateCases(profile) {
  if (!supportedProfiles.has(profile)) {
    throw new Error(`Unknown report regression template profile: ${profile}`);
  }

  return templateCatalog.flatMap(({ profiles, ...template }) => {
    const profileConfig = profiles[profile];
    if (!profileConfig) {
      return [];
    }

    return [{
      ...template,
      ...cloneProfileConfig(profileConfig),
    }];
  });
}

function cloneProfileConfig(profileConfig) {
  return JSON.parse(JSON.stringify(profileConfig));
}
