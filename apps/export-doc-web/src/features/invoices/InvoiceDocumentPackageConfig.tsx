import { ArrowDown, ArrowUp, FolderOpen, Plus, Trash2 } from "lucide-react";
import type { BatchExportConfigDraft, BatchExportItemSetting } from "./invoiceReportPreviewModel.ts";
type Option={value:string;label:string};
type Props={canEdit:boolean;desktopAvailable:boolean;draft:BatchExportConfigDraft;templateOptions:Option[];onAdd():void;onChooseTemplate(index:number):void|Promise<void>;onMove(index:number,direction:-1|1):void;onRemove(index:number):void;onUpdate(changes:Partial<BatchExportConfigDraft>):void;onUpdateItem(index:number,changes:Partial<BatchExportItemSetting>):void};
export function InvoiceDocumentPackageConfig(props:Props){const {canEdit:canEditPackageConfig,desktopAvailable,draft:packageConfigDraft,templateOptions:packageConfigTemplateOptions,onAdd:addPackageConfigItem,onChooseTemplate:choosePackageConfigTemplateFile,onMove:movePackageConfigItem,onRemove:removePackageConfigItem,onUpdate:updatePackageConfig,onUpdateItem:updatePackageConfigItem}=props;return (
        <details className="document-package-config">
          <summary>单据包配置</summary>
          <div className="document-package-config-body">
            <div className="document-package-rules-grid">
              <label>
                <span>文件名规则</span>
                <input
                  value={packageConfigDraft.fileNamePattern}
                  disabled={!canEditPackageConfig}
                  onChange={(event) => updatePackageConfig({ fileNamePattern: event.target.value })}
                />
              </label>
              <label>
                <span>文件夹规则</span>
                <input
                  value={packageConfigDraft.folderPattern}
                  disabled={!canEditPackageConfig}
                  onChange={(event) => updatePackageConfig({ folderPattern: event.target.value })}
                />
              </label>
              <label className="toggle-field">
                <input
                  type="checkbox"
                  checked={packageConfigDraft.mergePdf}
                  disabled={!canEditPackageConfig}
                  onChange={(event) => updatePackageConfig({ mergePdf: event.target.checked })}
                />
                <span>默认合并 PDF</span>
              </label>
              <label className="toggle-field">
                <input
                  type="checkbox"
                  checked={packageConfigDraft.zipAfterExport}
                  disabled={!canEditPackageConfig}
                  onChange={(event) => updatePackageConfig({ zipAfterExport: event.target.checked })}
                />
                <span>默认生成 ZIP</span>
              </label>
            </div>
            <div className="batch-export-items-toolbar">
              <span>可用占位符: {"{InvoiceNo}"} {"{Customer}"} {"{DocType}"} {"{Date}"}</span>
              <button className="command-button secondary" type="button" disabled={!canEditPackageConfig} onClick={addPackageConfigItem}>
                <Plus size={17} aria-hidden="true" />
                <span>新增单证</span>
              </button>
            </div>
            <div className="table-frame batch-export-items-frame document-package-config-frame">
              <table className="batch-export-items-table" aria-label="单据包配置项">
                <thead>
                  <tr>
                    <th>顺序</th>
                    <th>启用</th>
                    <th>名称</th>
                    <th>模板</th>
                    <th>模板路径</th>
                    <th>带章</th>
                    <th>操作</th>
                  </tr>
                </thead>
                <tbody>
                  {packageConfigDraft.items.length > 0 ? (
                    packageConfigDraft.items.map((item, index) => (
                      <tr key={`${index}-${item.templatePath || item.name}`}>
                        <td className="batch-export-order-cell">{index + 1}</td>
                        <td>
                          <input
                            className="batch-export-check-input"
                            type="checkbox"
                            checked={item.isEnabled}
                            disabled={!canEditPackageConfig}
                            aria-label={`启用 ${item.name || index + 1}`}
                            onChange={(event) => updatePackageConfigItem(index, { isEnabled: event.target.checked })}
                          />
                        </td>
                        <td>
                          <input
                            className="batch-export-cell-input"
                            value={item.name}
                            disabled={!canEditPackageConfig}
                            onChange={(event) => updatePackageConfigItem(index, { name: event.target.value })}
                          />
                        </td>
                        <td>
                          <select
                            className="batch-export-cell-input"
                            value={item.templatePath}
                            disabled={!canEditPackageConfig || packageConfigTemplateOptions.length === 0}
                            onChange={(event) => updatePackageConfigItem(index, { templatePath: event.target.value })}
                          >
                            {packageConfigTemplateOptions.map((option) => (
                              <option key={option.value} value={option.value}>
                                {option.label}
                              </option>
                            ))}
                          </select>
                        </td>
                        <td>
                          <div className="batch-export-path-control">
                            <input
                              className="batch-export-cell-input batch-export-path-input"
                              value={item.templatePath}
                              disabled={!canEditPackageConfig}
                              onChange={(event) => updatePackageConfigItem(index, { templatePath: event.target.value })}
                            />
                            {desktopAvailable ? (
                              <button
                                className="icon-button compact-icon-button batch-export-path-button"
                                type="button"
                                title="选择模板文件"
                                disabled={!canEditPackageConfig}
                                onClick={() => void choosePackageConfigTemplateFile(index)}
                              >
                                <FolderOpen size={15} aria-hidden="true" />
                              </button>
                            ) : null}
                          </div>
                        </td>
                        <td>
                          <input
                            className="batch-export-check-input"
                            type="checkbox"
                            checked={item.showSeal}
                            disabled={!canEditPackageConfig}
                            aria-label={`带章 ${item.name || index + 1}`}
                            onChange={(event) => updatePackageConfigItem(index, { showSeal: event.target.checked })}
                          />
                        </td>
                        <td>
                          <div className="batch-export-row-actions">
                            <button
                              className="icon-button compact-icon-button"
                              type="button"
                              title="上移"
                              disabled={!canEditPackageConfig || index === 0}
                              onClick={() => movePackageConfigItem(index, -1)}
                            >
                              <ArrowUp size={15} aria-hidden="true" />
                            </button>
                            <button
                              className="icon-button compact-icon-button"
                              type="button"
                              title="下移"
                              disabled={!canEditPackageConfig || index >= packageConfigDraft.items.length - 1}
                              onClick={() => movePackageConfigItem(index, 1)}
                            >
                              <ArrowDown size={15} aria-hidden="true" />
                            </button>
                            <button
                              className="icon-button compact-icon-button"
                              type="button"
                              title="删除"
                              disabled={!canEditPackageConfig || packageConfigDraft.items.length <= 1}
                              onClick={() => removePackageConfigItem(index)}
                            >
                              <Trash2 size={15} aria-hidden="true" />
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))
                  ) : (
                    <tr>
                      <td className="empty-cell" colSpan={7}>
                        暂无导出项
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </details>


);}

