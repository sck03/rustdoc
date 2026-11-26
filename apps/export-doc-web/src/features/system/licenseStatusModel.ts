import type { ApiLicenseStatusResponse } from "../../api/index.ts";

const stateLabels = {
  Trial: "未注册（试用中）",
  TrialExpired: "未注册（试用已到期）",
  Lifetime: "已注册（终身授权）",
  FixedTerm: "已注册（定期授权）",
  Expired: "授权已过期",
  Invalid: "授权无效",
} as const;

export function licenseStatusPresentation(status: ApiLicenseStatusResponse | null, failed: boolean) {
  if (!status) {
    return { state: failed ? "读取失败" : "读取中", termLabel: "授权类型", term: "—", remaining: "—", expires: "—" };
  }
  const lifetime = status.licenseState === "Lifetime";
  const trial = status.licenseState === "Trial" || status.licenseState === "TrialExpired";
  return {
    state: stateLabels[status.licenseState],
    termLabel: trial ? "试用期限" : "授权类型",
    term: trial ? `${status.trialDays} 天` : lifetime ? "终身授权"
      : status.licenseState === "FixedTerm" || status.licenseState === "Expired" ? "定期授权" : "—",
    remaining: lifetime ? "永久有效" : status.daysRemaining === null ? "—" : `${status.daysRemaining} 天`,
    expires: lifetime ? "永不过期" : status.expireDate ?? "—",
  };
}
