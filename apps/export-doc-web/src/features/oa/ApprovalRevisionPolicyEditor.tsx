import type { OaRevisionPolicy } from "../../api/index.ts";
import type { ApprovalAccount } from "./useApprovalSettings.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
const defaults:OaRevisionPolicy={allowAddSigners:false,allowRemoveSigners:false,allowReturnToNode:false,minimumSigners:1,pauseStopsClock:true,protectedUserIds:[]};
export function ApprovalRevisionPolicyEditor({value,accounts,onChange}:{value?:OaRevisionPolicy;accounts:ApprovalAccount[];onChange:(value:OaRevisionPolicy)=>void}) {
  const policy={...defaults,...value};
  const patch=(change:Partial<OaRevisionPolicy>)=>onChange({...policy,...change});
  return <details><summary>加减签、重审与暂停计时</summary><div className="office-form-grid">
    <label className="checkbox-field"><input type="checkbox" checked={policy.allowAddSigners} onChange={e=>patch({allowAddSigners:e.target.checked})}/>允许前加签、后加签和并行加签</label>
    <label className="checkbox-field"><input type="checkbox" checked={policy.allowRemoveSigners} onChange={e=>patch({allowRemoveSigners:e.target.checked})}/>允许受控减签</label>
    <label className="checkbox-field"><input type="checkbox" checked={policy.allowReturnToNode} onChange={e=>patch({allowReturnToNode:e.target.checked})}/>允许退回本节点重新审核</label>
    <label className="checkbox-field"><input type="checkbox" checked={policy.pauseStopsClock} onChange={e=>patch({pauseStopsClock:e.target.checked})}/>暂停期间停止节点时限计时</label>
    <OfficeField label="最低签署人数"><input type="number" min={1} max={200} step={1} value={policy.minimumSigners} onChange={e=>patch({minimumSigners:Number(e.target.value)})}/></OfficeField>
    <OfficeField label="不可减去的责任人"><select multiple value={(policy.protectedUserIds??[]).map(String)} onChange={e=>patch({protectedUserIds:Array.from(e.target.selectedOptions,o=>Number(o.value))})}>{accounts.map(account=><option key={account.id} value={account.id}>{account.fullName} · {account.username}</option>)}</select></OfficeField>
  </div></details>;
}
