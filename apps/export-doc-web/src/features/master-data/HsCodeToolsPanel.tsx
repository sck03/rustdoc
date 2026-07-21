import { useMutation } from "@tanstack/react-query";
import { Eye,ExternalLink,FileSpreadsheet,Globe2,RefreshCw,Save,Search,Trash2,Upload,X } from "lucide-react";
import { ChangeEvent,FormEvent,Fragment,useEffect,useRef,useState } from "react";
import {
ApiHsCodeDto,
ApiHsCodeImportPreviewResponse,
ExportDocManagerApiClient
} from "../../api/index.ts";
import { isDesktopBridgeAvailable,selectExcelFile } from "../../desktop/desktopBridge.ts";
import {
readApiError
} from "../../ui/formUtils.ts";


import {
applyHsCodeRemoteDetailResolution,
buildHsCodeResultKey,
normalizeHsCodeDtoForRequest,
normalizeHsCodeDtoForSave,
replaceHsCodeResult,
shouldFetchRemoteHsCodeDetail
} from "./masterDataModel.ts";
import { hsCodeClearAllConfirmationText } from "./masterDataTypes.ts";


export function HsCodeToolsPanel({
  client,
  disabled,
  keyword,
  onLocalDataChanged,
  onRemoteCandidatesChanged,
  mode = "hub",
}: {
  client: ExportDocManagerApiClient;
  disabled: boolean;
  keyword: string;
  onLocalDataChanged: () => Promise<void>;
  onRemoteCandidatesChanged?: () => Promise<void>;
  mode?: "hub" | "import" | "remote";
}) {
  const desktopAvailable = isDesktopBridgeAvailable();
  const uploadInputRef = useRef<HTMLInputElement | null>(null);
  const remoteDetailRequestRef = useRef(0);
  const [remoteKeyword, setRemoteKeyword] = useState(keyword);
  const [remoteResults, setRemoteResults] = useState<ApiHsCodeDto[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<"success" | "error">("success");
  const [expandedResultKey, setExpandedResultKey] = useState<string | null>(null);
  const [clearAllConfirmOpen, setClearAllConfirmOpen] = useState(false);
  const [clearAllConfirmation, setClearAllConfirmation] = useState("");
  const [workspace, setWorkspace] = useState<"none" | "import" | "remote">(mode === "hub" ? "none" : mode);
  const [importMode, setImportMode] = useState<"Incremental" | "CompleteSnapshot">("Incremental");
  const [importSourceName, setImportSourceName] = useState("");
  const [importEffectiveYear, setImportEffectiveYear] = useState(String(new Date().getFullYear()));
  const [importPreview, setImportPreview] = useState<ApiHsCodeImportPreviewResponse | null>(null);
  const [remoteHealth, setRemoteHealth] = useState<string | null>(null);

  useEffect(() => {
    if (!remoteKeyword.trim() && keyword.trim()) {
      setRemoteKeyword(keyword.trim());
    }
  }, [keyword, remoteKeyword]);

  useEffect(() => {
    return () => {
      remoteDetailRequestRef.current += 1;
    };
  }, []);

  const importPathMutation = useMutation({
    mutationFn: (filePath: string) =>
      client.previewHsCodesImportFromPath({
        body: {
          filePath,
          mode: importMode,
          sourceName: importSourceName.trim() || undefined,
          effectiveYear: parseImportYear(importEffectiveYear),
        },
      }),
    onSuccess: (response) => {
      setImportPreview(response);
      showSuccess("文件分析完成，请核对识别结果和数据变更后再确认导入。");
    },
    onError: (error) => showError(readApiError(error)),
  });

  const uploadImportMutation = useMutation({
    mutationFn: (file: File) =>
      client.previewHsCodesImportUpload({
        fileName: file.name,
        mode: importMode,
        sourceName: importSourceName.trim() || undefined,
        effectiveYear: parseImportYear(importEffectiveYear),
        body: file,
      }),
    onSuccess: (response) => {
      setImportPreview(response);
      showSuccess("文件分析完成，请核对识别结果和数据变更后再确认导入。");
    },
    onError: (error) => showError(readApiError(error)),
  });

  const commitImportMutation = useMutation({
    mutationFn: (token: string) => client.commitHsCodesImport({ body: { token } }),
    onSuccess: async (response) => {
      showSuccess(response.message);
      setImportPreview(null);
      setWorkspace(mode === "hub" ? "none" : mode);
      await onLocalDataChanged();
    },
    onError: (error) => showError(readApiError(error)),
  });

  const remoteHealthMutation = useMutation({
    mutationFn: () => client.getHsCodeRemoteHealth(),
    onSuccess: (response) => setRemoteHealth(response.message),
    onError: (error) => setRemoteHealth(readApiError(error)),
  });

  const remoteSearchMutation = useMutation({
    mutationFn: (request: { keyword: string; requestId: number }) =>
      client.searchRemoteHsCodes({
        keyword: request.keyword,
      }),
    onSuccess: async (response, request) => {
      if (!isCurrentRemoteDetailRequest(request.requestId)) {
        return;
      }

      const items = response.items ?? [];
      const pendingDetailCount = items.filter(shouldFetchRemoteHsCodeDetail).length;
      setRemoteResults(items);
      setExpandedResultKey(null);
      await onRemoteCandidatesChanged?.();
      if (!isCurrentRemoteDetailRequest(request.requestId)) {
        return;
      }

      if (pendingDetailCount > 0) {
        showSuccess(`联网查询完成：找到 ${response.standardCodeCount ?? items.length} 条当前编码，${response.declarationExampleCount ?? 0} 条申报实例已进入候选池；其中 ${pendingDetailCount} 条编码可按需补全详情。`);
        return;
      }

      showSuccess(
        items.length > 0
          ? `联网查询完成：找到 ${response.standardCodeCount ?? items.length} 条当前编码，${response.declarationExampleCount ?? 0} 条申报实例已进入候选池。`
          : (response.declarationExampleCount ?? 0) > 0
            ? `已保存 ${response.declarationExampleCount} 条申报实例到候选池，但暂未解析到当前标准编码。`
            : "联网查询完成，未找到当前编码或申报实例。",
      );
    },
    onError: (error, request) => {
      if (isCurrentRemoteDetailRequest(request.requestId)) {
        showError(readApiError(error));
      }
    },
  });

  const fetchDetailMutation = useMutation({
    mutationFn: (item: ApiHsCodeDto) => client.resolveRemoteHsCodeDetail({ body: normalizeHsCodeDtoForRequest(item) }),
    onSuccess: async (response, item) => {
      setRemoteResults((current) => applyHsCodeRemoteDetailResolution(current, item, response));
      setExpandedResultKey(null);
      showSuccess(response.message || `已补全 ${item.code} 的远程详情。`);
      await onRemoteCandidatesChanged?.();
    },
    onError: (error) => showError(readApiError(error)),
  });

  const saveRemoteMutation = useMutation({
    mutationFn: (item: ApiHsCodeDto) => client.createHsCode({ body: normalizeHsCodeDtoForSave(item) }),
    onSuccess: async (response, item) => {
      setRemoteResults((current) => replaceHsCodeResult(current, item, response));
      showSuccess(`HS编码 ${response.code || item.code} 已保存为本地参考资料，导入年度税则后才会标记为当前有效。`);
      await onLocalDataChanged();
    },
    onError: (error) => showError(readApiError(error)),
  });

  const clearAllMutation = useMutation({
    mutationFn: (confirmation: string) =>
      client.clearAllHsCodes({
        body: { confirmation },
      }),
    onSuccess: async (response) => {
      showSuccess(response.message || "本地HS编码库已清空。");
      remoteDetailRequestRef.current += 1;
      setRemoteResults([]);
      setClearAllConfirmOpen(false);
      setClearAllConfirmation("");
      await onLocalDataChanged();
    },
    onError: (error) => showError(readApiError(error)),
  });

  const isBusy =
    disabled ||
    importPathMutation.isPending ||
    uploadImportMutation.isPending ||
    commitImportMutation.isPending ||
    remoteSearchMutation.isPending ||
    fetchDetailMutation.isPending ||
    saveRemoteMutation.isPending ||
    clearAllMutation.isPending;
  const canClearAllLocalHsCodes = !isBusy && clearAllConfirmation.trim() === hsCodeClearAllConfirmationText;
  const remoteResultStatus = remoteResults.length > 0
    ? `${remoteResults.length} 条联网结果`
    : "本地库维护";

  async function importFromDesktopPath() {
    if (isBusy) {
      return;
    }

    try {
      const selected = await selectExcelFile();
      if (selected) {
        importPathMutation.mutate(selected);
      }
    } catch (error) {
      showError(readApiError(error));
    }
  }

  function openImportWorkspace() {
    setWorkspace("import");
    setImportPreview(null);
    setMessage(null);
  }

  function openRemoteWorkspace() {
    setWorkspace("remote");
    setMessage(null);
    if (!remoteHealthMutation.isPending) {
      remoteHealthMutation.mutate();
    }
  }

  function closeWorkspace() {
    if (isBusy) return;
    setWorkspace("none");
    setImportPreview(null);
    setMessage(null);
  }

  function chooseUploadFile() {
    if (!isBusy) {
      uploadInputRef.current?.click();
    }
  }

  function handleUploadFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0] ?? null;
    event.target.value = "";
    if (!file || isBusy) {
      return;
    }

    uploadImportMutation.mutate(file);
  }

  function commitImport() {
    if (importPreview && !isBusy) {
      commitImportMutation.mutate(importPreview.token);
    }
  }

  function handleRemoteSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const normalizedKeyword = remoteKeyword.trim();
    if (!normalizedKeyword || isBusy) {
      return;
    }

    const requestId = beginRemoteDetailRequest();
    remoteSearchMutation.mutate({ keyword: normalizedKeyword, requestId });
  }

  function clearAllLocalHsCodes() {
    if (isBusy) {
      return;
    }

    setClearAllConfirmOpen(true);
    setClearAllConfirmation("");
    setMessage(null);
  }

  function cancelClearAllLocalHsCodes() {
    if (clearAllMutation.isPending) {
      return;
    }

    setClearAllConfirmOpen(false);
    setClearAllConfirmation("");
  }

  function submitClearAllLocalHsCodes(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (isBusy) {
      return;
    }

    if (clearAllConfirmation.trim() !== hsCodeClearAllConfirmationText) {
      showError("请输入 CLEAR 确认清空本地HS编码库。");
      return;
    }

    clearAllMutation.mutate(clearAllConfirmation.trim());
  }

  function showSuccess(value: string) {
    setMessage(value);
    setMessageType("success");
  }

  function showError(value: string) {
    setMessage(value);
    setMessageType("error");
  }

  function beginRemoteDetailRequest() {
    remoteDetailRequestRef.current += 1;
    setRemoteResults([]);
    setExpandedResultKey(null);
    setMessage(null);
    return remoteDetailRequestRef.current;
  }

  function isCurrentRemoteDetailRequest(requestId: number) {
    return remoteDetailRequestRef.current === requestId;
  }

  return (
    <section className="form-section hs-code-tools" aria-label="HS 编码导入与联网查询">
      <div className="section-header">
        <div>
          <h2>{mode === "import" ? "年度税则智能导入" : mode === "remote" ? "联网补充" : "HS 编码工具"}</h2>
          <span>{mode === "import" ? "识别工作表和字段，预检差异后才写入本地库" : mode === "remote" ? "查询第三方申报实例并沉淀到本地知识库" : remoteResultStatus}</span>
        </div>
      </div>

      {message ? <div className={messageType === "error" ? "alert" : "success-alert"}>{message}</div> : null}

      {mode === "hub" ? <div className="hs-code-action-hub">
        <button className="hs-code-action-card" type="button" disabled={isBusy} onClick={openImportWorkspace}>
          <FileSpreadsheet size={22} aria-hidden="true" />
          <strong>智能导入</strong>
          <span>识别不同格式 Excel，预览新增、更新和疑似作废后再提交</span>
        </button>
        <button className="hs-code-action-card" type="button" disabled={isBusy} onClick={openRemoteWorkspace}>
          <Globe2 size={22} aria-hidden="true" />
          <strong>联网查询</strong>
          <span>独立查询第三方数据，确认后才保存到本地库</span>
        </button>
        <button className="hs-code-action-card danger-card" type="button" disabled={isBusy} onClick={clearAllLocalHsCodes}>
          <Trash2 size={22} aria-hidden="true" />
          <strong>本地库维护</strong>
          <span>普通修改请打开下方记录；清空整库需要管理员再次确认</span>
        </button>
      </div> : null}

      <input ref={uploadInputRef} type="file" accept=".xlsx,.xlsm" className="visually-hidden" onChange={handleUploadFile} />

      {workspace !== "none" ? (
        <div className={mode === "hub" ? "hs-code-workspace-backdrop" : "hs-code-inline-workspace"} role="presentation">
          <section className="hs-code-workspace" role={mode === "hub" ? "dialog" : "region"} aria-modal={mode === "hub" ? true : undefined} aria-label={workspace === "import" ? "HS编码智能导入" : "HS编码联网查询"}>
            <header>
              <div>
                <strong>{workspace === "import" ? "智能导入向导" : "联网查询"}</strong>
                <span>{workspace === "import" ? "先分析文件，确认差异后才写入本地库" : "联网结果不会自动覆盖本地资料"}</span>
              </div>
              {mode === "hub" ? <button className="icon-button" type="button" title="关闭" disabled={isBusy} onClick={closeWorkspace}><X size={18} /></button> : null}
            </header>

            {workspace === "import" ? (
              <div className="hs-code-import-wizard">
                {!importPreview ? (
                  <>
                    <div className="hs-code-import-settings">
                      <label><span>数据来源</span><input value={importSourceName} onChange={(event) => setImportSourceName(event.target.value)} placeholder="例如：2026年度税则资料" /></label>
                      <label><span>适用年份</span><input type="number" min="2000" max="2100" value={importEffectiveYear} onChange={(event) => setImportEffectiveYear(event.target.value)} /></label>
                      <label><span>导入方式</span><select value={importMode} onChange={(event) => setImportMode(event.target.value as "Incremental" | "CompleteSnapshot")}><option value="Incremental">增量资料（安全，不判断作废）</option><option value="CompleteSnapshot">完整年度库（缺失编码标记疑似作废）</option></select></label>
                    </div>
                    {importMode === "CompleteSnapshot" ? <div className="warning-note">只有确认文件是完整中国 HS 年度库时才使用此模式。系统不会删除历史记录或改写商业发票。</div> : null}
                    <div className="hs-code-import-source-actions">
                      {desktopAvailable
                        ? <button className="command-button" type="button" disabled={isBusy} onClick={() => void importFromDesktopPath()}><FileSpreadsheet size={17} /><span>选择 Excel 文件</span></button>
                        : <button className="command-button" type="button" disabled={isBusy} onClick={chooseUploadFile}><Upload size={17} /><span>选择 Excel 文件</span></button>}
                    </div>
                  </>
                ) : <HsCodeImportPreview preview={importPreview} busy={isBusy} onBack={() => setImportPreview(null)} onCommit={commitImport} />}
              </div>
            ) : (
              <div className="hs-code-remote-workspace">
                {remoteHealth ? <div className="hs-code-source-health">{remoteHealth}</div> : null}
                <form className="hs-code-remote-form" onSubmit={handleRemoteSearch}>
                  <div className="search-form hs-code-remote-search"><Search size={17} /><input aria-label="联网查询 HS 编码" value={remoteKeyword} onChange={(event) => setRemoteKeyword(event.target.value)} placeholder="HS 编码、品名或关键词" /></div>
                  <button className="command-button" type="submit" disabled={isBusy || !remoteKeyword.trim()}><Globe2 size={17} /><span>查询</span></button>
                </form>
                {remoteResults.length > 0 ? <HsCodeRemoteResults items={remoteResults} expandedResultKey={expandedResultKey} setExpandedResultKey={setExpandedResultKey} isBusy={isBusy} fetchDetail={(item) => fetchDetailMutation.mutate(item)} saveItem={(item) => saveRemoteMutation.mutate(item)} /> : null}
              </div>
            )}
          </section>
        </div>
      ) : null}

      {mode === "hub" && clearAllConfirmOpen ? (
        <form className="hs-code-clear-confirmation" aria-label="HS编码清空确认" onSubmit={submitClearAllLocalHsCodes}>
          <div>
            <strong>清空本地HS编码库</strong>
            <span>将删除当前运行数据根数据库中的全部 HS 编码记录。</span>
          </div>
          <label>
            <span>确认文本</span>
            <input
              value={clearAllConfirmation}
              placeholder={hsCodeClearAllConfirmationText}
              disabled={isBusy}
              onChange={(event) => setClearAllConfirmation(event.target.value)}
            />
          </label>
          <div className="toolbar-actions">
            <button className="command-button secondary" type="button" disabled={isBusy} onClick={cancelClearAllLocalHsCodes}>
              <X size={17} aria-hidden="true" />
              <span>取消</span>
            </button>
            <button className="command-button secondary danger" type="submit" disabled={!canClearAllLocalHsCodes}>
              <Trash2 size={17} aria-hidden="true" />
              <span>{clearAllMutation.isPending ? "清空中" : "确认清空"}</span>
            </button>
          </div>
        </form>
      ) : null}

    </section>
  );
}

function parseImportYear(value: string) {
  const year = Number.parseInt(value, 10);
  return Number.isInteger(year) && year >= 2000 && year <= 2100 ? year : undefined;
}

function HsCodeImportPreview({
  preview,
  busy,
  onBack,
  onCommit,
}: {
  preview: ApiHsCodeImportPreviewResponse;
  busy: boolean;
  onBack: () => void;
  onCommit: () => void;
}) {
  const labels: Record<string, string> = {
    Add: "新增",
    Update: "更新",
    Unchanged: "不变",
    SuspectedObsolete: "疑似作废",
    Conflict: "冲突",
    Invalid: "无效",
  };
  return (
    <div className="hs-code-preview">
      <div className="hs-code-preview-summary">
        <div><span>识别可信度</span><strong>{preview.confidence}%</strong></div>
        <div><span>新增</span><strong>{preview.addCount}</strong></div>
        <div><span>更新</span><strong>{preview.updateCount}</strong></div>
        <div><span>疑似作废</span><strong>{preview.suspectedObsoleteCount}</strong></div>
        <div><span>需处理</span><strong>{preview.conflictCount + preview.invalidCount}</strong></div>
      </div>
      <div className="hs-code-preview-meta">
        <span>工作表：{preview.worksheetName}</span>
        <span>标题行：第 {preview.headerRowNumber} 行</span>
        <span>来源：{preview.sourceName || "未填写"}</span>
        <span>年份：{preview.effectiveYear ?? "未填写"}</span>
      </div>
      {preview.warnings.map((warning) => <div className="warning-note" key={warning}>{warning}</div>)}
      <details className="hs-code-column-mapping"><summary>查看识别到的字段映射</summary><div>{preview.columns.map((column) => <span key={column.field}>{column.header} → {hsCodeImportFieldLabel(column.field)}（{column.confidence}%）</span>)}</div></details>
      <div className="table-frame hs-code-preview-table-frame">
        <table><thead><tr><th>处理</th><th>Excel 行</th><th>编码</th><th>名称</th><th>说明</th></tr></thead><tbody>
          {preview.items.map((row, index) => <tr key={`${row.changeType}-${row.item.code}-${row.rowNumber}-${index}`}><td><span className={`hs-code-change-badge change-${row.changeType.toLowerCase()}`}>{labels[row.changeType] ?? row.changeType}</span></td><td>{row.rowNumber || "-"}</td><td>{row.item.code || "-"}</td><td>{row.item.name || "-"}</td><td>{row.message}{row.replacementCandidates.length ? ` 候选：${row.replacementCandidates.join("、")}` : ""}</td></tr>)}
        </tbody></table>
      </div>
      <div className="hs-code-preview-actions"><button className="command-button secondary" type="button" disabled={busy} onClick={onBack}>重新选择</button><button className="command-button" type="button" disabled={busy || preview.conflictCount + preview.invalidCount > 0 && preview.addCount + preview.updateCount + preview.suspectedObsoleteCount === 0} onClick={onCommit}>{busy ? "正在导入" : "确认导入"}</button></div>
    </div>
  );
}

function hsCodeImportFieldLabel(field: string) {
  const labels: Record<string, string> = {
    Code: "商品编码",
    Name: "商品名称",
    Elements: "申报要素",
    Unit1: "法定第一单位",
    Unit2: "法定第二单位",
    SupervisionConditions: "海关监管条件",
    InspectionCategory: "检验检疫类别",
    RebateRate: "出口退税率",
    NormalTariffRate: "普通税率",
    PreferentialTariffRate: "优惠/最惠国税率",
    ExportTariffRate: "出口税率",
    ConsumptionTaxRate: "消费税率",
    ValueAddedTaxRate: "增值税率",
    Description: "英文描述",
    Notes: "备注",
  };
  return labels[field] ?? field;
}

function HsCodeRemoteResults({ items, expandedResultKey, setExpandedResultKey, isBusy, fetchDetail, saveItem }: {
  items: ApiHsCodeDto[];
  expandedResultKey: string | null;
  setExpandedResultKey: (value: string | null) => void;
  isBusy: boolean;
  fetchDetail: (item: ApiHsCodeDto) => void;
  saveItem: (item: ApiHsCodeDto) => void;
}) {
  return <div className="table-frame hs-code-remote-table-frame"><table className="hs-code-remote-table" aria-label="HS 编码联网结果"><thead><tr><th>编码</th><th>名称</th><th>来源证据</th><th>单位</th><th>退税率</th><th>监管条件</th><th className="row-actions-cell">操作</th></tr></thead><tbody>
    {items.map((item, index) => {
      const key = buildHsCodeResultKey(item, index);
      const expanded = expandedResultKey === key;
      return <Fragment key={key}><tr><td className="strong-cell">{item.code || "-"}</td><td>{item.name || "-"}</td><td><span className="remote-evidence-kind">{remoteRecordKindLabel(item.remoteRecordKind)}</span><small className="remote-evidence-count">{remoteEvidenceCount(item)}</small></td><td>{item.unit || "-"}</td><td>{item.rebateRate || "-"}</td><td>{item.supervisionConditions || "-"}</td><td className="row-actions-cell"><button className="icon-button compact-icon-button" type="button" title="查看详情" onClick={() => setExpandedResultKey(expanded ? null : key)}><Eye size={15} /></button><button className="icon-button compact-icon-button" type="button" title="补全详情" disabled={isBusy || !item.detailUrl} onClick={() => fetchDetail(item)}><RefreshCw size={15} /></button><button className="icon-button compact-icon-button" type="button" title="保存为本地参考资料" disabled={isBusy || !item.code} onClick={() => saveItem(item)}><Save size={15} /></button></td></tr>{expanded ? <tr className="hs-code-detail-row"><td colSpan={7}><HsCodeRemoteDetail item={item} /></td></tr> : null}</Fragment>;
    })}
  </tbody></table></div>;
}

function HsCodeRemoteDetail({ item }: { item: ApiHsCodeDto }) {
  return (
    <div className="hs-code-detail-panel">
      <div className="hs-code-detail-grid">
        <DetailField label="HS编码" value={item.code || item.normalizedCode} />
        <DetailField label="商品名称" value={item.name} />
        <DetailField label="法定单位" value={item.unit} />
        <DetailField label="退税率" value={item.rebateRate} />
        <DetailField label="普通税率" value={item.normalTariffRate} />
        <DetailField label="优惠/最惠国税率" value={item.preferentialTariffRate} />
        <DetailField label="出口税率" value={item.exportTariffRate} />
        <DetailField label="消费税率" value={item.consumptionTaxRate} />
        <DetailField label="增值税率" value={item.valueAddedTaxRate} />
        <DetailField label="监管条件" value={item.supervisionConditions} />
        <DetailField label="检验检疫" value={item.inspectionCategory} />
        <DetailField label="申报要素" value={item.elements} wide />
        <DetailField label="描述" value={item.description} wide />
        <DetailField label="备注" value={item.notes} wide />
      </div>
      <div className="hs-code-remote-evidence">
        <div><span>证据类型</span><strong>{remoteRecordKindLabel(item.remoteRecordKind)}</strong></div>
        <div><span>实例数量</span><strong>{remoteEvidenceCount(item)}</strong></div>
        <div><span>观察时间</span><strong>{formatObservedAt(item.observedAt)}</strong></div>
        {item.personalPostalTaxCode ? <div><span>个人行邮税号</span><strong>{item.personalPostalTaxCode}</strong></div> : null}
        <div className="hs-code-evidence-links">
          {item.evidenceUrl ? <a href={item.evidenceUrl} target="_blank" rel="noreferrer">来源详情<ExternalLink size={14} /></a> : null}
          {item.summaryUrl ? <a href={item.summaryUrl} target="_blank" rel="noreferrer">实例汇总<ExternalLink size={14} /></a> : null}
        </div>
      </div>
      {item.ciqEntries?.length ? <ReferenceList label="CIQ分类" entries={item.ciqEntries.map(entry => `${entry.code} ${entry.name}`)} /> : null}
      {item.classificationEntries?.length ? <ReferenceList label="所属分类" entries={item.classificationEntries.map(entry => `${entry.code} ${entry.name}`)} /> : null}
    </div>
  );
}

function ReferenceList({ label, entries }: { label: string; entries: string[] }) {
  return <div className="hs-code-reference-list"><span>{label}</span><div>{entries.map(entry => <small key={entry}>{entry}</small>)}</div></div>;
}

function remoteRecordKindLabel(kind?: string) {
  return kind === "DeclarationExample" ? "申报实例" : "标准税则";
}

function remoteEvidenceCount(item: ApiHsCodeDto) {
  if (item.remoteRecordKind === "DeclarationExample") return "申报实例 1 条";
  if (item.instanceCount != null) return `实例汇总 ${item.instanceCount.toLocaleString()} 条`;
  if (item.declarationExampleCount != null && item.declarationExampleCount > 0) return `已提取 ${item.declarationExampleCount} 条`;
  return "尚未提取实例";
}

function formatObservedAt(value?: string) {
  if (!value) return "-";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("zh-CN", { hour12: false });
}

function DetailField({ label, value, wide = false }: { label: string; value?: string; wide?: boolean }) {
  return (
    <div className={wide ? "hs-code-detail-field hs-code-detail-wide" : "hs-code-detail-field"}>
      <span>{label}</span>
      <p>{value?.trim() || "-"}</p>
    </div>
  );
}
