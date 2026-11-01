//! Direct task/service candidates and a bounded unit scan for completion roles.
use super::*;
use std::collections::BTreeSet;

pub(super) struct Candidates {
    explicit: BTreeSet<i64>,
    first: bool,
    unit_scan: bool,
    before: Option<i64>,
    company: String,
    date: String,
}
impl Candidates {
    pub fn new(
        tx: &Connection,
        actor: &Actor,
        row: &Value,
        review: bool,
        completion: bool,
        handling_event: bool,
        cc: &[i64],
    ) -> Result<Self> {
        let mut explicit = BTreeSet::new();
        if review {
            if let Some(plan) = super::super::oa::approval::plan(row)? {
                for index in plan.current_indices() {
                    explicit.insert(plan.steps[index].approver_user_id);
                }
            }
            let delegates =
                super::super::oa::settings::delegates_for(tx, &actor.company, &explicit)?;
            explicit.extend(delegates);
        } else {
            explicit.insert(records::positive(row, "ownerUserId", "申请人")?);
        }
        explicit.extend(cc);
        if handling_event {
            for service in super::super::handling::services(tx, &actor.company)? {
                if service["key"] == row["handlingKey"] {
                    for id in service["handlerUserIds"].as_array().into_iter().flatten() {
                        explicit.insert(id.as_i64().ok_or_else(|| {
                            super::super::error::unavailable("办理人员引用损坏。")
                        })?);
                    }
                }
            }
            for admin in store::matching(
                tx,
                "users",
                &[
                    ("isActive", "true".into()),
                    ("role", "Admin".into()),
                    ("companyScope", actor.company.clone()),
                ],
                &[("id", "id")],
            )? {
                explicit.insert(records::positive(&admin, "id", "接管账号")?);
            }
        }
        Ok(Self {
            explicit,
            first: true,
            unit_scan: completion,
            before: None,
            company: actor.company.clone(),
            date: super::super::units::today(tx)?.to_string(),
        })
    }
    pub fn next(&mut self, tx: &Connection) -> Result<Option<Vec<i64>>> {
        if self.first {
            self.first = false;
            if !self.explicit.is_empty() {
                return Ok(Some(self.explicit.iter().copied().collect()));
            }
        }
        if !self.unit_scan {
            return Ok(None);
        }
        let page =
            tx.workflow_notification_account_page(&self.company, &self.date, self.before, 100)?;
        if page.is_empty() {
            self.unit_scan = false;
            return Ok(None);
        }
        self.before = Some(records::positive(
            page.last().expect("nonempty candidate page"),
            "id",
            "通知候选游标",
        )?);
        let mut ids = vec![];
        for row in page {
            let id = records::positive(&row, "id", "通知候选账号")?;
            if !self.explicit.contains(&id) {
                ids.push(id);
            }
        }
        Ok(Some(ids))
    }
}
