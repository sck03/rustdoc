import type { OaApprovalStage } from "../../api/index.ts";

const isDecisionApproval = (stage: OaApprovalStage) =>
  (stage.kind ?? "Approval") === "Approval" && (stage.phase ?? "Decision") === "Decision";
const reconnect = (stage: OaApprovalStage, removed: OaApprovalStage) => stage.dependsOn.includes(removed.key)
  ? [...new Set(stage.dependsOn.flatMap(parent => parent === removed.key ? removed.dependsOn : [parent]))]
  : stage.dependsOn;

export function stageRemovalBlock(stages: OaApprovalStage[], stage: OaApprovalStage): string | null {
  if (stages.length <= 1) return "流程至少保留一个节点。";
  if (isDecisionApproval(stage) && !stages.some(node => node.key !== stage.key && (isDecisionApproval(node)
    || node.kind === "Subflow" && (node.phase ?? "Decision") === "Decision")))
    return "流程至少保留一个审批决定节点。";
  if (stages.some(node => node.branches?.some(branch => branch.target === stage.key)))
    return "此节点是排他分支目标，请先调整分支目标。";
  if (stage.pairKey || stages.some(node => node.pairKey === stage.key))
    return "此节点参与分叉或汇合配对，请先调整配对关系。";
  if (stage.branches?.length) return "请先移除此节点的排他分支。";
  const remaining = stages.filter(node => node.key !== stage.key);
  if (remaining.some(node => node.kind === "Join" && node.dependsOn.includes(stage.key) && reconnect(node, stage).length < 2
    || node.kind === "Fork" && stage.dependsOn.includes(node.key) && remaining.filter(child => reconnect(child, stage).includes(node.key)).length < 2))
    return "移除后并行分支不足两条，请先调整分叉与汇合结构。";
  return null;
}

/** Contract a regular node without dropping the ordering of its predecessors. */
export function removeWorkflowStage(stages: OaApprovalStage[], key: string): OaApprovalStage[] {
  const removed = stages.find(stage => stage.key === key);
  if (!removed || stageRemovalBlock(stages, removed)) return stages;
  return stages.filter(stage => stage.key !== key).map(stage => stage.dependsOn.includes(key)
    ? { ...stage, dependsOn: reconnect(stage, removed) }
    : stage);
}
