//! Technical nodes use an authorized owner identity and never approve business facts.
use super::*;
use export_doc_domain::approval_graph::{NodeState, SystemAction};
pub(super) fn tick(service: &NativeService) -> Result<()> {
    let entries = service
        .store
        .read(|tx| Ok(tx.workflow_system_requests("GenerateApprovalDocument")?))?;
    for entry in entries {
        crate::operation::check()?;
        let kind = text(&entry, "requestKind");
        let id = records::positive(&entry, "requestId", "申请")?;
        let prepared=service.store.read(|tx| {
            let row=store::get(tx,&kind,id)?;
            if row["submissionNumber"]!=entry["revision"] || !super::fulfillment::active_system(&row,SystemAction::GenerateApprovalDocument)? {return Ok(None);}
            let actor=super::super::units::resolve(tx,records::positive(&row,"ownerUserId","申请人")?,service.store.edition,&text(&row,"_requesterContext"))?;
            let meta=json!({"kind":approval::business_kind(&kind),"resource":approval::resource(&kind).ok_or_else(||invalid("申请类型无效。"))?});
            Ok(Some((actor,row,meta)))
        });
        match prepared {
            Ok(Some((actor, row, meta))) => {
                let result = super::documents::start_approved(service, &actor, &meta, &row)?;
                if !matches!(result["documentStatus"].as_str(), Some("Ready" | "Running")) {
                    block(service, &kind, id, &entry, "ApprovalDocumentFailed")?;
                }
            }
            Ok(None) => {}
            Err(cause) if matches!(cause.status, Some(400 | 401 | 403 | 404 | 409)) => {
                block(service, &kind, id, &entry, "DocumentIdentityUnavailable")?
            }
            Err(cause) => return Err(cause),
        }
    }
    Ok(())
}
fn block(service: &NativeService, kind: &str, id: i64, entry: &Value, code: &str) -> Result<()> {
    service.store.transaction(|tx| {
        let mut row = store::get(tx, kind, id)?;
        if row["submissionNumber"] != entry["revision"]
            || row["approvalPlan"]["generation"] != entry["generation"]
            || !super::fulfillment::active_system(&row, SystemAction::GenerateApprovalDocument)?
        {
            return Ok(());
        }
        super::fulfillment::system_result(
            &mut row,
            SystemAction::GenerateApprovalDocument,
            NodeState::Blocked,
            code,
        )?;
        super::super::workflow_tasks::persist(tx, kind, &row)?;
        Ok(())
    })
}
