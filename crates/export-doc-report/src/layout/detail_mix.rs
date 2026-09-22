use super::flow;
use crate::{ReportData, Result, error::invalid};
use export_doc_domain::designer::{Element, Kind, ReportBlock};

pub(super) fn render_preceding(
    svg: &mut String,
    data: &ReportData,
    flows: &[&Element],
    detail_top: f32,
) -> Result<()> {
    let mut ordered = flows.to_vec();
    ordered.sort_by_key(|element| (element.y_hundredth_mm, element.z_index));
    let mut previous_bottom = None;
    for element in ordered {
        reject_page_break(element, "前")?;
        let bottom =
            element.y_hundredth_mm as f32 / 100. + element.height_hundredth_mm as f32 / 100.;
        if bottom > detail_top {
            return Err(invalid(format!(
                "明细表前的普通 Flow“{}”与明细表重叠。",
                element.label
            )));
        }
        if let Some(previous_bottom) = previous_bottom
            && element.y_hundredth_mm as f32 / 100. < previous_bottom
        {
            return Err(invalid("明细表前的普通 Flow 互相重叠。"));
        }
        flow::render(svg, element, data)?;
        previous_bottom = Some(bottom);
    }
    Ok(())
}

pub(super) fn render_following(
    svg: &mut String,
    data: &ReportData,
    flows: &[&Element],
    detail_bottom: f32,
    footer_top: f32,
) -> Result<()> {
    let mut ordered = flows.to_vec();
    ordered.sort_by_key(|element| (element.y_hundredth_mm, element.z_index));
    let mut previous_bottom = None;
    for element in ordered {
        reject_page_break(element, "后")?;
        let top = element.y_hundredth_mm as f32 / 100.;
        let bottom = top + element.height_hundredth_mm as f32 / 100.;
        if top < detail_bottom {
            return Err(invalid(format!(
                "明细表后的普通 Flow“{}”与明细内容重叠。",
                element.label
            )));
        }
        if bottom > footer_top {
            return Err(invalid(format!(
                "明细表后的普通 Flow“{}”超出页脚边界。",
                element.label
            )));
        }
        if let Some(previous_bottom) = previous_bottom
            && top < previous_bottom
        {
            return Err(invalid("明细表后的普通 Flow 互相重叠。"));
        }
        flow::render(svg, element, data)?;
        previous_bottom = Some(bottom);
    }
    Ok(())
}

fn reject_page_break(element: &Element, position: &str) -> Result<()> {
    if matches!(
        &element.kind,
        Kind::Flow {
            block: ReportBlock::PageBreak(_),
            ..
        }
    ) {
        return Err(invalid(format!(
            "明细表{position}的普通 Flow 分页符暂不支持。"
        )));
    }
    Ok(())
}
