import { useId } from "react";
import { Trash2 } from "lucide-react";
import type { ApiCustomsCooAttachmentDto, ApiCustomsCooNonpartyCorpDto, ApiCustomsCooOptionDto } from "../../api/index.ts";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { CooItemSelectInput } from "./CustomsCooFields.tsx";
import { normalizeCooOptions, numberOrZero } from "./customsCooModel.ts";

export function CooNonpartyEditor({
  data,
  onChangeCorp,
  onRemoveCorp,
}: {
  data: ApiCustomsCooNonpartyCorpDto[];
  onChangeCorp: (index: number, next: Partial<ApiCustomsCooNonpartyCorpDto>) => void;
  onRemoveCorp: (index: number) => void;
}) {
  return (
    <div className="table-frame compact-table">
      <table className="coo-nonparty-table">
        <thead>
          <tr>
            <th>操作</th>
            <th>序号</th>
            <th>企业名称</th>
            <th>地址</th>
            <th>国家代码</th>
            <th>国家名称</th>
          </tr>
        </thead>
        <tbody>
          {data.length === 0 ? (
            <tr>
              <td colSpan={6} className="empty-cell small-empty">
                暂无第三方企业
              </td>
            </tr>
          ) : (
            data.map((corp, index) => (
              <tr key={`${corp.id || "new"}-${index}`}>
                <td className="item-action-cell">
                  <button className="icon-button compact-icon-button" type="button" title="删除第三方企业" onClick={() => onRemoveCorp(index)}>
                    <Trash2 size={16} aria-hidden="true" />
                  </button>
                </td>
                <td>
                  <CooItemNumberInput ariaLabel={`第 ${index + 1} 个第三方企业序号`} value={corp.sortNo} onChange={(value) => onChangeCorp(index, { sortNo: value })} />
                </td>
                <td>
                  <CooItemTextInput ariaLabel={`第 ${index + 1} 个第三方企业名称`} value={corp.entName} onChange={(value) => onChangeCorp(index, { entName: value })} />
                </td>
                <td>
                  <CooItemTextInput ariaLabel={`第 ${index + 1} 个第三方企业地址`} value={corp.entAddr} onChange={(value) => onChangeCorp(index, { entAddr: value })} />
                </td>
                <td>
                  <CooItemTextInput ariaLabel={`第 ${index + 1} 个第三方企业国家代码`} value={corp.entCountryCode} onChange={(value) => onChangeCorp(index, { entCountryCode: value })} />
                </td>
                <td>
                  <CooItemTextInput ariaLabel={`第 ${index + 1} 个第三方企业国家名称`} value={corp.entCountryName} onChange={(value) => onChangeCorp(index, { entCountryName: value })} />
                </td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}

export function CooAttachmentTable({
  data,
  certTypeOptions,
  disabled,
  onChangeAttachment,
  onRemoveAttachment,
  onPathError,
}: {
  data: ApiCustomsCooAttachmentDto[];
  certTypeOptions: ApiCustomsCooOptionDto[];
  disabled: boolean;
  onChangeAttachment: (index: number, next: Partial<ApiCustomsCooAttachmentDto>) => void;
  onRemoveAttachment: (index: number) => void;
  onPathError: (message: string) => void;
}) {
  return (
    <div className="table-frame compact-table">
      <table className="coo-attachment-table">
        <thead>
          <tr>
            <th>操作</th>
            <th>证书号</th>
            <th>证书类型</th>
            <th>录入企业</th>
            <th>出口商代码</th>
            <th>文件类型</th>
            <th>文件名</th>
            <th>文件路径</th>
            <th>说明</th>
            <th>文档类型</th>
            <th>延迟提交</th>
            <th>存在</th>
          </tr>
        </thead>
        <tbody>
          {data.length === 0 ? (
            <tr>
              <td colSpan={12} className="empty-cell small-empty">
                暂无附件
              </td>
            </tr>
          ) : (
            data.map((attachment, index) => (
              <tr key={`${attachment.id || attachment.fileName || "attachment"}-${index}`}>
                <td className="row-actions-cell">
                  {renderOpenPathAction(attachment.filePath, "打开附件", onPathError)}
                  <button
                    className="icon-button compact-icon-button danger"
                    type="button"
                    title="删除附件"
                    disabled={disabled}
                    onClick={() => onRemoveAttachment(index)}
                  >
                    <Trash2 size={15} aria-hidden="true" />
                  </button>
                </td>
                <td>
                  <CooItemTextInput
                    ariaLabel={`第 ${index + 1} 个附件证书号`}
                    value={attachment.certNo}
                    disabled={disabled}
                    onChange={(value) => onChangeAttachment(index, { certNo: value })}
                  />
                </td>
                <td>
                  <CooItemSelectInput
                    ariaLabel={`第 ${index + 1} 个附件证书类型`}
                    value={attachment.certType}
                    disabled={disabled}
                    options={certTypeOptions}
                    onChange={(value) => onChangeAttachment(index, { certType: value })}
                  />
                </td>
                <td>
                  <CooItemTextInput
                    ariaLabel={`第 ${index + 1} 个附件录入企业`}
                    value={attachment.aplRegNo}
                    disabled={disabled}
                    onChange={(value) => onChangeAttachment(index, { aplRegNo: value })}
                  />
                </td>
                <td>
                  <CooItemTextInput
                    ariaLabel={`第 ${index + 1} 个附件出口商代码`}
                    value={attachment.ciqRegNo}
                    disabled={disabled}
                    onChange={(value) => onChangeAttachment(index, { ciqRegNo: value })}
                  />
                </td>
                <td>
                  <CooItemTextInput
                    ariaLabel={`第 ${index + 1} 个附件文件类型`}
                    value={attachment.fileType}
                    disabled={disabled}
                    onChange={(value) => onChangeAttachment(index, { fileType: value })}
                  />
                </td>
                <td>
                  <CooItemTextInput
                    ariaLabel={`第 ${index + 1} 个附件文件名`}
                    value={attachment.fileName}
                    disabled={disabled}
                    onChange={(value) => onChangeAttachment(index, { fileName: value })}
                  />
                </td>
                <td>
                  <CooItemTextInput
                    ariaLabel={`第 ${index + 1} 个附件文件路径`}
                    value={attachment.filePath}
                    disabled={disabled}
                    onChange={(value) => onChangeAttachment(index, { filePath: value })}
                  />
                </td>
                <td>
                  <CooItemTextInput
                    ariaLabel={`第 ${index + 1} 个附件说明`}
                    value={attachment.description}
                    disabled={disabled}
                    onChange={(value) => onChangeAttachment(index, { description: value })}
                  />
                </td>
                <td>
                  <CooItemTextInput
                    ariaLabel={`第 ${index + 1} 个附件文档类型`}
                    value={attachment.docType}
                    disabled={disabled}
                    onChange={(value) => onChangeAttachment(index, { docType: value })}
                  />
                </td>
                <td className="coo-attachment-delay-cell">
                  <input
                    type="checkbox"
                    aria-label={`第 ${index + 1} 个附件延迟提交`}
                    checked={attachment.isDelay}
                    disabled={disabled}
                    onChange={(event) => onChangeAttachment(index, { isDelay: event.target.checked })}
                  />
                </td>
                <td>{attachment.fileExistsAtBuild ? "是" : "否"}</td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}

function CooItemTextInput({
  ariaLabel,
  value,
  options,
  disabled,
  onChange,
}: {
  ariaLabel: string;
  value?: string;
  options?: ApiCustomsCooOptionDto[];
  disabled?: boolean;
  onChange: (value: string) => void;
}) {
  const listId = `coo-item-text-${useId().replace(/:/g, "-")}`;
  const normalizedOptions = normalizeCooOptions(options ?? []).filter((option) => option.value);

  return (
    <>
    <input
      className="item-cell-input"
      aria-label={ariaLabel}
      list={normalizedOptions.length > 0 ? listId : undefined}
      value={value ?? ""}
      disabled={disabled}
      onChange={(event) => onChange(event.target.value)}
    />
      {normalizedOptions.length > 0 ? (
        <datalist id={listId}>
          {normalizedOptions.map((option) => (
            <option key={`${option.value}-${option.label}`} value={option.value} label={option.label} />
          ))}
        </datalist>
      ) : null}
    </>
  );
}

function CooItemNumberInput({
  ariaLabel,
  value,
  disabled,
  onChange,
}: {
  ariaLabel: string;
  value?: number;
  disabled?: boolean;
  onChange: (value: number) => void;
}) {
  return (
    <input
      className="item-cell-input item-number-input"
      type="number"
      step="1"
      aria-label={ariaLabel}
      value={String(numberOrZero(value))}
      disabled={disabled}
      onChange={(event) => onChange(numberOrZero(Number(event.target.value)))}
    />
  );
}

