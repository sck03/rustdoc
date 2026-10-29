//! Indexed task projection, published atomically with the owning business record.
pub struct WorkflowTask {
    pub key: String,
    pub node: String,
    pub assignee: i64,
    pub acted_by: Option<i64>,
    pub status: String,
    pub active: bool,
    pub due_at: String,
}
