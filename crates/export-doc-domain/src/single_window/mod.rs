//! Declaration rules shared by desktop and HTTP; no storage, GUI or host IO.
pub mod catalog;
pub mod draft;
pub mod mapping;
pub mod validation;
pub mod xml;
use crate::contracts;
use serde_json::Value;
use std::sync::OnceLock;

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Business {
    Coo,
    Acd,
}
impl Business {
    pub fn parse(value: &str) -> Result<Self, String> {
        match value {
            "CustomsCoo" | "coo" => Ok(Self::Coo),
            "AgentConsignment" | "acd" => Ok(Self::Acd),
            _ => Err("单一窗口业务类型无效。".into()),
        }
    }
    pub fn name(self) -> &'static str {
        match self {
            Self::Coo => "CustomsCoo",
            Self::Acd => "AgentConsignment",
        }
    }
    pub fn kind(self) -> &'static str {
        match self {
            Self::Coo => "sw-coo",
            Self::Acd => "sw-acd",
        }
    }
    pub fn scope(self) -> &'static str {
        match self {
            Self::Coo => "coo",
            Self::Acd => "acd",
        }
    }
    pub fn schema(self) -> &'static str {
        match self {
            Self::Coo => "ApiCustomsCooDocumentDto",
            Self::Acd => "ApiAgentConsignmentDocumentDto",
        }
    }
    pub fn blank(self) -> Value {
        contracts::initial(contracts::schema(self.schema()))
    }
}
pub fn reference() -> &'static Value {
    static DATA: OnceLock<Value> = OnceLock::new();
    DATA.get_or_init(|| {
        serde_json::from_str(include_str!("../../resources/single-window-reference.json"))
            .expect("generated first-party declaration rules")
    })
}
pub fn text<'a>(value: &'a Value, key: &str) -> &'a str {
    value[key].as_str().unwrap_or("").trim()
}
pub fn label(scope: &str, key: &str) -> String {
    reference()["labels"][format!("{scope}.{key}")]
        .as_str()
        .unwrap_or(key)
        .into()
}
pub fn pascal(key: &str) -> String {
    match key {
        "hsCode" => "HSCode".into(),
        "ieDate" => "IEDate".into(),
        _ => {
            let mut chars = key.chars();
            chars
                .next()
                .map(|first| first.to_uppercase().collect::<String>() + chars.as_str())
                .unwrap_or_default()
        }
    }
}
