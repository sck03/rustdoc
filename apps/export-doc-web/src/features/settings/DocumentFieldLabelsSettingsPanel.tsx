import { documentSpareKeys } from "../../ui/documentSpareFields.ts";
import { TextSetting } from "./SettingsFieldControls.tsx";
import type { SettingsRecord } from "./settingsTypes.ts";

const groups = [{ key: "invoice", label: "发票表头" }, { key: "item", label: "商品明细" }, { key: "payment", label: "付款报销" }] as const;

export function DocumentFieldLabelsSettingsPanel({ settings, disabled, onChange }: {
  settings: SettingsRecord;
  disabled: boolean;
  onChange: (path: string[], value: unknown) => void;
}) {
  return <section className="form-section" aria-label="单据字段名称">
    <div className="section-header"><h2>单据字段名称</h2></div>
    <p className="form-field-description">三类单据各有 10 个独立备用字段。名称最多 40 字，同组不能重复；留空显示“备用 1”等默认名称。</p>
    <p className="form-field-description">保存后供连接本服务的所有账号和各端共用，重新进入或刷新页面即可读取。已有内容保持不变；模板字段列表同步名称，已有模板的文字标题可在设计器内修改。</p>
    <fieldset className="settings-fieldset" disabled={disabled}>
      {groups.map((group) => <details className="document-spare-fields" key={group.key}>
        <summary>{group.label}<small>10 个字段</small></summary>
        <div className="field-grid">{documentSpareKeys.map((key, index) => <TextSetting key={key}
          settings={settings} path={["system", "documentFieldLabels", group.key, key]}
          label={`备用 ${index + 1}`} placeholder={index === 0 ? "例如：船名航次" : `备用 ${index + 1}`}
          maxLength={40} onChange={onChange} />)}</div>
      </details>)}
    </fieldset>
  </section>;
}
