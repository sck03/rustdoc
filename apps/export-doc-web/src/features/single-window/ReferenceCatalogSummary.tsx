import { SingleWindowReferenceCatalogModel } from "../../api/index.ts";
import { CatalogKey, catalogPages, getRows } from "./referenceCatalogModel.ts";

export function ReferenceCatalogSummary({
  catalog,
  activeKey,
  hasUnsavedChanges,
}: {
  catalog: SingleWindowReferenceCatalogModel | null;
  activeKey: CatalogKey;
  hasUnsavedChanges: boolean;
}) {
  return (
    <div className="detail-grid reference-catalog-summary-grid">
      {catalogPages.map((page) => (
        <div key={page.key} className={page.key === activeKey ? "detail-item reference-catalog-active-count" : "detail-item"}>
          <span>{page.label}</span>
          <strong>{getRows(catalog, page.key).length}</strong>
        </div>
      ))}
      <div className="detail-item">
        <span>状态</span>
        <strong>{hasUnsavedChanges ? "未保存" : "已同步"}</strong>
      </div>
    </div>
  );
}
