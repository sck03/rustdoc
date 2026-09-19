use crate::{generated_api::*, sales::clean};
use rust_decimal::{Decimal as D, prelude::ToPrimitive};

#[derive(Clone, Debug)]
pub struct Dimensions {
    pub length: D,
    pub width: D,
    pub height: D,
    pub volume: D,
    pub max_weight: D,
}
#[derive(Clone, Debug)]
pub struct Rules {
    pub rotate: bool,
    pub pallet: bool,
    pub pallet_length: D,
    pub pallet_width: D,
    pub pallet_height: D,
    pub pallet_weight: D,
    pub enforce_center: bool,
    pub tolerance: D,
    pub support: D,
    pub same_footprint: bool,
}
#[derive(Clone, Debug)]
pub struct Request {
    pub container: Dimensions,
    pub rules: Rules,
    pub(super) items: Vec<Item>,
    pub(super) volume_count: i64,
    pub(super) weight_count: i64,
}
#[derive(Clone, Debug)]
pub(super) struct Item {
    pub name: String,
    pub length: D,
    pub width: D,
    pub height: D,
    pub weight: D,
    pub top_load: D,
    pub count: i64,
    pub units: i64,
    pub color: i64,
    pub pallet: bool,
    pub zone: usize,
    pub sequence: i64,
    pub group: String,
}
impl Item {
    pub fn group_key(&self) -> (i64, String) {
        (self.sequence, self.group.to_lowercase())
    }
}
impl Dimensions {
    pub fn new(dto: &ApiContainerDimensionsDto) -> Result<Self, String> {
        let result = Self {
            length: D::from(dto.length.unwrap_or_default()),
            width: D::from(dto.width.unwrap_or_default()),
            height: D::from(dto.height.unwrap_or_default()),
            volume: dto.volume.unwrap_or_default(),
            max_weight: dto.max_weight.unwrap_or_default(),
        };
        for (value, label) in [
            (result.length, "集装箱长度"),
            (result.width, "集装箱宽度"),
            (result.height, "集装箱高度"),
        ] {
            dimension(value, label)?;
        }
        bounded(result.volume, D::from(1_000_000_000), "集装箱体积")?;
        bounded(result.max_weight, D::from(1_000_000_000), "最大载重")?;
        Ok(result)
    }
}
impl Rules {
    pub fn new(dto: &ApiContainerPackingRulesDto) -> Result<Self, String> {
        let result = Self {
            rotate: dto.allow_rotation.unwrap_or(true),
            pallet: dto.use_pallet_constraints.unwrap_or(false),
            pallet_length: D::from(dto.default_pallet_length.unwrap_or(120)),
            pallet_width: D::from(dto.default_pallet_width.unwrap_or(100)),
            pallet_height: D::from(dto.default_pallet_height.unwrap_or(15)),
            pallet_weight: dto.default_pallet_weight.unwrap_or(D::from(25)),
            enforce_center: dto.enforce_center_of_gravity.unwrap_or(false),
            tolerance: dto
                .center_of_gravity_tolerance_percent
                .unwrap_or(D::from(20)),
            support: dto.minimum_support_area_percent.unwrap_or(D::from(100)),
            same_footprint: dto.require_same_footprint_stacking.unwrap_or(false),
        };
        dimension(result.pallet_length, "托盘长度")?;
        dimension(result.pallet_width, "托盘宽度")?;
        bounded(result.pallet_height, D::from(100_000), "托盘高度")?;
        bounded(result.pallet_weight, D::from(1_000_000_000), "托盘重量")?;
        bounded(result.tolerance, D::from(100), "重心偏差百分比")?;
        bounded(result.support, D::from(100), "最小支撑面积百分比")?;
        Ok(result)
    }
}
impl Request {
    pub fn new(dto: &ApiContainerPackingAnalyzeRequest) -> Result<Self, String> {
        let container = Dimensions::new(dto.container.as_ref().ok_or("集装箱尺寸不能为空。")?)?;
        let rules = Rules::new(&dto.rules.clone().unwrap_or_default())?;
        let cargo = dto.cargo_items.as_deref().unwrap_or_default();
        if cargo.is_empty() || cargo.len() > 200 {
            return Err("货物明细须为 1 至 200 行。".into());
        }
        let mut items = vec![];
        let mut packages = 0_i64;
        let mut placements = 0_i64;
        for (index, row) in cargo.iter().enumerate() {
            let name = clean(row.name.as_deref().unwrap_or("货物"));
            let group = clean(row.priority_group.as_deref().unwrap_or(""));
            if name.chars().count() > 200 || group.chars().count() > 100 {
                return Err("货物名称或优先组过长。".into());
            }
            let quantity = row.quantity.unwrap_or_default();
            let units = row.units_per_pallet.unwrap_or(1);
            if !(1..=1_000_000).contains(&quantity) || !(1..=1_000_000).contains(&units) {
                return Err(format!(
                    "第 {} 行数量及每托数量须为 1 至 1000000。",
                    index + 1
                ));
            }
            let length = row.length.unwrap_or_default();
            let width = row.width.unwrap_or_default();
            let height = row.height.unwrap_or_default();
            for (value, label) in [
                (length, "货物长度"),
                (width, "货物宽度"),
                (height, "货物高度"),
            ] {
                dimension(value, label)?;
            }
            let weight = row.weight.unwrap_or_default();
            let top_load = row.max_top_load_weight.unwrap_or_default();
            bounded(weight, D::from(1_000_000_000), "单件重量")?;
            bounded(top_load, D::from(1_000_000_000), "顶部承重")?;
            let zone = match row.preferred_zone.as_deref().unwrap_or("Auto") {
                "Auto" => 0,
                "Head" => 1,
                "Middle" => 2,
                "Door" => 3,
                _ => return Err("请选择有效的装载区域。".into()),
            };
            let color = row.color_argb.unwrap_or(0xFF4287F5_u32 as i32 as i64);
            if i32::try_from(color).is_err() {
                return Err("货物颜色值无效。".into());
            }
            let pallet = rules.pallet && row.use_pallet.unwrap_or(false);
            let base = Item {
                name: if name.is_empty() {
                    "货物".into()
                } else {
                    name
                },
                length: if pallet {
                    length.max(rules.pallet_length)
                } else {
                    length
                },
                width: if pallet {
                    width.max(rules.pallet_width)
                } else {
                    width
                },
                height: height + if pallet { rules.pallet_height } else { D::ZERO },
                weight,
                top_load,
                count: quantity,
                units: 1,
                color,
                pallet,
                zone,
                sequence: row.load_sequence.unwrap_or(1).max(1),
                group,
            };
            packages += quantity;
            placements += if pallet {
                (quantity + units - 1) / units
            } else {
                quantity
            };
            if packages > 1_000_000 || placements > 5_000 {
                return Err(
                    "货物最多 1000000 件、5000 个装载单元；请使用托盘约束或拆分方案。".into(),
                );
            }
            if pallet {
                for (count, units) in [
                    (quantity / units, units),
                    (i64::from(quantity % units != 0), quantity % units),
                ] {
                    if count > 0 {
                        items.push(Item {
                            count,
                            units,
                            weight: weight * D::from(units) + rules.pallet_weight,
                            ..base.clone()
                        });
                    }
                }
            } else {
                items.push(base);
            }
        }
        let volume: D = items
            .iter()
            .map(|i| i.length * i.width * i.height * D::from(i.count) / D::from(1_000_000))
            .sum();
        let weight: D = items.iter().map(|i| i.weight * D::from(i.count)).sum();
        let volume_count = needed(volume, container.volume)?;
        let weight_count = needed(weight, container.max_weight)?;
        Ok(Self {
            container,
            rules,
            items,
            volume_count,
            weight_count,
        })
    }
}
fn needed(total: D, capacity: D) -> Result<i64, String> {
    if capacity == D::ZERO {
        return Ok(0);
    }
    total
        .checked_div(capacity)
        .and_then(|n| n.ceil().to_i64())
        .filter(|n| *n <= i32::MAX as i64)
        .ok_or_else(|| "柜型容量过小，预计柜数超出范围。".into())
}
fn dimension(value: D, label: &str) -> Result<(), String> {
    if value <= D::ZERO || value > D::from(100_000) {
        Err(format!("{label}必须大于 0 且不超过 100000 厘米。"))
    } else {
        Ok(())
    }
}
fn bounded(value: D, maximum: D, label: &str) -> Result<(), String> {
    if value < D::ZERO || value > maximum {
        Err(format!("{label}必须在 0 至 {maximum} 之间。"))
    } else {
        Ok(())
    }
}
