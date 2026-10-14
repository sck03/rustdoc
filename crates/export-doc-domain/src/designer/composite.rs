use super::DetailCellContent;

pub struct Slot<'a> {
    pub position: f32,
    pub parts: Vec<&'a DetailCellContent>,
}
pub enum Row<'a> {
    Content(Vec<Slot<'a>>),
    Spacer(f32),
}
fn line<'a>() -> Vec<Slot<'a>> {
    vec![Slot {
        position: 0.,
        parts: vec![],
    }]
}
/// Separate logical lines and intentional spacing after visibility is evaluated.
pub fn rows<'a>(parts: impl IntoIterator<Item = &'a DetailCellContent>) -> Vec<Row<'a>> {
    let mut rows = Vec::new();
    let mut current = line();
    for part in parts {
        if part.visible == Some(false) {
            continue;
        }
        match part.kind.as_str() {
            "LineBreak" => rows.push(Row::Content(std::mem::replace(&mut current, line()))),
            "Spacer" => {
                if current.iter().any(|slot| !slot.parts.is_empty()) {
                    rows.push(Row::Content(std::mem::replace(&mut current, line())));
                } else {
                    current = line();
                }
                rows.push(Row::Spacer(part.height_mm.unwrap_or(3.)));
            }
            "ColumnBreak" => current.push(Slot {
                position: part.position_percent.unwrap_or(50.),
                parts: vec![],
            }),
            _ => current.last_mut().unwrap().parts.push(part),
        }
    }
    if !matches!(rows.last(), Some(Row::Spacer(_)))
        || current.iter().any(|slot| !slot.parts.is_empty())
    {
        rows.push(Row::Content(current));
    }
    rows
}
