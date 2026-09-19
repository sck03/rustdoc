use super::{
    Builtin,
    commercial::{LEFT, WIDTH},
};
use crate::{ReportData, Result, canvas::Canvas, error::invalid};

pub fn draw(canvas: &mut Canvas, data: &ReportData, template: Builtin, first: bool) -> Result<f32> {
    let contract = template == Builtin::Contract;
    if contract && !first {
        return Ok(16.);
    }
    let mut y = 16.;
    y += canvas.text(
        &data.text("Exporter.ExporterNameEN"),
        LEFT,
        y,
        WIDTH,
        12.,
        true,
        "Center",
    );
    if !contract {
        y += canvas.text(
            &data.text("Exporter.AddressEN"),
            LEFT,
            y,
            WIDTH,
            7.5,
            false,
            "Center",
        );
    }
    y += 3.;
    y += canvas.text(
        match template {
            Builtin::Invoice => "INVOICE",
            Builtin::PackingList => "PACKING LIST",
            _ => "售货合同",
        },
        LEFT,
        y,
        WIDTH,
        12.,
        true,
        "Center",
    ) + 3.5;
    let info_top = y;
    let address_size = if contract {
        7.125
    } else if template == Builtin::PackingList {
        6.75
    } else {
        6.
    };
    let left_width = if contract { 104. } else { WIDTH * 0.68 };
    y += canvas.text(
        if contract {
            "买方: / Buyer:"
        } else {
            "TO: M/S"
        },
        LEFT,
        y,
        left_width,
        9.,
        false,
        "Left",
    );
    y += canvas.text(
        &data.text("Customer.CustomerNameEN"),
        LEFT,
        y,
        left_width,
        address_size,
        false,
        "Left",
    ) + 1.;
    y += canvas.text(
        &data.text("Customer.AddressEN"),
        LEFT,
        y,
        left_width,
        address_size,
        false,
        "Left",
    );
    if contract {
        let right = LEFT + 106.;
        canvas.text(
            &format!(
                "合同号码(Contract No.): {}",
                data.text("Invoice.ContractNo")
            ),
            right,
            info_top + 4.,
            WIDTH - 106.,
            9.,
            false,
            "Right",
        );
        canvas.text(
            &format!("日期(Date): {}", data.date("Invoice.InvoiceDate", false)?),
            right,
            info_top + 13.,
            WIDTH - 106.,
            9.,
            false,
            "Right",
        );
        y += 5.;
        y += canvas.text("卖方: / Seller:", LEFT, y, WIDTH, 9., false, "Left");
        y += canvas.text(
            &data.text("Exporter.ExporterNameEN"),
            LEFT,
            y,
            WIDTH,
            7.125,
            false,
            "Left",
        ) + 6.;
        y += canvas.text("双方同意按本合同所列条款由卖方出售，买方购进下列货物：\nThe sellers agree to sell and the Buyers agree to buy the under-mentioned commodities on the terms and conditions stated below:", LEFT, y, WIDTH, 7.125, false, "Left") + 3.;
    } else {
        let mut meta_y = info_top;
        let meta_x = LEFT + WIDTH * 0.69;
        let meta_width = WIDTH * 0.31;
        for (label, value) in [
            ("Invoice No.:", data.text("Invoice.InvoiceNo")),
            ("Contract No.:", data.text("Invoice.ContractNo")),
            ("Date:", data.date("Invoice.InvoiceDate", false)?),
        ] {
            meta_y += canvas.text(
                &format!("{label} {value}"),
                meta_x,
                meta_y,
                meta_width,
                address_size,
                false,
                "Right",
            ) + 0.7;
        }
        y = y.max(meta_y);
        if template == Builtin::Invoice {
            y += 1.;
            for (left, right) in [
                (
                    ("From:", data.text("Invoice.PortOfLoading")),
                    ("To:", data.text("Invoice.PortOfDestination")),
                ),
                (
                    ("Payment Terms:", data.text("Invoice.PaymentTerms")),
                    ("Issued by:", String::new()),
                ),
            ] {
                let mut row_height: f32 = 4.8;
                for (index, (label, value)) in [left, right].iter().enumerate() {
                    let x = LEFT + index as f32 * WIDTH / 2.;
                    let label_width = if *label == "Payment Terms:" {
                        20.5
                    } else if *label == "Issued by:" {
                        15.
                    } else {
                        11.
                    };
                    row_height = row_height
                        .max(Canvas::text_height(value, WIDTH / 2. - label_width - 1., 6.) + 1.2);
                    canvas.text(label, x, y, label_width, 6., false, "Left");
                    canvas.text(
                        value,
                        x + label_width,
                        y,
                        WIDTH / 2. - label_width - 1.,
                        6.,
                        false,
                        "Left",
                    );
                    canvas.dashed_line(
                        x + label_width,
                        y + row_height - 0.6,
                        x + WIDTH / 2.,
                        y + row_height - 0.6,
                        0.2,
                    );
                }
                y += row_height;
            }
        }
        y += 2.5;
    }
    if y > 125. {
        return Err(invalid(
            "单据抬头超过内置模板的页眉区域，请调整地址或使用自定义模板。",
        ));
    }
    Ok(y)
}
