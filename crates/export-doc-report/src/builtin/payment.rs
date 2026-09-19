use super::{Builtin, check};
use crate::{
    Document, ReportData, Result,
    canvas::{Canvas, PT_MM},
    error::invalid,
    layout::wrap,
};
use std::sync::atomic::AtomicBool;

const LEFT: f32 = 15.3;
const WIDTH: f32 = 179.4;

pub fn render(template: Builtin, data: &ReportData, cancelled: &AtomicBool) -> Result<Document> {
    check(cancelled)?;
    let mut canvas = Canvas::serif(210., 297.);
    let mut y = 16.;
    let company = data.text("Payment.PayerName");
    canvas.svg.push_str("<g font-family=\"Noto Sans CJK SC\">");
    if !company.is_empty() {
        y += canvas.text(&company, LEFT, y, WIDTH, 19.5, true, "Center") + 2.;
    }
    let voucher = template == Builtin::PaymentVoucher;
    y += canvas.text(
        if voucher {
            "付款单（费用支付专用）"
        } else {
            template.label()
        },
        LEFT,
        y,
        if voucher { 167. } else { WIDTH },
        if voucher { 13.5 } else { 15. },
        false,
        "Center",
    ) + 6.;
    canvas.svg.push_str("</g>");
    canvas.text(
        &format!(
            "{}: {}",
            if voucher { "部门" } else { "业务科别" },
            data.text("Payment.Department")
        ),
        LEFT + 2.,
        y,
        108.,
        if voucher { 9. } else { 10.5 },
        false,
        "Left",
    );
    if !voucher {
        let date = data
            .date("Payment.PaymentDate", true)?
            .replace('年', " 年   ")
            .replace('月', " 月   ")
            .replace('日', " 日");
        canvas.text(&date, 135., y, 57., 10.5, false, "Right");
    }
    y += if voucher { 6. } else { 7. };
    if y > 75. {
        return Err(invalid("付款单位名称过长，请调整名称或使用自定义模板。"));
    }
    let (y, overflow) = if voucher {
        payment_table(&mut canvas, data, y)?
    } else {
        expense_table(&mut canvas, data, y)?
    };
    finish(canvas, template, y, overflow, cancelled)
}

fn payment_table(canvas: &mut Canvas, data: &ReportData, y: f32) -> Result<(f32, Vec<String>)> {
    let left = 14.;
    let vertical_width = 20.3;
    let x = left + vertical_width;
    let width = 149.7;
    let widths = [14.97, 26.946, 22.455, 40.419, 17.964, 26.946];
    let usd = data.number("Payment.USDAmount", 2)?;
    let rows = [
        [
            "项目".into(),
            data.text("Payment.Project"),
            "出口发票号码".into(),
            data.text("Payment.InvoiceNo"),
            "出货日期".into(),
            data.date("Payment.ShipmentDate", true)?,
        ],
        [
            "美元".into(),
            if usd == "0.00" {
                String::new()
            } else {
                format!("USD {usd}")
            },
            "人民币(大\n写)".into(),
            data.text("cny_amount_upper"),
            "(小写)".into(),
            format!("￥{}", data.number("Payment.CNYAmount", 2)?),
        ],
    ];
    let heights: Vec<f32> = rows
        .iter()
        .map(|row| {
            row.iter()
                .zip(widths)
                .map(|(text, width)| Canvas::text_height(text, width - 4., 9.) + 4.)
                .fold(15.1, f32::max)
        })
        .collect();
    let mut bottom = y;
    for (row, height) in rows.iter().zip(&heights) {
        let mut cx = x;
        for (index, (text, cw)) in row.iter().zip(widths).enumerate() {
            canvas.rect(cx, bottom, cw, *height, "white", 0.2);
            canvas.label(
                text,
                cx + 2.,
                bottom + 2.,
                cw - 4.,
                height - 4.,
                9.,
                false,
                if index % 2 == 0 { "Center" } else { "Left" },
            )?;
            cx += cw;
        }
        bottom += height;
    }
    let payee_width = widths[..3].iter().sum::<f32>();
    let method_x = x + payee_width;
    let method_width = width - payee_width;
    let payee = format!(
        "支付单位\n名称：{}\n开户行：{}\n帐号：{}",
        data.text("Payment.PayeeName"),
        data.text("Payment.BankName"),
        data.text("Payment.AccountNo")
    );
    let payee_height = Canvas::text_height(&payee, payee_width - 4., 9.) + 5.;
    let note_width = method_width - 31.;
    let all_notes = wrap(&data.text("Payment.Notes"), note_width, 9. * PT_MM);
    let available = (260. - bottom).min(95.);
    if available < payee_height.max(29.) {
        return Err(invalid("付款信息超过单页可用区域，请缩短字段或调整模板。"));
    }
    let count = (((available - 19.) / (9. * PT_MM * 1.35)).floor() as usize)
        .max(1)
        .min(all_notes.len());
    let notes = &all_notes[..count];
    let height = payee_height
        .max(28.7)
        .max(19. + notes.len() as f32 * 9. * PT_MM * 1.35);
    canvas.rect(x, bottom, payee_width, height, "white", 0.2);
    canvas.text_box(
        &payee,
        x + 2.4,
        bottom + 2.2,
        payee_width - 4.8,
        height - 4.4,
        9.,
        false,
        "Left",
    )?;
    canvas.rect(method_x, bottom, method_width, height, "white", 0.2);
    canvas.label(
        "支付方式",
        method_x + 2.,
        bottom + 2.,
        27.,
        12.,
        9.,
        false,
        "Center",
    )?;
    let method = data.text("Payment.PaymentMethod");
    for (index, name) in ["支票", "电汇", "预付"].iter().enumerate() {
        let cx = method_x + 32. + index as f32 * 16.7;
        canvas.rect(cx, bottom + 5., 3.7, 3.7, "white", 0.2);
        if method == *name {
            canvas.line(cx + 0.5, bottom + 6.8, cx + 1.5, bottom + 8., 0.35);
            canvas.line(cx + 1.5, bottom + 8., cx + 3.2, bottom + 5.5, 0.35);
        }
        canvas.text(name, cx + 5., bottom + 4.6, 11., 9., false, "Left");
    }
    if !method.is_empty() && !["支票", "电汇", "预付"].contains(&method.as_str()) {
        return Err(invalid("内置付款单的支付方式只能是支票、电汇或预付。"));
    }
    canvas.line(
        method_x + 2.,
        bottom + 16.,
        method_x + method_width - 2.,
        bottom + 16.,
        0.2,
    );
    canvas.text(
        "备注",
        method_x + 4.,
        bottom + 18.,
        25.,
        9.,
        false,
        "Center",
    );
    canvas.text_box(
        &notes.join("\n"),
        method_x + 29.,
        bottom + 18.,
        note_width,
        height - 18.,
        9.,
        false,
        "Left",
    )?;
    let total_height = bottom + height - y;
    canvas.rect(left, y, vertical_width, total_height, "white", 0.2);
    canvas.label(
        "用\n款\n事\n项",
        left + 2.,
        y + 2.,
        vertical_width - 4.,
        total_height - 4.,
        9.,
        true,
        "Center",
    )?;
    canvas.label(
        "②\n付\n款\n联",
        189.,
        y,
        7.,
        total_height,
        10.5,
        true,
        "Center",
    )?;
    Ok((bottom + height + 7., all_notes[count..].to_vec()))
}

fn expense_table(canvas: &mut Canvas, data: &ReportData, mut y: f32) -> Result<(f32, Vec<String>)> {
    let first = WIDTH * 0.1;
    let other = WIDTH * 0.9 / 8.;
    let widths = [
        first, other, other, other, other, other, other, other, other,
    ];
    let headers = [
        "项目",
        "差旅费",
        "业务招\n待费",
        "电话费",
        "办公费",
        "修理费",
        "运杂费",
        "检验费",
        "其他",
    ];
    let fields = [
        "",
        "TravelExpense",
        "BusinessEntertainmentExpense",
        "TelephoneExpense",
        "OfficeExpense",
        "RepairExpense",
        "FreightMiscExpense",
        "InspectionExpense",
        "OtherExpense",
    ];
    let mut x = LEFT;
    for (index, (title, width)) in headers.iter().zip(widths).enumerate() {
        canvas.rect(x, y, width, 17.8, "white", 0.2);
        if index == 0 {
            canvas.line(x, y, x + width, y + 17.8, 0.2);
            canvas.text(title, x + 6., y + 2., width - 8., 10.5, true, "Right");
        } else {
            canvas.label(
                title,
                x + 1.,
                y + 1.,
                width - 2.,
                15.8,
                10.5,
                true,
                "Center",
            )?;
        }
        x += width;
    }
    y += 17.8;
    let amounts = fields
        .iter()
        .enumerate()
        .map(|(i, field)| {
            if i == 0 {
                Ok("金额".into())
            } else {
                data.number(&format!("Payment.{field}"), 2)
            }
        })
        .collect::<Result<Vec<String>>>()?;
    let amount_height = amounts
        .iter()
        .zip(widths)
        .map(|(text, width)| Canvas::text_height(text, width - 2., 10.5) + 4.)
        .fold(12.4, f32::max);
    for (values, height) in [
        (amounts, amount_height),
        (
            (0..9)
                .map(|i| {
                    if i == 0 {
                        "附件\n(张)".into()
                    } else if i == 8 {
                        data.text("Payment.AttachmentsCount")
                    } else {
                        String::new()
                    }
                })
                .collect(),
            17.8,
        ),
    ] {
        let mut x = LEFT;
        for (text, width) in values.iter().zip(widths) {
            canvas.rect(x, y, width, height, "white", 0.2);
            canvas.label(
                text,
                x + 1.,
                y + 1.,
                width - 2.,
                height - 2.,
                10.5,
                false,
                "Center",
            )?;
            x += width;
        }
        y += height;
    }
    let all_notes = wrap(
        &data.text("Payment.Notes"),
        WIDTH - first - 5.,
        10.5 * PT_MM,
    );
    let available = (251. - y).min(90.);
    if available < 12.1 {
        return Err(invalid("报销单金额超出单页区域。"));
    }
    let count = (((available - 4.) / (10.5 * PT_MM * 1.35)).floor() as usize)
        .max(1)
        .min(all_notes.len());
    let height = (count as f32 * 10.5 * PT_MM * 1.35 + 4.).max(12.1);
    canvas.rect(LEFT, y, first, height, "white", 0.2);
    canvas.label(
        "备注",
        LEFT + 1.,
        y + 1.,
        first - 2.,
        height - 2.,
        10.5,
        false,
        "Center",
    )?;
    canvas.rect(LEFT + first, y, WIDTH - first, height, "white", 0.2);
    canvas.text_box(
        &all_notes[..count].join("\n"),
        LEFT + first + 2.5,
        y + 2.,
        WIDTH - first - 5.,
        height - 4.,
        10.5,
        false,
        "Left",
    )?;
    y += height;
    let upper = format!("报销净额: {}", data.text("cny_amount_upper"));
    let height = Canvas::text_height(&upper, 129., 10.5).max(4.) + 6.;
    canvas.rect(LEFT, y, WIDTH, height, "white", 0.2);
    canvas.label(
        &upper,
        LEFT + 2.5,
        y + 1.,
        129.,
        height - 2.,
        10.5,
        false,
        "Left",
    )?;
    canvas.label(
        &format!("小计: ￥{}", data.number("Payment.CNYAmount", 2)?),
        LEFT + WIDTH * 0.75 + 2.,
        y + 1.,
        WIDTH * 0.25 - 4.,
        height - 2.,
        10.5,
        false,
        "Left",
    )?;
    Ok((y + height + 3., all_notes[count..].to_vec()))
}

fn finish(
    mut canvas: Canvas,
    template: Builtin,
    mut y: f32,
    overflow: Vec<String>,
    cancelled: &AtomicBool,
) -> Result<Document> {
    let mut pages = vec![];
    if !overflow.is_empty() {
        pages.push(canvas.finish());
        canvas = Canvas::serif(210., 297.);
        y = 16.;
        y += canvas.text(
            &format!("{}（备注续页）", template.label()),
            LEFT,
            y,
            WIDTH,
            13.5,
            true,
            "Center",
        ) + 6.;
        for line in overflow {
            check(cancelled)?;
            if y + 6. > 266. {
                pages.push(canvas.finish());
                if pages.len() >= 500 {
                    return Err(invalid("报表页数超过 500 页。"));
                }
                canvas = Canvas::serif(210., 297.);
                y = 16.;
            }
            canvas.text(&line, LEFT + 2., y, WIDTH - 4., 10.5, false, "Left");
            y += 5.5;
        }
        y += 5.;
    }
    let signatures = if template == Builtin::PaymentVoucher {
        ["业务经理签字:", "审批:", "复核:"]
    } else {
        ["报销人:", "主管签字:", "审批签字:"]
    };
    for (index, label) in signatures.iter().enumerate() {
        canvas.text(
            label,
            LEFT + index as f32 * WIDTH / 3.,
            y,
            WIDTH / 3.,
            10.5,
            false,
            "Left",
        );
    }
    pages.push(canvas.finish());
    Ok(Document { pages })
}
