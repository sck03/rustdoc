import type { ApiCustomsCooDocumentDto, ApiCustomsCooEditorOptionsResponse } from "../../api/index.ts";
import { DateField, TextAreaField, TextField } from "../../ui/FormFields.tsx";
import { CooSelectField } from "./CustomsCooFields.tsx";
import { shouldShowCooHeaderField, shouldShowCooModificationFields } from "./customsCooModel.ts";

type Props = {
  document: ApiCustomsCooDocumentDto;
  editorOptions: ApiCustomsCooEditorOptionsResponse;
  onPatch: (next: Partial<ApiCustomsCooDocumentDto>) => void;
};

export function CustomsCooTradeSections({ document, editorOptions, onPatch }: Props) {
  return <>
    <section id="coo-section-trade" className="form-section single-window-editor-section" aria-label="运输与贸易">
      <div className="section-header"><h2>运输与贸易</h2></div>
      <div className="field-grid field-grid-wide">
        <TextAreaField label="特殊条款（商品描述）" value={document.goodsSpecClause} onChange={(value) => onPatch({ goodsSpecClause: value })} />
        <TextAreaField label="唛头" value={document.mark} onChange={(value) => onPatch({ mark: value })} />
      </div>
      <div className="field-grid">
        <TextField label="启运港" value={document.loadPort} onChange={(value) => onPatch({ loadPort: value })} />
        <TextField label="卸货港" value={document.unloadPort} onChange={(value) => onPatch({ unloadPort: value })} />
        <TextField label="运输方式" value={document.transMeans} onChange={(value) => onPatch({ transMeans: value })} />
        <TextField label="船名/航次" value={document.transName} onChange={(value) => onPatch({ transName: value })} />
        <TextField label="中转国代码" value={document.transCountryCode} onChange={(value) => onPatch({ transCountryCode: value })} />
        <TextField label="中转国名称" value={document.transCountryName} onChange={(value) => onPatch({ transCountryName: value })} />
        <TextField label="转运港" value={document.transPort} onChange={(value) => onPatch({ transPort: value })} />
        <TextField label="目的港" value={document.destPort} onChange={(value) => onPatch({ destPort: value })} />
        <DateField label="出运日期" value={document.intendExpDate} onChange={(value) => onPatch({ intendExpDate: value })} />
        {shouldShowCooHeaderField(document, "PredictFlag") ? <CooSelectField label="预计离港标志" value={document.predictFlag} options={editorOptions.predictFlagOptions} onChange={(value) => onPatch({ predictFlag: value })} /> : null}
        <DateField label="出口报关日期" value={document.expDeclDate} onChange={(value) => onPatch({ expDeclDate: value })} />
        <CooSelectField label="贸易方式代码" value={document.tradeModeCode} options={editorOptions.cooTradeModeOptions} onChange={(value) => onPatch({ tradeModeCode: value })} />
        <TextField label="FOB值" value={document.fobValue} onChange={(value) => onPatch({ fobValue: value })} />
        <TextField label="总金额" value={document.totalAmt} onChange={(value) => onPatch({ totalAmt: value })} />
        <TextField label="合同号" value={document.contractNo} onChange={(value) => onPatch({ contractNo: value })} />
        <TextField label="信用证号" value={document.lcNo} onChange={(value) => onPatch({ lcNo: value })} />
        <TextField label="价格条款" value={document.priceTerms} onChange={(value) => onPatch({ priceTerms: value })} />
        <CooSelectField label="币制" value={document.curr} options={editorOptions.currencyOptions} onChange={(value) => onPatch({ curr: value.toUpperCase() })} />
      </div>
      <div className="field-grid field-grid-wide">
        <TextAreaField label="运输细节" value={document.transDetails} onChange={(value) => onPatch({ transDetails: value })} />
        <TextAreaField label="申请书备注" value={document.note} onChange={(value) => onPatch({ note: value })} />
        <TextAreaField label="发票特殊条款" value={document.specInvTerms} onChange={(value) => onPatch({ specInvTerms: value })} />
      </div>
    </section>

    <section id="coo-section-special" className="form-section single-window-editor-section" aria-label="补充与特殊项">
      <div className="section-header"><h2>补充与特殊项</h2></div>
      <div className="field-grid field-grid-wide">
        {shouldShowCooHeaderField(document, "Remark") ? <TextAreaField label="证书备注" value={document.remark} onChange={(value) => onPatch({ remark: value })} /> : null}
        {shouldShowCooHeaderField(document, "Producer") ? <TextAreaField label="证书货物生产商描述" value={document.producer} onChange={(value) => onPatch({ producer: value })} /> : null}
        {shouldShowCooHeaderField(document, "PrcsAssembly") ? <TextAreaField label="加工装配工序" value={document.prcsAssembly} onChange={(value) => onPatch({ prcsAssembly: value })} /> : null}
      </div>
      <div className="field-grid">
        <CooSelectField label="生产商保密" value={document.producerSertFlag} options={editorOptions.producerSecretOptions} onChange={(value) => onPatch({ producerSertFlag: value })} />
        {shouldShowCooHeaderField(document, "ExhibitFlag") ? <CooSelectField label="是否展览证书" value={document.exhibitFlag} options={editorOptions.exhibitFlagOptions} onChange={(value) => onPatch({ exhibitFlag: value })} /> : null}
        {shouldShowCooHeaderField(document, "ThirdPartyInvFlag") ? <CooSelectField label="第三方发票标志" value={document.thirdPartyInvFlag} options={editorOptions.thirdPartyInvoiceOptions} onChange={(value) => onPatch({ thirdPartyInvFlag: value })} /> : null}
        {shouldShowCooHeaderField(document, "OriCountryCode") ? <TextField label="原产国代码" value={document.oriCountryCode} onChange={(value) => onPatch({ oriCountryCode: value })} /> : null}
        {shouldShowCooHeaderField(document, "OriCountry") ? <TextField label="原产国名称" value={document.oriCountry} onChange={(value) => onPatch({ oriCountry: value })} /> : null}
        <DateField label="签发有效日期" value={document.chkValidDate} onChange={(value) => onPatch({ chkValidDate: value })} />
        <TextField label="报关单号" value={document.entryId} onChange={(value) => onPatch({ entryId: value })} />
        <CooSelectField label="企业承诺代码" value={document.aplPromiseCode} options={editorOptions.promiseOptions} onChange={(value) => onPatch({ aplPromiseCode: value })} />
      </div>
    </section>

    {shouldShowCooModificationFields(document) ? <section id="coo-section-modification" className="form-section single-window-editor-section" aria-label="更改与重发">
      <div className="section-header"><h2>更改与重发</h2></div>
      <div className="field-grid">
        <TextField label="原证书号" value={document.oldCertNo} onChange={(value) => onPatch({ oldCertNo: value })} />
        <TextField label="更改栏目" value={document.modColm} onChange={(value) => onPatch({ modColm: value })} />
        <DateField label="原证申请日期" value={document.oldDeclDate} onChange={(value) => onPatch({ oldDeclDate: value })} />
        <DateField label="原证签发日期" value={document.oldIssueDate} onChange={(value) => onPatch({ oldIssueDate: value })} />
      </div>
      <div className="field-grid field-grid-wide">
        <TextAreaField label="更改/重发原因" value={document.modReason} onChange={(value) => onPatch({ modReason: value })} />
        <TextAreaField label="原有情况描述" value={document.oldSituDesc} onChange={(value) => onPatch({ oldSituDesc: value })} />
        <TextAreaField label="更改情况描述" value={document.modSituDesc} onChange={(value) => onPatch({ modSituDesc: value })} />
      </div>
    </section> : null}
  </>;
}
