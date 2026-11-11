//! Anonymous workbook fixture shared by Excel and Engine integration tests.
use quick_xml::{
    Reader, Writer,
    events::{BytesEnd, BytesStart, BytesText, Event},
};
use std::io::{Cursor, Read, Write};

pub const TEMPLATE: &[u8] =
    include_bytes!("../../../../Resources/ExcelTemplates/single-window-coo-template.xlsx");

pub fn filled(overrides: &[(usize, &str, &str)]) -> Vec<u8> {
    let mut cells = vec![
        (1, "B3", "COO-001"),
        (1, "B4", "2026-11-11"),
        (1, "B5", "USD"),
        (1, "B6", "CIF"),
        (1, "B7", "测试出口企业"),
        (1, "B8", "TEST EXPORT CO., LTD."),
        (1, "B9", "1 EXPORT ROAD, CHINA"),
        (1, "B11", "TEST BUYER LTD."),
        (1, "B12", "2 IMPORT ROAD, THAILAND"),
        (1, "B13", "THAILAND"),
        (1, "B14", "NINGBO"),
        (1, "B15", "BANGKOK"),
        (1, "B16", "BY SEA"),
        (1, "B17", "2026-11-15"),
        (1, "B18", "N/M"),
        (1, "B19", "RC"),
        (1, "B22", "CHINA"),
        (2, "A4", "00123"),
        (2, "B4", "棉衬衫"),
        (2, "C4", "COTTON SHIRTS"),
        (2, "D4", "6109100000"),
        (2, "E4", "10"),
        (2, "F4", "PCS"),
        (2, "G4", "2"),
        (2, "H4", "CTN"),
        (2, "I4", "12"),
        (2, "J4", "10"),
        (2, "K4", "10.125"),
        (2, "L4", "101.25"),
        (2, "M4", "90.12345"),
        (2, "N4", "F001"),
        (2, "O4", "WO"),
        (2, "R4", "TWO CARTONS OF COTTON SHIRTS"),
        (2, "A6", "00124"),
        (2, "B6", "棉裤"),
        (2, "C6", "COTTON TROUSERS"),
        (2, "E6", "5"),
        (2, "L6", "50"),
        (2, "M6", "40"),
        (2, "N6", "F002"),
        (2, "S6", "VIETNAM"),
        (3, "A4", "F001"),
        (3, "B4", "测试工厂一"),
        (3, "C4", "TEST FACTORY ONE LTD."),
        (3, "D4", "3 FACTORY ROAD, CHINA"),
        (3, "E4", "012345678"),
        (3, "F4", "张三"),
        (3, "G4", "0574-12345678"),
        (3, "H4", "+86-574-12345678"),
        (3, "K4", "N"),
        (3, "A5", "F002"),
        (3, "B5", "测试工厂二"),
        (3, "C5", "TEST FACTORY TWO LTD."),
        (3, "D5", "4 FACTORY ROAD, CHINA"),
        (3, "E5", "123456789"),
        (3, "F5", "李四"),
        (3, "G5", "0574-87654321"),
    ];
    for &(sheet, address, value) in overrides {
        cells.retain(|&(s, a, _)| (s, a) != (sheet, address));
        cells.push((sheet, address, value));
    }
    let mut archive = zip::ZipArchive::new(Cursor::new(TEMPLATE)).unwrap();
    let mut output = zip::ZipWriter::new(Cursor::new(Vec::new()));
    for index in 0..archive.len() {
        let mut entry = archive.by_index(index).unwrap();
        let name = entry.name().unwrap().into_owned();
        let mut bytes = Vec::new();
        entry.read_to_end(&mut bytes).unwrap();
        for sheet in 1..=3 {
            if name == format!("xl/worksheets/sheet{sheet}.xml") {
                let mut reader = Reader::from_reader(bytes.as_slice());
                let mut writer = Writer::new(Vec::new());
                loop {
                    let event = reader.read_event().unwrap();
                    if matches!(event, Event::Eof) {
                        break;
                    }
                    let replacement = match &event {
                        Event::Start(cell) | Event::Empty(cell)
                            if cell.name().as_ref().rsplit(':').next() == Some("c") =>
                        {
                            let address = cell
                                .attributes()
                                .map(Result::unwrap)
                                .find(|a| a.key.as_ref() == "r")
                                .unwrap();
                            cells
                                .iter()
                                .find(|(s, a, _)| *s == sheet && *a == address.value.as_ref())
                        }
                        _ => None,
                    };
                    if let Some((_, address, value)) = replacement {
                        let cell_name = match &event {
                            Event::Start(cell) | Event::Empty(cell) => {
                                cell.name().as_ref().to_owned()
                            }
                            _ => unreachable!(),
                        };
                        let named = |local: &str| {
                            cell_name.rsplit_once(':').map_or_else(
                                || local.to_owned(),
                                |(prefix, _)| format!("{prefix}:{local}"),
                            )
                        };
                        if let Event::Start(cell) = &event {
                            reader.read_to_end(cell.name()).unwrap();
                        }
                        let mut cell = BytesStart::new(&cell_name);
                        cell.push_attribute(("r", *address));
                        cell.push_attribute(("t", "inlineStr"));
                        writer.write_event(Event::Start(cell)).unwrap();
                        writer
                            .write_event(Event::Start(BytesStart::new(named("is"))))
                            .unwrap();
                        writer
                            .write_event(Event::Start(BytesStart::new(named("t"))))
                            .unwrap();
                        writer
                            .write_event(Event::Text(BytesText::new(value)))
                            .unwrap();
                        for name in ["t", "is", "c"] {
                            writer
                                .write_event(Event::End(BytesEnd::new(named(name))))
                                .unwrap();
                        }
                    } else {
                        writer.write_event(event).unwrap();
                    }
                }
                bytes = writer.into_inner();
            }
        }
        output
            .start_file(
                name,
                zip::write::SimpleFileOptions::default()
                    .compression_method(zip::CompressionMethod::Deflated),
            )
            .unwrap();
        output.write_all(&bytes).unwrap();
    }
    output.finish().unwrap().into_inner()
}
