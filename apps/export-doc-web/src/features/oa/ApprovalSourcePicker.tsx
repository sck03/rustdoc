import { Button } from "../../ui/Button.tsx";
import { useInfiniteQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient, OaActorSelector } from "../../api/index.ts";
import type { ApprovalAccount } from "./useApprovalSettings.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
export const approvalSources: Record<OaActorSelector["source"],string>={Named:"指定人员",DepartmentManager:"任职部门主负责人",DepartmentLeaders:"部门负责人与分管负责人",LineManager:"直属负责人",FunctionalManagers:"职能负责人",CompanyExecutive:"公司主负责人",CompanyLeaders:"公司负责人与分管负责人",Position:"岗位承担人",Responsibility:"业务职责承担人",ProjectLeader:"项目负责人",Handlers:"办理分工",Requester:"申请人员"};
export function ApprovalSourcePicker({client,source,sourceId,unitId,onChange}:{client:ExportDocManagerApiClient;source:OaActorSelector["source"];sourceId?:string|null;unitId?:number;onChange:(id:string|null)=>void}) {
  const supported=source==="Position"||source==="Responsibility"||source==="ProjectLeader";
  const query=useInfiniteQuery({queryKey:["office","routing-sources",unitId,source],initialPageParam:1,enabled:supported,queryFn:({signal,pageParam})=>client.getOaRoutingSources({source:source as "Position"|"Responsibility"|"ProjectLeader",unitId,pageNumber:pageParam,pageSize:100},{signal}),getNextPageParam:(last,pages)=>pages.reduce((n,p)=>n+p.items.length,0)<last.totalCount?pages.length+1:undefined,refetchOnWindowFocus:false});
  if(!supported)return null;
  const items=query.data?.pages.flatMap(p=>p.items)??[];
  return <><OfficeField label="岗位／职责／项目"><select required value={sourceId??""} onChange={e=>onChange(e.target.value||null)}><option value="">选择有效来源</option>{sourceId&&!items.some(i=>i.id===sourceId)&&<option value={sourceId}>当前编号 #{sourceId}</option>}{items.map(item=><option key={item.id} value={item.id}>{item.name} · {item.code}</option>)}</select></OfficeField>{query.hasNextPage&&<Button type="button" disabled={query.isFetchingNextPage} onClick={()=>void query.fetchNextPage()}>加载更多职责</Button>}{query.isError&&<InlineNotice tone="error">{readApiError(query.error)} <Button type="button" onClick={()=>void query.refetch()}>重试职责查询</Button></InlineNotice>}</>;
}
export function EscalationTargetEditor({client,value,onChange,accounts,unitId}:{client:ExportDocManagerApiClient;value:OaActorSelector;onChange:(value:OaActorSelector)=>void;accounts:ApprovalAccount[];unitId?:number}) {
  return <><OfficeField label="升级责任来源"><select value={value.source} onChange={e=>onChange({source:e.target.value as OaActorSelector["source"],sourceId:null,userIds:[]})}>{Object.entries(approvalSources).filter(([v])=>v!=="Requester"&&v!=="Handlers").map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></OfficeField><ApprovalSourcePicker client={client} source={value.source} sourceId={value.sourceId} unitId={unitId} onChange={sourceId=>onChange({...value,sourceId})}/>{value.source==="Named"&&<OfficeField label="升级责任人员"><select multiple required value={(value.userIds??[]).map(String)} onChange={e=>onChange({...value,userIds:Array.from(e.target.selectedOptions,o=>Number(o.value))})}>{accounts.map(account=><option key={account.id} value={account.id}>{account.fullName} · {account.username}</option>)}</select></OfficeField>}</>;
}
