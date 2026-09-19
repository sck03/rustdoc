use super::{
    check,
    commercial::{LEFT, WIDTH},
};
use crate::{
    Page, ReportData, Result,
    canvas::{Canvas, PT_MM},
    error::invalid,
    layout::wrap,
};
use std::sync::atomic::AtomicBool;

fn space(
    pages: &mut Vec<Page>,
    canvas: &mut Canvas,
    y: &mut f32,
    height: f32,
    bottom: f32,
) -> Result<()> {
    if *y + height <= bottom {
        return Ok(());
    }
    let next = std::mem::replace(canvas, Canvas::new(210., 297.));
    pages.push(next.finish());
    if pages.len() >= 500 {
        return Err(invalid("报表页数超过 500 页。"));
    }
    *y = 16.;
    Ok(())
}

pub fn standard(
    pages: &mut Vec<Page>,
    mut canvas: Canvas,
    data: &ReportData,
    mut y: f32,
    show_terms: bool,
    cancelled: &AtomicBool,
) -> Result<()> {
    if show_terms && !data.text("Invoice.SpecialTerms").is_empty() {
        y += 2.;
        for line in wrap(
            &data.text("Invoice.SpecialTerms"),
            WIDTH - 21.2,
            7.5 * PT_MM,
        ) {
            check(cancelled)?;
            space(pages, &mut canvas, &mut y, 4., 256.)?;
            canvas.text(&line, LEFT + 21.2, y, WIDTH - 21.2, 7.5, false, "Left");
            y += 3.6;
        }
    }
    if data.root["ShowSeal"] == true {
        if let Some(image) = data.images.get("doc_seal_path") {
            y = (y + 4.).max(217.);
            space(pages, &mut canvas, &mut y, 34.4, 256.)?;
            canvas.image(image, LEFT + WIDTH - 13.2 - 58.2, y, 58.2, 34.4, 0.8)?;
        }
    }
    pages.push(canvas.finish());
    Ok(())
}

pub fn contract(
    pages: &mut Vec<Page>,
    mut canvas: Canvas,
    data: &ReportData,
    mut y: f32,
    cancelled: &AtomicBool,
) -> Result<()> {
    y += 4.;
    space(pages, &mut canvas, &mut y, 24., 280.)?;
    let column_widths = [WIDTH * 0.2, WIDTH * 0.2, WIDTH * 0.35, WIDTH * 0.25];
    for values in [
        [
            "(5)装运期限:".into(),
            data.date("Invoice.ShipmentDate", false)?,
            "(6)装运数量允许有 5 % 的增减。".into(),
            String::new(),
        ],
        [
            "Time of shipment:".into(),
            String::new(),
            "Shipment quantity 5 % more or less allowed".into(),
            String::new(),
        ],
        [
            "(7)装运口岸:".into(),
            data.text("Invoice.PortOfLoading"),
            "(8)目的港:".into(),
            data.text("Invoice.PortOfDestination"),
        ],
        [
            "Port of loading:".into(),
            String::new(),
            "Port of destination:".into(),
            String::new(),
        ],
    ] {
        let mut x = LEFT;
        let mut height: f32 = 5.;
        for (text, width) in values.iter().zip(column_widths) {
            let width = if text.starts_with("Shipment quantity") {
                column_widths[2] + column_widths[3]
            } else {
                width
            };
            height =
                height.max(canvas.text(text, x + 0.5, y, width - 1., 7.5, false, "Left") + 0.8);
            x += width;
        }
        y += height;
    }
    y += 6.;
    let text = format!(
        "(9)交货条件：FOB/CFR/CIF 若无另外规定均按照《国际贸易术语解释通则（TNCOTERMS）1990》办理。\nTerms of delivery: FOB/CFR/CIF shall conform to 《INCOTERMS1990》unless otherwise agreed.\n(10)保险：由卖方按发票总值的110%投保一切险加战争险，如买方欲增加其他险别或超过上述额度保险时须事先征得卖方同意，增加的保费由买方承担。\nInsurance: To be covered by the sellers for 110% of the total value against, all risks and war risks. Should the Buyers desire to cover for other risks besides the above mentioned or for an amount exceeding the above mentioned limit. The sellers’ approval must be obtained first and all additional premium charges incurred therewith shall be for buyers’ account.\n(11)付款条件：Terms of payment {}",
        data.text("Invoice.PaymentTerms")
    );
    for line in wrap(&text, WIDTH, 7.125 * PT_MM) {
        check(cancelled)?;
        space(pages, &mut canvas, &mut y, 4., 278.)?;
        canvas.text(&line, LEFT, y, WIDTH, 7.125, false, "Left");
        y += 3.6;
    }
    let seal = (data.root["ShowSeal"] == true)
        .then(|| data.images.get("doc_seal_path"))
        .flatten();
    space(
        pages,
        &mut canvas,
        &mut y,
        if seal.is_some() { 40. } else { 16. },
        282.,
    )?;
    if let Some(image) = seal {
        canvas.image(image, LEFT + WIDTH - 58.2, y + 1., 58.2, 34.4, 0.8)?;
        y += 26.;
    } else {
        y += 5.;
    }
    canvas.text(
        "买方签字：\nThe buyers’ signature:",
        LEFT,
        y,
        WIDTH / 2.,
        7.5,
        false,
        "Left",
    );
    canvas.text(
        "卖方签字：\nThe sellers’ signature:",
        LEFT + WIDTH - 68.8,
        y,
        68.8,
        7.5,
        false,
        "Left",
    );
    pages.push(canvas.finish());
    Ok(())
}
