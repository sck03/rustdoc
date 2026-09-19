use crate::{
    api::ApiClient,
    designer::{Design, Kind, field_catalog},
    generated_api::*,
    invoice::InvoiceDraft,
    jobs,
    paths::{atomic_write, ensure_safe_absolute, nonce, save_pdf},
    pdf,
    runtime::Runtime,
    template,
};
use serde_json::{Value, json};
use std::{fs, path::Path, sync::atomic::AtomicBool, time::Instant};

pub fn run(runtime: &Runtime, output: &Path) -> Result<(), String> {
    ensure_safe_absolute(output)?;
    fs::create_dir_all(output).map_err(|error| error.to_string())?;
    let started = Instant::now();
    let mut evidence = vec![];
    let (client, user) = runtime
        .client
        .login("admin".into(), String::new())
        .map_err(|error| error.to_string())?;
    let catalogs: ApiReportTemplateFieldCatalogResponse = client
        .json(
            GET_REPORT_TEMPLATE_FIELD_CATALOG,
            &[],
            &[("reportType", "ExportDocument".into())],
            None,
        )
        .map_err(|error| error.to_string())?;
    let fields = field_catalog(&catalogs);
    let suffix = nonce()?;
    let number = format!("NATIVE-{}", &suffix[..8]);
    let mut draft = InvoiceDraft::demo(&user.business_date, &number);
    use export_doc_domain::invoice_columns::column_index;
    draft.rows[0].cells[column_index("spare10").unwrap()] = "保留备用字段 NFC é".into();
    draft.paste(0, column_index("quantity").unwrap(), "1200\tPCS")?;
    draft.paste(0, column_index("unitPrice").unwrap(), "4.55")?;
    let saved = client
        .save_invoice(&draft.build()?)
        .map_err(|error| error.to_string())?;
    let original = saved.clone();
    let mut edited = InvoiceDraft::from_dto(saved);
    edited.header.contract_no = "原生编辑回读验证".into();
    let saved = client
        .save_invoice(&edited.build()?)
        .map_err(|error| error.to_string())?;
    let readback = client
        .get_invoice(saved.id)
        .map_err(|error| error.to_string())?;
    if readback.invoice_no != number
        || readback.items[0].spare10 != "保留备用字段 NFC é"
        || readback.items[0].quantity.to_string() != "1200"
    {
        return Err("发票回读与原生草稿不一致。".into());
    }
    let stale = client
        .save_invoice(&original)
        .expect_err("the old row version must conflict");
    if stale.status != Some(409) {
        return Err(format!("旧版本应得到 HTTP 409，实际为 {stale}"));
    }
    evidence.push(json!({"check":"invoice-create-edit-reload-conflict","passed":true,"invoiceId":saved.id,"total":readback.total_amount}));
    let mut design = Design::invoice();
    if let Kind::Text { text } = &mut design.element_mut("title").unwrap().kind {
        *text = "COMMERCIAL INVOICE / 原生商业发票".into();
    }
    let html = template::export(&design, &fields)?;
    let _: ApiReportTemplatePreviewResponse = client
        .json(
            PREVIEW_REPORT_TEMPLATE_CONTENT,
            &[],
            &[],
            Some(json!({"reportType":"ExportDocument","content":html,"withSeal":false})),
        )
        .map_err(|error| error.to_string())?;
    let record = client
        .save_template(&format!("原生发票验证 {}", &suffix[..8]), &html, None)
        .map_err(|error| error.to_string())?;
    let record = client
        .publish_template(&record)
        .map_err(|error| error.to_string())?;
    let reloaded: ApiUserReportTemplateDto = client
        .json(
            GET_USER_REPORT_TEMPLATE,
            &[("id", record.id.to_string())],
            &[],
            None,
        )
        .map_err(|error| error.to_string())?;
    if Design::from_html(&reloaded.content_html)? != design {
        return Err("设计器模板保存回读丢失结构。".into());
    }
    evidence.push(json!({"check":"design-save-publish-reload","passed":true,"templateId":record.id,"version":record.version_number}));
    let reference = format!("user-template:{}", record.id);
    atomic_write(&output.join("native-template.html"), html.as_bytes())?;
    let pdf_bytes = render(&client, saved.id, &reference)?;
    save_pdf(&output.join("native-invoice.pdf"), &pdf_bytes)?;
    let pdfium_path = runtime.paths.pdfium_path();
    let page = pdf::render_page(&pdf_bytes, 0, &pdfium_path)?;
    atomic_write(&output.join("native-invoice-page-1.png"), &page.png)?;
    if page.page_count != 1 {
        return Err(format!("3 行商品预期 1 页，实际 {} 页。", page.page_count));
    }
    evidence.push(json!({"check":"native-template-pdf","passed":true,"bytes":pdf_bytes.len(),"pages":page.page_count,"previewEngine":"existing-bundled-PDFium"}));
    let mut long = InvoiceDraft::from_dto(readback);
    long.header.id = 0;
    long.header.row_version.clear();
    long.header.invoice_no = format!("{number}-LONG");
    let fixture = long.rows[0].clone();
    long.rows.clear();
    for index in 0..75 {
        let mut row = fixture.clone();
        row.original.id = 0;
        row.original.invoice_id = 0;
        row.cells[1] = format!("NATIVE-{index:03}");
        row.cells[2] = format!(
            "COTTON SHIRT 商品中文排版验证 第 {} 行 / Long description with wrapping",
            index + 1
        );
        long.rows.push(row);
    }
    let long = client
        .save_invoice(&long.build()?)
        .map_err(|error| error.to_string())?;
    let long_pdf = render(&client, long.id, &reference)?;
    save_pdf(&output.join("native-invoice-75-rows.pdf"), &long_pdf)?;
    let first = pdf::render_page(&long_pdf, 0, &pdfium_path)?;
    let last = pdf::render_page(&long_pdf, first.page_count - 1, &pdfium_path)?;
    if first.page_count < 2 {
        return Err("75 行应产生多页 PDF。".into());
    }
    atomic_write(&output.join("native-long-page-1.png"), &first.png)?;
    atomic_write(&output.join("native-long-page-last.png"), &last.png)?;
    evidence.push(json!({"check":"75-rows-cjk-pagination","passed":true,"bytes":long_pdf.len(),"pages":first.page_count}));
    let templates: Vec<ApiReportTemplateDto> = client
        .json(
            LIST_REPORT_TEMPLATES,
            &[],
            &[("reportType", "ExportDocument".into())],
            None,
        )
        .map_err(|error| error.to_string())?;
    let built_in = templates
        .iter()
        .find(|item| item.template_path == "native:invoice")
        .ok_or("未找到原生内置发票模板。")?;
    let original_pdf = render(&client, saved.id, &built_in.template_path)?;
    save_pdf(&output.join("existing-engine-invoice.pdf"), &original_pdf)?;
    evidence.push(json!({"check":"native-builtin-invoice-output","passed":true,"bytes":original_pdf.len(),"template":built_in.template_path}));
    let result = json!({"schemaVersion":1,"platform":std::env::consts::OS,"architecture":std::env::consts::ARCH,"backendProcessId":runtime.process_id(),"elapsedSeconds":started.elapsed().as_secs_f64(),"checks":evidence});
    atomic_write(
        &output.join("acceptance.json"),
        serde_json::to_string_pretty(&result).unwrap().as_bytes(),
    )?;
    println!("{}", serde_json::to_string_pretty(&result).unwrap());
    Ok(())
}
fn render(client: &ApiClient, id: i64, reference: &str) -> Result<Vec<u8>, String> {
    let mut previous = String::new();
    jobs::render_pdf(client, id, reference, &AtomicBool::new(false), &mut |job| {
        if job.status != previous {
            eprintln!(
                "PDF {}: {} {}",
                job.job_id,
                job.status,
                job.progress_percent.unwrap_or(0)
            );
            previous = job.status.clone();
        }
    })
    .map_err(|error| error.to_string())
}

pub fn dump_openapi(runtime: &Runtime, output: &Path) -> Result<(), String> {
    let operation = Operation {
        id: "OfficialOpenApiDocument",
        method: "GET",
        path: "/openapi/v1.json",
        requires_authentication: false,
    };
    let document: Value = runtime
        .client
        .json(operation, &[], &[], None)
        .map_err(|error| error.to_string())?;
    atomic_write(
        output,
        serde_json::to_vec_pretty(&document).unwrap().as_slice(),
    )
}
