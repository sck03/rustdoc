use export_doc_domain::generated_api::*;
use export_doc_report::packing;
use serde::{Serialize, de::DeserializeOwned};
use serde_json::{Value, json};

fn fixture<T: Default + Serialize + DeserializeOwned>(value: Value) -> T {
    let defaults = serde_json::to_value(T::default()).unwrap();
    serde_json::from_value(export_doc_contracts::contracts::overlay(defaults, &value)).unwrap()
}

#[test]
fn job_sheet_keeps_three_dimensional_overview_and_groups_loading_instructions() {
    let mut item: ApiPackedCargoItemDto = fixture(
        json!({"name":"纸箱甲","width":50,"height":50,"occupiedHeight":50,"loadCount":1,"unitsRepresented":1,"colorArgb":-16744448}),
    );
    let mut blocks = vec![item.clone()];
    item.x = 50.into();
    blocks.push(item.clone());
    item.base_height = 50.into();
    blocks.push(item.clone());
    let analysis: ApiContainerPackingAnalysisDto = fixture(
        json!({"packedPackages":3,"unpackedPackages":2,"packedWeight":30,"packedItems":blocks}),
    );
    let request: ApiContainerPackingPdfRequest = fixture(
        json!({"projectName":"现场装柜","containerType":"测试箱","container":{"length":200,"width":100,"height":100,"volume":2,"maxWeight":1000},"analysis":analysis}),
    );
    let document = packing::document(&request).unwrap();
    assert_eq!(document.pages.len(), 1);
    let svg = &document.pages[0].svg;
    for label in [
        "三维总览",
        "俯视",
        "侧视",
        "柜头在左",
        "柜门在右",
        "未装货物",
        "离地 0",
        "离地 50",
        "2 件",
        "1 件",
    ] {
        assert!(svg.contains(label), "missing {label}");
    }
    let mut many = request.clone();
    let sample = many.analysis.as_ref().unwrap().packed_items[0].clone();
    many.analysis.as_mut().unwrap().packed_items = (0..100)
        .map(|i| {
            let mut item = sample.clone();
            item.name = format!("货物 {i}");
            item
        })
        .collect();
    let pages = packing::document(&many).unwrap();
    assert!(pages.pages.len() > 1);
    assert!(pages.pages[1].svg.contains("清单续页"));
    assert!(pages.pages.last().unwrap().svg.contains("货物 99"));
}
