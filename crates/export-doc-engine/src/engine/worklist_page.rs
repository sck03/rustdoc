//! Count every authorized item; retain at most one page plus a lookahead per source.
use super::*;
use std::{cmp::Ordering, collections::BinaryHeap};

pub(super) struct Selection<'a> {
    pub now: &'a BusinessTime,
    pub due: &'a str,
    pub source: &'a str,
    pub keep: usize,
    pub after: Option<&'a Cursor>,
}
#[derive(serde::Serialize, serde::Deserialize)]
#[serde(deny_unknown_fields)]
pub(super) struct Cursor {
    pub source: String,
    pub deadline: String,
    pub id: i64,
    pub due: String,
    pub filter: String,
    pub page: usize,
    pub size: usize,
}
impl Cursor {
    pub fn parse(
        raw: &str,
        due: &str,
        filter: &str,
        page: usize,
        size: usize,
    ) -> Result<Option<Self>> {
        if raw.is_empty() {
            return if page == 1 {
                Ok(None)
            } else {
                Err(invalid("请通过上一页返回的游标读取下一页。"))
            };
        }
        if raw.len() > 1024 {
            return Err(invalid("待办游标过长。"));
        }
        let cursor: Self = serde_json::from_str(raw).map_err(|_| invalid("待办游标无效。"))?;
        if cursor.id <= 0
            || cursor.due != due
            || cursor.filter != filter
            || cursor.page != page
            || cursor.size != size
        {
            return Err(invalid("待办筛选或分页已变化，请从首页重新读取。"));
        }
        Ok(Some(cursor))
    }
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
        if selection.after.is_some_and(|cursor| {
            cursor.source == self.key
                && (entry.deadline.as_str(), entry.id) <= (cursor.deadline.as_str(), cursor.id)
        }) {
            return Ok(());
        }
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

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn deep_cursor_keeps_one_page_and_orders_ties_without_duplicates() {
        let now = BusinessClock::default().now().unwrap();
        let cursor = Cursor {
            source: "test".into(),
            deadline: "~".into(),
            id: 9000,
            due: "All".into(),
            filter: "".into(),
            page: 901,
            size: 10,
        };
        let selection = Selection {
            now: &now,
            due: "All",
            source: "",
            keep: 11,
            after: Some(&cursor),
        };
        let mut group = Group::new("test", "test");
        for id in (1..=10_000).rev() {
            group
                .push(json!({"recordId":id,"title":"same deadline"}), &selection)
                .unwrap();
            assert!(group.rows.len() <= 11);
        }
        assert_eq!(group.count, 10_000);
        assert_eq!(
            group
                .into_rows()
                .map(|r| r["recordId"].as_i64().unwrap())
                .collect::<Vec<_>>(),
            (9001..=9011).collect::<Vec<_>>()
        );
        let encoded = serde_json::to_string(&cursor).unwrap();
        assert!(Cursor::parse(&encoded, "Overdue", "", 901, 10).is_err());
        assert!(Cursor::parse("", "All", "", 901, 10).is_err());
    }
}
