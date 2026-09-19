use crate::{
    Result,
    mapping::{address, cell},
    xml::{Node, Part},
};
use rust_decimal::Decimal;
use serde_json::Value;
use std::{collections::BTreeMap, str::FromStr};

pub struct Sheet(pub Node);
impl Sheet {
    pub fn new(mut node: Node) -> Result<Self> {
        let data = node
            .child_mut("sheetData")
            .ok_or("Excel 缺少单元格区域。")?;
        data.children
            .retain(|part| !matches!(part,Part::Text(text) if text.trim().is_empty()));
        for row in data.nodes_mut() {
            row.children
                .retain(|part| !matches!(part,Part::Text(text) if text.trim().is_empty()));
            row.children.sort_by_key(|part| match part {
                Part::Node(node) => node
                    .attr("r")
                    .and_then(cell)
                    .map(|(_, col)| col)
                    .unwrap_or(u32::MAX),
                _ => u32::MAX,
            });
        }
        data.children.sort_by_key(row_number);
        Ok(Self(node))
    }
    pub fn set(&mut self, row: u32, col: u32, value: &Value) -> Result<()> {
        if row == 0 || row > 11000 || col == 0 || col > 128 {
            return Err("Excel 输出坐标超出受支持范围。".into());
        }
        let data = self
            .0
            .child_mut("sheetData")
            .ok_or("Excel 缺少单元格区域。")?;
        let row_index = match data.children.binary_search_by_key(&row, row_number) {
            Ok(index) => index,
            Err(index) => {
                let mut node = data.named("row");
                node.set("r", row);
                data.children.insert(index, Part::Node(node));
                index
            }
        };
        let Part::Node(row_node) = &mut data.children[row_index] else {
            return Err("Excel 输出行无效。".into());
        };
        let reference = address(row, col);
        let col_index = match row_node
            .children
            .binary_search_by_key(&col, |part| match part {
                Part::Node(node) => node
                    .attr("r")
                    .and_then(cell)
                    .map(|(_, col)| col)
                    .unwrap_or(u32::MAX),
                _ => u32::MAX,
            }) {
            Ok(index) => index,
            Err(index) => {
                let mut node = row_node.named("c");
                node.set("r", &reference);
                row_node.children.insert(index, Part::Node(node));
                index
            }
        };
        let Part::Node(node) = &mut row_node.children[col_index] else {
            return Err("Excel 输出单元格无效。".into());
        };
        node.children.clear();
        match value {
            Value::Number(number) => {
                node.set("t", "n");
                let mut v = node.named("v");
                v.children.push(Part::Text(number.to_string()));
                node.children.push(Part::Node(v));
            }
            _ => {
                node.set("t", "inlineStr");
                let mut inline = node.named("is");
                let mut text = node.named("t");
                text.set("xml:space", "preserve");
                text.children.push(Part::Text(match value {
                    Value::String(value) => value.clone(),
                    Value::Null => String::new(),
                    value => value.to_string(),
                }));
                inline.children.push(Part::Node(text));
                node.children.push(Part::Node(inline));
            }
        }
        Ok(())
    }
    pub fn booking(&mut self) -> Result<()> {
        if self.0.child("cols").is_none() {
            let cols = self.0.named("cols");
            let index = self
                .0
                .children
                .iter()
                .position(|part| matches!(part,Part::Node(node) if node.local()=="sheetData"))
                .ok_or("Excel 缺少单元格区域。")?;
            self.0.children.insert(index, Part::Node(cols));
        }
        let cols = self.0.child_mut("cols").unwrap();
        let mut columns = BTreeMap::new();
        for node in cols.nodes() {
            let min = node
                .attr("min")
                .and_then(|v| v.parse::<u32>().ok())
                .ok_or("Excel 列范围无效。")?;
            let max = node
                .attr("max")
                .and_then(|v| v.parse::<u32>().ok())
                .ok_or("Excel 列范围无效。")?;
            if min == 0 || max < min || max > 16384 {
                return Err("Excel 列范围超出上限。".into());
            }
            for index in min..=max {
                let mut node = node.clone();
                node.set("min", index);
                node.set("max", index);
                columns.insert(index, node);
            }
        }
        for index in [8, 9, 11, 12, 13, 14] {
            let node = columns.entry(index).or_insert_with(|| {
                let mut node = cols.named("col");
                node.set("min", index);
                node.set("max", index);
                node
            });
            if index == 13 {
                let width = node
                    .attr("width")
                    .and_then(|v| Decimal::from_str(v).ok())
                    .unwrap_or_default()
                    .max(Decimal::from(16));
                node.set("width", width);
                node.set("customWidth", 1);
            } else {
                node.set("hidden", 1);
            }
        }
        cols.children = columns.into_values().map(Part::Node).collect();
        Ok(())
    }
    /// Grow the detail area before the original subtotal. Existing footer rows,
    /// merged cells and print references move together instead of being overwritten.
    pub fn extend(&mut self, start: u32, count: usize) -> Result<Option<(u32, u32)>> {
        let data = self.0.child("sheetData").ok_or("Excel 缺少单元格区域。")?;
        let subtotal = data
            .nodes()
            .filter_map(|row| {
                let index = row.attr("r")?.parse::<u32>().ok()?;
                (index > start
                    && row.nodes().any(|cell| {
                        cell.child("f")
                            .is_some_and(|f| f.text().starts_with("SUM("))
                    }))
                .then_some(index)
            })
            .min();
        let Some(at) = subtotal else {
            return Ok(None);
        };
        let required = start + count as u32;
        if required <= at {
            return Ok(None);
        }
        let added = required - at;
        let sample = data
            .nodes()
            .find(|row| row.attr("r").and_then(|n| n.parse::<u32>().ok()) == Some(at - 1))
            .cloned();
        fn shift(node: &mut Node, at: u32, added: u32) {
            if node.local() == "row" {
                if let Some(row) = node
                    .attr("r")
                    .and_then(|n| n.parse::<u32>().ok())
                    .filter(|row| *row >= at)
                {
                    node.set("r", row + added);
                }
            }
            for (key, value) in &mut node.attributes {
                if ["r", "ref", "sqref"].contains(&key.as_str()) {
                    *value = shift_references(value, at, added);
                }
            }
            if node.local() == "f" {
                let value = shift_references(&node.text(), at, added);
                node.children = vec![Part::Text(value)];
            }
            for child in node.nodes_mut() {
                shift(child, at, added);
            }
        }
        shift(&mut self.0, at, added);
        let data = self.0.child_mut("sheetData").unwrap();
        for row in at..at + added {
            let mut node = sample.clone().unwrap_or_else(|| data.named("row"));
            node.set("r", row);
            for cell_node in node.nodes_mut() {
                if let Some((_, col)) = cell_node.attr("r").and_then(cell) {
                    cell_node.set("r", address(row, col));
                    cell_node.children.clear();
                    cell_node.attributes.retain(|(key, _)| key != "t");
                }
            }
            data.children.push(Part::Node(node));
        }
        data.children.sort_by_key(|part| match part {
            Part::Node(node) => node
                .attr("r")
                .and_then(|n| n.parse::<u32>().ok())
                .unwrap_or(0),
            _ => 0,
        });
        Ok(Some((at, added)))
    }
    pub fn recalculate_sums(&mut self) -> Result<()> {
        let data = self
            .0
            .child_mut("sheetData")
            .ok_or("Excel 缺少单元格区域。")?;
        let numbers: BTreeMap<_, _> = data
            .nodes()
            .flat_map(Node::nodes)
            .filter(|node| node.attr("t").is_none_or(|kind| kind == "n"))
            .filter_map(|node| {
                Some((
                    cell(node.attr("r")?)?,
                    Decimal::from_str(&node.child("v")?.text()).ok()?,
                ))
            })
            .collect();
        for node in data.nodes_mut().flat_map(Node::nodes_mut) {
            let Some(formula) = node.child("f").map(Node::text) else {
                continue;
            };
            let sum = formula
                .strip_prefix("SUM(")
                .and_then(|body| body.strip_suffix(')'))
                .and_then(|range| range.split_once(':'))
                .and_then(|(first, last)| Some((cell(first)?, cell(last)?)));
            node.children
                .retain(|part| !matches!(part,Part::Node(node) if node.local()=="v"));
            if let Some(((first_row, first_col), (last_row, last_col))) = sum {
                let mut total = Decimal::ZERO;
                for (&(row, col), &value) in &numbers {
                    if (first_row..=last_row).contains(&row)
                        && (first_col..=last_col).contains(&col)
                    {
                        total = total.checked_add(value).ok_or("Excel 合计超出数值范围。")?;
                    }
                }
                let mut value = node.named("v");
                value.children.push(Part::Text(total.to_string()));
                node.children.push(Part::Node(value));
            }
        }
        Ok(())
    }
}

fn row_number(part: &Part) -> u32 {
    match part {
        Part::Node(node) => node
            .attr("r")
            .and_then(|n| n.parse::<u32>().ok())
            .unwrap_or(u32::MAX),
        _ => u32::MAX,
    }
}

pub fn shift_references(value: &str, at: u32, added: u32) -> String {
    let bytes = value.as_bytes();
    let mut output = String::new();
    let mut index = 0;
    let mut quoted = false;
    while index < bytes.len() {
        if bytes[index] == b'"' {
            quoted = !quoted;
            output.push('"');
            index += 1;
            continue;
        }
        if !quoted && (bytes[index].is_ascii_alphabetic() || bytes[index] == b'$') {
            let start = index;
            if bytes[index] == b'$' {
                index += 1;
            }
            while index < bytes.len() && bytes[index].is_ascii_alphabetic() {
                index += 1;
            }
            if index < bytes.len() && bytes[index] == b'$' {
                index += 1;
            }
            let number = index;
            while index < bytes.len() && bytes[index].is_ascii_digit() {
                index += 1;
            }
            if index > number && cell(&value[start..index]).is_some() {
                let row = value[number..index].parse::<u32>().unwrap();
                let range_end = start > 0 && bytes[start - 1] == b':';
                output.push_str(&value[start..number]);
                output.push_str(
                    &(if row >= at || (range_end && row == at - 1) {
                        row + added
                    } else {
                        row
                    })
                    .to_string(),
                );
            } else {
                output.push_str(&value[start..index]);
            }
        } else {
            let ch = value[index..].chars().next().unwrap();
            output.push(ch);
            index += ch.len_utf8();
        }
    }
    output
}
