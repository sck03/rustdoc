//! Preserve bounded electronic originals without interpreting or executing them.
use super::error::{Result, invalid};
use std::io::Cursor;

pub(super) fn xml(bytes: &[u8]) -> Result<String> {
    use quick_xml::events::Event;
    let mut reader = quick_xml::Reader::from_reader(bytes);
    let mut depth = 0usize;
    let mut root = None;
    loop {
        crate::operation::check()?;
        let event = reader
            .read_event()
            .map_err(|_| invalid("电子凭证 XML 结构损坏。"))?;
        let empty = matches!(&event, Event::Empty(_));
        match event {
            Event::Start(element) | Event::Empty(element) => {
                if depth == 0 {
                    if root.is_some() {
                        return Err(invalid("XML 只能包含一个根元素。"));
                    }
                    root = Some(element.local_name().as_ref().to_owned());
                }
                for attribute in element.attributes() {
                    attribute.map_err(|_| invalid("XML 属性无效。"))?;
                }
                if !empty {
                    depth += 1;
                }
                if depth > 128 {
                    return Err(invalid("XML 嵌套超过 128 层。"));
                }
            }
            Event::End(_) => {
                depth = depth
                    .checked_sub(1)
                    .ok_or_else(|| invalid("XML 层级无效。"))?;
            }
            Event::DocType(_) | Event::PI(_) => {
                return Err(invalid("电子凭证 XML 不接受 DTD、外部实体或处理指令。"));
            }
            Event::Text(text) if depth == 0 && !text.as_ref().trim().is_empty() => {
                return Err(invalid("XML 根元素外存在内容。"));
            }
            Event::CData(_) | Event::GeneralRef(_) if depth == 0 => {
                return Err(invalid("XML 根元素外存在内容。"));
            }
            Event::GeneralRef(reference)
                if !matches!(reference.as_ref(), "amp" | "lt" | "gt" | "apos" | "quot")
                    && !reference.as_ref().starts_with('#') =>
            {
                return Err(invalid("XML 包含未声明实体。"));
            }
            Event::Eof => break,
            _ => {}
        }
    }
    if depth != 0 {
        return Err(invalid("XML 元素未闭合。"));
    }
    root.ok_or_else(|| invalid("XML 缺少根元素。"))
}

pub(super) fn ofd(bytes: &[u8]) -> Result<()> {
    let mut archive =
        zip::ZipArchive::new(Cursor::new(bytes)).map_err(|_| invalid("OFD 不是有效 ZIP 容器。"))?;
    if archive.len() > 2048 {
        return Err(invalid("OFD 内部文件过多。"));
    }
    let mut names = std::collections::HashSet::new();
    let mut total = 0u64;
    let mut manifest = false;
    let mut documents = std::collections::HashSet::new();
    let mut references = vec![];
    for index in 0..archive.len() {
        crate::operation::check()?;
        let mut entry = archive
            .by_index(index)
            .map_err(|_| invalid("OFD 已损坏或加密。"))?;
        let name = entry
            .name()
            .map_err(|_| invalid("OFD 路径编码无效。"))?
            .into_owned();
        if entry.enclosed_name().is_none() || !names.insert(name.clone()) || entry.is_symlink() {
            return Err(invalid("OFD 存在无效路径、链接或重复文件。"));
        }
        total = total
            .checked_add(entry.size())
            .filter(|total| *total <= 50 * 1024 * 1024)
            .ok_or_else(|| invalid("OFD 展开内容超过 50 MiB。"))?;
        if name.ends_with(".xml") {
            let content = super::archive::read_entry(&mut entry, 10 * 1024 * 1024)?;
            let root = xml(&content)?;
            if name == "OFD.xml" {
                manifest = root == "OFD";
                let mut reader = quick_xml::Reader::from_reader(content.as_slice());
                loop {
                    match reader
                        .read_event()
                        .map_err(|_| invalid("OFD 根清单损坏。"))?
                    {
                        quick_xml::events::Event::Start(element)
                            if element.local_name().as_ref() == "DocRoot" =>
                        {
                            let value = reader
                                .read_text(element.name())
                                .map_err(|_| invalid("OFD 文档引用无效。"))?;
                            let value = value.trim().trim_start_matches('/');
                            if value.is_empty()
                                || value.contains(['\\', ':'])
                                || value.split('/').any(|part| matches!(part, "" | "." | ".."))
                            {
                                return Err(invalid("OFD 文档引用路径无效。"));
                            }
                            references.push(value.to_owned());
                        }
                        quick_xml::events::Event::Eof => break,
                        _ => {}
                    }
                }
            }
            if root == "Document" {
                documents.insert(name);
            }
        } else {
            super::archive::verify_entry(&mut entry, 50 * 1024 * 1024)?;
        }
    }
    if !manifest || references.is_empty() || references.iter().any(|name| !documents.contains(name))
    {
        return Err(invalid("OFD 缺少有效根清单或所引用的文档。"));
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn xml_is_bounded_and_cannot_resolve_external_content() {
        assert_eq!(xml("<?xml version=\"1.0\"?><Invoice><Amount>12.34</Amount><Note>原件&amp;凭证</Note></Invoice>".as_bytes()).unwrap(),"Invoice");
        for source in [
            "<a>",
            "<a></b>",
            "<a/><b/>",
            "<a>&unknown;</a>",
            "<!DOCTYPE a SYSTEM 'file:///private'><a/>",
            "<?xml-stylesheet href='https://example.com/x'?><a/>",
        ] {
            assert!(xml(source.as_bytes()).is_err(), "{source}");
        }
        assert!(ofd(b"not ofd").is_err());
    }
    #[test]
    fn ofd_requires_its_manifest_to_reference_a_real_document() {
        use std::io::Write;
        for reference in ["Doc_0/Document.xml", "missing.xml", "../Doc_0/Document.xml"] {
            let mut writer = zip::ZipWriter::new(Cursor::new(vec![]));
            for (name, content) in [
                (
                    "OFD.xml",
                    format!("<OFD><DocBody><DocRoot>{reference}</DocRoot></DocBody></OFD>"),
                ),
                ("Doc_0/Document.xml", "<Document/>".into()),
            ] {
                writer
                    .start_file(name, zip::write::SimpleFileOptions::default())
                    .unwrap();
                writer.write_all(content.as_bytes()).unwrap();
            }
            assert_eq!(
                ofd(&writer.finish().unwrap().into_inner()).is_ok(),
                reference == "Doc_0/Document.xml"
            );
        }
    }
}
