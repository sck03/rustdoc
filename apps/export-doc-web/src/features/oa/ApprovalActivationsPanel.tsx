import type { OaApprovalSettings } from "../../api/index.ts";
import { Button } from "../../ui/Button.tsx";

type Activation = NonNullable<OaApprovalSettings["activations"]>[number];
export function ApprovalActivationsPanel({
  rules,
  activations,
  busy,
  onCancel,
}: Pick<OaApprovalSettings, "rules" | "activations"> & {
  busy: boolean;
  onCancel: (entry: Activation) => void;
}) {
  const names = new Map(rules.map((rule) => [rule.key, rule.name]));
  return (
    <section className="office-resource-card" aria-label="定时发布">
      <h3>定时发布与待处理</h3>
      <ul>
        {activations?.map((entry) => (
          <li key={entry.id}>
            {names.get(entry.definitionKey) ?? entry.definitionKey} · 版本 {entry.definitionVersion} ·{" "}
            {entry.activateAt} · {entry.state === "Blocked" ? `待处理：${entry.errorCode}` : "等待启用"}
            <Button disabled={busy} onClick={() => onCancel(entry)}>
              取消定时启用
            </Button>
          </li>
        ))}
      </ul>
      {!activations?.length && <p>暂无待启用或失败调度。</p>}
    </section>
  );
}
