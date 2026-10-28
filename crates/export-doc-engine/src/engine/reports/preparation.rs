//! A document's source, related parties and images share one read snapshot.
use super::super::{report_template_files, tasks::PublicationCheck};
use super::*;
use std::{collections::BTreeMap, sync::Arc};

pub(in crate::engine) struct PreparedReport {
    data: Arc<ReportData>,
    template: Template,
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn all_templates_keep_the_same_source_after_the_saved_invoice_changes() {
        let workspace = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .unwrap()
            .parent()
            .unwrap()
            .to_path_buf();
        let root = workspace
            .join(".codex-runtime/report-snapshot-tests")
            .join(paths::nonce().unwrap());
        std::fs::create_dir_all(&root).unwrap();
        let paths = crate::paths::RuntimePaths::server(&workspace, &root.join("Data")).unwrap();
        let store = Store::open(&paths).unwrap();
        auth::seed(&store).unwrap();
        let actor = auth::current_actor(&store, 1).unwrap();
        let invoice = InvoiceDraft::demo("2026-10-27", "SNAPSHOT-OLD")
            .build()
            .unwrap();
        let saved = store
            .transaction(|tx| {
                super::super::super::store::save(
                    tx,
                    "invoices",
                    0,
                    serde_json::to_value(&invoice)?,
                    None,
                    &actor,
                    "create",
                )
            })
            .unwrap();
        let id = saved["id"].as_i64().unwrap();
        let requests = vec![
            json!({"reportType":"ExportDocument","templatePath":Builtin::Invoice.path(),"withSeal":false}),
            json!({"reportType":"ExportDocument","templatePath":Builtin::PackingList.path(),"withSeal":false}),
        ];
        let prepared =
            prepare_batch(&store, &paths, &actor, id, &requests, false, "export-pdf").unwrap();
        assert!(Arc::ptr_eq(&prepared[0].data, &prepared[1].data));
        store
            .transaction(|tx| {
                let mut current = super::super::super::store::get(tx, "invoices", id)?;
                current["invoiceNo"] = json!("SNAPSHOT-NEW");
                super::super::super::store::save(
                    tx, "invoices", id, current, None, &actor, "edit",
                )?;
                Ok(())
            })
            .unwrap();
        for report in prepared {
            assert!(report.file_name(id).contains("SNAPSHOT-OLD"));
            let document = report
                .render(
                    &render::Fonts::new(&paths.font_path),
                    &AtomicBool::new(false),
                )
                .unwrap();
            let svg = document
                .pages
                .iter()
                .map(|page| page.svg.as_str())
                .collect::<String>();
            assert!(svg.contains("SNAPSHOT-OLD"));
            assert!(!svg.contains("SNAPSHOT-NEW"));
        }
        drop(store);
        std::fs::remove_dir_all(root).unwrap();
    }
}
impl PreparedReport {
    pub(super) fn into_parts(self) -> (ReportData, Template) {
        (Arc::unwrap_or_clone(self.data), self.template)
    }
    pub(in crate::engine) fn render(
        &self,
        fonts: &render::Fonts,
        cancelled: &AtomicBool,
    ) -> Result<Document> {
        self.template.render(&self.data, fonts, cancelled)
    }
    pub(in crate::engine) fn file_name(&self, id: i64) -> String {
        let number = self
            .data
            .text(if self.data.report_type == "PaymentVoucher" {
                "Payment.VoucherNo"
            } else {
                "Invoice.InvoiceNo"
            });
        paths::suggested_pdf_name(&format!(
            "{}-{}-{id}",
            if number.is_empty() {
                "单据".into()
            } else {
                number
            },
            self.template.label()
        ))
    }
}

pub(in crate::engine) fn prepare_batch(
    store: &Store,
    paths: &crate::paths::RuntimePaths,
    actor: &Actor,
    id: i64,
    requests: &[Value],
    payment: bool,
    action: &str,
) -> Result<Vec<PreparedReport>> {
    let _lock = report_template_files::storage_lock(paths)?;
    store.read(|tx| {
        let actor = auth::current_actor_in(tx, actor.id, store.edition)?;
        let source = subject_in(
            tx,
            &actor,
            if payment { "payments" } else { "invoices" },
            id,
            action,
        )?;
        let related = if payment {
            vec![party_in(tx, &actor, "payees", source["payeeId"].as_i64())?]
        } else {
            vec![
                party_in(tx, &actor, "customers", source["customerId"].as_i64())?,
                party_in(tx, &actor, "exporters", source["exporterId"].as_i64())?,
            ]
        };
        let invoice = if payment {
            None
        } else {
            Some(
                InvoiceDraft::from_dto(serde_json::from_value(source.clone())?)
                    .build()
                    .map_err(invalid)?,
            )
        };
        let voucher = if payment {
            Some(serde_json::from_value::<ApiPaymentDto>(source)?)
        } else {
            None
        };
        if let Some(voucher) = &voucher {
            export_doc_domain::payment::validate(voucher)
                .map_err(|cause| invalid(cause.to_string()))?;
        }
        let mut groups = BTreeMap::<bool, ReportData>::new();
        let mut templates = Vec::with_capacity(requests.len());
        for request in requests {
            crate::operation::check()?;
            let seal = !payment && request["withSeal"] == true;
            if !groups.contains_key(&seal) {
                let data = if let Some(invoice) = &invoice {
                    ReportData::invoice(invoice, related[0].clone(), related[1].clone(), seal)?
                } else {
                    ReportData::payment(
                        voucher.as_ref().expect("payment source"),
                        related[0].clone(),
                    )?
                };
                groups.insert(seal, data);
            }
            let kind = if payment {
                "PaymentVoucher"
            } else {
                report_type(&text(request, "reportType"))?
            };
            if !payment && kind != "ExportDocument" {
                return Err(invalid("发票只能使用出口单据模板。"));
            }
            let template = template_in(
                tx,
                paths,
                &actor,
                &text(request, "templatePath"),
                kind,
                true,
            )?;
            report_assets::hydrate_in(
                tx,
                paths,
                &actor,
                groups.get_mut(&seal).expect("prepared data"),
                Some(template.content()),
            )?;
            let retained: usize = groups
                .values()
                .flat_map(|data| data.images.values())
                .map(|image| image.bytes.len())
                .sum();
            if retained > 128 * 1024 * 1024 {
                return Err(error(
                    429,
                    "本批报表图片快照超过 128 MiB，请减少模板或图片。",
                ));
            }
            templates.push((seal, template));
        }
        let groups: BTreeMap<_, _> = groups
            .into_iter()
            .map(|(seal, data)| (seal, Arc::new(data)))
            .collect();
        Ok(templates
            .into_iter()
            .map(|(seal, template)| PreparedReport {
                data: groups[&seal].clone(),
                template,
            })
            .collect())
    })
}

pub(super) fn validate_access(
    store: &Store,
    paths: &crate::paths::RuntimePaths,
    actor: &Actor,
    ids: &[i64],
    requests: &[Value],
    payment: bool,
    action: &str,
) -> Result<()> {
    let _lock = report_template_files::storage_lock(paths)?;
    store.read(|tx| {
        let current = auth::current_actor_in(tx, actor.id, store.edition)?;
        for id in ids {
            subject_in(
                tx,
                &current,
                if payment { "payments" } else { "invoices" },
                *id,
                action,
            )?;
        }
        for request in requests {
            let kind = if payment {
                "PaymentVoucher"
            } else {
                report_type(&text(request, "reportType"))?
            };
            template_in(
                tx,
                paths,
                &current,
                &text(request, "templatePath"),
                kind,
                true,
            )?;
        }
        Ok(())
    })
}

pub(in crate::engine) fn publication_check(
    paths: &crate::paths::RuntimePaths,
    ids: Vec<i64>,
    requests: Vec<Value>,
    payment: bool,
    action: &'static str,
) -> Result<PublicationCheck> {
    let lock = report_template_files::storage_lock(paths)?;
    let paths = paths.clone();
    Ok(PublicationCheck::new(move |tx, actor| {
        for id in &ids {
            subject_in(
                tx,
                actor,
                if payment { "payments" } else { "invoices" },
                *id,
                action,
            )?;
        }
        for request in &requests {
            let kind = if payment {
                "PaymentVoucher"
            } else {
                report_type(&text(request, "reportType"))?
            };
            template_in(
                tx,
                &paths,
                actor,
                &text(request, "templatePath"),
                kind,
                true,
            )?;
        }
        Ok(())
    })
    .with_file_lock(lock))
}
