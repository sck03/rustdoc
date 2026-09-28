import type { ReportDesignerReportType } from "./reportDesignerSchema.ts";

export type ReportDesignerPreviewSampleProfile =
  | "apiSample"
  | "exportStandard"
  | "exportImageMarks"
  | "exportLongItems"
  | "paymentVoucher";

export function getReportDesignerPreviewSampleProfiles(reportType: ReportDesignerReportType) {
  if (reportType === "PaymentVoucher") {
    return [
      { value: "apiSample" as const, label: "后端样例" },
      { value: "paymentVoucher" as const, label: "付款票据样例" },
    ];
  }

  return [
    { value: "apiSample" as const, label: "后端样例" },
    { value: "exportStandard" as const, label: "常规发票样例" },
    { value: "exportImageMarks" as const, label: "图片唛头样例" },
    { value: "exportLongItems" as const, label: "长明细分页样例" },
  ];
}
