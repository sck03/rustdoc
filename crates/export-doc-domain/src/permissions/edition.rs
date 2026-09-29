//! Product boundaries come from the same generated permission catalog as API policy.
use super::*;

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum ProductEdition {
    #[default]
    Full,
    Document,
    Sales,
    Administration,
}

impl ProductEdition {
    pub fn parse(value: &str) -> Result<Self, String> {
        match value {
            "Full" => Ok(Self::Full),
            "Document" => Ok(Self::Document),
            "Sales" => Ok(Self::Sales),
            "Administration" => Ok(Self::Administration),
            _ => Err("未知产品版本。".into()),
        }
    }

    pub fn name(self) -> &'static str {
        match self {
            Self::Full => "Full",
            Self::Document => "Document",
            Self::Sales => "Sales",
            Self::Administration => "Administration",
        }
    }

    pub fn allows(self, key: &str) -> bool {
        resource(key).is_some()
            && (self == Self::Full
                || catalog()
                    .editions
                    .get(self.name())
                    .is_some_and(|keys| keys.iter().any(|allowed| allowed == key)))
    }
}
