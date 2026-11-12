import fs from "node:fs/promises";
import path from "node:path";

// Authoring-only adapter: the caller supplies the spreadsheet toolkit. It is not
// a desktop/runtime dependency; business coordinates live in the existing XLSX/JSON.
export async function buildExcelTemplates({ Workbook, SpreadsheetFile, FileBlob }, root, output) {
  const directory = path.join(root, "Resources/ExcelTemplates");
  const layout = JSON.parse(await fs.readFile(path.join(directory, "single-window-coo-layout.json"), "utf8"));
  const tokens = await fs.readFile(path.join(root, "apps/export-doc-web/src/styles/tokens/color.css"), "utf8");
  const color = name => {
    const value = tokens.match(new RegExp(`--edm-${name}:\\s*(#[0-9a-f]{6});`, "i"))?.[1];
    if (!value) throw new Error(`Missing shared color token: ${name}`);
    return value;
  };
  const palette = { ink: color("ink-900"), primary: color("ink-900"), heading: color("ink-800"), muted: color("ink-600"), border: color("neutral-300"), input: color("warning-50"), soft: color("ink-100") };
  const typeTokens = await fs.readFile(path.join(root, "apps/export-doc-web/src/styles/tokens/type.css"), "utf8");
  const fontSize = name => {
    const pixels = typeTokens.match(new RegExp(`--edm-font-size-${name}:\\s*(\\d+)px;`))?.[1];
    if (!pixels) throw new Error(`Missing shared font size: ${name}`);
    return Number(pixels) * 72 / 96;
  };
  const fontName = typeTokens.match(/--edm-font-sans:\s*"([^"]+)"/)?.[1];
  if (!fontName) throw new Error("Missing shared font family.");
  const typography = { name: fontName, size: fontSize("md"), color: palette.ink };
  const heading = { fill: palette.primary, font: { ...typography, color: "#FFFFFF", size: fontSize("xl"), bold: true }, rowHeight: 24 };
  await fs.mkdir(output, { recursive: true });
  const save = async (workbook, name) => {
    const destination = path.join(directory, name);
    await (await SpreadsheetFile.exportXlsx(workbook)).save(destination);
    // The authoring toolkit emits an inspection sidecar; keep it with evidence.
    try { await fs.rename(`${destination}.inspect.ndjson`, path.join(output, `${name}.inspect.ndjson`)); }
    catch (error) { if (error.code !== "ENOENT") throw error; }
  };
  const render = async (book, sheetName, range, file) => {
    const preview = await book.render({ sheetName, range, scale: 1, format: "png" });
    await fs.writeFile(path.join(output, `${file}.png`), new Uint8Array(await preview.arrayBuffer()));
  };
  const book = Workbook.create();
  const base = book.worksheets.add("基本资料");
  const items = book.worksheets.add("商品明细");
  const producers = book.worksheets.add("生产企业");
  for (const sheet of [base, items, producers]) {
    sheet.showGridLines = false;
    sheet.getRange(`A1:${sheet === base ? "C22" : "K103"}`).format = {
      font: typography, verticalAlignment: "center", rowHeight: 18,
    };
  }
  base.getRange("A1:C1").merge();
  base.getRange("A1").values = [[layout.marker]];
  base.getRange("A1:C1").format = heading;
  base.getRange("A2:C2").merge();
  base.getRange("A2").values = [["浅黄色为填写区；可先导入草稿，再按所选证书补齐申报资料。"]];
  base.getRange("A2:C2").format = { font: { size: fontSize("sm"), color: palette.muted }, rowHeight: 20 };
  const end = layout.headers.length + 2;
  base.getRange(`A3:C${end}`).values = layout.headers.map(field => [field.label, null, field.note ?? (field.format === "date" ? "YYYY-MM-DD" : "")]);
  for (const [column, width] of [["A", 155], ["B", 340], ["C", 480]]) base.getRange(`${column}1:${column}${end}`).format.columnWidthPx = width;
  base.getRange(`A3:A${end}`).format = { font: { bold: true }, fill: palette.soft };
  base.getRange(`B3:B${end}`).format = { fill: palette.input, numberFormat: "@" };
  base.getRange(`C3:C${end}`).format.font = { size: fontSize("sm"), color: palette.muted };
  base.getRange(`A3:C${end}`).format = { wrapText: true, borders: { insideHorizontal: { style: "thin", color: palette.border } } };
  for (const [index, field] of layout.headers.entries()) {
    if (field.format === "date") base.getCell(index + 2, 1).setNumberFormat("yyyy-mm-dd");
    if (/Address|shippingMarks|transDetails|producer$/.test(field.key) || field.note?.length > 35) base.getCell(index + 2, 1).format.rowHeight = 30;
  }
  function table(sheet, fields, title, note, name) {
    sheet.getRangeByIndexes(0, 0, 1, fields.length).merge();
    sheet.getRange("A1").values = [[title]];
    sheet.getRangeByIndexes(0, 0, 1, fields.length).format = heading;
    sheet.getRangeByIndexes(1, 0, 1, fields.length).merge();
    sheet.getRange("A2").values = [[note]];
    sheet.getRangeByIndexes(1, 0, 1, fields.length).format = { font: { color: palette.muted, size: fontSize("sm") }, rowHeight: 20 };
    sheet.getRangeByIndexes(2, 0, 1, fields.length).values = [fields.map(field => field.label)];
    for (const [column, field] of fields.entries()) {
      const range = sheet.getRangeByIndexes(2, column, 101, 1);
      range.format.columnWidthPx = /Address|goodsDesc/.test(field.key) ? 200 : /Name|Email/.test(field.key) ? 140 : /producerKey|Criteria|Tel|Fax/.test(field.key) ? 105 : 85;
      range.format.numberFormat = field.format === "number" ? "#,##0.#####" : "@";
      sheet.getRangeByIndexes(3, column, 100, 1).format.horizontalAlignment = field.format === "number" ? "right" : "left";
    }
    sheet.getRangeByIndexes(2, 0, 1, fields.length).format = { fill: palette.heading, font: { color: "#FFFFFF", bold: true }, wrapText: true, horizontalAlignment: "center", rowHeight: 30 };
    sheet.getRangeByIndexes(3, 0, 100, fields.length).format = { fill: palette.input, borders: { insideHorizontal: { style: "thin", color: palette.border } } };
    sheet.freezePanes.freezeRows(3);
    sheet.tables.add(`A3:${String.fromCharCode(64 + fields.length)}103`, true, name);
  }
  table(items, layout.items, "商品明细", "从第 4 行填写。原产地标准、证书描述等申报资料，由操作员导入后在程序中补齐。", "CooGoods");
  table(producers, layout.producers, "生产企业", "每家企业填写一次，通过企业编号关联商品；组织机构代码按官方备案填写。", "CooProducers");
  producers.getRange("K4:K103").dataValidation = { rule: { type: "list", values: ["N", "Y"] } };
  book.recalculate();
  await render(book, "基本资料", `A1:C${end}`, "coo-basic");
  await render(book, "商品明细", "A1:K10", "coo-items");
  await render(book, "生产企业", "A1:K10", "coo-producers");
  await save(book, "single-window-coo-template.xlsx");

  const invoicePath = path.join(directory, "invoice-import-template.xlsx");
  const invoice = await SpreadsheetFile.importXlsx(await FileBlob.load(invoicePath));
  const sheet = invoice.worksheets.getItem("明细单");
  sheet.showGridLines = false;
  sheet.getRange("A1:X61").format = { font: typography, verticalAlignment: "center" };
  sheet.getRange("A1:X1").format = heading;
  sheet.getRange("A2:X2").format = { fill: palette.soft, font: { size: fontSize("lg"), bold: true }, rowHeight: 22 };
  sheet.getRange("A3:X17").format = { rowHeight: 16, borders: { preset: "all", style: "thin", color: palette.border } };
  for (const area of ["A3:A17", "M3:N17", "S3:X3"]) sheet.getRange(area).format = { fill: palette.soft, font: { bold: true, size: fontSize("sm") }, wrapText: true };
  for (const area of ["B3:L17", "O3:R17", "S4:X17", "A20:X60"]) sheet.getRange(area).format.fill = palette.input;
  sheet.getRange("A18:X19").format = { fill: palette.heading, font: { bold: true, color: "#FFFFFF", size: fontSize("sm") }, wrapText: true, horizontalAlignment: "center", rowHeight: 22 };
  for (const area of ["K18:L19", "N18:N19", "S19", "U19", "A3", "A8", "A13"]) {
    sheet.getRange(area).format = { font: { size: fontSize("xs") }, wrapText: false };
  }
  for (const [address, value] of [["B19", "PO No."], ["K19", "Unit EN"], ["L19", "Unit CN"], ["N19", "Pkg unit"], ["S19", "G.W./CTN"], ["U19", "N.W./CTN"]]) {
    sheet.getRange(address).values = [[value]];
    sheet.getRange(address).format.wrapText = false;
  }
  sheet.getRange("A20:X60").format.rowHeight = 17;
  sheet.getRange("A20:X61").format.borders = { preset: "all", style: "thin", color: palette.border };
  for (let row = 21; row <= 60; row += 2) sheet.getRange(`B${row}:X${row}`).format.fill = "#FFFFFF";
  sheet.getRange("A61:X61").format = { fill: palette.soft, font: { bold: true }, rowHeight: 20 };
  const widths = [145,70,70,160,120,130,70,80,80,65,55,55,55,55,32,32,32,60,55,65,55,65,65,75];
  for (const [column, width] of widths.entries()) sheet.getRangeByIndexes(0, column, 61, 1).format.columnWidthPx = width;
  sheet.freezePanes.unfreeze();
  sheet.freezePanes.freezeRows(2);
  invoice.recalculate();
  await render(invoice, "明细单", "A1:X61", "invoice-compact");
  await save(invoice, "invoice-import-template.xlsx");
}
