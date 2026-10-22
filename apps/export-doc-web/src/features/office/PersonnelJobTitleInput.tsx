import { useId, type InputHTMLAttributes } from "react";
import type { OrganizationPosition } from "../../api/index.ts";

const jobTitles = ["普通员工", "总经理", "副总经理", "部门经理", "主管", "外贸经理", "外贸业务员", "单证员", "财务经理", "会计", "出纳", "人事专员", "行政专员", "采购员", "助理"];

/** Shared employment suggestions; they never select an account role. */
export function JobTitleInput({ positions = [], ...props }: InputHTMLAttributes<HTMLInputElement> & { positions?: OrganizationPosition[] }) {
  const listId = useId();
  return <>
    <input name="jobTitle" required maxLength={120} placeholder="选择常用职位或直接输入自定义职位" {...props} list={listId} />
    <datalist id={listId}>{[...new Set([...positions.filter(p => p.isActive).map(p => p.name), ...jobTitles.filter(title => !positions.some(p => p.name === title))])].map(title => <option key={title} value={title} />)}</datalist>
  </>;
}
