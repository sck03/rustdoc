use super::check;
use crate::{
    Document, ReportData, Result,
    canvas::Canvas,
    data::{decimal, format_decimal, value_at},
    error::invalid,
};
use std::sync::atomic::AtomicBool;

const LEFT: f32 = 10.2;
const WIDTH: f32 = 274.2;
const COLUMNS: [f32; 15] = [
    49., 94., 41., 82., 34., 68., 76., 103., 49., 76., 77., 50., 35., 103., 100.,
];
fn width(start: usize, count: usize) -> f32 {
    COLUMNS[start..start + count].iter().sum::<f32>() / COLUMNS.iter().sum::<f32>() * WIDTH
}
fn detail_widths() -> [f32; 9] {
    [
        width(0, 1),
        width(1, 2),
        width(3, 4),
        width(7, 1),
        width(8, 2),
        width(10, 1),
        width(11, 2),
        width(13, 1),
        width(14, 1),
    ]
}

fn header(canvas: &mut Canvas, data: &ReportData, first: bool) -> Result<f32> {
    if !first {
        return Ok(17.);
    }
    canvas.text(
        "中华人民共和国海关出口货物报关单",
        LEFT,
        11.5,
        WIDTH,
        9.75,
        true,
        "Center",
    );
    canvas.text("预录入编号：", LEFT, 18., width(0, 5), 6.75, true, "Left");
    canvas.text(
        "海关编号：",
        LEFT + width(0, 5),
        18.,
        width(5, 6),
        6.75,
        true,
        "Left",
    );
    let mut y = 21.8;
    let rows = [
        vec![
            (
                format!("境内发货人  {}", data.text("Exporter.CreditCode")),
                data.text("Exporter.ExporterNameCN"),
                5,
            ),
            ("出境关别".into(), String::new(), 2),
            ("出口日期".into(), String::new(), 3),
            ("申报日期".into(), String::new(), 3),
            ("备案号".into(), String::new(), 2),
        ],
        vec![
            ("境外收货人".into(), data.text("Customer.CustomerNameEN"), 5),
            ("运输方式".into(), data.text("Invoice.TransportMode"), 2),
            ("运输工具名称及航次号".into(), String::new(), 3),
            ("提运单号".into(), String::new(), 5),
        ],
        vec![
            (
                format!("生产销售单位  {}", data.text("Exporter.CreditCode")),
                data.text("Exporter.ExporterNameCN"),
                5,
            ),
            ("监管方式".into(), data.text("Invoice.SupervisionMode"), 2),
            ("征免性质".into(), String::new(), 3),
            ("许可证号".into(), String::new(), 5),
        ],
        vec![
            ("合同协议号".into(), data.text("Invoice.ContractNo"), 5),
            (
                "贸易国(地区)".into(),
                data.text("Invoice.DestinationCountry"),
                2,
            ),
            (
                "运抵国(地区)".into(),
                data.text("Invoice.DestinationCountry"),
                3,
            ),
            ("指运港".into(), data.text("Invoice.PortOfDestination"), 3),
            ("离境口岸".into(), data.text("Invoice.PortOfLoading"), 2),
        ],
        vec![
            ("包装种类".into(), String::new(), 5),
            (
                "件数".into(),
                decimal(data.value("Invoice.TotalCartons", None))?
                    .normalize()
                    .to_string(),
                1,
            ),
            (
                "毛重(千克)".into(),
                format!("{}KGS", data.number("Invoice.TotalGrossWeight", 2)?),
                1,
            ),
            (
                "净重(千克)".into(),
                format!("{}KGS", data.number("Invoice.TotalNetWeight", 2)?),
                1,
            ),
            ("成交方式".into(), data.text("Invoice.PaymentTerms"), 2),
            ("运费".into(), String::new(), 2),
            ("保费".into(), String::new(), 2),
            ("杂费".into(), String::new(), 1),
        ],
    ];
    for row in rows {
        let mut column = 0;
        let mut height: f32 = 8.6;
        for (label, value, span) in &row {
            let w = width(column, *span);
            height = height.max(
                Canvas::text_height(label, w - 0.7, 6.75)
                    + Canvas::text_height(value, w - 0.7, 7.5)
                    + 0.6,
            );
            column += span;
        }
        let mut x = LEFT;
        column = 0;
        for (label, value, span) in row {
            let w = width(column, span);
            canvas.rect(x, y, w, height, "white", 0.15);
            let label_height = canvas.text(&label, x + 0.35, y + 0.2, w - 0.7, 6.75, true, "Left");
            canvas.text(
                &value,
                x + 0.35,
                y + 0.2 + label_height,
                w - 0.7,
                7.5,
                false,
                "Left",
            );
            x += w;
            column += span;
        }
        y += height;
    }
    for (label, height) in [("随附单证", 3.5), ("标记唛码及备注", 5.2)] {
        canvas.rect(LEFT, y, WIDTH, height, "white", 0.15);
        canvas.text(label, LEFT + 0.35, y + 0.1, WIDTH - 0.7, 6., true, "Left");
        y += height;
    }
    Ok(y)
}

fn footer(canvas: &mut Canvas, data: &ReportData, mut y: f32) -> Result<()> {
    canvas.rect(LEFT, y, WIDTH, 4., "none", 0.15);
    for (label, x, w) in [
        ("特殊关系确认：", 55., 55.),
        ("价格影响确认：", 117., 62.),
        ("支付特许权使用费确认：", 179., 58.),
        ("自报自缴：", 237., 47.4),
    ] {
        canvas.text(label, x, y + 0.15, w, 6.75, true, "Center");
    }
    y += 4.;
    canvas.rect(LEFT, y, WIDTH, 9., "none", 0.15);
    canvas.line(222., y, 222., y + 9., 0.15);
    for (text, x, w) in [
        ("报关人员", LEFT, 22.),
        ("报关人员证号", 33.5, 43.),
        ("电话", 81., 24.),
        ("兹申明对以上内容承担如实申报、依法纳税责任", 127., 93.),
        ("海关批注及签章", 222., 62.),
    ] {
        canvas.text(text, x + 0.35, y + 0.3, w - 0.7, 6.75, true, "Left");
    }
    canvas.text("申报单位", LEFT + 0.35, y + 4.5, 80., 6.75, true, "Left");
    canvas.text("申报单位(签章)", 186., y + 4.5, 35., 6.75, true, "Center");
    if data.root["ShowSeal"] == true {
        if let Some(image) = data.images.get("customs_seal_path") {
            canvas.image(image, 183., y - 14.2, 53.6, 33.7, 0.82)?;
        }
    }
    canvas.text(
        "境外品牌(贴牌生产)\n出口货物不能确定在最终目的国（地区）享受优惠",
        LEFT + 35.7,
        y + 14.,
        WIDTH - 35.7,
        8.4375,
        false,
        "Left",
    );
    Ok(())
}

pub fn render(data: &ReportData, cancelled: &AtomicBool) -> Result<Document> {
    let widths = detail_widths();
    let mut rows = vec![];
    for (index, item) in data.items().iter().enumerate() {
        check(cancelled)?;
        let text = |field| data.item_text(item, field);
        let chinese = text("StyleNameCN");
        let description = format!(
            "{}\n{}{}",
            if chinese.is_empty() {
                text("StyleName")
            } else {
                chinese
            },
            text("FabricComposition"),
            if text("Brand").is_empty() {
                String::new()
            } else {
                format!(" 品牌: {}", text("Brand"))
            }
        );
        let price = decimal(value_at(item, "UnitPrice"))?;
        let cells = vec![
            (index + 1).to_string(),
            text("HSCode"),
            description,
            format!(
                "{}{}\n{}千克",
                text("Quantity"),
                text("UnitCN"),
                data.item_number(item, "NWTotal", 2)?
            ),
            format!(
                "{}{}\n{}{}",
                data.text("Invoice.Currency"),
                format_decimal(price, price.normalize().scale().clamp(2, 6)),
                data.text("Invoice.Currency"),
                data.item_number(item, "TotalPrice", 2)?
            ),
            "中国".into(),
            data.text("Invoice.DestinationCountry"),
            text("Origin"),
            String::new(),
        ];
        let height = cells
            .iter()
            .zip(widths)
            .map(|(text, width)| Canvas::text_height(text, width - 0.8, 7.5) + 0.4)
            .fold(7.3, f32::max);
        rows.push((cells, height));
    }
    let headings = [
        "项号",
        "商品编号",
        "商品名称及规格型号",
        "数量及单位",
        "单价/总价/币制",
        "原产国(地区)",
        "最终目的国(地区)",
        "境内货源地",
        "征免",
    ];
    let mut pages = vec![];
    let mut cursor = 0;
    loop {
        check(cancelled)?;
        let first = pages.is_empty();
        let mut canvas = Canvas::serif(297., 210.);
        let mut y = header(&mut canvas, data, first)?;
        let header_height = headings
            .iter()
            .zip(widths)
            .map(|(text, width)| Canvas::text_height(text, width - 0.8, 6.75) + 0.8)
            .fold(4.5, f32::max);
        let mut x = LEFT;
        for (text, width) in headings.iter().zip(widths) {
            canvas.rect(x, y, width, header_height, "white", 0.15);
            canvas.label(
                text,
                x + 0.4,
                y + 0.2,
                width - 0.8,
                header_height - 0.4,
                6.75,
                true,
                "Center",
            )?;
            x += width;
        }
        y += header_height;
        let bottom = if first { 174. } else { 195. };
        let limit = if first { 6 } else { 15 };
        let mut count = 0;
        while cursor < rows.len() && count < limit {
            let (cells, height) = &rows[cursor];
            if y + height > bottom {
                if count == 0 {
                    return Err(invalid(
                        "报关明细单行超过所选页面的可用区域，请调整品名或自定义版式。",
                    ));
                }
                break;
            }
            canvas.rect(LEFT, y, WIDTH, *height, "none", 0.15);
            let mut x = LEFT;
            for (column, (text, width)) in cells.iter().zip(widths).enumerate() {
                canvas.text_box(
                    text,
                    x + 0.4,
                    y + 0.2,
                    width - 0.8,
                    height - 0.4,
                    7.5,
                    false,
                    if column == 2 { "Left" } else { "Center" },
                )?;
                x += width;
            }
            y += height;
            cursor += 1;
            count += 1;
        }
        if count == 0 {
            canvas.rect(LEFT, y, WIDTH, 7.3, "none", 0.15);
            y += 7.3;
        }
        if first {
            footer(&mut canvas, data, y)?;
        }
        pages.push(canvas.finish());
        if cursor >= rows.len() {
            break;
        }
        if pages.len() >= 500 {
            return Err(invalid("报关单页数超过 500 页。"));
        }
    }
    let count = pages.len();
    for (index, page) in pages.iter_mut().enumerate() {
        let fragment = format!(
            "<text x=\"258\" y=\"{}\" text-anchor=\"end\" font-size=\"2.38125\">页码/页数：{}/{count}</text>",
            if index == 0 { 20.5 } else { 15. },
            index + 1
        );
        page.svg = page.svg.replacen("</svg>", &format!("{fragment}</svg>"), 1);
    }
    Ok(Document { pages })
}
