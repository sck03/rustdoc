import { lazy, Suspense, useRef, useState, type FormEvent } from "react";
import { FileDown, FolderOpen, PackageCheck, Plus, RefreshCw, Save, Trash2 } from "lucide-react";
import type { ApiContainerPackingAnalysisDto, ApiContainerPackingProjectSummaryDto, ApiContainerTypeDto } from "../../../api/index.ts";
import { formatPlainNumber } from "../../../ui/formUtils.ts";
import { InlineNotice, PageState, PermissionNotice } from "../../../ui/PageState.tsx";
import { ResponsiveTableFrame } from "../../../ui/ResponsiveTable.tsx";
import type { ContainerPackingCargoRow, ContainerPackingFormState, ContainerPackingRenderModeValue, ContainerPackingRulesFormState, ContainerPackingZoneValue } from "./containerPackingModel.ts";
import { containerPackingRenderModeOptions, containerPackingZoneOptions, formatPackingPercent } from "./containerPackingModel.ts";
import { ContainerPackingVisualization, type ContainerPackingVisualizationDimensions } from "./ContainerPackingVisualization.tsx";
const ContainerPackingScene3d = lazy(() => import("./ContainerPackingScene3d.tsx"));

type Props = {
 analysis: ApiContainerPackingAnalysisDto | null; autoRefreshEnabled: boolean; autoRefreshState: string; canAnalyze: boolean;
 canDeleteContainerType: boolean; canLoadProject: boolean; canManage: boolean; canOperate: boolean; canSaveContainerType: boolean; canSaveProject: boolean;
 cargoRows: ContainerPackingCargoRow[]; container: ContainerPackingFormState; containerTypes: ApiContainerTypeDto[]; currentProjectId: number;
 hasVisibleError: boolean; isAnalyzing: boolean; isDeletingContainerType: boolean; isDeletingProject: boolean; isLoadingProject: boolean;
 isRefreshingProjects: boolean; isSavingContainerType: boolean; isSavingProject: boolean; packingStatusText: string; projectName: string;
 renderMode: ContainerPackingRenderModeValue; rules: ContainerPackingRulesFormState; savedProjects: ApiContainerPackingProjectSummaryDto[];
 selectedProjectId: string; validCargoCount: number; visibleMessage: string | null; visualizationDimensions: ContainerPackingVisualizationDimensions | null;
 onAddCargo(): void; onAnalyze(): void; onApplyContainerType(value:string):void; onAutoRefreshChange(value:boolean):void; onClearCargo():void;
 onContainerFieldChange(field:keyof ContainerPackingFormState,value:string):void; onDeleteContainerType():void; onDeleteProject():void; onLoadProject():void;
 onProjectNameChange(value:string):void; onRefreshProjects():void; onRemoveCargo(id:string):void; onRenderModeChange(value:ContainerPackingRenderModeValue):void;
 onRulesFieldChange<K extends keyof ContainerPackingRulesFormState>(field:K,value:ContainerPackingRulesFormState[K]):void; onSaveContainerType():void; onSaveProject():void;
 onSelectedProjectChange(value:string):void; onSubmit(event:FormEvent<HTMLFormElement>):void; onUpdateCargo(id:string,changes:Partial<ContainerPackingCargoRow>):void;
};

export function ContainerPackingWorkspace(props: Props) {
 const { analysis, autoRefreshEnabled, autoRefreshState, canAnalyze, canDeleteContainerType, canLoadProject, canManage, canOperate, canSaveContainerType, canSaveProject, cargoRows, container, containerTypes, currentProjectId, hasVisibleError, isAnalyzing, isDeletingContainerType, isDeletingProject, isLoadingProject, isRefreshingProjects, isSavingContainerType, isSavingProject, packingStatusText, projectName, renderMode, rules, savedProjects, selectedProjectId, validCargoCount, visibleMessage, visualizationDimensions } = props;
 const { onAddCargo:addCargoRow,onAnalyze:runContainerPackingAnalysis,onApplyContainerType:applyContainerType,onAutoRefreshChange:setAutoRefreshEnabled,onClearCargo:clearCargoRows,onContainerFieldChange:updateContainerField,onDeleteContainerType:handleDeleteContainerType,onDeleteProject:handleDeleteProject,onLoadProject:handleLoadProject,onProjectNameChange:updateProjectName,onRefreshProjects,onRemoveCargo:removeCargoRow,onRenderModeChange:setRenderMode,onRulesFieldChange:updateRulesField,onSaveContainerType:handleSaveContainerType,onSaveProject:handleSaveProject,onSelectedProjectChange:setSelectedProjectId,onSubmit:handleSubmit,onUpdateCargo:updateCargoRow }=props;
 const pdfRootRef = useRef<HTMLDivElement | null>(null);
 const [pdfExportState, setPdfExportState] = useState<"idle" | "exporting">("idle");
 const [pdfExportMessage, setPdfExportMessage] = useState<{ kind: "success" | "error"; text: string } | null>(null);

 async function handleExportPdf() {
   if (!analysis || !pdfRootRef.current || pdfExportState === "exporting") return;
   setPdfExportState("exporting");
   setPdfExportMessage(null);
   try {
     const { exportContainerPackingPdf } = await import("./containerPackingPdfExport.ts");
     const result = await exportContainerPackingPdf({ root: pdfRootRef.current, projectName, containerType: container.containerType });
     if (result.status === "cancelled") return;
     const sizeText = formatPdfSize(result.sizeBytes ?? 0);
     if (result.status === "save-failed") {
       setPdfExportMessage({
         kind: "error",
         text: `PDF 已生成，但没有保存成功：${result.error}。请重新点击“导出 PDF”并选择保存位置。`,
       });
       return;
     }
     setPdfExportMessage({
       kind: "success",
       text: result.mode === "desktop"
         ? `PDF 已保存到 ${result.path}（${result.pageCount} 页，${sizeText}）`
         : `PDF 已下载（${result.pageCount} 页，${sizeText}）。`,
     });
   } catch (error) {
     const errorText = error instanceof Error ? error.message : typeof error === "string" ? error : "未知错误";
     setPdfExportMessage({ kind: "error", text: `PDF 生成失败：${errorText}` });
   } finally {
     setPdfExportState("idle");
   }
 }

 function formatPdfSize(sizeBytes: number) {
   if (sizeBytes < 1024 * 1024) return `${Math.max(1, Math.round(sizeBytes / 1024))} KB`;
   return `${(sizeBytes / 1024 / 1024).toFixed(1)} MB`;
 }
  return (
    <form
      className="job-tool-panel"
      aria-label="装箱分析"
      onSubmit={handleSubmit}
    >
      <div className="tool-panel-heading">
        <div>
          <h2>装箱分析</h2>
          <span>
            {analysis
              ? `已装 ${analysis.packedPackages} / ${analysis.totalPackages}`
              : currentProjectId > 0
                ? `方案 #${currentProjectId}`
                : `${validCargoCount} 类货物`}
          </span>
        </div>
        <div className="tool-panel-actions">
          <div
            className="container-packing-render-mode"
            aria-label="装柜显示模式"
          >
            <span className="container-packing-render-mode-label">显示</span>
            <div
              className="segmented-control container-packing-render-buttons"
              role="group"
              aria-label="装柜显示模式"
            >
              {containerPackingRenderModeOptions.map((option) => (
                <button
                  key={option.value}
                  className={
                    renderMode === option.value ? "segmented-active" : ""
                  }
                  type="button"
                  aria-pressed={renderMode === option.value}
                  onClick={() => setRenderMode(option.value)}
                >
                  {option.label}
                </button>
              ))}
            </div>
          </div>
          <label
            className="toggle-field container-packing-auto-refresh"
            aria-label="装柜自动刷新"
            title="开启后，停止输入约 1 秒才会自动更新结果"
          >
            <input
              type="checkbox"
              checked={autoRefreshEnabled}
              disabled={!canOperate}
              onChange={(event) => setAutoRefreshEnabled(event.target.checked)}
            />
            <span>自动分析</span>
          </label>
          <button
            className="command-button secondary"
            type="button"
            disabled={!canOperate}
            onClick={addCargoRow}
          >
            <Plus size={16} aria-hidden="true" />
            <span>添加货物</span>
          </button>
          <button
            className="command-button secondary danger"
            type="button"
            disabled={!canOperate || cargoRows.length === 0}
            onClick={clearCargoRows}
          >
            <Trash2 size={16} aria-hidden="true" />
            <span>清空列表</span>
          </button>
          <button
            className="command-button secondary"
            type="button"
            disabled={!canAnalyze || isAnalyzing}
            onClick={runContainerPackingAnalysis}
          >
            <RefreshCw size={16} aria-hidden="true" />
            <span>立即刷新</span>
          </button>
          <button
            className="command-button secondary"
            type="button"
            disabled={!analysis}
            onClick={() => pdfRootRef.current?.scrollIntoView({ behavior: "smooth", block: "start" })}
          >
            <PackageCheck size={16} aria-hidden="true" />
            <span>查看效果图</span>
          </button>
          <button
            className="command-button secondary"
            type="button"
            disabled={!analysis || pdfExportState === "exporting"}
            onClick={() => void handleExportPdf()}
          >
            <FileDown size={16} aria-hidden="true" />
            <span>{pdfExportState === "exporting" ? "正在生成" : "导出 PDF"}</span>
          </button>
          <button
            className="solid action-button"
            type="submit"
            disabled={!canAnalyze || isAnalyzing}
          >
            <PackageCheck size={16} aria-hidden="true" />
            <span>分析</span>
          </button>
        </div>
      </div>

      {visibleMessage ? (
        <InlineNotice tone={hasVisibleError ? "error" : "success"}>
          {visibleMessage}
        </InlineNotice>
      ) : null}
      {pdfExportMessage ? <InlineNotice tone={pdfExportMessage.kind === "error" ? "error" : "success"}>{pdfExportMessage.text}</InlineNotice> : null}
      {!canOperate ? (
        <PermissionNotice>
          当前权限模板仅允许查看装箱方案；输入、分析、保存和柜型维护已禁用。
          {!canManage ? " 删除方案和自定义柜型同样需要管理权限。" : ""}
        </PermissionNotice>
      ) : null}
      <div
        className="container-packing-status-bar"
        aria-label="装柜分析状态"
        data-auto-refresh={autoRefreshEnabled ? "enabled" : "disabled"}
        data-auto-refresh-state={autoRefreshState}
      >
        <span>{packingStatusText}</span>
      </div>

      <div
        className="container-packing-project-panel"
        aria-label="装柜方案管理"
      >
        <label className="container-packing-project-name">
          <span>方案名称</span>
          <input
            value={projectName}
            disabled={!canOperate}
            onChange={(event) => updateProjectName(event.target.value)}
          />
        </label>
        <label className="container-packing-project-select">
          <span>已存方案</span>
          <select
            value={selectedProjectId}
            onChange={(event) => setSelectedProjectId(event.target.value)}
          >
            <option value="">选择方案</option>
            {savedProjects.map((project) => (
              <option key={project.id} value={project.id}>
                {project.name}{" "}
                {project.containerType ? `(${project.containerType})` : ""}
              </option>
            ))}
          </select>
        </label>
        <div className="container-packing-project-actions">
          <button
            className="command-button secondary"
            type="button"
            disabled={isRefreshingProjects}
            onClick={onRefreshProjects}
          >
            <RefreshCw size={16} aria-hidden="true" />
            <span>刷新方案</span>
          </button>
          <button
            className="command-button secondary"
            type="button"
            disabled={!canLoadProject || isLoadingProject}
            onClick={handleLoadProject}
          >
            <FolderOpen size={16} aria-hidden="true" />
            <span>加载方案</span>
          </button>
          <button
            className="command-button secondary"
            type="button"
            disabled={!canSaveProject || isSavingProject}
            onClick={handleSaveProject}
          >
            <Save size={16} aria-hidden="true" />
            <span>保存方案</span>
          </button>
          <button
            className="command-button secondary danger"
            type="button"
            disabled={
              !canManage ||
              (!canLoadProject && currentProjectId <= 0) ||
              isDeletingProject
            }
            onClick={handleDeleteProject}
          >
            <Trash2 size={16} aria-hidden="true" />
            <span>删除方案</span>
          </button>
        </div>
      </div>

      <fieldset className="permission-fieldset" disabled={!canOperate}>
      <div className="job-tool-grid container-packing-grid">
        <div className="job-tool-stack">
          <div className="container-packing-field-grid">
            <label>
              <span>柜型</span>
              <input
                list="container-packing-type-options"
                value={container.containerType}
                onChange={(event) => applyContainerType(event.target.value)}
              />
              <datalist id="container-packing-type-options">
                {containerTypes.map((type) => (
                  <option key={type.id} value={type.name} />
                ))}
              </datalist>
            </label>
            <label>
              <span>柜长 cm</span>
              <input
                inputMode="numeric"
                value={container.length}
                onChange={(event) =>
                  updateContainerField("length", event.target.value)
                }
              />
            </label>
            <label>
              <span>柜宽 cm</span>
              <input
                inputMode="numeric"
                value={container.width}
                onChange={(event) =>
                  updateContainerField("width", event.target.value)
                }
              />
            </label>
            <label>
              <span>柜高 cm</span>
              <input
                inputMode="numeric"
                value={container.height}
                onChange={(event) =>
                  updateContainerField("height", event.target.value)
                }
              />
            </label>
            <label>
              <span>体积 CBM</span>
              <input
                inputMode="decimal"
                value={container.volume}
                onChange={(event) =>
                  updateContainerField("volume", event.target.value)
                }
              />
            </label>
            <label>
              <span>限重 kg</span>
              <input
                inputMode="decimal"
                value={container.maxWeight}
                onChange={(event) =>
                  updateContainerField("maxWeight", event.target.value)
                }
              />
            </label>
          </div>
          <div className="container-packing-type-actions">
            <button
              className="command-button secondary"
              type="button"
              disabled={
                !canSaveContainerType || isSavingContainerType
              }
              onClick={handleSaveContainerType}
            >
              <Save size={16} aria-hidden="true" />
              <span>保存柜型</span>
            </button>
            <button
              className="command-button secondary danger"
              type="button"
              disabled={
                !canDeleteContainerType || isDeletingContainerType
              }
              onClick={handleDeleteContainerType}
            >
              <Trash2 size={16} aria-hidden="true" />
              <span>删除柜型</span>
            </button>
          </div>
        </div>

        <div className="job-tool-stack">
          <div className="container-packing-rules">
            <label className="toggle-field">
              <input
                type="checkbox"
                checked={rules.allowRotation}
                onChange={(event) =>
                  updateRulesField("allowRotation", event.target.checked)
                }
              />
              <span>允许旋转</span>
            </label>
            <label className="toggle-field">
              <input
                type="checkbox"
                checked={rules.usePalletConstraints}
                onChange={(event) =>
                  updateRulesField("usePalletConstraints", event.target.checked)
                }
              />
              <span>托盘约束</span>
            </label>
            <label className="toggle-field">
              <input
                type="checkbox"
                checked={rules.enforceCenterOfGravity}
                onChange={(event) =>
                  updateRulesField(
                    "enforceCenterOfGravity",
                    event.target.checked,
                  )
                }
              />
              <span>重心约束</span>
            </label>
            <label className="toggle-field">
              <input
                type="checkbox"
                checked={rules.requireSameFootprintStacking}
                onChange={(event) =>
                  updateRulesField(
                    "requireSameFootprintStacking",
                    event.target.checked,
                  )
                }
              />
              <span>同底堆叠</span>
            </label>
          </div>
          <div className="container-packing-field-grid container-packing-rules-grid">
            <label>
              <span>托盘长</span>
              <input
                inputMode="numeric"
                value={rules.defaultPalletLength}
                disabled={!rules.usePalletConstraints}
                onChange={(event) =>
                  updateRulesField("defaultPalletLength", event.target.value)
                }
              />
            </label>
            <label>
              <span>托盘宽</span>
              <input
                inputMode="numeric"
                value={rules.defaultPalletWidth}
                disabled={!rules.usePalletConstraints}
                onChange={(event) =>
                  updateRulesField("defaultPalletWidth", event.target.value)
                }
              />
            </label>
            <label>
              <span>托盘高</span>
              <input
                inputMode="numeric"
                value={rules.defaultPalletHeight}
                disabled={!rules.usePalletConstraints}
                onChange={(event) =>
                  updateRulesField("defaultPalletHeight", event.target.value)
                }
              />
            </label>
            <label>
              <span>托盘重</span>
              <input
                inputMode="decimal"
                value={rules.defaultPalletWeight}
                disabled={!rules.usePalletConstraints}
                onChange={(event) =>
                  updateRulesField("defaultPalletWeight", event.target.value)
                }
              />
            </label>
            <label>
              <span>重心偏差 %</span>
              <input
                inputMode="decimal"
                value={rules.centerOfGravityTolerancePercent}
                disabled={!rules.enforceCenterOfGravity}
                onChange={(event) =>
                  updateRulesField(
                    "centerOfGravityTolerancePercent",
                    event.target.value,
                  )
                }
              />
            </label>
            <label>
              <span>支撑 %</span>
              <input
                inputMode="decimal"
                value={rules.minimumSupportAreaPercent}
                onChange={(event) =>
                  updateRulesField(
                    "minimumSupportAreaPercent",
                    event.target.value,
                  )
                }
              />
            </label>
          </div>
        </div>
      </div>

      <ResponsiveTableFrame className="container-packing-cargo-frame" label="装柜货物清单">
        <table className="container-packing-cargo-table">
          <thead>
            <tr>
              <th>色</th>
              <th>名称</th>
              <th>长</th>
              <th>宽</th>
              <th>高</th>
              <th>重</th>
              <th>数量</th>
              <th>区域</th>
              <th>托盘</th>
              <th>每托</th>
              <th>顶载</th>
              <th>顺序</th>
              <th>组</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            {cargoRows.length === 0 ? (
              <tr>
                <td colSpan={14} className="empty-cell small-empty">
                  货物列表已清空
                </td>
              </tr>
            ) : (
              cargoRows.map((row) => (
                <tr key={row.id}>
                  <td>
                    <input
                      className="container-packing-color-input"
                      type="color"
                      aria-label={`${row.name || "货物"}颜色`}
                      value={row.colorHex}
                      onChange={(event) =>
                        updateCargoRow(row.id, { colorHex: event.target.value })
                      }
                    />
                  </td>
                  <td>
                    <input
                      className="item-cell-input"
                      value={row.name}
                      onChange={(event) =>
                        updateCargoRow(row.id, { name: event.target.value })
                      }
                    />
                  </td>
                  <td>
                    <input
                      className="item-cell-input item-number-input"
                      inputMode="decimal"
                      value={row.length}
                      onChange={(event) =>
                        updateCargoRow(row.id, { length: event.target.value })
                      }
                    />
                  </td>
                  <td>
                    <input
                      className="item-cell-input item-number-input"
                      inputMode="decimal"
                      value={row.width}
                      onChange={(event) =>
                        updateCargoRow(row.id, { width: event.target.value })
                      }
                    />
                  </td>
                  <td>
                    <input
                      className="item-cell-input item-number-input"
                      inputMode="decimal"
                      value={row.height}
                      onChange={(event) =>
                        updateCargoRow(row.id, { height: event.target.value })
                      }
                    />
                  </td>
                  <td>
                    <input
                      className="item-cell-input item-number-input"
                      inputMode="decimal"
                      value={row.weight}
                      onChange={(event) =>
                        updateCargoRow(row.id, { weight: event.target.value })
                      }
                    />
                  </td>
                  <td>
                    <input
                      className="item-cell-input item-number-input"
                      inputMode="numeric"
                      value={row.quantity}
                      onChange={(event) =>
                        updateCargoRow(row.id, { quantity: event.target.value })
                      }
                    />
                  </td>
                  <td>
                    <select
                      className="item-cell-input"
                      value={row.preferredZone}
                      onChange={(event) =>
                        updateCargoRow(row.id, {
                          preferredZone: event.target
                            .value as ContainerPackingZoneValue,
                        })
                      }
                    >
                      {containerPackingZoneOptions.map((option) => (
                        <option key={option.value} value={option.value}>
                          {option.label}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td className="container-packing-check-cell">
                    <input
                      type="checkbox"
                      aria-label={`${row.name || "货物"}使用托盘`}
                      checked={row.usePallet}
                      onChange={(event) =>
                        updateCargoRow(row.id, {
                          usePallet: event.target.checked,
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      className="item-cell-input item-number-input"
                      inputMode="numeric"
                      value={row.unitsPerPallet}
                      disabled={!row.usePallet}
                      onChange={(event) =>
                        updateCargoRow(row.id, {
                          unitsPerPallet: event.target.value,
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      className="item-cell-input item-number-input"
                      inputMode="decimal"
                      value={row.maxTopLoadWeight}
                      onChange={(event) =>
                        updateCargoRow(row.id, {
                          maxTopLoadWeight: event.target.value,
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      className="item-cell-input item-number-input"
                      inputMode="numeric"
                      value={row.loadSequence}
                      onChange={(event) =>
                        updateCargoRow(row.id, {
                          loadSequence: event.target.value,
                        })
                      }
                    />
                  </td>
                  <td>
                    <input
                      className="item-cell-input"
                      value={row.priorityGroup}
                      onChange={(event) =>
                        updateCargoRow(row.id, {
                          priorityGroup: event.target.value,
                        })
                      }
                    />
                  </td>
                  <td>
                    <button
                      className="icon-button compact-icon-button"
                      type="button"
                      title="删除货物" aria-label="删除货物"
                      onClick={() => removeCargoRow(row.id)}
                    >
                      <Trash2 size={15} aria-hidden="true" />
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </ResponsiveTableFrame>
      </fieldset>

      {!analysis ? <section className="container-packing-preview-placeholder" aria-label="装柜效果图状态">
        <PageState
          title="3D 与伪 3D 效果图等待分析"
          description="货物或柜型修改后，点击“立即刷新”或“分析”生成装载方案；生成后会同时显示可旋转 3D、俯视、侧视和柜门图。"
          action={<button className="command-button" type="button" disabled={!canAnalyze || isAnalyzing} onClick={runContainerPackingAnalysis}>{isAnalyzing ? "正在分析…" : "立即生成效果图"}</button>}
        />
      </section> : null}

      {analysis ? (
        <div className="container-packing-result" ref={pdfRootRef} data-container-packing-pdf>
          <div className="container-packing-pdf-cargo" aria-hidden="true">
            <h2>货物清单</h2>
            <table>
              <thead>
                <tr><th>货物</th><th>尺寸（cm）</th><th>单件重量</th><th>数量</th><th>装载区域</th><th>托盘</th></tr>
              </thead>
              <tbody>
                {cargoRows.map((row) => (
                  <tr key={`pdf-${row.id}`}>
                    <td>{row.name || "未命名货物"}</td>
                    <td>{row.length} × {row.width} × {row.height}</td>
                    <td>{row.weight || "-"}</td>
                    <td>{row.quantity || "-"}</td>
                    <td>{containerPackingZoneOptions.find((option) => option.value === row.preferredZone)?.label || "自动"}</td>
                    <td>{row.usePallet ? `是（每托 ${row.unitsPerPallet || "-"}）` : "否"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="detail-grid packing-summary-grid">
            <div className="detail-item">
              <span>装载件数</span>
              <strong>
                {formatPlainNumber(analysis.packedPackages)} /{" "}
                {formatPlainNumber(analysis.totalPackages)}
              </strong>
            </div>
            <div className="detail-item">
              <span>未装件数</span>
              <strong>{formatPlainNumber(analysis.unpackedPackages)}</strong>
            </div>
            <div className="detail-item">
              <span>估算柜数</span>
              <strong>
                {formatPlainNumber(analysis.estimatedContainerCount)}
              </strong>
            </div>
            <div className="detail-item">
              <span>体积利用</span>
              <strong>
                {formatPackingPercent(analysis.volumeUtilizationPercent)}
              </strong>
            </div>
            <div className="detail-item">
              <span>装载体积</span>
              <strong>
                {formatPlainNumber(analysis.packedVolume)} /{" "}
                {formatPlainNumber(analysis.totalVolume)}
              </strong>
            </div>
            <div className="detail-item">
              <span>重量利用</span>
              <strong>
                {formatPackingPercent(analysis.weightUtilizationPercent)}
              </strong>
            </div>
            <div className="detail-item">
              <span>装载重量</span>
              <strong>
                {formatPlainNumber(analysis.packedWeight)} /{" "}
                {formatPlainNumber(analysis.totalWeight)}
              </strong>
            </div>
            <div className="detail-item">
              <span>重心状态</span>
              <strong>
                {analysis.isCenterOfGravityWithinTolerance ? "正常" : "超限"}
              </strong>
            </div>
          </div>

          <div className="container-packing-pdf-instructions" aria-hidden="true">
            <h2>现场装柜提示</h2>
            <ol>
              <li>先确认实际柜型和内尺寸与作业单一致，再从柜头向柜门方向装载。</li>
              <li>按效果图颜色核对货物，结合俯视、侧视和柜门图确认每批货物位置。</li>
              <li>{analysis.unpackedPackages > 0 ? `当前仍有 ${formatPlainNumber(analysis.unpackedPackages)} 件未装入，请另行安排。` : "本方案货物已全部装入。"}</li>
              <li>{analysis.isCenterOfGravityWithinTolerance ? "装载重心在设定范围内。" : "装载重心超出设定范围，现场装柜前请复核配重和固定方式。"}</li>
            </ol>
          </div>

          {visualizationDimensions ? (
            <>
              <Suspense
                fallback={
                  <section
                    className="container-packing-3d-section"
                    aria-label="装柜三维可视化"
                  >
                    <div className="container-packing-3d-loading">
                      三维视图加载中
                    </div>
                  </section>
                }
              >
                <ContainerPackingScene3d
                  analysis={analysis}
                  dimensions={visualizationDimensions}
                  renderMode={renderMode}
                />
              </Suspense>
              <ContainerPackingVisualization
                analysis={analysis}
                dimensions={visualizationDimensions}
                renderMode={renderMode}
              />
            </>
          ) : null}
        </div>
      ) : null}
    </form>
  );
}
