import type { OaApprovalSettings } from "../../api/index.ts";
import { Button } from "../../ui/Button.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { ruleLabel } from "./approvalSettingsModel.ts";

export function ApprovalDefinitionList({
  rules,
  publications,
  onSelect,
}: Pick<OaApprovalSettings, "rules" | "publications"> & { onSelect: (key: string) => void }) {
  const published = new Map(publications.map((item) => [item.key, item]));
  return (
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
          {rules.map((rule) => {
            const publication = published.get(rule.key);
            return (
              <tr key={rule.key} data-definition-key={rule.key}>
                <td data-label="名称">{rule.name}</td>
                <td data-label="类型">{ruleLabel(rule)}</td>
                <td data-label="当前版本">{publication?.version ?? "尚未发布"}</td>
                <td data-label="启用状态">{publication?.enabled ? "已启用" : "未启用"}</td>
                <td data-label="操作">
                  <Button onClick={() => onSelect(rule.key)}>编辑流程</Button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </ResponsiveTableFrame>
  );
}
