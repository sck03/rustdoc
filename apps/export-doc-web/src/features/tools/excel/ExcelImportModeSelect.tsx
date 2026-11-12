import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { readApiError } from "../../../ui/formUtils.ts";

/** Shared explicit opt-in; saved global mappings never change automatic imports. */
export function ExcelImportModeSelect({ client, value, onChange, disabled }: {
  client: ExportDocManagerApiClient; value: string; onChange: (value: string) => void; disabled: boolean;
}) {
  const query = useQuery({ queryKey: queryKeys.settings(), queryFn: ({ signal }) => client.getSettings({ signal }), staleTime: 300_000 });
  const names = [...new Set((query.data?.settings.excelImportSchemes ?? []).map(item => item.schemeName.trim()).filter(Boolean))];
  return <div className="form-field">
    <label><span className="form-field-label">Excel 识别方式</span>
      <select value={value} disabled={disabled} onChange={event => onChange(event.target.value)}>
        <option value="">智能识别（默认）</option>
        {value && !names.includes(value) && <option value={value}>手动：{value}（需重新选择）</option>}
        {names.map(name => <option key={name} value={name}>手动：{name}</option>)}
      </select>
    </label>
    {query.isError && <span role="alert">导入方案读取失败：{readApiError(query.error)}</span>}
  </div>;
}
