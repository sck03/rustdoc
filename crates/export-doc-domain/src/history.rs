use std::collections::VecDeque;

/// A user gesture is one undo step; repeated keystrokes in a focused field can
/// share a group. The current document is held by the caller, not duplicated here.
pub struct History<T> {
    undo: VecDeque<T>,
    redo: Vec<T>,
    group: Option<String>,
    capacity: usize,
}
impl<T: Clone + PartialEq> History<T> {
    pub fn new(capacity: usize) -> Self {
        Self {
            undo: VecDeque::new(),
            redo: Vec::new(),
            group: None,
            capacity,
        }
    }
    pub fn record(&mut self, before: T, after: &T, group: Option<String>) {
        if &before == after {
            return;
        }
        if group.is_none() || group != self.group {
            self.undo.push_back(before);
            while self.undo.len() > self.capacity {
                self.undo.pop_front();
            }
        }
        self.group = group;
        self.redo.clear();
    }
    pub fn end_group(&mut self) {
        self.group = None;
    }
    pub fn can_undo(&self) -> bool {
        !self.undo.is_empty()
    }
    pub fn can_redo(&self) -> bool {
        !self.redo.is_empty()
    }
    pub fn undo(&mut self, current: &mut T) {
        if let Some(previous) = self.undo.pop_back() {
            self.redo.push(current.clone());
            *current = previous;
        }
        self.end_group();
    }
    pub fn redo(&mut self, current: &mut T) {
        if let Some(next) = self.redo.pop() {
            self.undo.push_back(current.clone());
            *current = next;
        }
        self.end_group();
    }
    pub fn clear(&mut self) {
        self.undo.clear();
        self.redo.clear();
        self.end_group();
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn typing_group_is_one_undo_and_new_edit_discards_redo() {
        let mut history = History::new(2);
        history.record("".to_owned(), &"a".into(), Some("cell".into()));
        history.record("a".into(), &"ab".into(), Some("cell".into()));
        let mut current = "ab".to_owned();
        history.undo(&mut current);
        assert_eq!(current, "");
        history.redo(&mut current);
        assert_eq!(current, "ab");
        history.undo(&mut current);
        history.record(current, &"c".into(), None);
        assert!(!history.can_redo());
    }
}
