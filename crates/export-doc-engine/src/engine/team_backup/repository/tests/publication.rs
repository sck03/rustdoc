use super::*;
use std::sync::{
    Arc,
    atomic::{AtomicBool, Ordering},
};

struct CommitFault {
    inner: io::Directory,
    published: Arc<AtomicBool>,
    fail_read: Arc<AtomicBool>,
    fail_create: bool,
    cancel: Option<crate::operation::OperationScope>,
}
impl io::ObjectStore for CommitFault {
    fn read(&self, name: &str, limit: usize) -> Result<Option<Vec<u8>>> {
        if name.ends_with(".commit")
            && self.published.load(Ordering::Acquire)
            && self.fail_read.load(Ordering::Acquire)
        {
            return Err(std::io::Error::from(std::io::ErrorKind::PermissionDenied).into());
        }
        self.inner.read(name, limit)
    }
    fn create(&self, name: &str, bytes: &[u8]) -> Result<bool> {
        let created = self.inner.create(name, bytes)?;
        if name.ends_with(".commit") && created {
            self.published.store(true, Ordering::Release);
            if let Some(scope) = &self.cancel {
                scope.cancel();
            }
            if self.fail_create {
                return Err(unavailable("模拟提交后的响应中断。"));
            }
        }
        Ok(created)
    }
    fn delete(&self, name: &str) -> Result<()> {
        self.inner.delete(name)
    }
    fn list(&self, prefix: &str, after: &str, limit: usize) -> Result<Vec<String>> {
        self.inner.list(prefix, after, limit)
    }
    fn check(&self) -> Result<()> {
        self.inner.check()
    }
}

#[test]
fn commit_publication_reconciles_lost_responses_and_preserves_unknown_receipts() {
    for mode in ["readback", "create-response", "cancel"] {
        let work = super::super::super::tests::Workspace::new();
        let root = work.0.join("commit-boundary");
        fs::create_dir(&root).unwrap();
        let published = Arc::new(AtomicBool::new(false));
        let fail_read = Arc::new(AtomicBool::new(mode == "readback"));
        let scope = crate::operation::OperationScope::new(std::time::Duration::from_secs(60));
        let repo = Repository::open(
            Box::new(CommitFault {
                inner: io::Directory::open(root.clone(), None).unwrap(),
                published: published.clone(),
                fail_read: fail_read.clone(),
                fail_create: mode == "create-response",
                cancel: (mode == "cancel").then(|| scope.clone()),
            }),
            "11111111111111111111111111111111",
            "independent-recovery-password",
            true,
        )
        .unwrap();
        let id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        let result = scope.run(|| {
            repo.writer(id, "SQLite", serde_json::json!({}))
                .unwrap()
                .finish()
        });
        assert!(published.load(Ordering::Acquire));
        assert!(root.join(format!("point-{id}.commit")).exists());
        if mode == "readback" {
            let error = result.err().unwrap();
            assert!(error.outcome_unknown);
            assert_eq!(error.public_body(id)["outcome"], "Unknown");
            assert_eq!(error.public_body(id)["actions"][0], "QueryReceipt");
            assert!(error.message.contains(id));
        } else {
            assert_eq!(result.unwrap().id, id);
        }
        fail_read.store(false, Ordering::Release);
        assert_eq!(repo.commit(id).unwrap().id, id);
    }
}
