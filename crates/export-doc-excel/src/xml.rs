use crate::Result;
use quick_xml::{
    Reader, Writer,
    events::{BytesEnd, BytesStart, BytesText, Event},
};

#[derive(Clone, Debug)]
pub enum Part {
    Node(Node),
    Text(String),
}
#[derive(Clone, Debug)]
pub struct Node {
    pub name: String,
    pub attributes: Vec<(String, String)>,
    pub children: Vec<Part>,
}
impl Node {
    pub fn new(name: &str) -> Self {
        Self {
            name: name.into(),
            attributes: vec![],
            children: vec![],
        }
    }
    pub fn local(&self) -> &str {
        self.name.rsplit(':').next().unwrap_or(&self.name)
    }
    pub fn named(&self, name: &str) -> Self {
        Self::new(
            &self
                .name
                .rsplit_once(':')
                .map(|(prefix, _)| format!("{prefix}:{name}"))
                .unwrap_or_else(|| name.into()),
        )
    }
    pub fn attr(&self, name: &str) -> Option<&str> {
        self.attributes
            .iter()
            .find(|(key, _)| key == name)
            .map(|(_, value)| value.as_str())
    }
    pub fn set(&mut self, name: &str, value: impl ToString) {
        if let Some((_, stored)) = self.attributes.iter_mut().find(|(key, _)| key == name) {
            *stored = value.to_string();
        } else {
            self.attributes.push((name.into(), value.to_string()));
        }
    }
    pub fn nodes(&self) -> impl Iterator<Item = &Node> {
        self.children.iter().filter_map(|part| {
            if let Part::Node(node) = part {
                Some(node)
            } else {
                None
            }
        })
    }
    pub fn nodes_mut(&mut self) -> impl Iterator<Item = &mut Node> {
        self.children.iter_mut().filter_map(|part| {
            if let Part::Node(node) = part {
                Some(node)
            } else {
                None
            }
        })
    }
    pub fn child(&self, name: &str) -> Option<&Node> {
        self.nodes().find(|node| node.local() == name)
    }
    pub fn child_mut(&mut self, name: &str) -> Option<&mut Node> {
        self.nodes_mut().find(|node| node.local() == name)
    }
    pub fn text(&self) -> String {
        self.children
            .iter()
            .filter_map(|part| {
                if let Part::Text(value) = part {
                    Some(value.as_str())
                } else {
                    None
                }
            })
            .collect()
    }
    pub fn parse(bytes: &[u8]) -> Result<Self> {
        let mut reader = Reader::from_reader(bytes);
        let mut stack = vec![Self::new("document")];
        let mut nodes = 0;
        loop {
            let event = reader
                .read_event()
                .map_err(|e| format!("Excel XML 无效：{e}"))?;
            let empty = matches!(&event, Event::Empty(_));
            match event {
                Event::Start(start) | Event::Empty(start) => {
                    let name = start.name().as_ref().to_owned();
                    let mut node = Self::new(&name);
                    for attribute in start.attributes() {
                        let attribute = attribute.map_err(|e| e.to_string())?;
                        let key = attribute.key.as_ref().to_owned();
                        node.attributes.push((
                            key,
                            attribute
                                .normalized_value(quick_xml::XmlVersion::Implicit1_0)
                                .map_err(|e| e.to_string())?
                                .into_owned(),
                        ));
                    }
                    nodes += 1;
                    if nodes > 500_000 || stack.len() > 128 {
                        return Err("Excel XML 结构超过上限。".into());
                    }
                    if empty {
                        stack.last_mut().unwrap().children.push(Part::Node(node));
                    } else {
                        stack.push(node);
                    }
                }
                Event::End(end) => {
                    if stack.len() < 2 {
                        return Err("Excel XML 结束标签无效。".into());
                    }
                    let node = stack.pop().unwrap();
                    if node.name != end.name().as_ref() {
                        return Err("Excel XML 标签不匹配。".into());
                    }
                    stack.last_mut().unwrap().children.push(Part::Node(node));
                }
                Event::Text(text) => {
                    let text = text.xml10_content();
                    let text = quick_xml::escape::unescape(&text)
                        .map_err(|e| e.to_string())?
                        .into_owned();
                    stack.last_mut().unwrap().children.push(Part::Text(text));
                }
                Event::CData(text) => stack
                    .last_mut()
                    .unwrap()
                    .children
                    .push(Part::Text(text.xml10_content().into_owned())),
                Event::DocType(_) => return Err("Excel XML 不允许外部实体或 DTD。".into()),
                Event::GeneralRef(reference) => {
                    let value = reference.as_ref();
                    let escaped = format!("&{value};");
                    let decoded = quick_xml::escape::unescape(&escaped)
                        .map_err(|e| e.to_string())?
                        .into_owned();
                    stack.last_mut().unwrap().children.push(Part::Text(decoded));
                }
                Event::Eof => break,
                _ => {}
            }
        }
        if stack.len() != 1 {
            return Err("Excel XML 未完整结束。".into());
        }
        let document = stack.pop().unwrap();
        let mut roots = document.children.into_iter().filter_map(|part| {
            if let Part::Node(node) = part {
                Some(node)
            } else {
                None
            }
        });
        let root = roots.next().ok_or("Excel XML 没有根节点。")?;
        if roots.next().is_some() {
            return Err("Excel XML 包含多个根节点。".into());
        }
        Ok(root)
    }
    pub fn bytes(&self) -> Result<Vec<u8>> {
        fn write(node: &Node, writer: &mut Writer<Vec<u8>>) -> Result<()> {
            let mut start = BytesStart::new(&node.name);
            for (key, value) in &node.attributes {
                start.push_attribute((key.as_str(), value.as_str()));
            }
            if node.children.is_empty() {
                writer
                    .write_event(Event::Empty(start))
                    .map_err(|e| e.to_string())?;
                return Ok(());
            }
            writer
                .write_event(Event::Start(start))
                .map_err(|e| e.to_string())?;
            for child in &node.children {
                match child {
                    Part::Node(child) => write(child, writer)?,
                    Part::Text(value) => writer
                        .write_event(Event::Text(BytesText::new(value)))
                        .map_err(|e| e.to_string())?,
                }
            }
            writer
                .write_event(Event::End(BytesEnd::new(&node.name)))
                .map_err(|e| e.to_string())?;
            Ok(())
        }
        let mut writer =
            Writer::new(b"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>".to_vec());
        write(self, &mut writer)?;
        Ok(writer.into_inner())
    }
}
