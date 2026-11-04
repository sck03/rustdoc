//! Bound scalar/JSON parameters shared by relational repositories.
use crate::{Adapter, Result};
use serde_json::Value;
pub(crate) struct Statement {
    pub(crate) pg: bool,
    pub(crate) values: Vec<String>,
}
impl Statement {
    pub(crate) fn new(db: &(impl Adapter + ?Sized)) -> Self {
        Self {
            pg: db.provider() == "PostgreSQL",
            values: vec![],
        }
    }
    pub(crate) fn text(&mut self, value: impl ToString) -> String {
        super::bind(&mut self.values, value, self.pg)
    }
    pub(crate) fn number(&mut self, value: impl ToString) -> String {
        format!("CAST({} AS BIGINT)", self.text(value))
    }
    pub(crate) fn optional_number(&mut self, value: Option<impl ToString>) -> String {
        format!(
            "CAST(NULLIF({},'') AS BIGINT)",
            self.text(value.map(|v| v.to_string()).unwrap_or_default())
        )
    }
    pub(crate) fn optional_text(&mut self, value: Option<&str>) -> String {
        format!("NULLIF({},'')", self.text(value.unwrap_or("")))
    }
    pub(crate) fn json(&mut self, value: &Value) -> Result<String> {
        let parameter = self.text(serde_json::to_string(value)?);
        Ok(if self.pg {
            format!("CAST({parameter} AS JSONB)")
        } else {
            parameter
        })
    }
    pub(crate) fn execute(
        self,
        db: &(impl Adapter + ?Sized),
        query: impl AsRef<str>,
    ) -> Result<u64> {
        db.execute_statement(query.as_ref(), &self.values)
    }
    pub(crate) fn execute_unit(
        self,
        db: &(impl Adapter + ?Sized),
        query: impl AsRef<str>,
    ) -> Result<()> {
        self.execute(db, query).map(|_| ())
    }
    pub(crate) fn query(
        self,
        db: &(impl Adapter + ?Sized),
        query: impl AsRef<str>,
    ) -> Result<Value> {
        db.query_json(query.as_ref(), &self.values)
    }
}
