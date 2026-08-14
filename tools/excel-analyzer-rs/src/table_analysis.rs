use super::*;

pub(super) fn build_field_candidates(
    cells: &[Vec<String>],
    header_row: usize,
    header_depth: usize,
) -> Vec<FieldCandidate> {
    let mut fields = Vec::new();
    let max_columns = cells
        .iter()
        .skip(header_row)
        .take(header_depth)
        .map(Vec::len)
        .max()
        .unwrap_or(0)
        .min(MAX_PROFILE_COLUMNS);

    let mut current_group = String::new();
    for column in 0..max_columns {
        let mut path = Vec::new();
        for row_offset in 0..header_depth {
            let row = header_row + row_offset;
            let value = get_cell(cells, row, column);
            if !value.is_empty() {
                if row_offset == 0 {
                    current_group = if is_group_header(value) {
                        value.to_string()
                    } else {
                        String::new()
                    };
                }
                path.push(value.to_string());
            } else if row_offset > 0 && !current_group.is_empty() {
                path.push(current_group.clone());
            }
        }

        path.dedup();
        let detected = detect_field_from_path(&path).or_else(|| {
            let joined = normalize_text(&path.join(" "));
            detect_field(&joined)
        });
        if let Some((canonical_field, confidence)) = detected {
            fields.push(FieldCandidate {
                canonical_field,
                column: column + 1,
                header_path: path,
                confidence,
            });
        }
    }

    if !fields
        .iter()
        .any(|field| field.canonical_field == "StyleName")
    {
        if let (Some(style_no), Some(quantity)) = (
            fields
                .iter()
                .find(|field| field.canonical_field == "StyleNo"),
            fields
                .iter()
                .find(|field| field.canonical_field == "Quantity"),
        ) {
            if style_no.column + 1 < quantity.column {
                fields.push(FieldCandidate {
                    canonical_field: "StyleName".to_string(),
                    column: style_no.column + 1,
                    header_path: vec!["inferred detail text".to_string()],
                    confidence: 0.55,
                });
            }
        }
    }

    disambiguate_weight_fields(fields)
}

pub(super) fn count_detected_fields_in_row(cells: &[Vec<String>], row: usize) -> usize {
    let Some(values) = cells.get(row) else {
        return 0;
    };

    values
        .iter()
        .filter(|value| detect_field(&normalize_text(value)).is_some())
        .count()
}

pub(super) fn detect_field_from_path(path: &[String]) -> Option<(String, f32)> {
    if let Some(detected) = detect_field_from_header_path_context(path) {
        return Some(detected);
    }

    path.iter()
        .rev()
        .find_map(|value| detect_field(&normalize_text(value)))
}

pub(super) fn detect_field_from_header_path_context(path: &[String]) -> Option<(String, f32)> {
    if header_path_contains(
        path,
        &[
            "中文品名",
            "中文名称",
            "品名中文",
            "中文描述",
            "报关品名",
            "货物中文名称",
            "中文货物名称",
        ],
    ) {
        return Some(("StyleNameCN".to_string(), 0.86));
    }

    if header_path_contains(path, &["品牌", "品牌名", "商标", "brand", "label"]) {
        return Some(("Brand".to_string(), 0.82));
    }

    if header_path_contains(
        path,
        &[
            "箱子尺寸",
            "箱规",
            "外箱尺寸",
            "包装尺寸",
            "尺寸",
            "长宽高",
            "carton size",
            "ctn size",
            "cartonsize",
            "dimension",
            "dimensions",
        ],
    ) {
        return Some(("Dimension".to_string(), 0.90));
    }

    if header_path_contains(
        path,
        &[
            "客人款号",
            "款号",
            "货号",
            "产品编号",
            "styleno",
            "style no",
            "style no.",
            "style code",
            "sku",
        ],
    ) {
        return Some(("StyleNo".to_string(), 0.92));
    }

    if header_path_contains(
        path,
        &[
            "英文品名",
            "英文名称",
            "货物英文品名",
            "货物名称",
            "商品名称",
            "产品名称",
            "stylename",
            "description",
            "product description",
        ],
    ) {
        return Some(("StyleName".to_string(), 0.88));
    }

    None
}

pub(super) fn header_path_contains(path: &[String], aliases: &[&str]) -> bool {
    path.iter().any(|value| {
        let normalized_value = normalize_text(value);
        aliases
            .iter()
            .any(|alias| normalized_value == normalize_text(alias))
    })
}

pub(super) fn disambiguate_weight_fields(mut fields: Vec<FieldCandidate>) -> Vec<FieldCandidate> {
    let mut gross_columns = Vec::new();
    let mut net_columns = Vec::new();

    for (index, field) in fields.iter().enumerate() {
        match field.canonical_field.as_str() {
            "GrossWeight" => gross_columns.push(index),
            "NetWeight" => net_columns.push(index),
            _ => {}
        }
    }

    if gross_columns.len() >= 2 {
        fields[gross_columns[0]].canonical_field = "GWPerCtn".to_string();
        fields[gross_columns[1]].canonical_field = "GWTotal".to_string();
    } else if gross_columns.len() == 1
        && fields
            .iter()
            .any(|field| field.canonical_field == "GWTotal")
    {
        fields[gross_columns[0]].canonical_field = "GWPerCtn".to_string();
    }

    if gross_columns.len() == 1
        && fields
            .iter()
            .any(|field| field.canonical_field == "GWPerCtn")
    {
        fields[gross_columns[0]].canonical_field = "GWTotal".to_string();
    }

    if net_columns.len() >= 2 {
        fields[net_columns[0]].canonical_field = "NWPerCtn".to_string();
        fields[net_columns[1]].canonical_field = "NWTotal".to_string();
    } else if net_columns.len() == 1
        && fields
            .iter()
            .any(|field| field.canonical_field == "NWTotal")
    {
        fields[net_columns[0]].canonical_field = "NWPerCtn".to_string();
    }

    if net_columns.len() == 1
        && fields
            .iter()
            .any(|field| field.canonical_field == "NWPerCtn")
    {
        fields[net_columns[0]].canonical_field = "NWTotal".to_string();
    }

    fields
}

pub(super) fn detect_field(header: &str) -> Option<(String, f32)> {
    let candidates = [
        (
            "PoNumber",
            0.85,
            [
                "客人订单号",
                "客户订单号",
                "订单号",
                "采购订单号",
                "销售订单号",
                "po number",
                "po no",
                "pono",
                "ponumber",
                "po",
                "po#",
                "purchaseorder",
                "orderno",
                "order",
            ]
            .as_slice(),
        ),
        (
            "StyleNo",
            0.90,
            [
                "客人款号",
                "款号",
                "货号",
                "品号",
                "产品编号",
                "产品货号",
                "商品编号",
                "商品货号",
                "物料号",
                "物料编号",
                "物料编码",
                "零件号",
                "零件编号",
                "部件号",
                "部件编号",
                "配件号",
                "产品型号",
                "款式",
                "型号",
                "款号款名",
                "款名款号",
                "styleno",
                "style#",
                "stylecode",
                "itemno",
                "item#",
                "itemcode",
                "itemnumber",
                "sku",
                "skuno",
                "productcode",
                "productno",
                "productid",
                "partno",
                "partnumber",
                "partcode",
                "partid",
                "materialno",
                "materialcode",
                "materialnumber",
                "componentno",
                "componentcode",
                "goodsno",
                "goodscode",
                "articleno",
                "article",
                "model",
                "modelno",
            ]
            .as_slice(),
        ),
        (
            "StyleNameCN",
            0.80,
            [
                "中文品名",
                "中文名称",
                "品名中文",
                "款式描述",
                "中文描述",
                "报关品名",
                "货物中文名称",
                "中文货物名称",
            ]
            .as_slice(),
        ),
        (
            "StyleName",
            0.85,
            [
                "英文品名",
                "英文名称",
                "品名",
                "名称",
                "货物英文品名",
                "货物名称",
                "货物描述",
                "商品名称",
                "商品描述",
                "产品名称",
                "产品描述",
                "款名",
                "物料名称",
                "物料描述",
                "零件名称",
                "零件描述",
                "部件名称",
                "部件描述",
                "品名规格",
                "规格描述",
                "style",
                "stylename",
                "description",
                "desc",
                "name",
                "product",
                "productname",
                "productdescription",
                "goods",
                "goodsname",
                "goodsdescription",
                "itemname",
                "itemdescription",
                "descriptionofgoods",
                "commodity",
                "commodityname",
                "commoditydescription",
                "materialname",
                "materialdescription",
                "partname",
                "partdescription",
                "componentname",
                "componentdescription",
            ]
            .as_slice(),
        ),
        (
            "FabricComposition",
            0.75,
            [
                "面料",
                "面料成分",
                "成份",
                "成分",
                "材质",
                "fabric",
                "composition",
                "material",
            ]
            .as_slice(),
        ),
        (
            "Brand",
            0.75,
            ["品牌", "品牌名", "商标", "brand", "label"].as_slice(),
        ),
        (
            "HSCode",
            0.90,
            [
                "hscode",
                "hs",
                "hs编码",
                "海关编码",
                "商品编码",
                "商品hs编码",
                "编码",
                "税号",
                "税则号",
                "customscode",
                "commoditycode",
                "tariffcode",
                "tariffno",
                "htscode",
            ]
            .as_slice(),
        ),
        (
            "Origin",
            0.75,
            [
                "原产地",
                "产地",
                "原产国",
                "生产国",
                "制造国",
                "境内货源地",
                "origin",
                "madein",
                "countryoforigin",
                "countryofmanufacture",
                "manufacturingcountry",
            ]
            .as_slice(),
        ),
        (
            "Quantity",
            0.95,
            [
                "数量",
                "总数量",
                "件数",
                "出货数量",
                "装运数量",
                "交货数量",
                "申报数量",
                "quantity",
                "qty",
                "pcs",
                "piece",
                "pieces",
                "qtypcs",
                "pcsqty",
                "totalqty",
                "units",
                "totalunits",
                "shipqty",
                "shippedqty",
                "deliveryqty",
                "exportqty",
                "declaredqty",
                "orderqty",
                "orderedqty",
            ]
            .as_slice(),
        ),
        (
            "UnitEN",
            0.70,
            [
                "单位",
                "数量单位",
                "计量单位",
                "英文单位",
                "unit",
                "uom",
                "unitofmeasure",
                "measureunit",
                "um",
            ]
            .as_slice(),
        ),
        ("UnitCN", 0.70, ["中文单位", "单位中文"].as_slice()),
        (
            "Cartons",
            0.95,
            [
                "箱数",
                "总箱数",
                "箱量",
                "包装件数",
                "包装数量",
                "包装",
                "件数箱数",
                "carton",
                "cartons",
                "ctns",
                "ctn",
                "ctnqty",
                "cartonqty",
                "noofctns",
                "noofcartons",
                "packages",
                "packageqty",
                "packagesqty",
                "numberofpackages",
                "pkg",
                "pkgs",
                "boxes",
                "box",
                "cases",
                "case",
                "pallets",
                "pallet",
            ]
            .as_slice(),
        ),
        (
            "CtnUnitEN",
            0.70,
            ["箱数单位", "ctnunit", "cartonunit"].as_slice(),
        ),
        (
            "Dimension",
            0.85,
            [
                "箱子尺寸",
                "箱规",
                "外箱尺寸",
                "包装尺寸",
                "规格",
                "尺寸",
                "长宽高",
                "cartonsize",
                "ctnsize",
                "cartondimension",
                "cartondimensions",
                "packingsize",
                "packsize",
                "packagedimension",
                "packagedimensions",
                "dimension",
                "dimensions",
                "size",
                "measurement",
            ]
            .as_slice(),
        ),
        ("Length", 0.90, ["长", "长度", "长cm", "length"].as_slice()),
        ("Width", 0.90, ["宽", "宽度", "宽cm", "width"].as_slice()),
        ("Height", 0.90, ["高", "高度", "高cm", "height"].as_slice()),
        (
            "Volume",
            0.95,
            [
                "体积",
                "总体积",
                "体积立方数",
                "立方数",
                "立方米",
                "方数",
                "空间",
                "m3",
                "m³",
                "cbm",
                "cbms",
                "totalcbm",
                "totalcbms",
                "volume",
                "measurement",
                "meas",
            ]
            .as_slice(),
        ),
        (
            "GWPerCtn",
            0.95,
            [
                "毛重箱",
                "毛重每箱",
                "每箱毛重",
                "单箱毛重",
                "毛重ctn",
                "gwctn",
                "gwperctn",
                "gwcarton",
                "gwctns",
                "grossweightctn",
                "grossweightcarton",
                "grossweightpercarton",
            ]
            .as_slice(),
        ),
        (
            "GWTotal",
            0.95,
            [
                "总毛重",
                "毛重总",
                "合计毛重",
                "毛重合计",
                "总重量",
                "毛重kg",
                "totalgw",
                "gwt",
                "grosskg",
                "grosskgs",
                "gwkg",
                "gwkgs",
                "totalgrossweight",
                "grossweighttotal",
                "grossweightkg",
                "grossweightkgs",
                "grosswt",
                "totalgross",
                "totalgrosskg",
                "totalgrosskgs",
                "totalgwkg",
                "totalgwkgs",
                "totalgweight",
            ]
            .as_slice(),
        ),
        (
            "GrossWeight",
            0.75,
            ["毛重", "gw", "grossweight"].as_slice(),
        ),
        (
            "NWPerCtn",
            0.95,
            [
                "净重箱",
                "净重每箱",
                "每箱净重",
                "单箱净重",
                "净重ctn",
                "nwctn",
                "nwperctn",
                "nwcarton",
                "nwctns",
                "netweightctn",
                "netweightcarton",
                "netweightpercarton",
            ]
            .as_slice(),
        ),
        (
            "NWTotal",
            0.95,
            [
                "总净重",
                "净重总",
                "合计净重",
                "净重合计",
                "净重kg",
                "totalnw",
                "nwt",
                "netkg",
                "netkgs",
                "nwkg",
                "nwkgs",
                "totalnetweight",
                "netweighttotal",
                "netweightkg",
                "netweightkgs",
                "netwt",
                "totalnet",
                "totalnetkg",
                "totalnetkgs",
                "totalnwkg",
                "totalnwkgs",
                "totalnweight",
            ]
            .as_slice(),
        ),
        ("NetWeight", 0.75, ["净重", "nw", "netweight"].as_slice()),
        (
            "UnitPrice",
            0.90,
            [
                "单价",
                "单价usd",
                "销售单价",
                "报关单价",
                "申报单价",
                "fob价",
                "unitprice",
                "unitpriceusd",
                "unitvalue",
                "unitvalueusd",
                "unitamount",
                "unitcost",
                "price",
                "priceusd",
                "priceperunit",
                "fobusd",
                "uprice",
                "customsunitprice",
                "declaredunitprice",
            ]
            .as_slice(),
        ),
        (
            "TotalPrice",
            0.95,
            [
                "总价",
                "金额",
                "金额usd",
                "总金额",
                "合计金额",
                "货值",
                "申报总价",
                "申报金额",
                "小计",
                "amount",
                "amountusd",
                "lineamount",
                "linevalue",
                "itemamount",
                "goodsvalue",
                "customsvalue",
                "declaredvalue",
                "exportamount",
                "invoiceamount",
                "total",
                "totalprice",
                "totalamount",
                "totalvalue",
                "subtotal",
                "value",
            ]
            .as_slice(),
        ),
    ];

    candidates
        .into_iter()
        .filter_map(|(field, confidence, aliases)| {
            aliases
                .iter()
                .filter_map(|alias| header_alias_match_score(header, alias))
                .max()
                .map(|score| (field.to_string(), confidence as f32, score))
        })
        .max_by(|left, right| {
            left.2
                .cmp(&right.2)
                .then_with(|| left.1.total_cmp(&right.1))
        })
        .map(|(field, confidence, _)| (field, confidence))
}

pub(super) fn header_alias_match_score(header: &str, alias: &str) -> Option<usize> {
    let normalized_alias = normalize_text(alias);
    if normalized_alias.is_empty() {
        return None;
    }

    if header == normalized_alias {
        return Some(10_000 + normalized_alias.chars().count());
    }

    let contains_cjk = normalized_alias.chars().any(is_cjk);
    let latin_length = normalized_alias
        .chars()
        .filter(|character| character.is_ascii_alphanumeric())
        .count();
    if (!contains_cjk && latin_length <= 3) || alias_requires_exact_match(&normalized_alias) {
        return None;
    }

    header
        .contains(&normalized_alias)
        .then_some(normalized_alias.chars().count())
}

pub(super) fn alias_requires_exact_match(alias: &str) -> bool {
    matches!(
        alias,
        "order"
            | "unit"
            | "units"
            | "price"
            | "value"
            | "total"
            | "product"
            | "goods"
            | "name"
            | "box"
            | "case"
    )
}

pub(super) fn is_group_header(value: &str) -> bool {
    let normalized = normalize_text(value);
    ["毛重", "净重", "体积", "体积立方数", "立方数"]
        .iter()
        .any(|candidate| normalized.contains(&normalize_text(candidate)))
}

pub(super) fn find_first_data_row(
    cells: &[Vec<String>],
    start_row: usize,
    fields: &[FieldCandidate],
) -> Option<usize> {
    let quantity_column = fields
        .iter()
        .find(|field| field.canonical_field == "Quantity")
        .map(|field| field.column - 1)?;

    let style_columns: Vec<usize> = fields
        .iter()
        .filter(|field| field.canonical_field == "StyleNo" || field.canonical_field == "StyleName")
        .map(|field| field.column - 1)
        .collect();

    for row in start_row..cells.len().min(start_row + 40) {
        let quantity = get_cell(cells, row, quantity_column);
        if parse_excel_decimal(quantity).is_none() {
            continue;
        }

        if style_columns.iter().any(|column| {
            let value = get_cell(cells, row, *column);
            !value.is_empty() && !is_summary_or_note_row(value)
        }) {
            return Some(row);
        }
    }

    None
}

pub(super) fn is_summary_or_note_row(value: &str) -> bool {
    let normalized = normalize_text(value);
    normalized.contains("合计")
        || normalized.contains("总计")
        || normalized.contains("小计")
        || normalized.contains("total")
        || normalized.contains("subtotal")
        || normalized.contains("唛头")
        || normalized.contains("shippingmark")
}

pub(super) fn collect_sample_rows(
    cells: &[Vec<String>],
    start_row: usize,
    fields: &[FieldCandidate],
) -> Vec<SampleRow> {
    let mut rows = Vec::new();
    for row in start_row..cells.len().min(start_row + 8) {
        if fields
            .iter()
            .any(|field| is_summary_or_note_row(get_cell(cells, row, field.column - 1)))
        {
            break;
        }

        let mut values = Vec::new();
        for field in fields {
            let raw = get_cell(cells, row, field.column - 1).to_string();
            if raw.is_empty() {
                continue;
            }

            values.push(SampleValue {
                canonical_field: field.canonical_field.clone(),
                raw: raw.clone(),
                normalized_decimal: parse_excel_decimal(&raw),
                normalized_dimension: parse_dimension(&raw),
            });
        }

        if !values.is_empty() {
            rows.push(SampleRow {
                row: row + 1,
                values,
            });
        }
    }

    rows
}

pub(super) fn get_cell(cells: &[Vec<String>], row: usize, column: usize) -> &str {
    cells
        .get(row)
        .and_then(|values| values.get(column))
        .map(|value| value.trim())
        .unwrap_or("")
}

pub(super) fn cell_to_string(cell: &Data) -> String {
    match cell {
        Data::Empty => String::new(),
        Data::String(value) => bounded_cell_text(value),
        Data::Float(value) => trim_number(*value),
        Data::Int(value) => value.to_string(),
        Data::Bool(value) => value.to_string(),
        Data::DateTime(value) => trim_number(value.as_f64()),
        Data::DateTimeIso(value) => bounded_cell_text(value),
        Data::DurationIso(value) => bounded_cell_text(value),
        Data::Error(value) => format!("{value:?}"),
    }
}

pub(super) fn bounded_cell_text(value: &str) -> String {
    value.trim().chars().take(MAX_CELL_CHARACTERS).collect()
}

pub(super) fn trim_number(value: f64) -> String {
    if value.fract().abs() < f64::EPSILON {
        format!("{}", value as i64)
    } else {
        let text = format!("{value:.6}");
        text.trim_end_matches('0').trim_end_matches('.').to_string()
    }
}

pub(super) fn parse_excel_decimal(text: &str) -> Option<f64> {
    let mut normalized = text
        .trim()
        .replace('\u{00a0}', " ")
        .replace('，', ",")
        .replace('．', ".")
        .replace('－', "-")
        .replace('（', "(")
        .replace('）', ")");

    if normalized.is_empty() {
        return None;
    }

    let negative = normalized.starts_with('(') && normalized.ends_with(')');
    normalized = normalized
        .chars()
        .filter(|c| c.is_ascii_digit() || matches!(c, '.' | ',' | '-' | '(' | ')'))
        .collect::<String>()
        .trim_matches(['(', ')'])
        .replace(',', "");

    if !normalized.chars().any(|c| c.is_ascii_digit()) {
        return None;
    }

    normalized
        .parse::<f64>()
        .ok()
        .map(|value| if negative { -value } else { value })
}

pub(super) fn parse_dimension(text: &str) -> Option<DimensionValue> {
    let trimmed = text.trim();
    let digits_only: String = trimmed.chars().filter(|c| c.is_ascii_digit()).collect();
    if trimmed == digits_only && digits_only.len() == 6 {
        return Some(DimensionValue {
            length: digits_only[0..2].parse().ok()?,
            width: digits_only[2..4].parse().ok()?,
            height: digits_only[4..6].parse().ok()?,
        });
    }

    let mut numbers = Vec::new();
    let mut current = String::new();
    for c in trimmed.chars() {
        if c.is_ascii_digit() || c == '.' {
            current.push(c);
        } else if !current.is_empty() {
            numbers.push(current.clone());
            current.clear();
        }
    }
    if !current.is_empty() {
        numbers.push(current);
    }

    if numbers.len() == 3 {
        return Some(DimensionValue {
            length: numbers[0].parse().ok()?,
            width: numbers[1].parse().ok()?,
            height: numbers[2].parse().ok()?,
        });
    }

    None
}

pub(super) fn normalize_text(value: &str) -> String {
    value
        .chars()
        .filter(|c| c.is_alphanumeric() || is_cjk(*c))
        .flat_map(char::to_lowercase)
        .collect()
}

pub(super) fn is_cjk(value: char) -> bool {
    ('\u{4e00}'..='\u{9fff}').contains(&value)
}
