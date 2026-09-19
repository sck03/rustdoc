use super::{Desktop, forms, theme};
use eframe::egui::{self, RichText};
use export_doc_native::{
    contracts,
    generated_api::{ApiInvoiceDetailDto, ApiInvoiceItemDto},
    invoice::ItemRow,
    workspace::Editor,
};
use serde_json::{Value, json};

impl Desktop {
    pub(super) fn additional_invoice_fields(&mut self, ui: &mut egui::Ui) {
        let keys: &[&str] = match self.invoice_tab {
            0 => &[
                "type",
                "exporterId",
                "customerId",
                "exporterAddressCN",
                "exporterCreditCode",
                "exporterCustomsCode",
                "notifyPartyMode",
                "notifyPartyName",
                "notifyPartyAddress",
                "spare1",
                "spare2",
                "spare3",
                "spare4",
                "spare5",
                "spare6",
                "spare7",
                "spare8",
                "spare9",
                "spare10",
            ],
            1 => &["supervisionMode", "customsBrokerName", "customsBrokerCode"],
            _ => &["exchangeRate", "issuingBank", "letterOfCreditContent"],
        };
        egui::CollapsingHeader::new(match self.invoice_tab{0=>"更多基本信息与备用字段",1=>"申报信息",_=>"利润核算与信用证内容"}).id_salt(("invoice-additional",self.invoice_tab)).default_open(self.invoice_tab==2).show(ui,|ui|{
            let properties=contracts::properties(contracts::schema("ApiInvoiceDetailDto")).unwrap();
            let schema=json!({"type":"object","properties":keys.iter().filter_map(|key|properties.get(*key).map(|value|((*key).to_owned(),value.clone()))).collect::<serde_json::Map<_,_>>()});
            let Ok(mut value)=serde_json::to_value(&self.invoice.header)else{return;};let before=value.clone();
            self.invoice_fields_error=forms::form(ui,&schema,&mut value,&mut self.invoice_field_buffers,&self.workspace.lookups,"invoice-extra",&mut self.probes);
            if value!=before&&self.invoice_fields_error.is_none(){match serde_json::from_value::<ApiInvoiceDetailDto>(value){Ok(header)=>{self.invoice.header=header;self.invoice_changed(ui.ctx());},Err(error)=>self.error=Some(error.to_string())}}
            if self.invoice_tab==2{if let Ok(invoice)=self.invoice.build(){ui.add_space(10.);ui.horizontal_wrapped(|ui|{
                for (label,value) in [("采购合计",invoice.total_purchase_amount),("退税合计",invoice.total_tax_refund_amount),("预计利润",invoice.total_profit)]{ui.label(RichText::new(format!("{label}  ¥ {value:.2}")).strong());}
            });if invoice.exchange_rate.is_none()&&!invoice.currency.eq_ignore_ascii_case("CNY"){ui.label(RichText::new("设置汇率后计算人民币利润。").color(theme::MUTED));}}}
        });
    }
    pub(super) fn edit_item_details(&mut self, index: usize) {
        let Some(row) = self.invoice.rows.get(index) else {
            return;
        };
        match row
            .to_dto()
            .and_then(|item| serde_json::to_value(item).map_err(|error| error.to_string()))
        {
            Ok(value) => {
                self.item_editor = Some((
                    index,
                    Editor {
                        record: value.clone(),
                        baseline: value,
                        buffers: Default::default(),
                        schema: contracts::schema("ApiInvoiceItemDto"),
                        id: 0,
                        invalid: None,
                    },
                ))
            }
            Err(error) => self.error = Some(error),
        }
    }
    pub(super) fn item_dialog(&mut self, ctx: &egui::Context) {
        let Some((index, mut editor)) = self.item_editor.take() else {
            return;
        };
        let mut save = false;
        let mut cancel = false;
        egui::Window::new(format!("第 {} 行 · 商品详细信息", index + 1))
            .id(egui::Id::new("item-details"))
            .collapsible(false)
            .resizable(true)
            .default_width(900.)
            .anchor(egui::Align2::CENTER_CENTER, [0., 0.])
            .show(ctx, |ui| {
                ui.add_enabled_ui(!self.busy(), |ui| {
                    egui::ScrollArea::vertical()
                        .id_salt("item-detail-fields")
                        .max_height((ctx.content_rect().height() - 190.).max(220.))
                        .show(ui, |ui| {
                            editor.invalid = forms::form(
                                ui,
                                editor.schema,
                                &mut editor.record,
                                &mut editor.buffers,
                                &self.workspace.lookups,
                                "item-detail",
                                &mut self.probes,
                            );
                        });
                    if let Some(error) = &editor.invalid {
                        ui.colored_label(theme::ERROR, error);
                    }
                    ui.separator();
                    ui.horizontal(|ui| {
                        save = ui.add(theme::primary("应用到商品表格")).clicked();
                        cancel = ui.button("返回").clicked();
                    });
                });
            });
        if save && editor.invalid.is_none() {
            if editor.record["totalPrice"] != editor.baseline["totalPrice"]
                && editor.record["unitPrice"] == editor.baseline["unitPrice"]
            {
                editor.record["priceCalculationMode"] = json!("LineAmountDriven");
            }
            let result = serde_json::from_value::<ApiInvoiceItemDto>(editor.record.clone())
                .map_err(|error| error.to_string())
                .and_then(|item| {
                    let row = ItemRow::from_dto(item);
                    row.to_dto().map(ItemRow::from_dto)
                });
            match result {
                Ok(row) if index < self.invoice.rows.len() => {
                    self.invoice.rows[index] = row;
                    self.invoice_changed(ctx);
                    return;
                }
                Ok(_) => {
                    self.error = Some("原商品行已变化，请重新打开。".into());
                    return;
                }
                Err(error) => editor.invalid = Some(error),
            }
        }
        if !cancel {
            self.item_editor = Some((index, editor));
        }
    }
}
