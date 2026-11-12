import { useState } from "react";
import type { ApiSingleWindowExcelImportPreviewResponse } from "../../api/index.ts";
import { Pagination } from "../../ui/primitives/Pagination.tsx";

export function SingleWindowImportPreview({ preview }: { preview: ApiSingleWindowExcelImportPreviewResponse }) {
  const [cursor, setCursor] = useState({ preview, page: 1 });
  const page = cursor.preview === preview ? cursor.page : 1;
  const imported = preview.result, cooItems = preview.coo?.items;
  const rows = imported.invoice?.items ?? [];
  const offset = (page - 1) * 50;
  return <div>
    <p>{imported.success ? `已识别 ${rows.length} 行商品，请核对后创建。` : "未识别出有效资料。"}</p>
    {imported.errors.map((error, index) => <p key={index}>{error}</p>)}
    {imported.invoice && <>
      <div className="table-scroll"><table><thead><tr><th>中文品名</th><th>英文品名</th><th>HS 编码</th><th>数量</th>{cooItems && <><th>FOB 值</th><th>生产企业</th><th>联系人</th><th>联系电话</th></>}</tr></thead>
        <tbody>{rows.slice(offset, offset + 50).map((item, index) => {
          const coo = cooItems?.[offset + index];
          return <tr key={offset + index}><td>{item.styleNameCN}</td><td>{item.styleName}</td><td>{item.hsCode}</td><td>{item.quantity}</td>{cooItems && <><td>{coo?.fobValue}</td><td>{coo?.prdcEtpsName}</td><td>{coo?.prdcEtpsConcEr}</td><td>{coo?.prdcEtpsTel}</td></>}</tr>;
        })}</tbody></table></div>
      <Pagination pageNumber={page} totalCount={rows.length} pageSize={50} isBusy={false} onPageChange={page => setCursor({ preview, page })} />
    </>}
  </div>;
}
