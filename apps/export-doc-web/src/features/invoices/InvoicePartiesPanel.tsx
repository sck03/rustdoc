import { BellRing, Building2, ContactRound, Landmark, RefreshCw, type LucideIcon } from "lucide-react";
import type {
  ApiCustomerDto,
  ApiExporterDto,
  ApiInvoiceDetailDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { NumberField, SelectField, TextAreaField, TextField } from "../../ui/FormFields.tsx";
import { InlineNotice } from "../../ui/PageState.tsx";
import { RemoteSelectField } from "../../ui/RemoteSelectField.tsx";
import { ExporterSealField, type ExporterSealType } from "../master-data/ExporterSealField.tsx";

type InvoicePatch = Partial<ApiInvoiceDetailDto>;

function PartyGroupHeading({
  icon: Icon,
  title,
  description,
  status,
  connected = false,
}: {
  icon: LucideIcon;
  title: string;
  description: string;
  status: string;
  connected?: boolean;
}) {
  return (
    <div className="invoice-party-group-heading">
      <span className="invoice-party-group-icon" aria-hidden="true"><Icon size={17} /></span>
      <div>
        <strong>{title}</strong>
        <span>{description}</span>
      </div>
      <small className={connected ? "connected" : undefined}>{status}</small>
    </div>
  );
}

export function InvoicePartiesPanel({
  invoice,
  client,
  selectedCustomer,
  selectedExporter,
  isBusy,
  isEditable,
  message,
  canManageExporterSeals,
  sealBusy,
  onRefresh,
  onChange,
  onSealUpload,
  onSealError,
}: {
  invoice: ApiInvoiceDetailDto;
  client: ExportDocManagerApiClient;
  selectedCustomer?: ApiCustomerDto | null;
  selectedExporter?: ApiExporterDto | null;
  isBusy: boolean;
  isEditable: boolean;
  message: string | null;
  canManageExporterSeals: boolean;
  sealBusy: boolean;
  onRefresh: () => void;
  onChange: (next: InvoicePatch) => void;
  onSealUpload: (sealType: ExporterSealType, file: File) => void;
  onSealError: (error: unknown) => void;
}) {
  function applyCustomer(customer: ApiCustomerDto | null) {
    if (!customer) {
      onChange({ customerId: undefined });
      return;
    }

    onChange({
      customerId: customer.id,
      customerNameEN: customer.customerNameEN,
      customerAddressEN: customer.addressEN ?? "",
      notifyPartyMode: customer.notifyPartyMode,
      notifyPartyName: customer.notifyPartyName ?? "",
      notifyPartyAddress: customer.notifyPartyAddress ?? "",
    });
  }

  function applyExporter(exporter: ApiExporterDto | null) {
    if (!exporter) {
      onChange({ exporterId: undefined });
      return;
    }

    onChange({
      exporterId: exporter.id,
      exporterNameEN: exporter.exporterNameEN,
      exporterNameCN: exporter.exporterNameCN,
      exporterAddressEN: exporter.addressEN ?? "",
      exporterAddressCN: exporter.addressCN ?? "",
      exporterCreditCode: exporter.creditCode ?? "",
      exporterCustomsCode: exporter.customsCode ?? "",
      bankName: exporter.bankName ?? "",
      bankAccount: exporter.bankAccount ?? "",
      swiftCode: exporter.swiftCode ?? "",
    });
  }

  const sealActionDisabled = !canManageExporterSeals || !selectedExporter || sealBusy;
  const sealActionTitle = !selectedExporter
    ? "请先选择出口商档案"
    : !canManageExporterSeals
      ? "当前权限不能维护出口商印章"
      : undefined;

  return (
    <section className="form-section information-tier-required" aria-label="客户与出口商">
      <div className="section-header">
        <h2>客户与出口商</h2>
        <button className="icon-button" type="button" title="刷新客户和出口商" aria-label="刷新客户和出口商" disabled={isBusy} onClick={onRefresh}>
          <RefreshCw size={17} aria-hidden="true" />
        </button>
      </div>
      {message ? <InlineNotice tone="warning" title="客户与出口商资料未完整加载">{message}</InlineNotice> : null}
      <div className="invoice-party-groups">
        <section className="invoice-party-group" aria-label="客户信息">
          <PartyGroupHeading
            icon={ContactRound}
            title="客户信息"
            description="关联档案后仍可调整本张发票内容"
            status={selectedCustomer ? "已关联档案" : "手工资料"}
            connected={Boolean(selectedCustomer)}
          />
          <div className="field-grid">
            <RemoteSelectField<ApiCustomerDto>
              label="客户档案"
              value={invoice.customerId && invoice.customerId > 0 ? String(invoice.customerId) : ""}
              selectedOption={selectedCustomer}
              selectedLabel={invoice.customerNameEN || undefined}
              disabled={isBusy || !isEditable}
              queryKey={["master-data", "customers", "lookup"]}
              loadOptions={async (keyword, signal) => (await client.listCustomersPage({
                keyword: keyword || undefined,
                pageNumber: 1,
                pageSize: 50,
              }, { signal })).items}
              getValue={(customer) => String(customer.id)}
              getLabel={(customer) => customer.displayName || customer.customerNameEN || "-"}
              onChange={applyCustomer}
            />
            <TextField
              label="客户英文名"
              value={invoice.customerNameEN}
              disabled={!isEditable}
              onChange={(value) => onChange({ customerNameEN: value })}
            />
            <TextAreaField
              className="field-grid-span-all invoice-party-address-field"
              label="客户英文地址"
              value={invoice.customerAddressEN ?? ""}
              disabled={!isEditable}
              onChange={(value) => onChange({ customerAddressEN: value })}
            />
          </div>
        </section>

        <section className="invoice-party-group" aria-label="通知人信息">
          <PartyGroupHeading
            icon={BellRing}
            title="通知人信息"
            description="明确选择无通知人、同收货人或独立通知人"
            status={invoice.notifyPartyMode === "SameAsConsignee" ? "同收货人" : invoice.notifyPartyMode === "Separate" ? "独立通知人" : "无"}
            connected={invoice.notifyPartyMode !== "None"}
          />
          <div className="field-grid">
            <SelectField
              className="field-grid-span-all"
              label="通知人方式"
              value={invoice.notifyPartyMode}
              disabled={!isEditable}
              includeEmptyOption={false}
              options={[
                { value: "None", label: "无通知人" },
                { value: "SameAsConsignee", label: "同收货人" },
                { value: "Separate", label: "独立通知人" },
              ]}
              onChange={(value) => onChange({
                notifyPartyMode: value as ApiInvoiceDetailDto["notifyPartyMode"],
                ...(value === "Separate" ? {} : { notifyPartyName: "", notifyPartyAddress: "" }),
              })}
            />
            {invoice.notifyPartyMode === "Separate" ? <TextField className="field-grid-span-all" label="通知人" value={invoice.notifyPartyName ?? ""} disabled={!isEditable} onChange={(value) => onChange({ notifyPartyName: value })} /> : null}
            {invoice.notifyPartyMode === "Separate" ? <TextAreaField
              className="field-grid-span-all invoice-party-address-field"
              label="通知人英文地址"
              value={invoice.notifyPartyAddress ?? ""}
              disabled={!isEditable}
              onChange={(value) => onChange({ notifyPartyAddress: value })}
            /> : null}
          </div>
        </section>

        <section className="invoice-party-group invoice-party-group-exporter" aria-label="出口商与收款信息">
          <PartyGroupHeading
            icon={Building2}
            title="出口商与收款信息"
            description="企业身份、海关、银行与印章集中维护"
            status={selectedExporter ? "已关联档案" : "手工资料"}
            connected={Boolean(selectedExporter)}
          />
          <div className="invoice-party-subsections">
            <section className="invoice-party-subsection" aria-label="企业与海关资料">
              <h3><Building2 size={16} aria-hidden="true" />企业与海关资料</h3>
              <div className="field-grid">
                <RemoteSelectField<ApiExporterDto>
                  className="field-grid-span-2"
                  label="出口商档案"
                  value={invoice.exporterId && invoice.exporterId > 0 ? String(invoice.exporterId) : ""}
                  selectedOption={selectedExporter}
                  selectedLabel={invoice.exporterNameEN || invoice.exporterNameCN || undefined}
                  disabled={isBusy || !isEditable}
                  queryKey={["master-data", "exporters", "lookup"]}
                  loadOptions={async (keyword, signal) => (await client.listExportersPage({
                    keyword: keyword || undefined,
                    pageNumber: 1,
                    pageSize: 50,
                  }, { signal })).items}
                  getValue={(exporter) => String(exporter.id)}
                  getLabel={(exporter) => exporter.exporterNameEN || exporter.exporterNameCN || "-"}
                  onChange={applyExporter}
                />
                <TextField className="field-grid-span-2" label="出口商英文名" value={invoice.exporterNameEN} disabled={!isEditable} onChange={(value) => onChange({ exporterNameEN: value })} />
                <TextField className="field-grid-span-2" label="出口商中文名" value={invoice.exporterNameCN ?? ""} disabled={!isEditable} onChange={(value) => onChange({ exporterNameCN: value })} />
                <TextAreaField className="field-grid-span-2 invoice-party-address-field" label="出口商英文地址" value={invoice.exporterAddressEN ?? ""} disabled={!isEditable} onChange={(value) => onChange({ exporterAddressEN: value })} />
                <TextAreaField className="field-grid-span-2 invoice-party-address-field" label="出口商中文地址" value={invoice.exporterAddressCN ?? ""} disabled={!isEditable} onChange={(value) => onChange({ exporterAddressCN: value })} />
                <TextField label="统一信用代码" value={invoice.exporterCreditCode ?? ""} disabled={!isEditable} onChange={(value) => onChange({ exporterCreditCode: value })} />
                <TextField label="出口商海关编码" value={invoice.exporterCustomsCode ?? ""} disabled={!isEditable} onChange={(value) => onChange({ exporterCustomsCode: value })} />
              </div>
            </section>

            <section className="invoice-party-subsection" aria-label="收款与印章资料">
              <h3><Landmark size={16} aria-hidden="true" />收款与印章资料</h3>
              <div className="field-grid">
                <TextField className="field-grid-span-2" label="银行名称" value={invoice.bankName ?? ""} disabled={!isEditable} onChange={(value) => onChange({ bankName: value })} />
                <TextField className="field-grid-span-2" label="银行账号" value={invoice.bankAccount ?? ""} disabled={!isEditable} onChange={(value) => onChange({ bankAccount: value })} />
                <TextField label="SWIFT" value={invoice.swiftCode ?? ""} disabled={!isEditable} onChange={(value) => onChange({ swiftCode: value })} />
                <NumberField label="汇率" value={invoice.exchangeRate ?? 0} step="0.0001" disabled={!isEditable} onChange={(value) => onChange({ exchangeRate: value })} />
                <ExporterSealField label="单证章路径" value={selectedExporter?.docSealPath ?? ""} inputReadOnly actionDisabled={sealActionDisabled} actionTitle={sealActionTitle} onUploadFile={(file) => onSealUpload("document", file)} onError={onSealError} />
                <ExporterSealField label="报关章路径" value={selectedExporter?.customsSealPath ?? ""} inputReadOnly actionDisabled={sealActionDisabled} actionTitle={sealActionTitle} onUploadFile={(file) => onSealUpload("customs", file)} onError={onSealError} />
              </div>
            </section>
          </div>
        </section>
      </div>
    </section>
  );
}
