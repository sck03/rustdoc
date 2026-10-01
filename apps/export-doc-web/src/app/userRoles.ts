const rolePresentation: Record<string, { label: string; description: string }> = {
  Admin: { label: "系统管理员", description: "管理系统设置、账号以及全部业务数据" },
  User: { label: "单证人员", description: "处理发票、付款、单一窗口及日常单证业务" },
  Sales: { label: "业务人员", description: "管理客户、跟进、商机、邮件模板和供应商" },
  OfficeEmployee: { label: "普通员工", description: "人事与行政自助服务：通讯录、本人申请、公告和通知" },
  OfficeManager: { label: "行政管理员", description: "管理行政资源、审批与办结行政申请" },
  PersonnelManager: { label: "人事管理员", description: "维护人员档案及人事业务" },
  SalesManager: { label: "销售主管", description: "管理团队客户与销售业务" },
  Finance: { label: "财务人员", description: "处理付款报销、单据查询、报表、汇率、邮件和 OCR" },
};

export function getRolePresentation(role?: string) {
  const normalized = role?.trim() || "User";
  return rolePresentation[normalized] ?? {
    label: normalized,
    description: "使用管理员为该岗位配置的权限方案",
  };
}
