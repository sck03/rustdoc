//! Product-library mapping matches the public React invoice editor contract.
use crate::{
    generated_api::{ApiInvoiceItemDto, ApiProductDto},
    invoice::{ItemRow, normalize},
};

pub fn invoice_item(product: &ApiProductDto, invoice_id: i64) -> ApiInvoiceItemDto {
    ApiInvoiceItemDto {
        invoice_id,
        style_no: normalize(&product.product_code),
        style_name: normalize(&product.name_en),
        style_name_cn: normalize(&product.name_cn),
        fabric_composition: normalize(&product.material),
        brand: normalize(&product.brand),
        hs_code: normalize(&product.hs_code),
        origin: normalize(&product.origin),
        unit_en: normalize(&product.unit_en),
        unit_cn: normalize(&product.unit_cn),
        length: product.length,
        width: product.width,
        height: product.height,
        gw_per_ctn: product.gw_per_ctn,
        nw_per_ctn: product.nw_per_ctn,
        pcs_per_ctn: product.pcs_per_ctn,
        ctn_unit_en: normalize(&product.package_unit_en),
        ctn_unit_cn: normalize(&product.package_unit_cn),
        unit_price: product.default_price,
        tax_rebate_rate: product.tax_rebate_rate,
        ..ItemRow::blank().original
    }
}
pub fn from_invoice_item(
    item: &ApiInvoiceItemDto,
    existing: Option<ApiProductDto>,
) -> ApiProductDto {
    let mut product = existing.unwrap_or_else(|| ApiProductDto {
        created_at: "1970-01-01T00:00:00Z".into(),
        updated_at: "1970-01-01T00:00:00Z".into(),
        ..Default::default()
    });
    product.product_code = normalize(&item.style_no);
    product.name_en = normalize(&item.style_name);
    product.name_cn = normalize(&item.style_name_cn);
    product.material = normalize(&item.fabric_composition);
    product.brand = normalize(&item.brand);
    product.hs_code = normalize(&item.hs_code);
    product.origin = normalize(&item.origin);
    product.unit_en = normalize(&item.unit_en);
    product.unit_cn = normalize(&item.unit_cn);
    product.length = item.length;
    product.width = item.width;
    product.height = item.height;
    product.gw_per_ctn = item.gw_per_ctn;
    product.nw_per_ctn = item.nw_per_ctn;
    product.pcs_per_ctn = item.pcs_per_ctn;
    product.package_unit_en = normalize(&item.ctn_unit_en);
    product.package_unit_cn = normalize(&item.ctn_unit_cn);
    product.default_price = item.unit_price;
    product.tax_rebate_rate = item.tax_rebate_rate;
    product
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn product_names_packaging_and_exact_prices_survive_the_invoice_round_trip() {
        let product = ApiProductDto {
            product_code: " P-001 ".into(),
            name_en: "Cotton coat".into(),
            name_cn: "棉外套".into(),
            material: "Cotton".into(),
            default_price: "123.4567".parse().unwrap(),
            package_unit_en: "CTNS".into(),
            row_version: "native:2".into(),
            description: "Master description".into(),
            ..Default::default()
        };
        let item = invoice_item(&product, 123);
        assert_eq!(item.style_no, "P-001");
        assert_eq!(item.style_name_cn, "棉外套");
        assert_eq!(item.ctn_unit_en, "CTNS");
        assert_eq!(item.unit_price.to_string(), "123.4567");
        let saved = from_invoice_item(&item, Some(product));
        assert_eq!(saved.row_version, "native:2");
        assert_eq!(saved.description, "Master description");
        assert_eq!(saved.default_price, item.unit_price);
    }
}
