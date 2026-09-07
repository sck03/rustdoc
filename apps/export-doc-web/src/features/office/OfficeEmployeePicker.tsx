import type { ApiUserDto, ExportDocManagerApiClient, PersonnelDirectoryRecord } from "../../api/index.ts";
import { RemoteSelectField } from "../../ui/RemoteSelectField.tsx";

export function OfficeEmployeePicker({ client, user, value, onChange, disabled }: {
  client: ExportDocManagerApiClient; user: ApiUserDto; value: PersonnelDirectoryRecord | null;
  onChange: (employee: PersonnelDirectoryRecord | null) => void; disabled: boolean;
}) {
  return <RemoteSelectField label="登记人员" className="office-field-wide" value={value ? String(value.id) : ""}
    selectedOption={value} disabled={disabled} onChange={onChange} emptyLabel="请选择在职人员"
    searchPlaceholder="按姓名或工号查找" description="从人员档案中选择；最多显示 50 项，可输入姓名或工号缩小范围。"
    queryKey={["office", "people", "picker", user.id, user.companyScope]}
    loadOptions={async (keyword, signal) => (await client.listPersonnel({ keyword, pageSize: 50 }, { signal })).items}
    getValue={(employee) => String(employee.id)}
    getLabel={(employee) => `${employee.employeeNumber} · ${employee.fullName} · ${employee.departmentName}`} />;
}
