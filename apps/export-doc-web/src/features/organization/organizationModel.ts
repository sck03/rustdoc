export type DepartmentNode = { code: string; name: string; parentCode?: string | null; isActive: boolean; sortOrder?: number };

export function compareDepartments(left: DepartmentNode, right: DepartmentNode) {
  return (left.sortOrder ?? Number.MAX_SAFE_INTEGER) - (right.sortOrder ?? Number.MAX_SAFE_INTEGER)
    || left.name.localeCompare(right.name, "zh-CN", { numeric: true }) || left.code.localeCompare(right.code);
}
type DepartmentManager = { managerName: string; managerEmployeeNumber?: string };

export function departmentManagerLabel(manager: DepartmentManager) {
  return [manager.managerName, manager.managerEmployeeNumber].filter(Boolean).join(" · ");
}

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
  }).sort((left, right) => {
    const leftPath = [...left.ancestors, left.code];
    const rightPath = [...right.ancestors, right.code];
    for (let i = 0; i < Math.min(leftPath.length, rightPath.length); i++) {
      if (leftPath[i] !== rightPath[i]) return compareDepartments(directory.get(leftPath[i])!, directory.get(rightPath[i])!);
    }
    return leftPath.length - rightPath.length;
  });
}

export function parentDepartmentOptions<T extends DepartmentNode>(departments: T[], code?: string) {
  return departmentOptions(departments).filter((item) => item.code !== code && (!code || !item.ancestors.includes(code)));
}

export function filterDepartmentTree<T extends DepartmentNode & DepartmentManager>(departments: T[], keyword: string, entries?: Array<T & { ancestors: string[] }>) {
  const search = keyword.trim().normalize("NFC").toLocaleLowerCase();
  if (!search) return departments;
  const keep = new Set<string>();
  for (const item of entries ?? departmentOptions(departments)) {
    if (`${item.code} ${item.name} ${departmentManagerLabel(item)}`.normalize("NFC").toLocaleLowerCase().includes(search)) {
      keep.add(item.code);
      item.ancestors.forEach((code) => keep.add(code));
    }
  }
  return departments.filter((item) => keep.has(item.code));
}

export function buildDepartmentTreeRows<T extends DepartmentNode & DepartmentManager>(departments: T[], keyword: string, expandedDepth: number, overrides: Readonly<Record<string, boolean>>) {
  const entries = departmentOptions(departments);
  const visible = new Set(filterDepartmentTree(departments, keyword, entries).map((item) => item.code));
  const groups = new Map<string, typeof entries>();
  for (const entry of entries) {
    const parent = entry.parentCode ?? "";
    const siblings = groups.get(parent) ?? [];
    siblings.push(entry);
    groups.set(parent, siblings);
  }
  const rows: Array<typeof entries[number] & { childCount: number; expanded: boolean }> = [];
  function visit(parentCode: string) {
    for (const entry of groups.get(parentCode) ?? []) {
      if (!visible.has(entry.code)) continue;
      const children = groups.get(entry.code) ?? [];
      const expanded = keyword.trim() ? true : overrides[entry.code] ?? entry.depth < expandedDepth;
      rows.push({ ...entry, childCount: children.length, expanded });
      if (expanded) visit(entry.code);
    }
  }
  visit("");
  return rows;
}
