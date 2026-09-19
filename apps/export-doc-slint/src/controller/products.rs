use super::*;
use export_doc_domain::{
    invoice::{ItemRow, normalize},
    product,
};
use serde_json::Value;
impl Desktop {
    pub fn product_action(&mut self, action: &str) {
        if self.task.is_some() {
            return;
        }
        let Some(ui) = self.ui.upgrade() else {
            return;
        };
        let app = ui.global::<App>();
        if action == "search" {
            self.request(
                LIST_PRODUCTS,
                0,
                vec![
                    ("keyword", app.get_product_search().into()),
                    ("pageSize", "100".into()),
                ],
                None,
                "products",
            );
            return;
        }
        if let Err(cause) = self.commit_cell() {
            self.error(cause);
            return;
        }
        if action == "save" {
            if !self.can(CREATE_PRODUCT) && !self.can(UPDATE_PRODUCT) {
                return;
            }
            let item = self
                .grid
                .draft
                .rows
                .get(self.grid.selection.active.row)
                .ok_or("请先选择商品明细。".to_owned())
                .and_then(ItemRow::to_dto);
            match item {
                Ok(item) if !normalize(&item.style_no).is_empty() => {
                    let code = item.style_no.clone();
                    self.pending_product = Some(item);
                    self.request(
                        LIST_PRODUCTS,
                        0,
                        vec![("keyword", code), ("pageSize", "100".into())],
                        None,
                        "product-save-candidates",
                    );
                }
                Ok(_) => self.error("保存到商品库前请填写商品编码（款号）。"),
                Err(cause) => self.error(cause),
            }
            return;
        }
        if !app.get_invoice_editable() {
            self.error("已核对发票不能直接加入商品。");
            return;
        }
        if let Some(value) = self.products.get(app.get_product_index().max(0) as usize) {
            match serde_json::from_value::<ApiProductDto>(value.clone()) {
                Ok(product) => {
                    let row = ItemRow::from_dto(product::invoice_item(
                        &product,
                        self.grid.draft.header.id,
                    ));
                    let mut draft = self.grid.draft.clone();
                    if let Some(index) = draft.rows.iter().position(ItemRow::is_blank) {
                        draft.rows[index] = row;
                    } else {
                        draft.rows.push(row);
                    }
                    self.grid.replace(draft);
                    self.sync_grid();
                    self.sync_cell(false);
                    self.sync_invoice();
                }
                Err(cause) => self.error(format!("商品资料无效：{cause}")),
            }
        }
    }
    pub fn product_save_candidates(&mut self, value: Value) {
        let Some(item) = self.pending_product.take() else {
            return;
        };
        let code = normalize(&item.style_no).to_uppercase();
        let existing = value["items"]
            .as_array()
            .into_iter()
            .flatten()
            .find(|product| {
                normalize(product["productCode"].as_str().unwrap_or("")).to_uppercase() == code
            });
        let existing = match existing
            .cloned()
            .map(serde_json::from_value::<ApiProductDto>)
            .transpose()
        {
            Ok(value) => value,
            Err(cause) => {
                self.error(cause.to_string());
                return;
            }
        };
        let product = product::from_invoice_item(&item, existing);
        let operation = if product.id > 0 {
            UPDATE_PRODUCT
        } else {
            CREATE_PRODUCT
        };
        if !self.can(operation) {
            self.error("当前账号没有新建或更新此商品的权限。");
            return;
        }
        if product.id > 0 {
            let message = format!(
                "商品库已有编码“{}”，确认用当前发票明细更新？其他历史发票保持原有快照。",
                product.product_code
            );
            self.confirm(Pending::ProductSave(Box::new(product)), &message);
        } else {
            self.save_product(product);
        }
    }
    pub fn save_product(&mut self, product: ApiProductDto) {
        self.request(
            if product.id > 0 {
                UPDATE_PRODUCT
            } else {
                CREATE_PRODUCT
            },
            product.id,
            vec![],
            Some(serde_json::json!(product)),
            "product-saved",
        );
    }
}
