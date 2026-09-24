use super::DetailCellContent;

pub struct Slot<'a> {
    pub position: f32,
    pub parts: Vec<&'a DetailCellContent>,
}
/// Separate logical lines and alignment slots without interpreting field values.
pub fn rows(parts: &[DetailCellContent]) -> Vec<Vec<Slot<'_>>> {
    let mut rows = vec![vec![Slot {
        position: 0.,
        parts: vec![],
    }]];
    for part in parts {
        if part.visible == Some(false) {
            continue;
        }
        match part.kind.as_str() {
            "LineBreak" => rows.push(vec![Slot {
                position: 0.,
                parts: vec![],
            }]),
            "ColumnBreak" => rows.last_mut().unwrap().push(Slot {
                position: part.position_percent.unwrap_or(50.),
                parts: vec![],
            }),
            _ => rows
                .last_mut()
                .unwrap()
                .last_mut()
                .unwrap()
                .parts
                .push(part),
        }
    }
    rows
}
