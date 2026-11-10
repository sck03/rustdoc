import { Button } from "../../ui/Button.tsx";
import { useState } from "react";
import { useInfiniteQuery,useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient,OaApprovalRule } from "../../api/index.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
export function ApprovalDefinitionHistory({client,definitionKey,onCopy}:{client:ExportDocManagerApiClient;definitionKey:string;onCopy:(rule:OaApprovalRule)=>void}) {
  const [opened,setOpened]=useState(false),[from,setFrom]=useState(0),[to,setTo]=useState(0);
  const versions=useInfiniteQuery({queryKey:["office","approval-settings","versions",definitionKey],initialPageParam:1,enabled:opened,queryFn:({signal,pageParam})=>client.listOaDefinitionVersions({definitionKey,pageNumber:pageParam,pageSize:100},{signal}),getNextPageParam:(last,pages)=>pages.reduce((n,p)=>n+p.items.length,0)<last.totalCount?pages.length+1:undefined,refetchOnWindowFocus:false});
  const difference=useQuery({queryKey:["office","approval-settings","diff",definitionKey,from,to],enabled:opened&&from>0&&to>0&&from!==to,queryFn:({signal})=>client.diffOaDefinitions({definitionKey,fromVersion:from,toVersion:to},{signal}),refetchOnWindowFocus:false});
  const items=versions.data?.pages.flatMap(p=>p.items)??[];
  return <details onToggle={e=>setOpened(e.currentTarget.open)}><summary>发布版本与差异</summary>{versions.isError&&<InlineNotice tone="error">{readApiError(versions.error)}</InlineNotice>}<ul>{items.map(version=><li key={version.id}>版本 {version.version} · {version.publishedAt}<Button type="button" onClick={()=>onCopy(version.definition)}>复制此版本为当前草稿</Button></li>)}</ul>{versions.hasNextPage&&<Button type="button" onClick={()=>void versions.fetchNextPage()}>加载更早版本</Button>}<div className="office-form-grid">{([[from,setFrom,"比较起始版本"],[to,setTo,"比较目标版本"]] as const).map(([value,setValue,label])=><OfficeField key={label} label={label}><select value={value} onChange={e=>setValue(Number(e.target.value))}><option value={0}>选择版本</option>{items.map(v=><option key={v.id} value={v.version}>版本 {v.version}</option>)}</select></OfficeField>)}</div>{difference.isError&&<InlineNotice tone="error">{readApiError(difference.error)}</InlineNotice>}{difference.data&&<div style={{overflow:"auto",maxHeight:360}}><table className="office-table"><thead><tr><th>变更字段</th><th>之前</th><th>之后</th></tr></thead><tbody>{difference.data.changes.map(change=><tr key={change.path}><td>{change.path}</td><td><pre>{JSON.stringify(change.before,null,2)}</pre></td><td><pre>{JSON.stringify(change.after,null,2)}</pre></td></tr>)}</tbody></table>{!difference.data.changes.length&&<p>两个版本内容一致。</p>}</div>}</details>;
}
