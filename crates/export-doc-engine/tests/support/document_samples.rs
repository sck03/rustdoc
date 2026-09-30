use std::io::{Cursor, Write};
pub fn office_files() -> Vec<(&'static str, Vec<u8>)> {
    let mut files = vec![];
    for (extension, part, root, content_type) in [
        (
            "docx",
            "word/document.xml",
            "document",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
        ),
        (
            "xlsx",
            "xl/workbook.xml",
            "workbook",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
        ),
    ] {
        let mut zip = zip::ZipWriter::new(Cursor::new(Vec::new()));
        for (name, body) in [
            (
                "[Content_Types].xml",
                format!(
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/{part}\" ContentType=\"{content_type}\"/></Types>"
                ),
            ),
            (part, format!("<{root}/>")),
        ] {
            zip.start_file(name, zip::write::SimpleFileOptions::default())
                .unwrap();
            zip.write_all(body.as_bytes()).unwrap();
        }
        files.push((extension, zip.finish().unwrap().into_inner()));
    }
    for (extension, stream, header) in [
        ("doc", "WordDocument", [0xec, 0xa5, 0, 0, 0, 0, 0, 0]),
        ("xls", "Workbook", [0x09, 0x08, 0x04, 0, 0, 0x06, 0x05, 0]),
    ] {
        let mut compound = cfb::CompoundFile::create(Cursor::new(Vec::new())).unwrap();
        compound
            .create_stream(stream)
            .unwrap()
            .write_all(&header)
            .unwrap();
        files.push((extension, compound.into_inner().into_inner()));
    }
    files
}
