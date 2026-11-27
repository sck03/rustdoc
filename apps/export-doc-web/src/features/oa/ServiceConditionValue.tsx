import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { Button } from "../../ui/Button.tsx";
import { readApiError } from "../../ui/formUtils.ts";
export function ServiceConditionValue({client,company,value,onChange}:{client:ExportDocManagerApiClient;company:string;value:string;onChange:(value:string)=>void}) {
  const [keyword,setKeyword]=useState("");
  const [after,setAfter]=useState("");
  const query=useQuery({queryKey:["office","handling-services",company,"condition",keyword,after,value],queryFn:({signal})=>client.listManagedHandlingServices({keyword,after,selected:value,pageSize:50},{signal})});
  const rows=(query.data?.items??[]).filter(row=>!['Room','Supply'].includes(row.category));
  if(query.data?.selected&&!rows.some(row=>row.key===value))rows.unshift(query.data.selected);
  return <span>
    <input aria-label="搜索条件办理事项" type="search" maxLength={100} value={keyword} onChange={event=>{setKeyword(event.target.value);setAfter("");}} onKeyDown={event=>{if(event.key==="Enter")event.preventDefault();}}/>
    <select required value={value} disabled={query.isPending} onChange={event=>onChange(event.target.value)}><option value="">请选择办理事项</option>{value&&!rows.some(row=>row.key===value)&&<option value={value} disabled>原办理事项不可用</option>}{rows.map(row=><option key={row.key} value={row.key} disabled={!row.isActive}>{row.name}{row.isActive?"":"（停用）"}</option>)}</select>
    {query.data?.nextCursor&&<Button type="button" onClick={()=>setAfter(query.data!.nextCursor)}>下一页事项</Button>}
    {after&&<Button type="button" onClick={()=>setAfter("")}>返回首批</Button>}
    {query.isError&&<span role="alert">{readApiError(query.error)}</span>}
  </span>;
}
