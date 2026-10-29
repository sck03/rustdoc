import type { ApiUserDto } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";

export function canIdentifyOfficeApplicant(user: ApiUserDto) {
  return user.capabilities.canManageUsers || Boolean(user.employee && user.employee.status !== "Departed");
}

export function OfficeApplicantIdentity({ user }: { user: ApiUserDto }) {
  if (user.capabilities.canManageUsers) return null;
  const employee = user.employee;
  return canIdentifyOfficeApplicant(user) && employee
    ? <p className="office-field-wide">申请人：{employee.fullName} · 工号 {employee.employeeNumber} · 部门 {user.departmentId} · {employee.jobTitle}。申请使用本人关联档案。</p>
    : <InlineNotice tone="warning" title="尚未关联在职人员档案">请联系管理员关联本人的人员档案与登录账号，再刷新登录信息后申请。人员任职与账号授权分别维护，不能代选其他人员提交。</InlineNotice>;
}
