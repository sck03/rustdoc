import { ExportDocManagerApiClient } from "../../../api/index.ts";
import { useModulePermission } from "../../../app/PermissionAccessContext.tsx";
import { ExcelToolsPanel } from "./ExcelToolsPanel.tsx";

export function ExcelToolsPage({ client }: { client: ExportDocManagerApiClient }) {
  const excelPermission = useModulePermission("document.excel");
  const invoicePermission = useModulePermission("document.invoices");

  return (
    <section className="work-surface excel-tools-surface" aria-label="Excel 模板与托单">
      {!excelPermission.canOperate ? (
        <div className="permission-readonly-notice">
          当前权限模板仅允许查看 Excel 工具说明；模板导出、托单转换和发票托单输出已禁用。
        </div>
      ) : null}
      <ExcelToolsPanel
        client={client}
        canOperate={excelPermission.canOperate}
        canReadInvoices={invoicePermission.canView}
      />
    </section>
  );
}
