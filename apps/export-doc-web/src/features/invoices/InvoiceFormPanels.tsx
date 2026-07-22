import { useEffect, useState } from "react";
import { ClipboardList, Copy, FileText, Image as ImageIcon, Maximize2, Pencil, Plus, RefreshCw, RotateCcw, Save } from "lucide-react";
import {
  ApiCustomerDto,
  ApiExporterDto,
  ApiInvoiceDetailDto,
  ApiInvoiceItemDto,
  ApiProductDto,
  ApiUnitDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { DateField, EditableComboField, NumberField, SelectField, TextAreaField, TextField } from "../../ui/FormFields.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { CustomOptionMap, getCustomOptions } from "../custom-options/customOptionModel.ts";
import { type InvoiceItemCellSelection, InvoiceItemsEditor } from "./InvoiceItemsEditor.tsx";
import { type EditableInvoiceItemField } from "./invoiceItemTableModel.ts";
import { ShippingMarkEditorDialog } from "./ShippingMarkEditorDialog.tsx";
import { getInvoiceStatusOptions, invoiceTypeOptions, normalizeInvoiceStatus, normalizeInvoiceType } from "./invoiceModel.ts";

type InvoicePatch = Partial<ApiInvoiceDetailDto>;

export function InvoiceBasicInfoPanel({
  invoice,
  canOpenSingleWindowDocuments,
  canCloneInvoiceType,
  cloneInvoiceTypeLabel,
  canUnverifyInvoice,
  isEditable,
  isBusy,
  isCloneInvoiceTypeBusy,
  isUnverifyInvoiceBusy,
  onChange,
  onCloneInvoiceType,
  onUnverifyInvoice,
  onOpenCustomsCoo,
  onOpenAgentConsignment,
  customOptions,
  onCommitCustomOption,
}: {
  invoice: ApiInvoiceDetailDto;
  canOpenSingleWindowDocuments: boolean;
  canCloneInvoiceType: boolean;
  cloneInvoiceTypeLabel: string;
  canUnverifyInvoice: boolean;
  isEditable: boolean;
  isBusy: boolean;
  isCloneInvoiceTypeBusy: boolean;
  isUnverifyInvoiceBusy: boolean;
  onChange: (next: InvoicePatch) => void;
  onCloneInvoiceType: () => void;
  onUnverifyInvoice: () => void;
  onOpenCustomsCoo: () => void;
  onOpenAgentConsignment: () => void;
  customOptions?: CustomOptionMap;
  onCommitCustomOption?: (optionType: string, value: string) => void;
}) {
  return (
    <section className="form-section" aria-label="基础信息">
      <div className="section-header">
        <h2>基础信息</h2>
        <div className="toolbar-actions">
          {canOpenSingleWindowDocuments ? (
            <>
              <button className="command-button secondary" type="button" onClick={onOpenCustomsCoo}>
                <FileText size={17} aria-hidden="true" />
                <span>海关原产地证</span>
              </button>
              <button className="command-button secondary" type="button" onClick={onOpenAgentConsignment}>
                <ClipboardList size={17} aria-hidden="true" />
                <span>代理委托</span>
              </button>
            </>
          ) : null}
          {canCloneInvoiceType ? (
            <button
              className="command-button secondary"
              type="button"
              disabled={isBusy || isCloneInvoiceTypeBusy}
              onClick={onCloneInvoiceType}
            >
              <Copy size={17} aria-hidden="true" />
              <span>{cloneInvoiceTypeLabel}</span>
            </button>
          ) : null}
          {canUnverifyInvoice ? (
            <button
              className="command-button secondary"
              type="button"
              disabled={isBusy || isUnverifyInvoiceBusy}
              onClick={onUnverifyInvoice}
            >
              <RotateCcw size={17} aria-hidden="true" />
              <span>反审核</span>
            </button>
          ) : null}
          <button className="command-button" type="submit" disabled={isBusy || !isEditable}>
            <Save size={17} aria-hidden="true" />
            <span>保存</span>
          </button>
        </div>
      </div>
      <div className="field-grid">
        <TextField label="发票号" value={invoice.invoiceNo} required disabled={!isEditable} onChange={(value) => onChange({ invoiceNo: value })} />
        <TextField label="合同号" value={invoice.contractNo} disabled={!isEditable} onChange={(value) => onChange({ contractNo: value })} />
        <DateField label="发票日期" value={invoice.invoiceDate} disabled={!isEditable} onChange={(value) => onChange({ invoiceDate: value })} />
        <DateField label="出运日期" value={invoice.shipmentDate} disabled={!isEditable} onChange={(value) => onChange({ shipmentDate: value })} />
        <EditableComboField
          label="币种"
          value={invoice.currency}
          disabled={!isEditable}
          options={getCustomOptions(customOptions, "Currency")}
          transformValue={(value) => value.toUpperCase()}
          onChange={(value) => onChange({ currency: value })}
          onCommit={(value) => onCommitCustomOption?.("Currency", value)}
        />
        <EditableComboField
          label="监管方式"
          value={invoice.supervisionMode ?? ""}
          disabled={!isEditable}
          options={getCustomOptions(customOptions, "SupervisionMode")}
          onChange={(value) => onChange({ supervisionMode: value })}
          onCommit={(value) => onCommitCustomOption?.("SupervisionMode", value)}
        />
        <SelectField
          label="状态"
          value={normalizeInvoiceStatus(invoice.status)}
          disabled={!isEditable}
          includeEmptyOption={false}
          options={getInvoiceStatusOptions(invoice.status)}
          onChange={(value) => onChange({ status: normalizeInvoiceStatus(value) })}
        />
        <SelectField
          label="业务类型"
          value={normalizeInvoiceType(invoice.type)}
          disabled={!isEditable}
          includeEmptyOption={false}
          options={invoiceTypeOptions}
          onChange={(value) => onChange({ type: normalizeInvoiceType(value) })}
        />
        <NumberField label="总金额" value={invoice.totalAmount} disabled={!isEditable} onChange={(value) => onChange({ totalAmount: value })} />
      </div>
    </section>
  );
}

export function InvoicePartiesPanel({
  invoice,
  customers,
  exporters,
  isBusy,
  isEditable,
  message,
  onRefresh,
  onChange,
}: {
  invoice: ApiInvoiceDetailDto;
  customers: ApiCustomerDto[];
  exporters: ApiExporterDto[];
  isBusy: boolean;
  isEditable: boolean;
  message: string | null;
  onRefresh: () => void;
  onChange: (next: InvoicePatch) => void;
}) {
  function applyCustomer(customerId: string) {
    if (!customerId) {
      onChange({ customerId: undefined });
      return;
    }

    const customer = customers.find((item) => item.id === Number(customerId));
    if (!customer) {
      return;
    }

    onChange({
      customerId: customer.id,
      customerNameEN: customer.customerNameEN,
      customerAddressEN: customer.addressEN ?? "",
      notifyPartyName: customer.notifyPartyName ?? "",
      notifyPartyAddress: customer.notifyPartyAddress ?? "",
    });
  }

  function applyExporter(exporterId: string) {
    if (!exporterId) {
      onChange({ exporterId: undefined });
      return;
    }

    const exporter = exporters.find((item) => item.id === Number(exporterId));
    if (!exporter) {
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

  return (
    <section className="form-section" aria-label="客户与出口商">
      <div className="section-header">
        <h2>客户与出口商</h2>
        <button className="icon-button" type="button" title="刷新客户和出口商" aria-label="刷新客户和出口商" disabled={isBusy} onClick={onRefresh}>
          <RefreshCw size={17} aria-hidden="true" />
        </button>
      </div>
      {message ? <div className="alert">{message}</div> : null}
      <div className="invoice-party-groups">
        <section className="invoice-party-group" aria-label="客户信息">
          <div className="invoice-party-group-heading"><strong>客户信息</strong><span>选择客户档案后可继续调整本张发票内容</span></div>
          <div className="field-grid">
          <SelectField
          label="客户档案"
          value={invoice.customerId && invoice.customerId > 0 ? String(invoice.customerId) : ""}
          disabled={isBusy || !isEditable}
          options={customers.map((customer) => ({
            value: String(customer.id),
            label: customer.displayName || customer.customerNameEN || "-",
          }))}
          onChange={applyCustomer}
        />
          <TextField
            className="field-grid-span-2"
            label="客户英文名"
            value={invoice.customerNameEN}
            disabled={!isEditable}
            onChange={(value) => onChange({ customerNameEN: value })}
          />
          <TextField
            className="field-grid-span-all"
            label="客户地址"
            value={invoice.customerAddressEN ?? ""}
            disabled={!isEditable}
            onChange={(value) => onChange({ customerAddressEN: value })}
          />
          </div>
        </section>

        <section className="invoice-party-group" aria-label="通知人信息">
          <div className="invoice-party-group-heading"><strong>通知人信息</strong><span>与客户不同的收货通知对象可单独填写</span></div>
          <div className="field-grid">
            <TextField className="field-grid-span-all" label="通知人" value={invoice.notifyPartyName ?? ""} disabled={!isEditable} onChange={(value) => onChange({ notifyPartyName: value })} />
            <TextField
              className="field-grid-span-all"
              label="通知人地址"
              value={invoice.notifyPartyAddress ?? ""}
              disabled={!isEditable}
              onChange={(value) => onChange({ notifyPartyAddress: value })}
            />
          </div>
        </section>

        <section className="invoice-party-group invoice-party-group-exporter" aria-label="出口商与收款信息">
          <div className="invoice-party-group-heading"><strong>出口商与收款信息</strong><span>企业身份、地址、海关信息和银行资料集中维护</span></div>
          <div className="field-grid">
          <SelectField
          label="出口商档案"
          value={invoice.exporterId && invoice.exporterId > 0 ? String(invoice.exporterId) : ""}
          disabled={isBusy || !isEditable}
          options={exporters.map((exporter) => ({
            value: String(exporter.id),
            label: exporter.exporterNameEN || exporter.exporterNameCN || "-",
          }))}
          onChange={applyExporter}
        />
        <TextField
          className="field-grid-span-2"
          label="出口商英文名"
          value={invoice.exporterNameEN}
          disabled={!isEditable}
          onChange={(value) => onChange({ exporterNameEN: value })}
        />
        <TextField
          className="field-grid-span-2"
          label="出口商中文名"
          value={invoice.exporterNameCN ?? ""}
          disabled={!isEditable}
          onChange={(value) => onChange({ exporterNameCN: value })}
        />
        <TextField
          className="field-grid-span-2"
          label="出口商英文地址"
          value={invoice.exporterAddressEN ?? ""}
          disabled={!isEditable}
          onChange={(value) => onChange({ exporterAddressEN: value })}
        />
        <TextField
          className="field-grid-span-2"
          label="出口商中文地址"
          value={invoice.exporterAddressCN ?? ""}
          disabled={!isEditable}
          onChange={(value) => onChange({ exporterAddressCN: value })}
        />
        <TextField label="统一信用代码" value={invoice.exporterCreditCode ?? ""} disabled={!isEditable} onChange={(value) => onChange({ exporterCreditCode: value })} />
        <TextField label="出口商海关编码" value={invoice.exporterCustomsCode ?? ""} disabled={!isEditable} onChange={(value) => onChange({ exporterCustomsCode: value })} />
        <TextField
          className="field-grid-span-2"
          label="银行名称"
          value={invoice.bankName ?? ""}
          disabled={!isEditable}
          onChange={(value) => onChange({ bankName: value })}
        />
        <TextField
          className="field-grid-span-2"
          label="银行账号"
          value={invoice.bankAccount ?? ""}
          disabled={!isEditable}
          onChange={(value) => onChange({ bankAccount: value })}
        />
        <TextField label="SWIFT" value={invoice.swiftCode ?? ""} disabled={!isEditable} onChange={(value) => onChange({ swiftCode: value })} />
        <NumberField label="汇率" value={invoice.exchangeRate ?? 0} step="0.0001" disabled={!isEditable} onChange={(value) => onChange({ exchangeRate: value })} />
          </div>
        </section>
      </div>
    </section>
  );
}

export function InvoiceExtendedFieldsPanel({
  invoice,
  isEditable,
  onChange,
}: {
  invoice: ApiInvoiceDetailDto;
  isEditable: boolean;
  onChange: (next: InvoicePatch) => void;
}) {
  return (
    <section className="form-section invoice-extended-fields-section" aria-label="报关与扩展字段">
      <div className="section-header">
        <h2>报关与扩展字段</h2>
      </div>
      <div className="field-grid">
        <TextField
          label="报关行名称"
          value={invoice.customsBrokerName ?? ""}
          disabled={!isEditable}
          onChange={(value) => onChange({ customsBrokerName: value })}
        />
        <TextField
          label="报关行编码"
          value={invoice.customsBrokerCode ?? ""}
          disabled={!isEditable}
          onChange={(value) => onChange({ customsBrokerCode: value })}
        />
        <TextField label="备用1" value={invoice.spare1 ?? ""} disabled={!isEditable} onChange={(value) => onChange({ spare1: value })} />
        <TextField label="备用2" value={invoice.spare2 ?? ""} disabled={!isEditable} onChange={(value) => onChange({ spare2: value })} />
        <TextField label="备用3" value={invoice.spare3 ?? ""} disabled={!isEditable} onChange={(value) => onChange({ spare3: value })} />
        <TextAreaField
          className="field-grid-span-all invoice-custom-fields-json"
          label="扩展字段 JSON"
          value={invoice.customFieldsJson ?? ""}
          disabled={!isEditable}
          onChange={(value) => onChange({ customFieldsJson: value })}
        />
      </div>
    </section>
  );
}

export function InvoiceShippingTermsPanel({
  invoice,
  isNewInvoice = false,
  isEditable,
  onChange,
  customOptions,
  onCommitCustomOption,
}: {
  invoice: ApiInvoiceDetailDto;
  isNewInvoice?: boolean;
  isEditable: boolean;
  onChange: (next: InvoicePatch) => void;
  customOptions?: CustomOptionMap;
  onCommitCustomOption?: (optionType: string, value: string) => void;
}) {
  const totalsFields = (
    <>
      <NumberField label="总箱数" value={invoice.totalCartons ?? 0} disabled={!isEditable} onChange={(value) => onChange({ totalCartons: value })} />
      <NumberField label="总数量" value={invoice.totalQuantity ?? 0} disabled={!isEditable} onChange={(value) => onChange({ totalQuantity: value })} />
      <NumberField label="总毛重" value={invoice.totalGrossWeight ?? 0} disabled={!isEditable} onChange={(value) => onChange({ totalGrossWeight: value })} />
      <NumberField label="总净重" value={invoice.totalNetWeight ?? 0} disabled={!isEditable} onChange={(value) => onChange({ totalNetWeight: value })} />
      <NumberField label="总体积" value={invoice.totalVolume ?? 0} disabled={!isEditable} onChange={(value) => onChange({ totalVolume: value })} />
      <NumberField label="采购总额" value={invoice.totalPurchaseAmount ?? 0} disabled onChange={() => undefined} />
      <NumberField label="退税总额" value={invoice.totalTaxRefundAmount ?? 0} disabled onChange={() => undefined} />
      <NumberField label="利润总额" value={invoice.totalProfit ?? 0} disabled onChange={() => undefined} />
    </>
  );

  return (
    <section className="form-section" aria-label="运输与条款">
      <div className="section-header">
        <h2>运输与条款</h2>
      </div>
      <div className="field-grid">
        <TextField label="目的国" value={invoice.destinationCountry ?? ""} disabled={!isEditable} onChange={(value) => onChange({ destinationCountry: value })} />
        <EditableComboField
          label="装运港"
          value={invoice.portOfLoading ?? ""}
          disabled={!isEditable}
          options={getCustomOptions(customOptions, "PortOfLoading")}
          onChange={(value) => onChange({ portOfLoading: value })}
          onCommit={(value) => onCommitCustomOption?.("PortOfLoading", value)}
        />
        <EditableComboField
          label="目的港"
          value={invoice.portOfDestination ?? ""}
          disabled={!isEditable}
          options={getCustomOptions(customOptions, "PortOfDestination")}
          onChange={(value) => onChange({ portOfDestination: value })}
          onCommit={(value) => onCommitCustomOption?.("PortOfDestination", value)}
        />
        <TextField label="贸易条款" value={invoice.tradeTerms ?? ""} disabled={!isEditable} onChange={(value) => onChange({ tradeTerms: value })} />
        <EditableComboField
          label="运输方式"
          value={invoice.transportMode ?? ""}
          disabled={!isEditable}
          options={getCustomOptions(customOptions, "TransportMode")}
          onChange={(value) => onChange({ transportMode: value })}
          onCommit={(value) => onCommitCustomOption?.("TransportMode", value)}
        />
        <EditableComboField
          label="付款条款"
          value={invoice.paymentTerms ?? ""}
          disabled={!isEditable}
          options={getCustomOptions(customOptions, "PaymentTerms")}
          onChange={(value) => onChange({ paymentTerms: value })}
          onCommit={(value) => onCommitCustomOption?.("PaymentTerms", value)}
        />
        {isNewInvoice ? null : totalsFields}
      </div>
      {isNewInvoice ? (
        <details className="invoice-inline-details">
          <summary>汇总与派生金额</summary>
          <div className="field-grid invoice-inline-details-grid">{totalsFields}</div>
        </details>
      ) : null}
    </section>
  );
}

export function InvoiceMarksAndItemsPanel({
  client,
  invoice,
  canRedoItemEdit,
  canSaveToProductLibrary,
  canUseHsKnowledge,
  canUndoItemEdit,
  invoiceItemBlankRowCount,
  isEditable,
  isFocusedWorkbench = false,
  isProductLibraryBusy,
  onChange,
  onAddItem,
  onApplyProductLibraryItem,
  onChangeItem,
  onClearItemCells,
  onDuplicateItem,
  onFillDownItemCells,
  onFillDownItemField,
  onMoveItem,
  onOpenFocusedWorkbench,
  onPasteItemTable,
  onRedoItemEdit,
  onRefreshProductLibrary,
  onRemoveItem,
  onSaveItemToProductLibrary,
  onSearchProductLibrary,
  onUndoItemEdit,
  productLibraryMessage,
  productLibraryProducts,
  unitLookupMessage,
  unitOptions,
}: {
  client: ExportDocManagerApiClient;
  invoice: ApiInvoiceDetailDto;
  canRedoItemEdit: boolean;
  canSaveToProductLibrary: boolean;
  canUseHsKnowledge: boolean;
  canUndoItemEdit: boolean;
  invoiceItemBlankRowCount: number;
  isEditable: boolean;
  isFocusedWorkbench?: boolean;
  isProductLibraryBusy: boolean;
  onChange: (next: InvoicePatch) => void;
  onAddItem: () => void;
  onApplyProductLibraryItem: (product: ApiProductDto, insertAfterIndex: number | null) => void;
  onChangeItem: (index: number, next: Partial<ApiInvoiceItemDto>) => void;
  onClearItemCells: (cells: InvoiceItemCellSelection[]) => void;
  onDuplicateItem: (index: number) => void;
  onFillDownItemCells: (cells: InvoiceItemCellSelection[]) => void;
  onFillDownItemField: (index: number, field: EditableInvoiceItemField) => void;
  onMoveItem: (index: number, direction: -1 | 1) => void;
  onOpenFocusedWorkbench?: () => void;
  onPasteItemTable: (
    startRowIndex: number,
    startField: EditableInvoiceItemField,
    rows: string[][],
    targetFields?: EditableInvoiceItemField[],
  ) => void;
  onRedoItemEdit: () => void;
  onRefreshProductLibrary: () => void;
  onRemoveItem: (index: number) => void;
  onSaveItemToProductLibrary: (index: number) => void;
  onSearchProductLibrary: (keyword: string) => void;
  onUndoItemEdit: () => void;
  productLibraryMessage: string | null;
  productLibraryProducts: ApiProductDto[];
  unitLookupMessage: string | null;
  unitOptions: ApiUnitDto[];
}) {
  const [isShippingMarkEditorOpen, setIsShippingMarkEditorOpen] = useState(false);
  const [isShippingMarkSaving, setIsShippingMarkSaving] = useState(false);
  const [isShippingMarkPreviewBusy, setIsShippingMarkPreviewBusy] = useState(false);
  const [shippingMarkPreviewDataUrl, setShippingMarkPreviewDataUrl] = useState<string | null>(null);
  const [shippingMarkMessage, setShippingMarkMessage] = useState<string | null>(null);

  const shippingMarksMode = invoice.shippingMarksType?.trim() === "Image" ? "Image" : "Text";
  const shippingMarksImagePath = invoice.shippingMarksImage?.trim() ?? "";

  useEffect(() => {
    if (shippingMarksMode !== "Image" || !shippingMarksImagePath) {
      setShippingMarkPreviewDataUrl(null);
      setIsShippingMarkPreviewBusy(false);
      return;
    }

    let isCancelled = false;
    setIsShippingMarkPreviewBusy(true);
    setShippingMarkMessage(null);

    client
      .previewShippingMarkImage({ body: { imagePath: shippingMarksImagePath } })
      .then((response) => {
        if (isCancelled) {
          return;
        }

        setShippingMarkPreviewDataUrl(response.dataUrl);
        setShippingMarkMessage(null);
      })
      .catch((error) => {
        if (isCancelled) {
          return;
        }

        setShippingMarkPreviewDataUrl(null);
        setShippingMarkMessage(readApiError(error));
      })
      .finally(() => {
        if (!isCancelled) {
          setIsShippingMarkPreviewBusy(false);
        }
      });

    return () => {
      isCancelled = true;
    };
  }, [client, shippingMarksImagePath, shippingMarksMode]);

  function changeShippingMarksMode(nextMode: "Text" | "Image") {
    if (nextMode === shippingMarksMode) {
      return;
    }

    setShippingMarkMessage(null);
    onChange({ shippingMarksType: nextMode });
  }

  async function saveShippingMarkImage(imageDataUrl: string) {
    setIsShippingMarkSaving(true);
    setShippingMarkMessage(null);

    try {
      const response = await client.saveShippingMarkImage({ body: { imageDataUrl } });
      setShippingMarkPreviewDataUrl(imageDataUrl);
      onChange({
        shippingMarks: "",
        shippingMarksType: "Image",
        shippingMarksImage: response.imagePath,
      });
      setShippingMarkMessage("唛头图片已保存。");
      setIsShippingMarkEditorOpen(false);
    } catch (error) {
      setShippingMarkMessage(readApiError(error));
      throw error;
    } finally {
      setIsShippingMarkSaving(false);
    }
  }

  const supportFields = (
    <>
      <div className="shipping-mark-field invoice-items-support-panel">
        <div className="shipping-mark-mode-row">
          <span className="shipping-mark-field-title">唛头</span>
          <div className="segmented-control shipping-mark-mode-control" role="group" aria-label="唛头类型">
            <button
              type="button"
              className={shippingMarksMode === "Text" ? "segmented-active" : ""}
              disabled={!isEditable}
              onClick={() => changeShippingMarksMode("Text")}
            >
              <FileText size={15} aria-hidden="true" />
              <span>文本</span>
            </button>
            <button
              type="button"
              className={shippingMarksMode === "Image" ? "segmented-active" : ""}
              disabled={!isEditable}
              onClick={() => changeShippingMarksMode("Image")}
            >
              <ImageIcon size={15} aria-hidden="true" />
              <span>图片</span>
            </button>
          </div>
        </div>
        {shippingMarksMode === "Text" ? (
          <label className="textarea-field shipping-mark-textarea-field">
            <textarea
              value={invoice.shippingMarks ?? ""}
              disabled={!isEditable}
              onChange={(event) => onChange({ shippingMarks: event.target.value, shippingMarksType: "Text" })}
            />
          </label>
        ) : (
          <div className="shipping-mark-image-panel">
            <div className="shipping-mark-preview-frame">
              {isShippingMarkPreviewBusy ? <span>加载中</span> : null}
              {!isShippingMarkPreviewBusy && shippingMarkPreviewDataUrl ? (
                <img src={shippingMarkPreviewDataUrl} alt="唛头图片" />
              ) : null}
              {!isShippingMarkPreviewBusy && !shippingMarkPreviewDataUrl ? <span>未设置图片</span> : null}
            </div>
            <div className="shipping-mark-image-actions">
              <button
                className="command-button secondary"
                type="button"
                disabled={!isEditable || isShippingMarkSaving}
                onClick={() => {
                  setShippingMarkMessage(null);
                  setIsShippingMarkEditorOpen(true);
                }}
              >
                <Pencil size={16} aria-hidden="true" />
                <span>编辑图片</span>
              </button>
              {shippingMarksImagePath ? <span className="shipping-mark-image-path">{shippingMarksImagePath}</span> : null}
            </div>
          </div>
        )}
        {shippingMarkMessage ? <div className="item-editor-message shipping-mark-message">{shippingMarkMessage}</div> : null}
      </div>
      <TextAreaField
        className="invoice-special-terms-field"
        label="特殊条款"
        value={invoice.specialTerms ?? ""}
        disabled={!isEditable}
        onChange={(value) => onChange({ specialTerms: value })}
      />
    </>
  );

  return (
    <section
      className={isFocusedWorkbench ? "form-section invoice-items-workbench invoice-items-focus-panel" : "form-section invoice-items-workbench"}
      aria-label="商品明细"
    >
      <div className="section-header">
        <div>
          <h2>商品明细</h2>
          <span>{invoice.items?.length ?? 0} 行已录入</span>
        </div>
        <div className="toolbar-actions invoice-items-header-actions">
          {!isFocusedWorkbench && onOpenFocusedWorkbench ? (
            <button className="command-button secondary" type="button" onClick={onOpenFocusedWorkbench}>
              <Maximize2 size={16} aria-hidden="true" />
              <span>明细工作台</span>
            </button>
          ) : null}
          <button className="icon-button" type="button" title="新增商品明细" aria-label="新增商品明细" disabled={!isEditable} onClick={onAddItem}>
            <Plus size={17} aria-hidden="true" />
          </button>
        </div>
      </div>
      {isFocusedWorkbench ? (
        <details className="invoice-items-support-details">
          <summary>唛头与特殊条款</summary>
          <div className="invoice-items-support-details-body">{supportFields}</div>
        </details>
      ) : (
        supportFields
      )}
      <InvoiceItemsEditor
        client={client}
        items={invoice.items}
        canRedoItemEdit={canRedoItemEdit}
        canSaveToProductLibrary={canSaveToProductLibrary}
        canUseHsKnowledge={canUseHsKnowledge}
        canUndoItemEdit={canUndoItemEdit}
        blankRowCount={invoiceItemBlankRowCount}
        currency={invoice.currency}
        isProductLibraryBusy={isProductLibraryBusy}
        readOnly={!isEditable}
        onAddItem={onAddItem}
        onApplyProductLibraryItem={onApplyProductLibraryItem}
        onChangeItem={onChangeItem}
        onClearItemCells={onClearItemCells}
        onDuplicateItem={onDuplicateItem}
        onFillDownItemCells={onFillDownItemCells}
        onFillDownItemField={onFillDownItemField}
        onMoveItem={onMoveItem}
        onPasteItemTable={onPasteItemTable}
        onRedoItemEdit={onRedoItemEdit}
        onRefreshProductLibrary={onRefreshProductLibrary}
        onRemoveItem={onRemoveItem}
        onSaveItemToProductLibrary={onSaveItemToProductLibrary}
        onSearchProductLibrary={onSearchProductLibrary}
        onUndoItemEdit={onUndoItemEdit}
        productLibraryMessage={productLibraryMessage}
        productLibraryProducts={productLibraryProducts}
        focusedWorkbench={isFocusedWorkbench}
        unitLookupMessage={unitLookupMessage}
        unitOptions={unitOptions}
      />
      {isShippingMarkEditorOpen ? (
        <ShippingMarkEditorDialog
          initialImageDataUrl={shippingMarkPreviewDataUrl}
          isSaving={isShippingMarkSaving}
          message={shippingMarkMessage}
          onClose={() => setIsShippingMarkEditorOpen(false)}
          onSave={saveShippingMarkImage}
        />
      ) : null}
    </section>
  );
}
