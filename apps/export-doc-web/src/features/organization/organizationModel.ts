export type DepartmentNode = { code: string; name: string; parentCode?: string | null; isActive: boolean };

export function departmentOptions<T extends DepartmentNode>(departments: T[]) {
  const directory = new Map(departments.map((item) => [item.code, item]));
  return departments.map((item) => {
    const names = [item.name];
    const ancestors: string[] = [];
    const visited = new Set([item.code]);
    let parent = item.parentCode;
    while (parent) {
      if (visited.has(parent)) throw new Error("部门层级存在循环，请重新加载组织架构。");
      visited.add(parent);
      const node = directory.get(parent);
      if (!node) throw new Error("部门层级不完整，请重新加载组织架构。");
      names.unshift(node.name);
      ancestors.unshift(node.code);
      parent = node.parentCode;
    }
    return { ...item, label: names.join(" / "), depth: ancestors.length, ancestors };
  }).sort((left, right) => left.label.localeCompare(right.label, "zh-CN") || left.code.localeCompare(right.code));
}

export function parentDepartmentOptions<T extends DepartmentNode>(departments: T[], code?: string) {
  return departmentOptions(departments).filter((item) => item.code !== code && (!code || !item.ancestors.includes(code)));
}

export function filterDepartmentTree<T extends DepartmentNode & { managerName: string }>(departments: T[], keyword: string) {
  const search = keyword.trim().normalize("NFC").toLocaleLowerCase();
  if (!search) return departments;
  const keep = new Set<string>();
  for (const item of departmentOptions(departments)) {
    if (`${item.code} ${item.name} ${item.managerName}`.normalize("NFC").toLocaleLowerCase().includes(search)) {
      keep.add(item.code);
      item.ancestors.forEach((code) => keep.add(code));
    }
  }
  return departments.filter((item) => keep.has(item.code));
}
