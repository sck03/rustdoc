import { useEffect, useState } from "react";
import { Copy, FileCheck2, Save, Trash2, WandSparkles } from "lucide-react";
import type { ApiCustomsCooEditorOptionsResponse, ApiCustomsCooItemDto } from "../../api/index.ts";
import { FieldShell, TextAreaField } from "../../ui/FormFields.tsx";
import { CooDatalistField, CooSelectField, buildCooItemPreviousValueOptions, mergeCooDatalistOptions } from "./CustomsCooFields.tsx";
import {
  firstNonEmpty,
  getCooOriginCriteriaOptions,
  getCooOriginCriteriaSubOptions,
  getCooGoodsDescriptionActionTitle,
  normalizeText,
  numberOrZero,
  shouldShowCooGoodsOriginCriteria,
  shouldShowCooGoodsOriginCriteriaRef,
  shouldShowCooGoodsOriginCriteriaSub,
  shouldShowCooGoodsProducerContactFields,
  shouldShowCooGoodsProducerDescription,
  shouldShowCooGoodsRcepFields,
} from "./customsCooModel.ts";

export function CooItemsEditor({
  items,
  certType,
  editorOptions,
  disabled,
  savingProducerRowIndex,
  onChangeItem,
  onRemoveItem,
  onGenerateGoodsDescription,
  onCopyOriginAndEnterpriseToFollowingRows,
  onOpenProducerProfile,
  onSaveProducerProfile,
}: {
  items: ApiCustomsCooItemDto[];
  certType: string;
  editorOptions: ApiCustomsCooEditorOptionsResponse;
  disabled: boolean;
  savingProducerRowIndex: number | null;
  onChangeItem: (index: number, next: Partial<ApiCustomsCooItemDto>) => void;
  onRemoveItem: (index: number) => void;
  onGenerateGoodsDescription: (index: number) => void;
  onCopyOriginAndEnterpriseToFollowingRows: (index: number) => void;
  onOpenProducerProfile: (index: number) => void;
  onSaveProducerProfile: (index: number) => void;
}) {
  const [selectedItemIndex, setSelectedItemIndex] = useState(0);

  useEffect(() => {
    setSelectedItemIndex((current) => {
      if (items.length === 0) {
        return 0;
      }

      return Math.min(Math.max(current, 0), items.length - 1);
    });
  }, [items.length]);

  if (items.length === 0) {
    return <div className="coo-items-empty">暂无商品</div>;
  }

  const activeIndex = Math.min(selectedItemIndex, items.length - 1);
  const item = items[activeIndex];
  const rowLabel = `第 ${activeIndex + 1} 行`;
  const showGoodsRcepFields = shouldShowCooGoodsRcepFields(certType);
  const showGoodsOriginCriteria = shouldShowCooGoodsOriginCriteria(certType);
  const showGoodsOriginCriteriaSub = shouldShowCooGoodsOriginCriteriaSub(certType);
  const showGoodsOriginCriteriaRef = shouldShowCooGoodsOriginCriteriaRef(certType, item.oriCriteria);
  const showGoodsProducerDescription = shouldShowCooGoodsProducerDescription(certType);
  const showGoodsProducerContactFields = shouldShowCooGoodsProducerContactFields(certType);
  const previousValueOptions = (field: keyof ApiCustomsCooItemDto) =>
    buildCooItemPreviousValueOptions(items, activeIndex, field);

  return (
    <div className="coo-items-workbench">
      <aside className="coo-items-rail" aria-label="商品列表">
        <div className="coo-items-rail-header">
          <strong>商品列表</strong>
          <span>{items.length} 行</span>
        </div>
        <div className="coo-items-list" role="listbox" aria-label="商品行">
          {items.map((row, index) => (
            <button
              className={index === activeIndex ? "coo-item-row-card coo-item-row-card-active" : "coo-item-row-card"}
              type="button"
              role="option"
              aria-selected={index === activeIndex}
              key={`${row.id || "new"}-${index}`}
              onClick={() => setSelectedItemIndex(index)}
            >
              <span className="coo-item-row-card-index">{formatCooItemIndex(row, index)}</span>
              <span className="coo-item-row-card-main">{formatCooItemTitle(row, index)}</span>
              <span className="coo-item-row-card-meta">{formatCooItemMeta(row)}</span>
            </button>
          ))}
        </div>
      </aside>

      <div className="coo-item-detail-panel">
        <div className="coo-item-detail-header">
          <div className="coo-item-detail-title">
            <span>{rowLabel}</span>
            <strong title={formatCooItemTitle(item, activeIndex)}>{formatCooItemTitle(item, activeIndex)}</strong>
          </div>
          <div className="coo-item-detail-actions">
            <button
              className="icon-button compact-icon-button"
              type="button"
              title={getCooGoodsDescriptionActionTitle(item)}
              disabled={disabled}
              onClick={() => onGenerateGoodsDescription(activeIndex)}
            >
              <WandSparkles size={16} aria-hidden="true" />
            </button>
            <button
              className="icon-button compact-icon-button"
              type="button"
              title="复制原产标准和生产企业到后续项"
              disabled={disabled || activeIndex >= items.length - 1}
              onClick={() => onCopyOriginAndEnterpriseToFollowingRows(activeIndex)}
            >
              <Copy size={16} aria-hidden="true" />
            </button>
            <button
              className="icon-button compact-icon-button"
              type="button"
              title="选择生产企业资料"
              disabled={disabled}
              onClick={() => onOpenProducerProfile(activeIndex)}
            >
              <FileCheck2 size={16} aria-hidden="true" />
            </button>
            <button
              className="icon-button compact-icon-button"
              type="button"
              title="保存当前生产企业资料"
              disabled={disabled || savingProducerRowIndex === activeIndex}
              onClick={() => onSaveProducerProfile(activeIndex)}
            >
              <Save size={16} aria-hidden="true" />
            </button>
            <button
              className="icon-button compact-icon-button danger"
              type="button"
              title="删除商品"
              disabled={disabled}
              onClick={() => onRemoveItem(activeIndex)}
            >
              <Trash2 size={16} aria-hidden="true" />
            </button>
          </div>
        </div>

        <div className="coo-item-detail-groups">
          <section className="coo-item-field-group" aria-label="商品基础">
            <h3>商品基础</h3>
            <div className="coo-item-field-grid">
              <CooItemNumberField label="项号" value={item.gNo} disabled />
              <CooDatalistField label="货号" value={item.sourceStyleNo} options={previousValueOptions("sourceStyleNo")} disabled onChange={() => undefined} />
              <CooSelectField label="货项标志" value={item.goodsItemFlag} options={editorOptions.goodsItemFlagOptions} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { goodsItemFlag: value })} />
              <CooDatalistField label="HS编码" value={item.hsCode} options={previousValueOptions("hsCode")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { hsCode: value })} />
              <CooDatalistField label="中文名" value={item.goodsName} options={previousValueOptions("goodsName")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { goodsName: value })} />
              <CooDatalistField label="英文名" value={item.goodsNameE} options={previousValueOptions("goodsNameE")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { goodsNameE: value })} />
              {showGoodsRcepFields ? <CooDatalistField label="明细发票号" value={item.invNo} options={previousValueOptions("invNo")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { invNo: value })} /> : null}
              {showGoodsRcepFields ? <CooSelectField label="最高税率标志" value={item.goodsTaxRate} options={editorOptions.goodsTaxRateOptions} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { goodsTaxRate: value })} /> : null}
            </div>
          </section>

          <section className="coo-item-field-group" aria-label="数量包装重量">
            <h3>数量、包装与重量</h3>
            <div className="coo-item-field-grid">
              <CooDatalistField label="标准数量" value={item.goodsQty} options={previousValueOptions("goodsQty")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { goodsQty: value })} />
              <CooDatalistField label="单位(英)" value={item.goodsUnitE} options={previousValueOptions("goodsUnitE")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { goodsUnitE: value })} />
              <CooDatalistField label="单位(中)" value={item.goodsUnit} options={previousValueOptions("goodsUnit")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { goodsUnit: value })} />
              <CooDatalistField label="辅助数量" value={item.goodsQtyRef} options={previousValueOptions("goodsQtyRef")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { goodsQtyRef: value })} />
              <CooDatalistField label="辅助单位" value={item.goodsUnitRef} options={previousValueOptions("goodsUnitRef")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { goodsUnitRef: value })} />
              <CooDatalistField label="第二辅助数" value={item.secdGoodsQtyRef} options={previousValueOptions("secdGoodsQtyRef")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { secdGoodsQtyRef: value })} />
              <CooDatalistField label="第二辅助单位" value={item.secdGoodsUnitRef} options={previousValueOptions("secdGoodsUnitRef")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { secdGoodsUnitRef: value })} />
              <CooDatalistField label="包装件数" value={item.packQty} options={previousValueOptions("packQty")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { packQty: value })} />
              <CooDatalistField label="包装单位(英)" value={item.packUnit} options={mergeCooDatalistOptions(previousValueOptions("packUnit"), editorOptions.packUnitOptions)} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { packUnit: value.toUpperCase() })} />
              <CooSelectField label="包装类型" value={item.packType} options={editorOptions.packTypeOptions} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { packType: value })} />
              <CooDatalistField label="毛重" value={item.grossWt} options={previousValueOptions("grossWt")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { grossWt: value })} />
              <CooDatalistField label="净重" value={item.netWt} options={previousValueOptions("netWt")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { netWt: value })} />
              <CooDatalistField label="重量单位" value={item.wtUnit} options={previousValueOptions("wtUnit")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { wtUnit: value })} />
            </div>
          </section>

          <section className="coo-item-field-group" aria-label="金额和原产规则">
            <h3>金额与原产规则</h3>
            <div className="coo-item-field-grid">
              <CooDatalistField label="发票单价" value={item.invPrice} options={previousValueOptions("invPrice")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { invPrice: value })} />
              <CooDatalistField label="发票金额" value={item.invValue} options={previousValueOptions("invValue")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { invValue: value })} />
              <CooDatalistField label="FOB值" value={item.fobValue} options={previousValueOptions("fobValue")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { fobValue: value })} />
              {showGoodsRcepFields ? <CooDatalistField label="进口成份比例" value={item.iCompPrpr} options={previousValueOptions("iCompPrpr")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { iCompPrpr: value })} /> : null}
              {showGoodsOriginCriteria ? <CooDatalistField label="原产标准" value={item.oriCriteria} options={mergeCooDatalistOptions(previousValueOptions("oriCriteria"), getCooOriginCriteriaOptions(editorOptions, certType))} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { oriCriteria: value.toUpperCase() })} /> : null}
              {showGoodsOriginCriteriaSub ? <CooDatalistField label="子标准" value={item.oriCriteriaSub} options={mergeCooDatalistOptions(previousValueOptions("oriCriteriaSub"), getCooOriginCriteriaSubOptions(editorOptions, certType, item.oriCriteria))} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { oriCriteriaSub: value.toUpperCase() })} /> : null}
              {showGoodsOriginCriteriaRef ? <CooDatalistField label="原产标准辅助项" value={item.oriCriteriaRef} options={previousValueOptions("oriCriteriaRef")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { oriCriteriaRef: value })} /> : null}
              {showGoodsRcepFields ? <CooDatalistField label="协定原产国代码" value={item.goodsOriginCountry} options={previousValueOptions("goodsOriginCountry")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { goodsOriginCountry: value })} /> : null}
              {showGoodsRcepFields ? <CooDatalistField label="协定原产国英文" value={item.goodsOriginCountryEn} options={previousValueOptions("goodsOriginCountryEn")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { goodsOriginCountryEn: value })} /> : null}
            </div>
          </section>

          <section className="coo-item-field-group" aria-label="描述和生产企业">
            <h3>描述与生产企业</h3>
            <div className="coo-item-field-grid coo-item-field-grid-wide">
              <TextAreaField label="货物描述" value={item.goodsDesc} disabled={disabled} className="coo-item-textarea-span" onChange={(value) => onChangeItem(activeIndex, { goodsDesc: value })} />
              {showGoodsProducerDescription ? <TextAreaField label="生产商描述" value={item.producer} disabled={disabled} className="coo-item-textarea-span" onChange={(value) => onChangeItem(activeIndex, { producer: value })} /> : null}
            </div>
            <div className="coo-item-field-grid">
              <CooDatalistField label="生产企业代码" value={item.ciqRegNo} options={previousValueOptions("ciqRegNo")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { ciqRegNo: value })} />
              <CooDatalistField label="生产企业名称" value={item.prdcEtpsName} options={previousValueOptions("prdcEtpsName")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { prdcEtpsName: value })} />
              <CooDatalistField label="生产企业联系人" value={item.prdcEtpsConcEr} options={previousValueOptions("prdcEtpsConcEr")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { prdcEtpsConcEr: value })} />
              <CooDatalistField label="生产企业联系电话" value={item.prdcEtpsTel} options={previousValueOptions("prdcEtpsTel")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { prdcEtpsTel: value })} />
              {showGoodsProducerContactFields ? <CooDatalistField label="生产商电话" value={item.producerTel} options={previousValueOptions("producerTel")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { producerTel: value })} /> : null}
              {showGoodsProducerContactFields ? <CooDatalistField label="生产商传真" value={item.producerFax} options={previousValueOptions("producerFax")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { producerFax: value })} /> : null}
              {showGoodsProducerContactFields ? <CooDatalistField label="生产商邮箱" value={item.producerEmail} options={previousValueOptions("producerEmail")} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { producerEmail: value })} /> : null}
              {showGoodsProducerContactFields ? <CooSelectField label="生产商保密" value={item.producerSertFlag} options={editorOptions.producerSecretOptions} disabled={disabled} onChange={(value) => onChangeItem(activeIndex, { producerSertFlag: value.toUpperCase() })} /> : null}
            </div>
          </section>
        </div>
      </div>
    </div>
  );
}

function CooItemNumberField({ label, value, disabled }: { label: string; value?: number; disabled?: boolean }) {
  return (
    <FieldShell label={label} disabled={disabled}>
      {(descriptionId) => (
        <input
          type="number"
          step="1"
          value={String(numberOrZero(value))}
          disabled={disabled}
          aria-describedby={descriptionId}
          onChange={() => undefined}
        />
      )}
    </FieldShell>
  );
}

function formatCooItemIndex(item: ApiCustomsCooItemDto, index: number) {
  return `#${numberOrZero(item.gNo) || index + 1}`;
}

function formatCooItemTitle(item: ApiCustomsCooItemDto, index: number) {
  return firstNonEmpty(item.sourceStyleNo, item.goodsNameE, item.goodsName, `第 ${index + 1} 行商品`);
}

function formatCooItemMeta(item: ApiCustomsCooItemDto) {
  const quantity = [normalizeText(item.goodsQty), normalizeText(item.goodsUnitE || item.goodsUnit)]
    .filter(Boolean)
    .join(" ");
  const amount = firstNonEmpty(item.invValue, item.fobValue);
  const origin = firstNonEmpty(item.oriCriteria, item.goodsOriginCountryEn, item.goodsOriginCountry);
  const meta = [
    item.hsCode ? `HS ${item.hsCode}` : "",
    quantity,
    amount ? `金额 ${amount}` : "",
    origin ? `原产 ${origin}` : "",
  ].filter(Boolean);

  return meta.length > 0 ? meta.join(" · ") : "未填写关键字段";
}
