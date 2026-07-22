import type { ApiCustomsCooDocumentDto, ApiCustomsCooEditorOptionsResponse, ApiCustomsCooOptionDto } from "../../api/index.ts";
import { DateField, TextAreaField, TextField } from "../../ui/FormFields.tsx";
import { CooDatalistField, CooSelectField } from "./CustomsCooFields.tsx";
import { shouldShowCooHeaderField } from "./customsCooModel.ts";

type Props = {
  document: ApiCustomsCooDocumentDto;
  editorOptions: ApiCustomsCooEditorOptionsResponse;
  issuingAuthorityOptions: ApiCustomsCooOptionDto[];
  onPatch: (next: Partial<ApiCustomsCooDocumentDto>) => void;
  onOrgCodeChange: (value: string) => void;
  onFetchPlaceChange: (value: string) => void;
  onApplicationAddressChange: (value: string) => void;
};

export function CustomsCooIdentitySections({
  document,
  editorOptions,
  issuingAuthorityOptions,
  onPatch,
  onOrgCodeChange,
  onFetchPlaceChange,
  onApplicationAddressChange,
}: Props) {
  return <>
    <section id="coo-section-basic" className="form-section single-window-editor-section" aria-label="证书基础">
      <div className="section-header"><h2>证书基础</h2></div>
      <div className="field-grid">
        <CooSelectField label="申请类型" value={document.applyType} options={editorOptions.applyTypeOptions} onChange={(value) => onPatch({ applyType: value })} />
        <CooSelectField label="证书类别" value={document.certStatus} options={editorOptions.certStatusOptions} onChange={(value) => onPatch({ certStatus: value })} />
        <TextField label="原产地证编号" value={document.certNo} onChange={(value) => onPatch({ certNo: value })} />
        <CooSelectField label="证书类型" value={document.certType} options={editorOptions.certTypeOptions} onChange={(value) => onPatch({ certType: value })} />
        <TextField label="企业名称(中文)" value={document.etpsName} onChange={(value) => onPatch({ etpsName: value })} />
        <TextField label="企业编号" value={document.entMgrNo} onChange={(value) => onPatch({ entMgrNo: value })} />
        <TextField label="出口商代码" value={document.ciqRegNo} onChange={(value) => onPatch({ ciqRegNo: value })} />
        <TextField label="录入企业代码" value={document.aplRegNo} onChange={(value) => onPatch({ aplRegNo: value })} />
      </div>
    </section>

    <section id="coo-section-parties" className="form-section single-window-editor-section" aria-label="申报与对象">
      <div className="section-header"><h2>申报与对象</h2></div>
      <div className="field-grid">
        <TextField label="申报员姓名" value={document.applName} onChange={(value) => onPatch({ applName: value })} />
        <TextField label="申报员身份证号" value={document.applicant} onChange={(value) => onPatch({ applicant: value })} />
        <TextField label="申报员电话" value={document.applTel} onChange={(value) => onPatch({ applTel: value })} />
        <CooDatalistField label="签证机构代码(4位)" value={document.orgCode} options={issuingAuthorityOptions} onChange={onOrgCodeChange} />
        <CooDatalistField label="领证机构代码(4位)" value={document.fetchPlace} options={issuingAuthorityOptions} onChange={onFetchPlaceChange} />
        <CooDatalistField label="申请地址(机构所在地)" value={document.aplAdd} options={[]} onChange={onApplicationAddressChange} />
        <DateField label="发票日期" value={document.invDate} onChange={(value) => onPatch({ invDate: value })} />
        <TextField label="发票号" value={document.invNo} onChange={(value) => onPatch({ invNo: value })} />
        <DateField label="申请日期" value={document.aplDate} onChange={(value) => onPatch({ aplDate: value })} />
        <TextField label="进口国/地区英文" value={document.destCountry} onChange={(value) => onPatch({ destCountry: value })} />
        <TextField label="进口国代码" value={document.destCountryCode} onChange={(value) => onPatch({ destCountryCode: value })} />
        <TextField label="进口国中文名" value={document.destCountryName} onChange={(value) => onPatch({ destCountryName: value })} />
      </div>
      <div className="field-grid field-grid-wide">
        <TextAreaField label="出口商" value={document.exporter} onChange={(value) => onPatch({ exporter: value })} />
        <TextAreaField label="收货人" value={document.consignee} onChange={(value) => onPatch({ consignee: value })} />
      </div>
      {shouldShowCooHeaderField(document, "ExporterTel") || shouldShowCooHeaderField(document, "ConsigneeTel") || shouldShowCooHeaderField(document, "EtpsConcEr") ? <div className="field-grid">
        {shouldShowCooHeaderField(document, "ExporterTel") ? <TextField label="出口商电话" value={document.exporterTel} onChange={(value) => onPatch({ exporterTel: value })} /> : null}
        {shouldShowCooHeaderField(document, "ExporterFax") ? <TextField label="出口商传真" value={document.exporterFax} onChange={(value) => onPatch({ exporterFax: value })} /> : null}
        {shouldShowCooHeaderField(document, "ExporterEmail") ? <TextField label="出口商邮箱" value={document.exporterEmail} onChange={(value) => onPatch({ exporterEmail: value })} /> : null}
        {shouldShowCooHeaderField(document, "ConsigneeTel") ? <TextField label="进口商电话" value={document.consigneeTel} onChange={(value) => onPatch({ consigneeTel: value })} /> : null}
        {shouldShowCooHeaderField(document, "ConsigneeFax") ? <TextField label="进口商传真" value={document.consigneeFax} onChange={(value) => onPatch({ consigneeFax: value })} /> : null}
        {shouldShowCooHeaderField(document, "ConsigneeEmail") ? <TextField label="进口商邮箱" value={document.consigneeEmail} onChange={(value) => onPatch({ consigneeEmail: value })} /> : null}
        {shouldShowCooHeaderField(document, "EtpsConcEr") ? <TextField label="企业联系人" value={document.etpsConcEr} onChange={(value) => onPatch({ etpsConcEr: value })} /> : null}
        {shouldShowCooHeaderField(document, "EtpsTel") ? <TextField label="企业联系电话" value={document.etpsTel} onChange={(value) => onPatch({ etpsTel: value })} /> : null}
      </div> : null}
    </section>
  </>;
}
