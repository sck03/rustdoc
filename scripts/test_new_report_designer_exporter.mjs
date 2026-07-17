import { spawnSync } from "node:child_process";
import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { assert, toImportSpecifier } from "./lib/report-regression-common.mjs";

const require = createRequire(import.meta.url);
const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, "..");
const workspaceRoot = path.join(repoRoot, ".codex-runtime", "new-report-designer-exporter-test");
const entryPath = path.join(workspaceRoot, "entry.ts");
const bundlePath = path.join(workspaceRoot, "bundle.mjs");

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
  const parserPath = path.join(
    repoRoot,
    "apps",
    "export-doc-web",
    "src",
    "features",
    "report-designer",
    "reportDesignerTemplateParser.ts",
  );

  fs.mkdirSync(workspaceRoot, { recursive: true });
  fs.writeFileSync(
    entryPath,
    `
import { exportReportDesignerSchemaToHtml } from "${toImportSpecifier(workspaceRoot, exporterPath)}";
import { parseReportDesignerSchemaFromHtml } from "${toImportSpecifier(workspaceRoot, parserPath)}";
import { getReportDesignerPreviewSampleProfiles, renderReportDesignerLocalPreviewSample } from "${toImportSpecifier(workspaceRoot, path.join(repoRoot, "apps", "export-doc-web", "src", "features", "report-designer", "reportDesignerPreviewSamples.ts"))}";
import { hasBlockingReportDesignerSchemaIssues, normalizeReportDesignerSchema, validateReportDesignerSchema } from "${toImportSpecifier(workspaceRoot, path.join(repoRoot, "apps", "export-doc-web", "src", "features", "report-designer", "reportDesignerSchemaValidation.ts"))}";
import { collectReportDesignerBlockFieldBindings, summarizeReportDesignerSchemaModel } from "${toImportSpecifier(workspaceRoot, path.join(repoRoot, "apps", "export-doc-web", "src", "features", "report-designer", "reportDesignerModel.ts"))}";
import { applyDetailTableBorderToColumns, applyGridBorderToCells, applyGridDefaultCellStyle, clearDetailTableColumnBorders, createDetailTableBlock, createFieldBlock, createGridBlock, createPageBreakBlock, createTextBlock, distributeDetailTableColumnWidths, distributeGridColumnWidths, duplicateDetailTableColumn, insertBlockAfterSelection, insertBlockAtDropTarget, moveBlockToDropTarget, moveDetailTableColumn, reorderDetailTableColumn, resizeAdjacentDetailTableColumnWidths, resizeAdjacentGridColumnWidths, resizeAdjacentRowColumnWidths } from "${toImportSpecifier(workspaceRoot, path.join(repoRoot, "apps", "export-doc-web", "src", "features", "report-designer", "reportDesignerMutations.ts"))}";

export function run() {
  const schema = {
    version: 2,
    reportType: "ExportDocument",
    page: {
      size: "A5",
      orientation: "Landscape",
      marginTopMm: 10,
      marginRightMm: 11,
      marginBottomMm: 12,
      marginLeftMm: 13,
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
          minHeightMm: 18,
        },
        blocks: [
          {
            id: "block-title",
            type: "Text",
            text: "Invoice <Draft> & Review",
            style: { fontSizePt: 14, bold: true, align: "Center", marginBottomMm: 4 },
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
          minHeightMm: 80,
        },
        blocks: [
          {
            id: "block-invoice-no",
            type: "Field",
            label: "Invoice No.",
            fieldPath: "Invoice.InvoiceNo",
            fallbackText: "INV-0001",
            style: { fontSizePt: 10, align: "Left", marginTopMm: 1, marginBottomMm: 1 },
          },
          {
            id: "block-detail",
            type: "DetailTable",
            title: "Quantities and Descriptions",
            detailWidthMm: 126,
            sourcePath: "Invoice.Items",
            repeatMode: "ScribanFor",
            print: {
              repeatHeaderOnPageBreak: true,
              keepRowsTogether: true,
            },
            sideBand: {
              title: "唛头 Marks",
              widthMm: 42,
              contentKind: "Field",
              text: "Fixed marks",
              fieldPath: "Invoice.ShippingMarks",
              style: { fontSizePt: 9, bold: true, align: "Left", marginTopMm: 12 },
            },
            grouping: {
              fieldPath: "Invoice.Items.ProductNameEN",
              label: "Product Group",
              showFieldValue: true,
              keepTogether: true,
              pageBreakBefore: true,
              footer: {
                label: "SUBTOTAL",
                labelColumnSpan: 1,
                cells: [
                  { columnId: "col-amount", contentKind: "Sum", text: "", fieldPath: "Invoice.Items.TotalPrice" },
                ],
                style: { fontSizePt: 9, bold: true, align: "Right" },
              },
              style: { fontSizePt: 9, bold: true, align: "Left" },
            },
            columns: [
              {
                id: "col-name",
                title: "Product",
                headerGroupTitle: "Quantities and Descriptions",
                headerGroupSpan: 1,
                contentKind: "Composite",
                fieldPath: "Invoice.Items.ProductNameEN",
                content: [
                  { id: "part-name", kind: "Field", text: "", fieldPath: "Invoice.Items.ProductNameEN" },
                  { id: "part-space", kind: "Text", text: " / SKU: ", fieldPath: "" },
                  { id: "part-sku", kind: "Field", text: "", fieldPath: "Invoice.Items.Sku" },
                  { id: "part-line", kind: "LineBreak", text: "", fieldPath: "" },
                  { id: "part-qty", kind: "Field", text: "", fieldPath: "Invoice.Items.Quantity" },
                  { id: "part-ctns", kind: "Text", text: " CTNS", fieldPath: "" },
                ],
                widthMm: 64,
                align: "Left",
                border: { color: "#333333", widthPx: 1, style: "Dashed", top: false, right: false, bottom: true, left: false },
              },
              {
                id: "col-amount",
                title: "Amount",
                headerGroupTitle: "Amount",
                headerGroupSpan: 1,
                contentKind: "Field",
                fieldPath: "Invoice.Items.TotalPrice",
                widthMm: 28,
                align: "Right",
                border: { color: "#333333", widthPx: 1, style: "None", top: false, right: false, bottom: false, left: false },
              },
            ],
            summaryRow: {
              label: "TOTAL",
              labelColumnSpan: 1,
              cells: [
                { columnId: "col-amount", contentKind: "Field", text: "", fieldPath: "Invoice.TotalAmount" },
              ],
              style: { fontSizePt: 9, bold: true, align: "Right" },
            },
            headerStyle: { fontSizePt: 9, bold: true, align: "Center" },
            bodyStyle: { fontSizePt: 9, align: "Left" },
            border: { color: "#333333", widthPx: 1, top: true, right: true, bottom: true, left: true },
          },
          {
            id: "block-conditional",
            type: "Conditional",
            condition: {
              fieldPath: "Invoice.SpecialTerms",
              operator: "HasValue",
              value: "",
            },
            content: {
              kind: "Text",
              text: "Show only when special terms <exist> & ready",
              fieldPath: "",
            },
            style: { fontSizePt: 9, align: "Left", marginTopMm: 2, marginBottomMm: 2 },
          },
          {
            id: "block-seal",
            type: "Image",
            title: "Document seal",
            sourceKind: "Field",
            fieldPath: "doc_seal_path",
            url: "",
            altText: "Seal <safe>",
            widthMm: 42,
            heightMm: 24,
            align: "Right",
            marginTopMm: 3,
            marginBottomMm: 2,
            hideWhenSourceEmpty: true,
            keepTogether: true,
          },
          {
            id: "block-static-logo",
            type: "Image",
            title: "Static logo",
            sourceKind: "StaticUrl",
            fieldPath: "",
            url: "data:image/png;base64,iVBORw0KGgo=",
            altText: "Logo & mark",
            widthMm: 18,
            align: "Left",
            marginTopMm: 1,
            marginBottomMm: 1,
            hideWhenSourceEmpty: false,
            keepTogether: false,
          },
          {
            id: "block-two-column-row",
            type: "Row",
            marginTopMm: 2,
            marginBottomMm: 2,
            columns: [
              {
                id: "row-left",
                contentKind: "Text",
                text: "Seller: ACME EXPORT",
                fieldPath: "",
                widthPercent: 58,
                style: { fontSizePt: 9, align: "Left" },
              },
              {
                id: "row-right",
                contentKind: "Field",
                text: "",
                label: "Invoice No.",
                fieldPath: "Invoice.InvoiceNo",
                fallbackText: "",
                widthPercent: 42,
                style: { fontSizePt: 9, bold: true, align: "Right" },
                border: { color: "#111111", widthPx: 1, top: false, right: false, bottom: true, left: false },
              },
            ],
          },
          { id: "block-break", type: "PageBreak" },
        ],
      },
      {
        id: "section-footer",
        type: "Footer",
        print: {
          repeatOnEveryPage: true,
          keepTogether: true,
          pinToPageBottom: true,
          minHeightMm: 12,
        },
        blocks: [],
      },
    ],
  };

  const html = exportReportDesignerSchemaToHtml(schema);
  const disabledOutputSchema = JSON.parse(JSON.stringify(schema));
  disabledOutputSchema.sections[1].blocks[0].label = "Hidden Invoice Marker";
  disabledOutputSchema.sections[1].blocks[0].output = {
    enabled: false,
    note: "停用旧发票号字段",
  };
  const disabledOutputHtml = exportReportDesignerSchemaToHtml(disabledOutputSchema);
  const disabledOutputBodyHtml = disabledOutputHtml.slice(disabledOutputHtml.indexOf("-->"));
  const parsed = parseReportDesignerSchemaFromHtml(html);
  const normalizedSchema = normalizeReportDesignerSchema(schema).schema;
  const modelSummary = summarizeReportDesignerSchemaModel(schema);
  const detailModelBindings = collectReportDesignerBlockFieldBindings(schema.sections[1].blocks[1]);
  const conditionalModelBindings = collectReportDesignerBlockFieldBindings(schema.sections[1].blocks[2]);
  const legacySchema = JSON.parse(JSON.stringify(schema));
  legacySchema.version = 1;
  legacySchema.sections.forEach((section) => {
    delete section.print;
  });
  const migrated = parseReportDesignerSchemaFromHtml(\`<!-- EXPORTDOC_REPORT_DESIGNER_SCHEMA
\${JSON.stringify(legacySchema)}
-->\`);
  const schemaWithoutPrintSettings = JSON.parse(JSON.stringify(schema));
  delete schemaWithoutPrintSettings.sections[1].blocks[1].print;
  const parsedWithoutPrintSettings = parseReportDesignerSchemaFromHtml(\`<!-- EXPORTDOC_REPORT_DESIGNER_SCHEMA
\${JSON.stringify(schemaWithoutPrintSettings)}
-->\`);
  const validIssues = validateReportDesignerSchema(schema);
  const invalidSchema = JSON.parse(JSON.stringify(schema));
  invalidSchema.sections[1].blocks[0].fieldPath = "Invoice.InvoiceNo }}</span><script>alert(1)</script>";
  invalidSchema.sections[1].blocks[1].summaryRow.cells[0].fieldPath = "Invoice.TotalAmount }}</td><script>alert(2)</script>";
  invalidSchema.sections[1].blocks[1].grouping.fieldPath = "Invoice.Items.ProductNameEN }}<script>alert(6)</script>";
  invalidSchema.sections[1].blocks[1].grouping.footer.cells[0].fieldPath = "Invoice.Items.TotalPrice }}<script>alert(8)</script>";
  invalidSchema.sections[1].blocks[1].columns[0].content[0].fieldPath = "Invoice.Items.ProductNameEN }}<script>alert(7)</script>";
  invalidSchema.sections[1].blocks[2].condition.fieldPath = "Invoice.SpecialTerms }}<script>alert(3)</script>";
  invalidSchema.sections[1].blocks[3].fieldPath = "doc_seal_path }}<script>alert(4)</script>";
  invalidSchema.sections[1].blocks[4].url = "javascript:alert(5)";
  const invalidIssues = validateReportDesignerSchema(invalidSchema);
  const invalidHtml = exportReportDesignerSchemaToHtml(invalidSchema);
  const invalidGroupFooterSumSchema = JSON.parse(JSON.stringify(schema));
  invalidGroupFooterSumSchema.sections[1].blocks[1].grouping.footer.cells[0].fieldPath = "Invoice.TotalAmount";
  const invalidGroupFooterSumIssues = validateReportDesignerSchema(invalidGroupFooterSumSchema);
  const invalidPaymentSchema = {
    version: 2,
    reportType: "PaymentVoucher",
    page: schema.page,
    sections: [
      {
        id: "payment-body",
        type: "Body",
        print: {
          repeatOnEveryPage: false,
          keepTogether: false,
          pinToPageBottom: false,
        },
        blocks: [
          {
            id: "payment-field",
            type: "Field",
            label: "Bad invoice field",
            fieldPath: "Invoice.InvoiceNo",
            fallbackText: "",
            style: { fontSizePt: 9, align: "Left" },
          },
        ],
      },
    ],
  };
  const invalidPaymentIssues = validateReportDesignerSchema(invalidPaymentSchema);
  const invalidPlacementSchema = JSON.parse(JSON.stringify(schema));
  invalidPlacementSchema.sections[0].blocks.push(JSON.parse(JSON.stringify(schema.sections[1].blocks[1])));
  invalidPlacementSchema.sections[1].blocks = invalidPlacementSchema.sections[1].blocks.filter((block) => block.id !== "block-detail");
  const invalidPlacementIssues = validateReportDesignerSchema(invalidPlacementSchema);
  const reorderedSchema = JSON.parse(JSON.stringify(schema));
  const movedDetail = moveDetailTableColumn(reorderedSchema.sections[1].blocks[1], "col-amount", "up");
  const duplicatedDetail = duplicateDetailTableColumn(movedDetail, "col-amount");
  reorderedSchema.sections[1].blocks[1] = duplicatedDetail;
  const reorderedHtml = exportReportDesignerSchemaToHtml(reorderedSchema);
  const bodyHtml = reorderedHtml.slice(reorderedHtml.indexOf("</style>"));
  const detailBatchSource = JSON.parse(JSON.stringify(schema.sections[1].blocks[1]));
  const detailEqualized = distributeDetailTableColumnWidths(detailBatchSource);
  const detailResized = resizeAdjacentDetailTableColumnWidths(detailBatchSource, "col-name", 12);
  const detailResizeClamped = resizeAdjacentDetailTableColumnWidths(detailBatchSource, "col-name", 500);
  const detailBordered = applyDetailTableBorderToColumns({
    ...detailBatchSource,
    border: { color: "#224466", widthPx: 2, style: "Dashed", top: true, right: true, bottom: true, left: true },
  });
  const detailBorderCleared = clearDetailTableColumnBorders(detailBordered);
  const dragColumnSchema = JSON.parse(JSON.stringify(schema));
  dragColumnSchema.sections[1].blocks[1] = reorderDetailTableColumn(dragColumnSchema.sections[1].blocks[1], "col-name", "col-amount");
  const dragColumnHtml = exportReportDesignerSchemaToHtml(dragColumnSchema);
  const dragColumnBodyHtml = dragColumnHtml.slice(dragColumnHtml.indexOf("</style>"));
  const noRepeatSchema = JSON.parse(JSON.stringify(schema));
  noRepeatSchema.sections[1].blocks[1].print = {
    repeatHeaderOnPageBreak: false,
    keepRowsTogether: false,
  };
  const noRepeatHtml = exportReportDesignerSchemaToHtml(noRepeatSchema);
  const noFooterGroupPageBreakSchema = JSON.parse(JSON.stringify(schema));
  delete noFooterGroupPageBreakSchema.sections[1].blocks[1].grouping.footer;
  noFooterGroupPageBreakSchema.sections[1].blocks[1].grouping.pageBreakBefore = true;
  const noFooterGroupPageBreakHtml = exportReportDesignerSchemaToHtml(noFooterGroupPageBreakSchema);
  const equalsConditionSchema = JSON.parse(JSON.stringify(schema));
  equalsConditionSchema.sections[1].blocks[2].condition = {
    fieldPath: "Invoice.TradeTerm",
    operator: "Equals",
    value: "FOB",
  };
  const equalsConditionHtml = exportReportDesignerSchemaToHtml(equalsConditionSchema);
  const dragState = {
    schema: JSON.parse(JSON.stringify(schema)),
    selectedBlockId: "block-invoice-no",
    selectedSectionId: null,
  };
  const movedBlockState = moveBlockToDropTarget(dragState, "block-invoice-no", {
    sectionId: "section-footer",
    placement: "inside",
  });
  const insertedFieldState = insertBlockAtDropTarget(movedBlockState, createFieldBlock("Customer", "Customer.NameEN"), {
    sectionId: "section-body",
    blockId: "block-detail",
    placement: "before",
  });
  const insertedComponentState = insertBlockAtDropTarget(insertedFieldState, createTextBlock("Dragged note"), {
    sectionId: "section-body",
    blockId: "block-detail",
    placement: "after",
  });
  const bodyBlocksAfterDrop = insertedComponentState.schema.sections.find((section) => section.id === "section-body").blocks;
  const footerBlocksAfterDrop = insertedComponentState.schema.sections.find((section) => section.id === "section-footer").blocks;
  const blockedDetailMoveState = moveBlockToDropTarget({
    schema: JSON.parse(JSON.stringify(schema)),
    selectedBlockId: "block-detail",
    selectedSectionId: null,
  }, "block-detail", {
    sectionId: "section-footer",
    placement: "inside",
  });
  const blockedDetailBodyBlocks = blockedDetailMoveState.schema.sections.find((section) => section.id === "section-body").blocks;
  const blockedDetailFooterBlocks = blockedDetailMoveState.schema.sections.find((section) => section.id === "section-footer").blocks;
  const insertedDetailToHeaderState = insertBlockAtDropTarget({
    schema: JSON.parse(JSON.stringify(schema)),
    selectedBlockId: null,
    selectedSectionId: null,
  }, createDetailTableBlock(), {
    sectionId: "section-header",
    placement: "inside",
  });
  const insertedDetailHeaderBlocks = insertedDetailToHeaderState.schema.sections.find((section) => section.id === "section-header").blocks;
  const insertedDetailBodyBlocks = insertedDetailToHeaderState.schema.sections.find((section) => section.id === "section-body").blocks;
  const pageBreakRoutingSource = {
    schema: JSON.parse(JSON.stringify(schema)),
    selectedBlockId: null,
    selectedSectionId: "section-header",
  };
  const bodyPageBreaksBefore = pageBreakRoutingSource.schema.sections.find((section) => section.id === "section-body").blocks.filter((block) => block.type === "PageBreak").length;
  const pageBreakRoutedState = insertBlockAfterSelection(pageBreakRoutingSource, createPageBreakBlock());
  const pageBreakHeaderBlocks = pageBreakRoutedState.schema.sections.find((section) => section.id === "section-header").blocks;
  const pageBreakBodyBlocks = pageBreakRoutedState.schema.sections.find((section) => section.id === "section-body").blocks;
  const paymentGridBlock = createGridBlock("PaymentVoucher");
  paymentGridBlock.columns = Array.from({ length: 6 }, (_, index) => ({ id: \`grid-col-\${index + 1}\`, widthPercent: index === 0 ? 10 : 18 }));
  paymentGridBlock.rows = [
    {
      id: "grid-row-1",
      heightMm: 9,
      cells: [
        { id: "grid-cell-label", contentKind: "Text", text: "业务部门名称", fieldPath: "", colSpan: 1, rowSpan: 1, style: { fontSizePt: 9, bold: true, align: "Center" } },
        { id: "grid-cell-dept", contentKind: "Field", text: "", fieldPath: "Payment.Department", colSpan: 2, rowSpan: 1, style: { fontSizePt: 9, bold: true, align: "Center" } },
        { id: "grid-cell-method-label", contentKind: "Text", text: "付款方式", fieldPath: "", colSpan: 1, rowSpan: 1, style: { fontSizePt: 9, bold: true, align: "Center" } },
        {
          id: "grid-cell-method",
          contentKind: "CheckboxGroup",
          text: "",
          fieldPath: "Payment.PaymentMethod",
          colSpan: 2,
          rowSpan: 1,
          checkboxOptions: [
            { id: "opt-cheque", label: "支票", value: "支票" },
            { id: "opt-wire", label: "电汇", value: "电汇" },
          ],
          style: { fontSizePt: 9, align: "Left" },
        },
      ],
    },
    {
      id: "grid-row-2",
      heightMm: 12,
      cells: [
        { id: "grid-cell-side", contentKind: "Text", text: "用款事项", fieldPath: "", colSpan: 1, rowSpan: 2, verticalText: true, style: { fontSizePt: 9, bold: true, align: "Center" } },
        { id: "grid-cell-goods", contentKind: "Field", text: "", fieldPath: "Payment.GoodsName", colSpan: 2, rowSpan: 1, style: { fontSizePt: 9, align: "Center" } },
        { id: "grid-cell-invoice", contentKind: "Field", text: "", fieldPath: "Payment.InvoiceNo", colSpan: 3, rowSpan: 1, style: { fontSizePt: 9, align: "Center" } },
      ],
    },
    {
      id: "grid-row-3",
      heightMm: 9,
      cells: [
        { id: "grid-cell-upper", contentKind: "Field", text: "", fieldPath: "cny_amount_upper", colSpan: 3, rowSpan: 1, style: { fontSizePt: 10, bold: true, align: "Left" } },
        { id: "grid-cell-amount", contentKind: "Field", text: "", label: "小写", fieldPath: "Payment.CNYAmount", colSpan: 2, rowSpan: 1, style: { fontSizePt: 9, bold: true, align: "Right" } },
      ],
    },
  ];
  const paymentGridSchema = {
    version: 2,
    reportType: "PaymentVoucher",
    page: {
      size: "A5",
      orientation: "Landscape",
      marginTopMm: 8,
      marginRightMm: 8,
      marginBottomMm: 8,
      marginLeftMm: 8,
      fontFamily: "Microsoft YaHei, SimSun, Arial, sans-serif",
      fontSizePt: 9,
    },
    sections: [
      { id: "payment-header", type: "Header", print: { repeatOnEveryPage: true, keepTogether: true, pinToPageBottom: false }, blocks: [createTextBlock("工厂付款单")] },
      { id: "payment-body", type: "Body", print: { repeatOnEveryPage: false, keepTogether: false, pinToPageBottom: false }, blocks: [paymentGridBlock] },
      { id: "payment-footer", type: "Footer", print: { repeatOnEveryPage: false, keepTogether: true, pinToPageBottom: false }, blocks: [] },
    ],
  };
  const paymentGridHtml = exportReportDesignerSchemaToHtml(paymentGridSchema);
  const paymentGridIssues = validateReportDesignerSchema(paymentGridSchema);
  const exportSampleProfiles = getReportDesignerPreviewSampleProfiles("ExportDocument");
  const paymentSampleProfiles = getReportDesignerPreviewSampleProfiles("PaymentVoucher");
  const standardSampleHtml = renderReportDesignerLocalPreviewSample(html, "exportStandard");
  const longSampleHtml = renderReportDesignerLocalPreviewSample(html, "exportLongItems");
  const paymentSampleHtml = renderReportDesignerLocalPreviewSample(paymentGridHtml, "paymentVoucher");
  const gridEqualized = distributeGridColumnWidths(paymentGridBlock);
  const gridResized = resizeAdjacentGridColumnWidths(paymentGridBlock, "grid-col-1", 5);
  const gridResizeClamped = resizeAdjacentGridColumnWidths(paymentGridBlock, "grid-col-1", -30);
  const gridDefaultSource = {
    ...paymentGridBlock,
    defaultCellStyle: { fontSizePt: 11, bold: true, align: "Center" },
    border: { color: "#446622", widthPx: 2, style: "Dashed", top: true, right: true, bottom: true, left: true },
  };
  const gridStyled = applyGridDefaultCellStyle(gridDefaultSource);
  const gridBordered = applyGridBorderToCells(gridDefaultSource);
  const rowResized = resizeAdjacentRowColumnWidths(schema.sections[1].blocks[5].columns, "row-left", 7);
  const rowResizeClamped = resizeAdjacentRowColumnWidths(schema.sections[1].blocks[5].columns, "row-left", 300);

  return {
    hasEscapedText: html.includes("Invoice &lt;Draft&gt; &amp; Review"),
    hasEmbeddedSchemaComment: html.includes("EXPORTDOC_REPORT_DESIGNER_SCHEMA"),
    hasPageSize: html.includes("@page { size: 210mm 148mm; margin: 10mm 11mm 12mm 13mm; }"),
    hasPrintCss: html.includes("html, body { margin: 0; padding: 0; }") && html.includes("print-color-adjust: exact") && html.includes("display: table-header-group") && html.includes("page-break-inside: avoid") && html.includes("@media print"),
    hasSectionBandShell: html.includes('class="edm-report-page-table edm-report-repeat-header edm-report-repeat-footer edm-report-pin-footer"') &&
      html.includes('class="edm-report-section edm-report-section-header edm-report-section-keep-together"') &&
      html.includes('class="edm-report-section edm-report-section-footer edm-report-section-keep-together"'),
    hasSectionBandCss: html.includes(".edm-report-page-table.edm-report-repeat-header > thead { display: table-header-group; }") &&
      html.includes(".edm-report-page-table.edm-report-repeat-footer > tfoot { display: table-footer-group; }") &&
      html.includes(".edm-report-section-keep-together { break-inside: avoid; page-break-inside: avoid; }"),
    hasSectionBandMinHeight: html.includes('class="edm-report-section edm-report-section-header edm-report-section-keep-together" style="min-height: 18mm"') &&
      html.includes('class="edm-report-section edm-report-section-body" style="min-height: 80mm"') &&
      html.includes('class="edm-report-section edm-report-section-footer edm-report-section-keep-together" style="min-height: 12mm"'),
    hasDetailPrintOptions: html.includes('class="edm-detail-table edm-detail-repeat-header edm-detail-keep-rows"') &&
      html.includes('class="edm-detail-layout edm-detail-repeat-header"'),
    noRepeatPrintOptions: noRepeatHtml.includes('class="edm-detail-table edm-detail-no-repeat-header edm-detail-split-rows"') &&
      noRepeatHtml.includes('class="edm-detail-layout edm-detail-no-repeat-header"'),
    hasField: html.includes("{{ Invoice.InvoiceNo }}"),
    hasDetailLoop: html.includes("{{ for item in Invoice.Items }}") && html.includes("{{ item.ProductNameEN }}") && html.includes("{{ item.TotalPrice }}"),
    hasDetailCompositeCell: html.includes("{{ item.ProductNameEN }} / SKU: {{ item.Sku }}<br>{{ item.Quantity }} CTNS"),
    hasDetailHeaderGroup: html.includes('class="edm-detail-header-group-row"') &&
      html.includes('colspan="1"') &&
      html.includes("Quantities and Descriptions") &&
      html.includes(">Amount</th>"),
    hasDetailBorderStyles: html.includes("border-bottom: 1px dashed #333333") &&
      html.includes("border-top: 0; border-right: 0; border-bottom: 0; border-left: 0"),
    hasDetailGrouping: html.includes('class="edm-detail-group-row edm-detail-group-keep"') &&
      html.includes("Product Group") &&
      html.includes("{{ edm_current_group_block_detail = item.ProductNameEN }}") &&
      html.includes("{{ if edm_group_started_block_detail == false }}") &&
      html.includes("{{ edm_group_block_detail = edm_current_group_block_detail }}"),
    hasDetailGroupPageBreak: html.includes(".edm-detail-group-page-break { page-break-before: always; break-before: page; }") &&
      html.includes('class="edm-detail-group-row edm-detail-group-keep edm-detail-group-page-break"') &&
      html.includes("{{ if edm_current_group_block_detail != edm_group_block_detail }}") &&
      noFooterGroupPageBreakHtml.includes("{{ edm_group_started_block_detail = false }}") &&
      noFooterGroupPageBreakHtml.includes('class="edm-detail-group-row edm-detail-group-keep edm-detail-group-page-break"') &&
      !noFooterGroupPageBreakHtml.includes('class="edm-detail-group-footer-row"'),
    hasDetailGroupFooter: html.includes('class="edm-detail-group-footer-row"') &&
      html.includes("SUBTOTAL") &&
      html.includes("{{ edm_group_sum_block_detail_col_amount = 0 }}") &&
      html.includes("{{ edm_group_count_block_detail = edm_group_count_block_detail + 1 }}") &&
      html.includes("{{ edm_group_sum_block_detail_col_amount = edm_group_sum_block_detail_col_amount + item.TotalPrice }}") &&
      html.includes("{{ edm_group_sum_block_detail_col_amount }}") &&
      html.includes("{{ if edm_group_started_block_detail }}"),
    hasDetailSummaryRow: html.includes("edm-detail-summary-row") && html.includes('colspan="1"') && html.includes("TOTAL") && html.includes("{{ Invoice.TotalAmount }}"),
    hasDetailSideBand: html.includes("edm-detail-layout") && html.includes("唛头 Marks") && html.includes("{{ Invoice.ShippingMarks }}") && html.includes("Quantities and Descriptions"),
    hasDetailWidthsAndWrap: html.includes("width: 42mm") && html.includes("width: 126mm") && html.includes("overflow-wrap: anywhere") && html.includes("word-break: break-word"),
    hasConditionalBlock: html.includes("{{ if Invoice.SpecialTerms }}") &&
      html.includes("edm-conditional-block") &&
      html.includes("Show only when special terms &lt;exist&gt; &amp; ready") &&
      html.includes("{{ end }}"),
    hasEqualsConditionalBlock: equalsConditionHtml.includes('{{ if Invoice.TradeTerm == "FOB" }}'),
    hasFieldImageBlock: html.includes("{{ if doc_seal_path }}") &&
      html.includes('class="edm-image-block edm-image-keep-together"') &&
      html.includes('src="{{ doc_seal_path }}"') &&
      html.includes('alt="Seal &lt;safe&gt;"') &&
      html.includes("width: 42mm") &&
      html.includes("height: 24mm") &&
      html.includes("text-align: right"),
    hasStaticImageBlock: html.includes('class="edm-image-block"') &&
      html.includes('src="data:image/png;base64,iVBORw0KGgo="') &&
      html.includes('alt="Logo &amp; mark"') &&
      html.includes("width: 18mm") &&
      html.includes("height: auto"),
    hasRowBlock: html.includes('class="edm-report-row"') &&
      html.includes("Seller: ACME EXPORT") &&
      html.includes("width: 58%") &&
      html.includes("width: 42%") &&
      html.includes("Invoice No.: <span>{{ Invoice.InvoiceNo }}</span>") &&
      html.includes("border-top: 0; border-right: 0; border-bottom: 1px solid #111111; border-left: 0"),
    hasPageBreak: html.includes("page-break-before: always") && html.includes("break-before: page"),
    disabledOutputKeepsSchemaNote: disabledOutputHtml.includes('"note": "停用旧发票号字段"'),
    disabledOutputOmitsBlockHtml: !disabledOutputBodyHtml.includes("Hidden Invoice Marker"),
    modelSummaryReadsReportTypeAndPage: modelSummary.reportTypeLabel === "出口单据" &&
      modelSummary.pageLabel === "A5 横版" &&
      modelSummary.sectionCount === 3 &&
      modelSummary.blockCount === 8,
    modelSummaryCollectsDataSourcesAndBindings: modelSummary.dataSources.join("|") === "Invoice|Invoice.Items" &&
      modelSummary.fieldBindingCount >= 10,
    detailModelBindingsExposeDataSourceAndFields: detailModelBindings.some((binding) => binding.label === "明细数据源" && binding.fieldPath === "Invoice.Items") &&
      detailModelBindings.some((binding) => binding.fieldPath === "Invoice.Items.Sku") &&
      detailModelBindings.some((binding) => binding.label === "表尾合计" && binding.fieldPath === "Invoice.TotalAmount"),
    conditionalModelBindingsExposeConditionAndOutput: conditionalModelBindings.some((binding) => binding.label === "条件字段" && binding.fieldPath === "Invoice.SpecialTerms") &&
      conditionalModelBindings.length === 1,
    parsedText: parsed?.sections?.[0]?.blocks?.[0]?.text,
    roundtrippedSchemaSame: JSON.stringify(parsed) === JSON.stringify(normalizedSchema),
    migratedVersion: migrated?.version,
    migratedLegacyHeaderRepeat: migrated?.sections?.[0]?.print?.repeatOnEveryPage,
    migratedLegacyFooterPin: migrated?.sections?.[2]?.print?.pinToPageBottom,
    migratedLegacyHeaderHeightMissing: migrated?.sections?.[0]?.print?.minHeightMm === undefined,
    missingPrintDefaults: parsedWithoutPrintSettings?.sections?.[1]?.blocks?.[1]?.print?.repeatHeaderOnPageBreak === true &&
      parsedWithoutPrintSettings?.sections?.[1]?.blocks?.[1]?.print?.keepRowsTogether === true,
    validIssueCount: validIssues.length,
    invalidIssuesBlocked: hasBlockingReportDesignerSchemaIssues(invalidIssues),
    invalidGroupFooterSumBlocked: hasBlockingReportDesignerSchemaIssues(invalidGroupFooterSumIssues),
    invalidPaymentDomainBlocked: hasBlockingReportDesignerSchemaIssues(invalidPaymentIssues),
    invalidPlacementBlocked: hasBlockingReportDesignerSchemaIssues(invalidPlacementIssues),
    invalidHtmlIsSanitized: !invalidHtml.includes("<script>") &&
      !invalidHtml.includes("alert(1)") &&
      !invalidHtml.includes("alert(2)") &&
      !invalidHtml.includes("alert(3)") &&
      !invalidHtml.includes("alert(4)") &&
      !invalidHtml.includes("javascript:alert(5)") &&
      !invalidHtml.includes("alert(6)") &&
      !invalidHtml.includes("alert(7)") &&
      !invalidHtml.includes("alert(8)"),
    movedColumnExportsBeforeOriginal: bodyHtml.indexOf(">Amount<") >= 0 && bodyHtml.indexOf(">Amount<") < bodyHtml.indexOf(">Product<"),
    dragReorderedColumnExportsBeforeOriginal: dragColumnBodyHtml.indexOf(">Product<") >= 0 &&
      dragColumnBodyHtml.indexOf(">Product<") > dragColumnBodyHtml.indexOf(">Amount<"),
    duplicatedColumnHasFreshSummaryCell: duplicatedDetail.columns.length === 3 &&
      new Set(duplicatedDetail.columns.map((column) => column.id)).size === 3 &&
      duplicatedDetail.summaryRow.cells.some((cell) => cell.columnId === duplicatedDetail.columns[1].id && cell.contentKind === "Empty") &&
      duplicatedDetail.grouping.footer.cells.some((cell) => cell.columnId === duplicatedDetail.columns[1].id && cell.contentKind === "Empty"),
    detailBatchEqualizesColumnWidths: new Set(detailEqualized.columns.map((column) => column.widthMm)).size === 1 &&
      detailEqualized.columns[0].widthMm === 46,
    detailAdjacentResizePreservesTotal: detailResized.columns[0].widthMm === 76 &&
      detailResized.columns[1].widthMm === 16 &&
      detailResized.columns.reduce((sum, column) => sum + column.widthMm, 0) === 92,
    detailAdjacentResizeClampsMinimum: detailResizeClamped.columns[0].widthMm === 84 &&
      detailResizeClamped.columns[1].widthMm === 8,
    detailBatchAppliesColumnBorders: detailBordered.columns.every((column) =>
      column.border?.color === "#224466" &&
      column.border?.widthPx === 2 &&
      column.border?.style === "Dashed" &&
      column.border?.top &&
      column.border?.right &&
      column.border?.bottom &&
      column.border?.left,
    ),
    detailBatchClearsColumnBorderOverrides: detailBorderCleared.columns.every((column) => !("border" in column)),
    hasPaymentGridTable: paymentGridHtml.includes("edm-report-grid"),
    hasPaymentGridColspan: paymentGridHtml.includes('colspan="3"'),
    hasPaymentGridRowspan: paymentGridHtml.includes('rowspan="2"'),
    hasPaymentGridVerticalText: paymentGridHtml.includes("writing-mode: vertical-rl"),
    hasPaymentGridCheckboxScriban: paymentGridHtml.includes('Payment.PaymentMethod == "支票"'),
    hasPaymentGridAmountUpper: paymentGridHtml.includes("{{ cny_amount_upper }}"),
    paymentGridDomainValid: !hasBlockingReportDesignerSchemaIssues(paymentGridIssues),
    hasPreviewSampleProfiles: exportSampleProfiles.map((profile) => profile.value).join("|") === "apiSample|exportStandard|exportLongItems" &&
      paymentSampleProfiles.map((profile) => profile.value).join("|") === "apiSample|paymentVoucher",
    standardSampleRendersInvoiceValues: standardSampleHtml.includes("INV-STD-2026-0707") &&
      standardSampleHtml.includes("Sample product 01") &&
      !standardSampleHtml.includes("{{"),
    longSampleRendersManyRows: longSampleHtml.includes("INV-LONG-2026-0707") &&
      longSampleHtml.includes("Sample product 72") &&
      !longSampleHtml.includes("{{"),
    paymentSampleRendersVoucherValues: paymentSampleHtml.includes("外贸业务部") &&
      paymentSampleHtml.includes("电汇") &&
      paymentSampleHtml.includes("人民币壹万贰仟叁佰肆拾伍元陆角柒分") &&
      !paymentSampleHtml.includes("{{"),
    gridBatchEqualizesColumnWidths: new Set(gridEqualized.columns.map((column) => column.widthPercent)).size === 1 &&
      gridEqualized.columns[0].widthPercent === 16.7,
    gridAdjacentResizePreservesTotal: gridResized.columns[0].widthPercent === 15 &&
      gridResized.columns[1].widthPercent === 13 &&
      gridResized.columns.slice(0, 2).reduce((sum, column) => sum + column.widthPercent, 0) === 28,
    gridAdjacentResizeClampsMinimum: gridResizeClamped.columns[0].widthPercent === 1 &&
      gridResizeClamped.columns[1].widthPercent === 27,
    gridBatchAppliesDefaultCellStyle: gridStyled.rows.every((row) =>
      row.cells.every((cell) => cell.style.fontSizePt === 11 && cell.style.bold === true && cell.style.align === "Center"),
    ),
    gridBatchAppliesCellBorders: gridBordered.rows.every((row) =>
      row.cells.every((cell) => cell.border?.color === "#446622" && cell.border?.widthPx === 2 && cell.border?.style === "Dashed"),
    ),
    rowAdjacentResizePreservesNormalizedTotal: rowResized[0].widthPercent === 65 &&
      rowResized[1].widthPercent === 35 &&
      Math.round(rowResized.reduce((sum, column) => sum + column.widthPercent, 0) * 10) / 10 === 100,
    rowAdjacentResizeClampsMinimum: rowResizeClamped[0].widthPercent === 99 &&
      rowResizeClamped[1].widthPercent === 1,
    movedBlockAcrossSections: footerBlocksAfterDrop.some((block) => block.id === "block-invoice-no") &&
      !bodyBlocksAfterDrop.some((block) => block.id === "block-invoice-no"),
    insertedDropBlocksKeepTargetOrder: bodyBlocksAfterDrop.findIndex((block) => block.fieldPath === "Customer.NameEN") <
      bodyBlocksAfterDrop.findIndex((block) => block.id === "block-detail") &&
      bodyBlocksAfterDrop.findIndex((block) => block.text === "Dragged note") >
      bodyBlocksAfterDrop.findIndex((block) => block.id === "block-detail"),
    detailTablePlacementIsProtected: blockedDetailBodyBlocks.some((block) => block.id === "block-detail") &&
      !blockedDetailFooterBlocks.some((block) => block.id === "block-detail") &&
      insertedDetailHeaderBlocks.filter((block) => block.type === "DetailTable").length === 0 &&
      insertedDetailBodyBlocks.filter((block) => block.type === "DetailTable").length === 2,
    pageBreakPlacementIsRoutedToBody: pageBreakHeaderBlocks.filter((block) => block.type === "PageBreak").length === 0 &&
      pageBreakBodyBlocks.filter((block) => block.type === "PageBreak").length === bodyPageBreaksBefore + 1,
  };
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

async function run() {
  await buildBundle();
  const result = spawnSync(process.execPath, [bundlePath], {
    encoding: "utf8",
    timeout: 30000,
    windowsHide: true,
  });

  if (result.status !== 0) {
    throw new Error(`Exporter bundle failed:\n${result.stdout || ""}\n${result.stderr || ""}`);
  }

  const module = await import(pathToFileURL(bundlePath).href);
  const output = module.run();
  assert(output.hasEscapedText, "Expected text block HTML to be escaped");
  assert(output.hasEmbeddedSchemaComment, "Expected exporter to embed report designer schema comment");
  assert(output.hasPageSize, "Expected A5 landscape page and margin CSS");
  assert(output.hasPrintCss, "Expected Chromium-oriented print CSS output");
  assert(output.hasSectionBandShell, "Expected section band shell and classes to be exported");
  assert(output.hasSectionBandCss, "Expected section band print CSS output");
  assert(output.hasSectionBandMinHeight, "Expected report section band min-height to be exported");
  assert(output.hasDetailPrintOptions, "Expected detail table print settings to export as print CSS classes");
  assert(output.noRepeatPrintOptions, "Expected detail table print settings to allow disabling repeated headers and row keep-together");
  assert(output.hasField, "Expected field block Scriban output");
  assert(output.hasDetailLoop, "Expected detail table Scriban loop output");
  assert(output.hasDetailCompositeCell, "Expected detail table composite cell output");
  assert(output.hasDetailHeaderGroup, "Expected detail table grouped header output");
  assert(output.hasDetailBorderStyles, "Expected detail table dashed and no-border output");
  assert(output.hasDetailGrouping, "Expected detail table group header Scriban output");
  assert(output.hasDetailGroupPageBreak, "Expected detail table groups to support controlled page breaks before later groups");
  assert(output.hasDetailGroupFooter, "Expected detail table group footer subtotal Scriban output");
  assert(output.hasDetailSummaryRow, "Expected detail table summary row output");
  assert(output.hasDetailSideBand, "Expected detail table non-repeating side band output");
  assert(output.hasDetailWidthsAndWrap, "Expected detail side band widths and wrapping CSS output");
  assert(output.hasConditionalBlock, "Expected conditional block Scriban output");
  assert(output.hasEqualsConditionalBlock, "Expected conditional block equality output");
  assert(output.hasFieldImageBlock, "Expected field-backed image/seal block output");
  assert(output.hasStaticImageBlock, "Expected static image block output");
  assert(output.hasRowBlock, "Expected two-column row block with single-side border output");
  assert(output.hasPageBreak, "Expected print page-break CSS output");
  assert(output.disabledOutputKeepsSchemaNote, "Expected disabled output metadata to remain in embedded schema");
  assert(output.disabledOutputOmitsBlockHtml, "Expected disabled blocks to be omitted from exported report HTML");
  assert(output.modelSummaryReadsReportTypeAndPage, "Expected report model summary to expose report type, page, sections and blocks");
  assert(output.modelSummaryCollectsDataSourcesAndBindings, "Expected report model summary to collect data sources and field bindings");
  assert(output.detailModelBindingsExposeDataSourceAndFields, "Expected detail table model bindings to expose data source, item fields and summary fields");
  assert(output.conditionalModelBindingsExposeConditionAndOutput, "Expected conditional block model bindings to expose condition/output fields");
  assert(output.parsedText === "Invoice <Draft> & Review", "Expected embedded schema to roundtrip through parser");
  assert(output.roundtrippedSchemaSame, "Expected exported schema comment to parse back to the same schema");
  assert(output.migratedVersion === 2, "Expected parser to migrate legacy schema comments to v2");
  assert(output.migratedLegacyHeaderRepeat === false, "Expected legacy v1 headers to preserve non-repeating output by default");
  assert(output.migratedLegacyFooterPin === false, "Expected legacy v1 footers to preserve non-pinned output by default");
  assert(output.migratedLegacyHeaderHeightMissing, "Expected legacy v1 section bands to preserve missing min-height by default");
  assert(output.missingPrintDefaults, "Expected parser to default missing detail table print settings");
  assert(output.validIssueCount === 0, "Expected fixture schema to have no validation issues");
  assert(output.invalidIssuesBlocked, "Expected invalid field paths to block schema validation");
  assert(output.invalidGroupFooterSumBlocked, "Expected group footer sums to reject non-detail fields");
  assert(output.invalidPaymentDomainBlocked, "Expected payment templates to reject export-document field paths");
  assert(output.invalidPlacementBlocked, "Expected report model placement rules to block detail bands outside the body section");
  assert(output.invalidHtmlIsSanitized, "Expected invalid field paths to be sanitized during export");
  assert(output.movedColumnExportsBeforeOriginal, "Expected moved detail table column to change exported HTML order");
  assert(output.dragReorderedColumnExportsBeforeOriginal, "Expected drag-style detail table column reorder to change exported HTML order");
  assert(output.duplicatedColumnHasFreshSummaryCell, "Expected duplicated detail table column to receive a fresh id and empty summary cell");
  assert(output.detailBatchEqualizesColumnWidths, "Expected detail table batch layout action to equalize column widths");
  assert(output.detailAdjacentResizePreservesTotal, "Expected detail table visual width resize to preserve total mm width");
  assert(output.detailAdjacentResizeClampsMinimum, "Expected detail table visual width resize to clamp adjacent columns to the minimum width");
  assert(output.detailBatchAppliesColumnBorders, "Expected detail table batch layout action to apply table border to all columns");
  assert(output.detailBatchClearsColumnBorderOverrides, "Expected detail table batch layout action to clear column border overrides");
  assert(output.hasPaymentGridTable, "Expected fixed ticket grid to export as a table");
  assert(output.hasPaymentGridColspan, "Expected fixed ticket grid to support colspan");
  assert(output.hasPaymentGridRowspan, "Expected fixed ticket grid to support rowspan");
  assert(output.hasPaymentGridVerticalText, "Expected fixed ticket grid to support vertical text");
  assert(output.hasPaymentGridCheckboxScriban, "Expected fixed ticket grid checkbox group to export Scriban equality checks");
  assert(output.hasPaymentGridAmountUpper, "Expected fixed ticket grid to export upper-case amount field");
  assert(output.paymentGridDomainValid, "Expected fixed ticket grid to pass payment field-domain validation");
  assert(output.hasPreviewSampleProfiles, "Expected preview panel sample profile choices to match report type");
  assert(output.standardSampleRendersInvoiceValues, "Expected standard export-document sample preview to render invoice and detail values locally");
  assert(output.longSampleRendersManyRows, "Expected long export-document sample preview to render many local detail rows");
  assert(output.paymentSampleRendersVoucherValues, "Expected payment voucher sample preview to render payment values locally");
  assert(output.gridBatchEqualizesColumnWidths, "Expected fixed ticket grid batch action to equalize column widths");
  assert(output.gridAdjacentResizePreservesTotal, "Expected fixed ticket grid visual width resize to preserve adjacent percentage total");
  assert(output.gridAdjacentResizeClampsMinimum, "Expected fixed ticket grid visual width resize to clamp adjacent columns to the minimum percentage");
  assert(output.gridBatchAppliesDefaultCellStyle, "Expected fixed ticket grid batch action to apply default style to all cells");
  assert(output.gridBatchAppliesCellBorders, "Expected fixed ticket grid batch action to apply table border to all cells");
  assert(output.rowAdjacentResizePreservesNormalizedTotal, "Expected row visual width resize to keep normalized percentage total");
  assert(output.rowAdjacentResizeClampsMinimum, "Expected row visual width resize to clamp adjacent columns to the minimum percentage");
  assert(output.movedBlockAcrossSections, "Expected drag-style block move to support moving blocks across report sections");
  assert(output.insertedDropBlocksKeepTargetOrder, "Expected drag-style inserts to preserve before/after target order");
  assert(output.detailTablePlacementIsProtected, "Expected detail tables to remain in or route to the report body section");
  assert(output.pageBreakPlacementIsRoutedToBody, "Expected page breaks inserted from a selected header to route into the body section");
  process.stdout.write("new-report-designer-exporter test passed\n");
}

run().catch((error) => {
  process.stderr.write(`${error.stack || error.message}\n`);
  process.exit(1);
});
