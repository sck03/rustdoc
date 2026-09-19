use super::{Builtin, check, commercial_footer, commercial_header};
use crate::{
    Document, ReportData, Result,
    canvas::Canvas,
    data::{decimal, format_decimal, value_at},
    error::invalid,
};
use std::sync::atomic::AtomicBool;

pub(super) const LEFT: f32 = 15.3;
pub(super) const WIDTH: f32 = 179.4;

struct Row {
    cells: Vec<String>,
    height: f32,
}

fn quantities(
    data: &ReportData,
    item: &serde_json::Value,
    quantity: &str,
    unit: &str,
) -> Result<String> {
    let number = decimal(value_at(item, quantity))?;
    Ok(if number > rust_decimal::Decimal::ZERO {
        format!("{}{}", number.normalize(), data.item_text(item, unit))
    } else {
        String::new()
    })
}
fn positive(
    data: &ReportData,
    item: &serde_json::Value,
    field: &str,
    precision: u32,
) -> Result<String> {
    if decimal(value_at(item, field))? <= rust_decimal::Decimal::ZERO {
        Ok(String::new())
    } else {
        data.item_number(item, field, precision)
    }
}
fn item_rows(template: Builtin, data: &ReportData, widths: &[f32]) -> Result<Vec<Row>> {
    data.items()
        .iter()
        .map(|item| {
            let text = |field| data.item_text(item, field);
            let price = decimal(value_at(item, "UnitPrice"))?;
            let price = format_decimal(price, price.normalize().scale().clamp(2, 6));
            let currency = data.text("Invoice.Currency");
            let quantity = quantities(data, item, "Quantity", "UnitEN")?;
            let cartons = quantities(data, item, "Cartons", "CtnUnitEN")?;
            let amount = format!("{currency}{}", data.item_number(item, "TotalPrice", 2)?);
            let cells = match template {
                Builtin::Invoice => vec![
                    text("StyleName"),
                    text("PoNumber"),
                    text("StyleNo"),
                    cartons,
                    quantity,
                    format!("@{currency}{price}"),
                    amount,
                ],
                Builtin::PackingList => vec![
                    [text("StyleName"), text("StyleNo"), text("PoNumber")]
                        .into_iter()
                        .filter(|v| !v.is_empty())
                        .collect::<Vec<_>>()
                        .join("\n"),
                    cartons,
                    quantity,
                    positive(data, item, "GWTotal", 2)?,
                    positive(data, item, "NWTotal", 2)?,
                    positive(data, item, "Volume", 3)?,
                ],
                _ => vec![
                    format!("{} {}", text("StyleNo"), text("StyleName")),
                    quantity,
                    format!("{currency}{price}"),
                    amount,
                ],
            };
            let height = if template == Builtin::Invoice {
                let description = Canvas::text_height(&cells[0], widths[1] - 5., 6.)
                    + if cells[1].is_empty() {
                        0.
                    } else {
                        Canvas::text_height(&cells[1], widths[1] - 5., 6.) + 0.4
                    };
                let details = cells[2..6]
                    .iter()
                    .zip([26., 16., 16., widths[1] - 63.])
                    .map(|(text, width)| Canvas::text_height(text, width, 6.))
                    .fold(0., f32::max);
                (description + details + 5.)
                    .max(Canvas::text_height(&cells[6], widths[2] - 4., 6.) + 4.)
            } else {
                cells
                    .iter()
                    .zip(if template == Builtin::PackingList {
                        &widths[1..]
                    } else {
                        widths
                    })
                    .map(|(text, width)| {
                        Canvas::text_height(
                            text,
                            width - 2.,
                            if template == Builtin::PackingList {
                                6.375
                            } else {
                                7.5
                            },
                        ) + if template == Builtin::PackingList {
                            2.
                        } else {
                            4.
                        }
                    })
                    .fold(8.3, f32::max)
            };
            Ok(Row { cells, height })
        })
        .collect()
}

pub fn render(template: Builtin, data: &ReportData, cancelled: &AtomicBool) -> Result<Document> {
    let (headings, widths) = match template {
        Builtin::Invoice => (
            vec![
                "唛头 / Marks",
                "货品名称 / Quantities and Descriptions",
                "总值 / Amount",
            ],
            vec![WIDTH * 0.25, WIDTH * 0.60, WIDTH * 0.15],
        ),
        Builtin::PackingList => (
            vec![
                "唛头\nSHIPPING MARKS",
                "货品说明\nDESCRIPTION OF GOODS",
                "包装\nPACKAGE",
                "数量\nQUANTITY",
                "毛重\nGROSS WEIGHT",
                "净重\nNET WEIGHT",
                "体积\nMEAS.",
            ],
            vec![
                WIDTH * 0.12,
                WIDTH * 0.38,
                WIDTH * 0.1,
                WIDTH * 0.1,
                WIDTH * 0.1,
                WIDTH * 0.1,
                WIDTH * 0.1,
            ],
        ),
        _ => (
            vec![
                "Name of Commodity, Specification, Packing and Shipping Marks",
                "Quantity",
                "Unit Price",
                "Total Amount",
            ],
            vec![111., 20., 21., WIDTH - 152.],
        ),
    };
    let packing = template == Builtin::PackingList;
    let contract = template == Builtin::Contract;
    let rows = item_rows(template, data, &widths)?;
    let header_height = if packing { 8. } else { 8.5 };
    let trade_height = if template == Builtin::Invoice { 7. } else { 0. };
    let mut sample = Canvas::new(210., 297.);
    let first_top = commercial_header::draw(&mut sample, data, template, true)?;
    let capacity = 246. - first_top - header_height - trade_height;
    let mut groups: Vec<Vec<usize>> = vec![vec![]];
    let mut used = 0.;
    for (index, row) in rows.iter().enumerate() {
        check(cancelled)?;
        let available = if contract && groups.len() > 1 {
            246. - 16. - header_height
        } else {
            capacity
        };
        if row.height > available {
            return Err(invalid(
                "商品描述超过单页空间，请调整列宽或使用自定义模板。",
            ));
        }
        if used + row.height > available || (!contract && groups.last().unwrap().len() >= 12) {
            groups.push(vec![]);
            used = 0.;
        }
        groups.last_mut().unwrap().push(index);
        used += row.height;
        if groups.len() > 500 {
            return Err(invalid("报表页数超过 500 页。"));
        }
    }
    let mut pages = vec![];
    for (page_index, group) in groups.iter().enumerate() {
        check(cancelled)?;
        let mut canvas = Canvas::new(210., 297.);
        let top = commercial_header::draw(&mut canvas, data, template, page_index == 0)?;
        let mut x = LEFT;
        for (heading, width) in headings.iter().zip(&widths) {
            canvas.rect(x, top, *width, header_height, "#f2f2f2", 0.);
            if packing {
                let (cn, en) = heading.split_once('\n').unwrap();
                canvas.text(cn, x + 0.5, top + 1., width - 1., 6.375, true, "Center");
                canvas.text(en, x + 0.5, top + 4., width - 1., 5.1, true, "Center");
            } else {
                canvas.label(
                    heading,
                    x + 2.,
                    top + 1.,
                    width - 4.,
                    header_height - 2.,
                    if contract { 7.5 } else { 9. },
                    true,
                    "Left",
                )?;
            }
            x += width;
        }
        canvas.rect(LEFT, top, WIDTH, header_height, "none", 0.2);
        let mut x = LEFT;
        for width in widths.iter().take(widths.len() - 1) {
            x += width;
            if !packing || (x - LEFT - widths[0]).abs() < 0.1 {
                canvas.line(x, top, x, top + header_height, 0.2);
            }
        }
        let row_top = top + header_height;
        let mut table_height =
            group.iter().map(|index| rows[*index].height).sum::<f32>() + trade_height;
        let mark_width = widths[0] - 3.;
        let mark = if packing && data.text("Invoice.ShippingMarks").trim().is_empty() {
            "N/M".into()
        } else {
            data.text("Invoice.ShippingMarks")
        };
        let mark_height = if data.images.contains_key("Invoice.ShippingMarks") {
            52.9
        } else {
            Canvas::text_height(&mark, mark_width, if packing { 6.75 } else { 6. })
        };
        if !contract && page_index == 0 {
            if mark_height + 4. > 246. - row_top {
                return Err(invalid("唛头内容超过内置模板单页区域，请调整唛头或模板。"));
            }
            table_height = table_height.max(mark_height + 4.);
        }
        table_height = table_height.max(8.);
        let table_bottom = row_top + table_height;
        for x in if contract {
            vec![
                LEFT,
                LEFT + widths[0],
                LEFT + widths[0] + widths[1],
                LEFT + WIDTH - widths[3],
                LEFT + WIDTH,
            ]
        } else if packing {
            vec![LEFT, LEFT + widths[0], LEFT + WIDTH]
        } else {
            vec![
                LEFT,
                LEFT + widths[0],
                LEFT + widths[0] + widths[1],
                LEFT + WIDTH,
            ]
        } {
            canvas.line(x, row_top, x, table_bottom, 0.2);
        }
        if !contract && page_index == 0 {
            if let Some(image) = data.images.get("Invoice.ShippingMarks") {
                canvas.image(
                    image,
                    LEFT + 1.5,
                    row_top + (table_height - 52.9).max(0.) / 2.,
                    mark_width,
                    52.9,
                    1.,
                )?;
            } else {
                canvas.label(
                    &mark,
                    LEFT + 1.5,
                    row_top + 2.,
                    mark_width,
                    table_height - 4.,
                    if packing { 6.75 } else { 6. },
                    false,
                    "Left",
                )?;
            }
        }
        if template == Builtin::Invoice && page_index == 0 {
            canvas.label(
                &data.text("Invoice.TradeTerms"),
                LEFT + WIDTH - widths[2] + 2.,
                row_top,
                widths[2] - 4.,
                trade_height,
                7.5,
                false,
                "Right",
            )?;
        }
        let extra =
            (table_height - trade_height - group.iter().map(|i| rows[*i].height).sum::<f32>())
                .max(0.)
                / group.len().max(1) as f32;
        let mut y = row_top + trade_height;
        for index in group {
            let row = &rows[*index];
            let height = row.height + extra;
            if template == Builtin::Invoice {
                let x = LEFT + widths[0] + 2.6;
                let mut text_y = y + 2.;
                text_y +=
                    canvas.text(&row.cells[0], x, text_y, widths[1] - 5., 6., false, "Left") + 0.4;
                if !row.cells[1].is_empty() {
                    text_y +=
                        canvas.text(&row.cells[1], x, text_y, widths[1] - 5., 6., false, "Left")
                            + 0.4;
                }
                let mut cx = x;
                for (text, width) in row.cells[2..6].iter().zip([26., 16., 16., widths[1] - 63.]) {
                    canvas.text(text, cx, text_y, width, 6., false, "Left");
                    cx += width + 1.;
                }
                canvas.label(
                    &row.cells[6],
                    LEFT + WIDTH - widths[2] + 2.,
                    y + 1.,
                    widths[2] - 4.,
                    height - 2.,
                    6.,
                    false,
                    "Left",
                )?;
            } else {
                let mut x = LEFT + if packing { widths[0] } else { 0. };
                let content_widths = if packing { &widths[1..] } else { &widths[..] };
                for (column, (text, width)) in row.cells.iter().zip(content_widths).enumerate() {
                    if packing && column == 0 {
                        let mut ly = y + 1.;
                        for (line, value) in text.lines().enumerate() {
                            ly += canvas.text(
                                value,
                                x + 1.,
                                ly,
                                width - 2.,
                                if line == 0 { 6.375 } else { 5.7 },
                                line == 0,
                                "Center",
                            );
                        }
                    } else {
                        canvas.text(
                            text,
                            x + if packing { 1. } else { 2. },
                            y + if packing { 1.5 } else { 2. },
                            width - if packing { 2. } else { 4. },
                            if packing { 6. } else { 7.5 },
                            false,
                            if packing { "Center" } else { "Left" },
                        );
                    }
                    x += width;
                }
            }
            y += height;
        }
        y = table_bottom;
        if page_index + 1 == groups.len() {
            y += totals(&mut canvas, template, data, y, &widths)?;
            if contract {
                commercial_footer::contract(&mut pages, canvas, data, y, cancelled)?;
            } else {
                commercial_footer::standard(&mut pages, canvas, data, y, !packing, cancelled)?;
            }
        } else {
            pages.push(canvas.finish());
        }
    }
    let count = pages.len();
    if count > 1 || contract {
        for (index, page) in pages.iter_mut().enumerate() {
            let mut footer = Canvas::new(210., 297.);
            footer.text(
                &format!("{} / {count}", index + 1),
                90.,
                if contract { 290. } else { 264. },
                30.,
                7.5,
                false,
                "Center",
            );
            let fragment = footer.svg.split_once("fill=\"white\"/>").unwrap().1;
            page.svg = page.svg.replacen("</svg>", &format!("{fragment}</svg>"), 1);
        }
    }
    Ok(Document { pages })
}

fn totals(
    canvas: &mut Canvas,
    template: Builtin,
    data: &ReportData,
    y: f32,
    widths: &[f32],
) -> Result<f32> {
    let packing = template == Builtin::PackingList;
    let totals = match template {
        Builtin::Invoice => vec![
            format!(
                "TOTAL: {}       {}",
                data.unit_totals("total_by_ctn_unit"),
                data.unit_totals("total_by_qty_unit")
            ),
            format!(
                "{}{}",
                data.text("Invoice.Currency"),
                data.number("Invoice.TotalAmount", 2)?
            ),
        ],
        Builtin::PackingList => vec![
            "TOTAL:".into(),
            data.unit_totals("total_by_ctn_unit"),
            data.unit_totals("total_by_qty_unit"),
            format!("{} KGS", data.number("Invoice.TotalGrossWeight", 2)?),
            format!("{} KGS", data.number("Invoice.TotalNetWeight", 2)?),
            format!("{} CBM", data.number("Invoice.TotalVolume", 3)?),
        ],
        _ => vec![
            "TOTAL:".into(),
            data.unit_totals("total_by_qty_unit"),
            String::new(),
            format!(
                "{}{}",
                data.text("Invoice.Currency"),
                data.number("Invoice.TotalAmount", 2)?
            ),
        ],
    };
    let total_widths = match template {
        Builtin::Invoice => vec![widths[0] + widths[1], widths[2]],
        Builtin::PackingList => vec![
            widths[0] + widths[1],
            widths[2],
            widths[3],
            widths[4],
            widths[5],
            widths[6],
        ],
        _ => widths.to_vec(),
    };
    let size = if packing { 6.375 } else { 7.5 };
    let height = totals
        .iter()
        .zip(&total_widths)
        .map(|(text, width)| Canvas::text_height(text, width - 2., size) + 3.)
        .fold(if packing { 6.4 } else { 8.4 }, f32::max);
    if packing {
        canvas.line(LEFT, y, LEFT, y + height, 0.2);
        canvas.line(LEFT + WIDTH, y, LEFT + WIDTH, y + height, 0.2);
        canvas.line(LEFT, y + height, LEFT + WIDTH, y + height, 0.2);
        canvas.dashed_line(LEFT, y, LEFT + WIDTH, y, 0.2);
    } else {
        canvas.rect(LEFT, y, WIDTH, height, "none", 0.2);
    }
    let mut x = LEFT;
    for (index, (text, width)) in totals.iter().zip(total_widths).enumerate() {
        if !packing && index > 0 {
            canvas.line(x, y, x, y + height, 0.2);
        }
        canvas.label(
            text,
            x + 1.,
            y + 1.,
            width - 2.,
            height - 2.,
            size,
            true,
            if packing { "Center" } else { "Left" },
        )?;
        x += width;
    }
    Ok(height)
}
