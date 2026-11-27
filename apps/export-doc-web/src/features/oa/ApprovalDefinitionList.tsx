import type { OaApprovalSettings } from "../../api/index.ts";
import { Button } from "../../ui/Button.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { ruleLabel } from "./approvalSettingsModel.ts";
import { useState } from "react";
import { OfficeField } from "../office/OfficeUi.tsx";

export function ApprovalDefinitionList({
  rules,
  publications,
  onSelect,
}: Pick<OaApprovalSettings, "rules" | "publications"> & { onSelect: (key: string) => void }) {
  const published = new Map(publications.map((item) => [item.key, item]));
  const [keyword,setKeyword]=useState("");
  const [state,setState]=useState("");
  const status=(key:string)=>!published.has(key)?"draft":published.get(key)!.enabled?"active":"inactive";
  const visible=rules.filter(rule=>(!keyword||rule.name.toLocaleLowerCase().includes(keyword.toLocaleLowerCase()))&&(!state||status(rule.key)===state));
  return (
    <><div className="office-form-grid"><OfficeField label="搜索流程"><input type="search" value={keyword} onChange={e=>setKeyword(e.target.value)} onKeyDown={e=>{if(e.key==="Enter")e.preventDefault();}}/></OfficeField><OfficeField label="发布状态"><select value={state} onChange={e=>setState(e.target.value)}><option value="">全部状态</option><option value="draft">未发布</option><option value="active">已启用</option><option value="inactive">已停用</option></select></OfficeField></div>
    <ResponsiveTableFrame className="office-field-wide" mobileLayout="cards">
      <table aria-label="流程定义列表">
        <thead>
          <tr>
            <th>名称</th>
            <th>类型</th>
            <th>当前版本</th>
            <th>启用状态</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          {visible.map((rule) => {
            const publication = published.get(rule.key);
            return (
              <tr key={rule.key} data-definition-key={rule.key}>
                <td data-label="名称">{rule.name}</td>
                <td data-label="类型">{ruleLabel(rule)}</td>
                <td data-label="当前版本">{publication?.version ?? "尚未发布"}</td>
                <td data-label="启用状态">{publication?.enabled ? "已启用" : publication ? "已停用" : "未发布"}</td>
                <td data-label="操作">
                  <Button type="button" onClick={() => onSelect(rule.key)}>编辑流程</Button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </ResponsiveTableFrame>{!visible.length&&<p className="office-muted">没有匹配的流程。</p>}</>
  );
}
