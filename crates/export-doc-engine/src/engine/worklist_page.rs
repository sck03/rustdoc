//! Count every authorized item, retaining only the requested page prefix per source.
use super::*;
use std::{cmp::Ordering, collections::BinaryHeap};

pub(super) struct Selection<'a> {
    pub now: &'a BusinessTime,
    pub due: &'a str,
    pub source: &'a str,
    pub keep: usize,
}
struct Entry {
    deadline: String,
    id: i64,
    value: Value,
}
impl PartialEq for Entry {
    fn eq(&self, other: &Self) -> bool {
        self.cmp(other) == Ordering::Equal
    }
}
impl Eq for Entry {}
impl PartialOrd for Entry {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}
impl Ord for Entry {
    fn cmp(&self, other: &Self) -> Ordering {
        self.deadline
            .cmp(&other.deadline)
            .then(self.id.cmp(&other.id))
    }
}

pub(super) struct Group {
    pub key: &'static str,
    pub name: &'static str,
    pub count: usize,
    rows: BinaryHeap<Entry>,
}
impl Group {
    pub fn new(key: &'static str, name: &'static str) -> Self {
        Self {
            key,
            name,
            count: 0,
            rows: BinaryHeap::new(),
        }
    }
    pub fn push(&mut self, mut value: Value, selection: &Selection<'_>) -> Result<()> {
        crate::operation::check()?;
        let due = deadline(&value, selection.now)?;
        let included = match selection.due {
            "Overdue" => due.as_ref().is_some_and(|d| d.0),
            "Upcoming" => due.as_ref().is_some_and(|d| d.1),
            "Undated" => due.is_none(),
            _ => true,
        };
        if !included {
            return Ok(());
        }
        self.count += 1;
        if !selection.source.is_empty() && selection.source != self.key {
            return Ok(());
        }
        value["source"] = json!(self.key);
        value["isOverdue"] = json!(due.as_ref().is_some_and(|d| d.0));
        let entry = Entry {
            deadline: due.map(|d| d.2).unwrap_or_else(|| "~".into()),
            id: value["recordId"]
                .as_i64()
                .ok_or_else(|| unavailable("待办编号无效。"))?,
            value,
        };
        if self.rows.len() < selection.keep {
            self.rows.push(entry);
        } else if self.rows.peek().is_some_and(|last| entry < *last) {
            self.rows.pop();
            self.rows.push(entry);
        }
        Ok(())
    }
    pub fn into_rows(self) -> impl Iterator<Item = Value> {
        self.rows
            .into_sorted_vec()
            .into_iter()
            .map(|entry| entry.value)
    }
}
