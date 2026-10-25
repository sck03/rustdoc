//! Page validation is independent of template contents and rendering.
use super::Page;
use crate::contracts;
#[cfg(test)]
use crate::test_design_fixture as fixture;
use export_doc_contracts::report_pages;

pub fn validate(page: &Page) -> Result<(), String> {
    let landscape = match page.orientation.as_str() {
        "Portrait" => false,
        "Landscape" => true,
        _ => return Err("纸张方向无效。".into()),
    };
    let actual = (page.width_hundredth_mm, page.height_hundredth_mm);
    if page.size == "Custom" {
        let range = report_pages::MIN_DIMENSION..=report_pages::MAX_DIMENSION;
        if !range.contains(&actual.0) || !range.contains(&actual.1) {
            return Err("自定义纸张宽高须在 40–600 mm 之间。".into());
        }
        if (landscape && actual.0 < actual.1) || (!landscape && actual.0 > actual.1) {
            return Err("纸张方向与宽高不一致。".into());
        }
    } else if report_pages::dimensions(&page.size, landscape) != Some(actual) {
        return Err("纸张名称、方向与标准尺寸不一致。".into());
    }
    let max_margin = contracts::schema("ApiReportTemplateV3LimitsContract")["properties"]
        ["maxPageMarginHundredthMm"]["default"].as_i64().expect("page margin limit") as i32;
    let margins = [
        page.margin_top_hundredth_mm,
        page.margin_right_hundredth_mm,
        page.margin_bottom_hundredth_mm,
        page.margin_left_hundredth_mm,
    ];
    if margins
        .iter()
        .any(|value| !(0..=max_margin).contains(value))
    {
        return Err("页边距须在 0–60 mm 之间。".into());
    }
    if page.margin_left_hundredth_mm + page.margin_right_hundredth_mm >= actual.0
        || page.margin_top_hundredth_mm + page.margin_bottom_hundredth_mm >= actual.1
    {
        return Err("页边距之间必须保留可打印区域。".into());
    }
    if !(6. ..=48.).contains(&page.font_size_pt) {
        return Err("页面字号须在 6–48 pt 之间。".into());
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn page_contract_matches_frontend_fixtures() {
        let cases: serde_json::Value = serde_json::from_str(include_str!(
            "../../../../tests/ReportTemplateFixtures/designer-page-cases.json"
        ))
        .unwrap();
        for case in cases.as_array().unwrap() {
            let mut value = serde_json::to_value(fixture::invoice().page).unwrap();
            for (key, field) in case["page"].as_object().unwrap() {
                value[key] = field.clone();
            }
            let accepted =
                serde_json::from_value::<Page>(value).is_ok_and(|page| validate(&page).is_ok());
            assert_eq!(
                accepted,
                case["valid"].as_bool().unwrap(),
                "{}",
                case["name"]
            );
        }
    }
}
