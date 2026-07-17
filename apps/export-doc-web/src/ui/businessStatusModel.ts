export type BusinessStatusTone = "info" | "positive" | "warning" | "danger" | "muted";

export function getBusinessStatusTone(value: string): BusinessStatusTone {
  if (["已成交", "合作中", "启用", "供货中", "已完成", "优先合作", "合格"].includes(value)) return "positive";
  if (["跟进中", "待跟进", "已报价", "谈判中", "考察中", "备选", "观察"].includes(value)) return "warning";
  if (["已流失", "已失单", "已逾期"].includes(value)) return "danger";
  if (["暂停", "停用", "暂停合作"].includes(value)) return "muted";
  return "info";
}
