import { FileInput } from "../../ui/FileInput.tsx";
import { useQuery } from "@tanstack/react-query";
import { FileInput as FileInputIcon, Upload } from "lucide-react";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { DesktopIconButton } from "../../ui/DesktopPathActions.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { PathField } from "../../ui/PathField.tsx";
import { formatBusinessType } from "./singleWindowOperationCenterModel.ts";
import { useSingleWindowPackageImport } from "./useSingleWindowPackageImport.ts";

export function StationSubmitPackageImportPanel({ client, canOperate }: { client: ExportDocManagerApiClient; canOperate: boolean }) {
  const profilesQuery = useQuery({
    queryKey: queryKeys.singleWindowClientProfiles(),
    queryFn: ({ signal }) => client.getSingleWindowClientProfiles({ signal }),
    staleTime: 60_000,
  });
  const activeProfile = profilesQuery.data?.profiles.find((profile) => profile.isActive) ?? null;

  const { packagePath, setPackagePath, message, setMessage, result, isError, isBusy, choosePackage, submit } =
    useSingleWindowPackageImport(client, canOperate, activeProfile?.profileKey ?? "");

  return (
    <section className="form-section single-window-intake-card" aria-label="持卡机提交包导入">
      <div className="section-header">
        <div><h2>导入待办提交包</h2><span>导入时核对当前档案、公司抬头、业务类型和申报文件完整性</span></div>
        <button className="command-button" type="button" disabled={!canOperate || isBusy || !packagePath.trim() || !activeProfile} onClick={() => submit()}><Upload size={17} aria-hidden="true" /><span>导入并绑定当前档案</span></button>
      </div>
      {activeProfile ? (
        <InlineNotice tone="info" title="当前操作档案">
          {activeProfile.profileName} · {activeProfile.companyScope} · {activeProfile.cardIdentifier}
        </InlineNotice>
      ) : <InlineNotice tone="warning">请先在上方创建并启用公司与操作卡档案。</InlineNotice>}
      <PathField label="提交包文件" value={packagePath} disabled={!canOperate || isBusy} actions={<DesktopIconButton title="选择提交包" disabled={!canOperate || isBusy} onClick={() => void choosePackage()}><FileInputIcon size={17} aria-hidden="true" /></DesktopIconButton>} onChange={(value) => { setPackagePath(value); setMessage(null); }} />
      {message ? <InlineNotice tone={isError ? "error" : "success"}>{message}</InlineNotice> : null}
      {result ? <div className="single-window-import-summary"><strong>{result.manifest.invoiceNo || result.manifest.batchReference}</strong><span>{formatBusinessType(result.manifest.businessType)}</span><span>{result.manifest.companyScope}</span></div> : null}
    </section>
  );
}

export function OfficeReceiptPackageImportPanel({ client, canOperate }: { client: ExportDocManagerApiClient; canOperate: boolean }) {
  const { file, setFile, message, setMessage, isError, isBusy, receipt } = useSingleWindowPackageImport(client, canOperate);

  return (
    <section className="form-section single-window-intake-card" aria-label="办公室回执包导入">
      <div className="section-header">
        <div><h2>导入持卡机回执包</h2><span>只接受能精确绑定原提交批次、公司抬头和原包摘要的 .swpkg</span></div>
        <button className="command-button" type="button" disabled={!canOperate || isBusy || !file} onClick={() => receipt()}><Upload size={17} aria-hidden="true" /><span>导入回执包</span></button>
      </div>
      <label className="form-field"><span className="form-field-label"><span>回执包文件</span></span><FileInput accept=".swpkg" disabled={!canOperate || isBusy} onChange={(event) => { const nextFile = event.currentTarget.files?.[0] ?? null; event.currentTarget.value = ""; setFile(nextFile); setMessage(null); }} /></label>
      {message ? <InlineNotice tone={isError ? "error" : "success"}>{message}</InlineNotice> : null}
    </section>
  );
}
