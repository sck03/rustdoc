import type { OaApprovalPlan, OaWorkflowCommandSave } from "../../api/index.ts";

export function workflowTargets(plan: OaApprovalPlan | null | undefined, reviewTasks: string[] | undefined, actions: OaWorkflowCommandSave["command"][], command: OaWorkflowCommandSave["command"]) {
  if (!plan) return [];
  const manages = actions.some(action => ["Suspend", "Resume", "Terminate"].includes(action));
  const controlled = new Set(plan.steps.filter(step => step.status === "Waiting" && (manages ? Boolean(plan.nodes[step.stage]?.startedAt) : reviewTasks?.includes(step.taskKey))).map(step => step.stage));
  if (command === "ReturnToNode") {
    for (const index of controlled) for (const dependency of plan.nodes[index].dependsOn) {
      const previous = plan.nodes.findIndex(node => node.key === dependency);
      if (previous >= 0) controlled.add(previous);
    }
  }
  return plan.nodes.filter((node, index) => controlled.has(index) && (command === "ReturnToNode" ? node.revisionPolicy?.allowReturnToNode && plan.steps.some(step => step.stage === index && step.actedByUserId) : command === "RemoveSigner" ? node.revisionPolicy?.allowRemoveSigners && !node.independentReview : node.revisionPolicy?.allowAddSigners));
}
