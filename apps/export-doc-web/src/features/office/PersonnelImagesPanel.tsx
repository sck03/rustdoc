import { useState } from "react";
import type { ExportDocManagerApiClient, PersonnelImageKind, PersonnelImageRecord, PersonnelRecord } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { OfficeDialog } from "./OfficeUi.tsx";
import type { useOfficeOperation } from "./useOfficeData.ts";
import { usePersonnelBlobUrl, usePersonnelImage } from "./usePersonnelImage.ts";

const imageLabels: Record<PersonnelImageKind, string> = { Avatar: "人员头像", IdentityFront: "身份证人像面（正面）", IdentityBack: "身份证国徽面（反面）" };

export function PersonnelImagesPanel({ client, record, operation, onPendingChange }: {
  client: ExportDocManagerApiClient; record: PersonnelRecord; operation: ReturnType<typeof useOfficeOperation>;
  onPendingChange: (kind: PersonnelImageKind, pending: boolean) => void;
}) {
  const requestConfirmation = useConfirmation();
  function upload(kind: PersonnelImageKind, file: File, done: () => void) {
    void operation.run((signal) => {
      if (file.size === 0 || file.size > 5 * 1024 * 1024) throw new Error("请选择不超过 5 MB 的 PNG 或 JPEG 图片。");
      const body = new FormData();
      body.append("file", file);
      body.append("expectedVersion", String(record.versionNumber));
      return client.uploadPersonnelImage({ id: record.employee.id, kind, body }, { signal });
    }, done);
  }
  async function remove(kind: PersonnelImageKind) {
    if (!await requestConfirmation({ title: `移除${imageLabels[kind]}？`, description: "保存的图片将被移除，人员其他资料继续保留。", confirmLabel: "移除图片" })) return;
    void operation.run((signal) => client.deletePersonnelImage({ id: record.employee.id, kind, expectedVersion: record.versionNumber }, { signal }), () => {});
  }
  return <div className="personnel-detail-stack">
    <p className="office-muted">头像用于公司通讯录；身份证图片仅限获授权的人事人员查看。支持 PNG、JPEG，每张不超过 5 MB。上传和移除会立即保存。</p>
    {operation.error && <InlineNotice tone="error" title="图片操作未完成">{operation.error}</InlineNotice>}
    <div className="personnel-images">{(Object.keys(imageLabels) as PersonnelImageKind[]).map((kind) =>
      <PersonnelImageEditor key={kind} client={client} id={record.employee.id} kind={kind} image={record.images.find((item) => item.kind === kind)}
        editable={record.canEdit} busy={operation.busy} upload={upload} remove={() => void remove(kind)} onPendingChange={onPendingChange} />)}</div>
  </div>;
}

function PersonnelImageEditor({ client, id, kind, image, editable, busy, upload, remove, onPendingChange }: {
  client: ExportDocManagerApiClient; id: number; kind: PersonnelImageKind; image?: PersonnelImageRecord; editable: boolean; busy: boolean;
  upload: (kind: PersonnelImageKind, file: File, done: () => void) => void; remove: () => void;
  onPendingChange: (kind: PersonnelImageKind, pending: boolean) => void;
}) {
  const [file, setFile] = useState<File | null>(null);
  const [inputKey, setInputKey] = useState(0);
  const [zoom, setZoom] = useState(false);
  const [failedUrl, setFailedUrl] = useState("");
  const [selectionError, setSelectionError] = useState("");
  const stored = usePersonnelImage(client, id, kind, image?.contentHash);
  const preview = usePersonnelBlobUrl(file);
  const url = preview || stored.url;
  const label = imageLabels[kind];
  function clearFile() { setFile(null); setInputKey((value) => value + 1); onPendingChange(kind, false); }
  function selectFile(selected: File | null) {
    setSelectionError(""); setFailedUrl("");
    if (selected && (selected.size === 0 || selected.size > 5 * 1024 * 1024 ||
      (selected.type ? !["image/png", "image/jpeg"].includes(selected.type) : !/\.(?:png|jpe?g)$/i.test(selected.name)))) {
      clearFile(); setSelectionError("请选择不超过 5 MB 的 PNG 或 JPEG 图片。"); return;
    }
    setFile(selected); onPendingChange(kind, Boolean(selected));
  }
  return <section className="personnel-image-card" aria-label={label}>
    <h3>{label}</h3>
    {url && failedUrl !== url ? <button type="button" className="personnel-image-preview" aria-label={`放大查看${label}`} onClick={() => setZoom(true)}>
      <img src={url} alt={file ? `待上传的${label}` : label} onError={() => setFailedUrl(url)} /></button>
      : <div className="personnel-image-empty">{failedUrl && failedUrl === url ? "图片无法显示，请选择有效的原始图片。" : stored.pending ? "图片加载中…" : image ? "图片未能加载" : "尚未上传"}</div>}
    {stored.error && <InlineNotice tone="error" title="图片加载失败">{stored.error} <button type="button" onClick={stored.retry}>重试</button></InlineNotice>}
    {selectionError && <InlineNotice tone="error" title="图片未选择">{selectionError}</InlineNotice>}
    {editable && <>
      <label className="office-field"><span>选择{label}</span><input key={inputKey} type="file" accept="image/png,image/jpeg,.png,.jpg,.jpeg" disabled={busy}
        onChange={(event) => selectFile(event.target.files?.[0] ?? null)} /></label>
      {file && <p className="office-muted">待保存：{file.name}</p>}
      <div className="office-card-actions"><button type="button" className="command-button" disabled={busy || !file || Boolean(url && failedUrl === url)}
        onClick={() => { if (file) upload(kind, file, clearFile); }}>{image ? "替换图片" : "上传图片"}</button>
        {file ? <button type="button" className="command-button secondary" disabled={busy} onClick={clearFile}>取消选择</button>
          : image && <button type="button" className="command-button secondary" disabled={busy} onClick={remove}>移除图片</button>}</div>
    </>}
    {zoom && url && <OfficeDialog title={label} onClose={() => setZoom(false)}><img className="personnel-image-zoom" src={url} alt={label} /></OfficeDialog>}
  </section>;
}
