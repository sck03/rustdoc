//! Publication and sharing are independent template properties.
use serde::{Deserialize, Serialize};

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub enum Status {
    Draft,
    Published,
    Disabled,
    Archived,
}
#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub enum ShareScope {
    Private,
    Department,
    Company,
    All,
}
#[derive(Clone, Copy, Debug)]
pub enum Action {
    SaveDraft,
    Publish,
    Share(ShareScope),
    Disable,
    Restore,
    Archive,
    RestoreVersion,
}

pub fn transition(
    status: Status,
    scope: ShareScope,
    action: Action,
) -> Result<(Status, ShareScope), &'static str> {
    use {Action as A, ShareScope as S, Status as T};
    match (status, action) {
        (T::Archived, A::SaveDraft) => Err("归档模板必须先恢复，不能直接修改内容。"),
        (_, A::SaveDraft | A::RestoreVersion) => Ok((T::Draft, S::Private)),
        (T::Draft, A::Publish) => Ok((T::Published, scope)),
        (T::Published | T::Disabled, A::Share(next)) => Ok((status, next)),
        (T::Published, A::Disable) => Ok((T::Disabled, scope)),
        (T::Disabled, A::Restore) => Ok((T::Published, scope)),
        (T::Archived, A::Restore) => Ok((T::Draft, S::Private)),
        (T::Archived, A::Archive) => Err("模板已经归档。"),
        (_, A::Archive) => Ok((T::Archived, S::Private)),
        (_, A::Publish) => Err("只有草稿模板可以发布。"),
        (_, A::Share(_)) => Err("模板发布后才能设置共享范围。"),
        (_, A::Disable) => Err("只有已发布模板可以停用。"),
        (_, A::Restore) => Err("当前模板状态不需要恢复。"),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn publication_scope_and_draft_restoration_follow_separate_rules() {
        let published = transition(Status::Draft, ShareScope::Private, Action::Publish).unwrap();
        let shared =
            transition(published.0, published.1, Action::Share(ShareScope::Company)).unwrap();
        assert_eq!(shared, (Status::Published, ShareScope::Company));
        let disabled = transition(shared.0, shared.1, Action::Disable).unwrap();
        assert_eq!(
            transition(disabled.0, disabled.1, Action::Restore).unwrap(),
            shared
        );
        assert_eq!(
            transition(shared.0, shared.1, Action::SaveDraft).unwrap(),
            (Status::Draft, ShareScope::Private)
        );
        assert!(
            transition(
                Status::Draft,
                ShareScope::Private,
                Action::Share(ShareScope::All)
            )
            .is_err()
        );
        assert!(transition(Status::Published, ShareScope::Private, Action::Publish).is_err());
        assert!(transition(Status::Archived, ShareScope::Private, Action::SaveDraft).is_err());
    }
}
