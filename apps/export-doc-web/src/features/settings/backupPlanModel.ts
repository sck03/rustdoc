import type { ApiBackupPlanSaveRequest, ApiBackupPlanStatus } from "../../api/index.ts";

export function backupPlanDraft(status: ApiBackupPlanStatus): ApiBackupPlanSaveRequest {
  return {
    expectedRevision: status.revision,
    expectedSettingsRevision: status.settingsRevision,
    plan: structuredClone(status.plan),
    webDav: { ...status.webDav },
    webDavPassword: null,
    s3SecretAccessKey: null,
    s3SessionToken: null,
    encryptionPassword: "",
  };
}

export function validateBackupPlan(draft: ApiBackupPlanSaveRequest, status: ApiBackupPlanStatus, confirmation: string): string | null {
  const { plan, encryptionPassword: password } = draft;
  if (password && ([...password].length < 12 || [...password].length > 1024)) return "新备份密码须为 12–1024 个字符。";
  if (password !== confirmation) return "两次输入的备份密码不一致。";
  if (plan.enabled && !password && !status.encryptionPasswordSet) return "启用日常灾备前，请先设置备份包密码。";
  if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(plan.time)) return "请选择有效的备份时间。";
  if (!Number.isInteger(plan.retentionCount) || plan.retentionCount < 1 || plan.retentionCount > 365) return "保留份数须为 1–365。";
  if (!Number.isInteger(plan.retentionDays) || plan.retentionDays < 0 || plan.retentionDays > 3650) return "保留天数须为 0–3650，0 表示只按份数保留。";
  if (plan.replicaMode === "WebDav" && (!draft.webDav.url.trim() || !draft.webDav.userName.trim())) return "请填写 WebDAV 地址和用户名。";
  if (plan.replicaMode === "S3") {
    if (!status.s3Available) return "当前程序未包含 S3 备份模块。";
    if (![plan.s3.endpoint, plan.s3.region, plan.s3.bucket, plan.s3.accessKeyId].every((value) => value.trim())) return "请填写 S3 端点、区域、桶和访问密钥 ID。";
    if (!(draft.s3SecretAccessKey ?? (status.s3SecretAccessKeySet ? "saved" : ""))) return "请填写 S3 密钥。";
  }
  return null;
}

export function backupResultLabel(result: string) {
  return ({ NotRun: "尚未执行", Running: "执行中", Succeeded: "已完成", Partial: "部分完成，需处理", Failed: "失败", Canceled: "已取消", Interrupted: "执行中断", Deferred: "系统繁忙，稍后重试", NeedsReview: "需要复核配置" } as Record<string, string>)[result] ?? result;
}
export function replicaResultLabel(result: string) {
  return ({ NotRequested: "仅本地", Pending: "传输中", Verified: "已回读校验", Uploaded: "已上传", Failed: "副本未完成" } as Record<string, string>)[result] ?? result;
}
