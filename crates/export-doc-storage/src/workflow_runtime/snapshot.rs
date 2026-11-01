//! One bounded read per save, with at most one batch for referenced historical signatures.
use super::{Adapter, Result, Statement, Value, conflict, positive, required};
use std::collections::{BTreeSet, HashMap, HashSet};

pub(super) fn signed_task(task: &Value) -> bool {
    matches!(
        task["status"].as_str(),
        Some("Approved" | "Rejected" | "Returned")
    )
}
fn without_source(task: &Value) -> Value {
    let mut task = task.clone();
    if let Some(fields) = task.as_object_mut() {
        fields.remove("sourceGeneration");
    }
    task
}
pub(super) struct Snapshot {
    pub generation: i64,
    nodes: Vec<Value>,
    tasks: HashMap<String, Value>,
    active: HashSet<String>,
    signatures: HashMap<(i64, String), Value>,
}
impl Snapshot {
    pub fn load(
        db: &(impl Adapter + ?Sized),
        instance: &str,
        request: i64,
        revision: i64,
        next: &Value,
    ) -> Result<Self> {
        let mut s = Statement::new(db);
        let key = s.text(instance);
        let request_key = s.number(request);
        let (object, plan, active, cast) = if s.pg {
            (
                "jsonb_build_object",
                format!("(SELECT body FROM workflow_plans WHERE id={key})"),
                format!(
                    "(SELECT COALESCE(jsonb_agg(task_key),'[]'::jsonb) FROM workflow_tasks WHERE request_id={request_key} AND active=1)"
                ),
                "::text",
            )
        } else {
            (
                "json_object",
                format!("json((SELECT body FROM workflow_plans WHERE id={key}))"),
                format!(
                    "json((SELECT json_group_array(task_key) FROM workflow_tasks WHERE request_id={request_key} AND active=1))"
                ),
                "",
            )
        };
        let old = s.query(
            db,
            &format!("SELECT {object}('plan',{plan},'activeTaskKeys',{active}){cast}"),
        )?;
        let plan = &old["plan"];
        let mut snapshot = Self {
            generation: plan["generation"].as_i64().unwrap_or(0),
            nodes: plan["nodes"].as_array().cloned().unwrap_or_default(),
            tasks: HashMap::new(),
            active: old["activeTaskKeys"]
                .as_array()
                .into_iter()
                .flatten()
                .filter_map(|key| key.as_str().map(str::to_owned))
                .collect(),
            signatures: HashMap::new(),
        };
        for task in plan["steps"].as_array().into_iter().flatten() {
            snapshot
                .tasks
                .insert(required(task, "taskKey")?.into(), task.clone());
            if signed_task(task) {
                let source = task["sourceGeneration"]
                    .as_i64()
                    .unwrap_or(snapshot.generation);
                snapshot.signatures.insert(
                    (source, required(task, "taskKey")?.into()),
                    without_source(task),
                );
            }
        }
        let generation = positive(next, "generation")?;
        let mut missing = BTreeSet::new();
        for task in next["steps"].as_array().into_iter().flatten() {
            if let Some(source) = task["sourceGeneration"].as_i64() {
                if source <= 0 || source >= generation {
                    return Err(conflict("复用签署的执行代次无效。"));
                }
                let key = required(task, "taskKey")?.to_owned();
                if !snapshot.signatures.contains_key(&(source, key.clone())) {
                    missing.insert((source, key));
                }
            }
        }
        if !missing.is_empty() {
            let mut s = Statement::new(db);
            let r = s.number(request);
            let v = s.number(revision);
            let mut filters = vec![];
            for (generation, key) in missing {
                let g = s.number(generation);
                let k = s.text(key);
                filters.push(format!("(generation={g} AND task_key={k})"));
            }
            let fields = "'generation',generation,'taskKey',task_key,'signature',";
            let aggregate = if s.pg {
                format!(
                    "COALESCE(jsonb_agg(jsonb_build_object({fields}signature)),'[]'::jsonb)::text"
                )
            } else {
                format!("json_group_array(json_object({fields}json(signature)))")
            };
            let signatures = s.query(db, &format!("SELECT {aggregate} FROM workflow_tasks WHERE request_id={r} AND submission={v} AND signature IS NOT NULL AND ({})", filters.join(" OR ")))?;
            for entry in signatures.as_array().into_iter().flatten() {
                snapshot.signatures.insert(
                    (
                        positive(entry, "generation")?,
                        required(entry, "taskKey")?.into(),
                    ),
                    without_source(&entry["signature"]),
                );
            }
        }
        Ok(snapshot)
    }
    pub fn node_unchanged(&self, generation: i64, position: usize, node: &Value) -> bool {
        self.generation == generation && self.nodes.get(position) == Some(node)
    }
    pub fn task(&self, generation: i64, key: &str) -> Option<&Value> {
        (self.generation == generation)
            .then(|| self.tasks.get(key))
            .flatten()
    }
    pub fn active(&self, key: &str) -> bool {
        self.active.contains(key)
    }
    pub fn verify_source(&self, task: &Value, generation: i64) -> Result<()> {
        if let Some(source) = task["sourceGeneration"].as_i64() {
            if source <= 0
                || source >= generation
                || self
                    .signatures
                    .get(&(source, required(task, "taskKey")?.into()))
                    != Some(&without_source(task))
            {
                return Err(conflict("复用材料与原签署不一致，不能复制为新批准。"));
            }
        }
        Ok(())
    }
}
