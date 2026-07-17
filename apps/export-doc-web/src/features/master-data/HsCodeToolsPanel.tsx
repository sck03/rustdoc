import { useMutation } from "@tanstack/react-query";
import { Eye,FileSpreadsheet,Globe2,RefreshCw,Save,Search,Trash2,Upload,X } from "lucide-react";
import { ChangeEvent,FormEvent,Fragment,useEffect,useRef,useState } from "react";
import {
ApiHsCodeDto,
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
}: {
  client: ExportDocManagerApiClient;
  disabled: boolean;
  keyword: string;
  onLocalDataChanged: () => Promise<void>;
}) {
  const desktopAvailable = isDesktopBridgeAvailable();
  const uploadInputRef = useRef<HTMLInputElement | null>(null);
  const remoteDetailRequestRef = useRef(0);
  const [remoteKeyword, setRemoteKeyword] = useState(keyword);
  const [remoteResults, setRemoteResults] = useState<ApiHsCodeDto[]>([]);
  const [autoDetailProgress, setAutoDetailProgress] = useState<{ completed: number; total: number } | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<"success" | "error">("success");
  const [expandedResultKey, setExpandedResultKey] = useState<string | null>(null);
  const [clearAllConfirmOpen, setClearAllConfirmOpen] = useState(false);
  const [clearAllConfirmation, setClearAllConfirmation] = useState("");

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
      client.importHsCodesFromPath({
        body: { filePath },
      }),
    onSuccess: async (response) => {
      showSuccess(`${response.message} 当前本地库 ${response.totalCount} 条。`);
      await onLocalDataChanged();
    },
    onError: (error) => showError(readApiError(error)),
  });

  const uploadImportMutation = useMutation({
    mutationFn: (file: File) =>
      client.uploadHsCodesImportFile({
        fileName: file.name,
        body: file,
      }),
    onSuccess: async (response) => {
      showSuccess(`${response.message} 当前本地库 ${response.totalCount} 条。`);
      await onLocalDataChanged();
    },
    onError: (error) => showError(readApiError(error)),
  });

  const remoteSearchMutation = useMutation({
    mutationFn: (request: { keyword: string; requestId: number }) =>
      client.searchRemoteHsCodes({
        keyword: request.keyword,
      }),
    onSuccess: (response, request) => {
      if (!isCurrentRemoteDetailRequest(request.requestId)) {
        return;
      }

      const items = response.items ?? [];
      const pendingDetailCount = items.filter(shouldFetchRemoteHsCodeDetail).length;
      setRemoteResults(items);
      setExpandedResultKey(null);
      if (pendingDetailCount > 0) {
        setAutoDetailProgress({ completed: 0, total: pendingDetailCount });
        showSuccess(`联网查询完成：${items.length} 条，正在补全详情。`);
        void enrichRemoteDetails(items, request.requestId);
        return;
      }

      setAutoDetailProgress(null);
      showSuccess(items.length > 0 ? `联网查询完成：${items.length} 条。` : "联网查询完成，未找到记录。");
    },
    onError: (error, request) => {
      if (isCurrentRemoteDetailRequest(request.requestId)) {
        setAutoDetailProgress(null);
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
      if (response.updatedCount > 0) {
        await onLocalDataChanged();
      }
    },
    onError: (error) => showError(readApiError(error)),
  });

  const saveRemoteMutation = useMutation({
    mutationFn: (item: ApiHsCodeDto) => client.createHsCode({ body: normalizeHsCodeDtoForSave(item) }),
    onSuccess: async (response, item) => {
      setRemoteResults((current) => replaceHsCodeResult(current, item, response));
      showSuccess(`HS编码 ${response.code || item.code} 已保存到本地库。`);
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
      setAutoDetailProgress(null);
      setClearAllConfirmOpen(false);
      setClearAllConfirmation("");
      await onLocalDataChanged();
    },
    onError: (error) => showError(readApiError(error)),
  });

  const isAutoDetailLoading = autoDetailProgress !== null;
  const isBusy =
    disabled ||
    importPathMutation.isPending ||
    uploadImportMutation.isPending ||
    remoteSearchMutation.isPending ||
    isAutoDetailLoading ||
    fetchDetailMutation.isPending ||
    saveRemoteMutation.isPending ||
    clearAllMutation.isPending;
  const canClearAllLocalHsCodes = !isBusy && clearAllConfirmation.trim() === hsCodeClearAllConfirmationText;
  const remoteResultStatus =
    remoteResults.length > 0
      ? autoDetailProgress
        ? `${remoteResults.length} 条联网结果 · 正在补全 ${autoDetailProgress.completed}/${autoDetailProgress.total}`
        : `${remoteResults.length} 条联网结果`
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
    setAutoDetailProgress(null);
    setMessage(null);
    return remoteDetailRequestRef.current;
  }

  function isCurrentRemoteDetailRequest(requestId: number) {
    return remoteDetailRequestRef.current === requestId;
  }

  async function enrichRemoteDetails(items: ApiHsCodeDto[], requestId: number) {
    const pendingItems = items.filter(shouldFetchRemoteHsCodeDetail);
    if (pendingItems.length === 0) {
      if (isCurrentRemoteDetailRequest(requestId)) {
        setAutoDetailProgress(null);
      }

      return;
    }

    let processed = 0;
    let updatedCount = 0;
    let removedCount = 0;
    let failed = 0;

    for (const item of pendingItems) {
      if (!isCurrentRemoteDetailRequest(requestId)) {
        return;
      }

      try {
        const response = await client.resolveRemoteHsCodeDetail({ body: normalizeHsCodeDtoForRequest(item) });
        if (!isCurrentRemoteDetailRequest(requestId)) {
          return;
        }

        processed += 1;
        updatedCount += response.updatedCount;
        removedCount += response.removedCount;
        setRemoteResults((current) => applyHsCodeRemoteDetailResolution(current, item, response));
      } catch {
        if (!isCurrentRemoteDetailRequest(requestId)) {
          return;
        }

        processed += 1;
        failed += 1;
      }

      if (!isCurrentRemoteDetailRequest(requestId)) {
        return;
      }

      setAutoDetailProgress(processed < pendingItems.length ? { completed: processed, total: pendingItems.length } : null);
    }

    if (!isCurrentRemoteDetailRequest(requestId)) {
      return;
    }

    setAutoDetailProgress(null);
    if (updatedCount > 0) {
      await onLocalDataChanged();
    }

    if (updatedCount > 0 && removedCount > 0 && failed > 0) {
      showSuccess(`联网查询完成：已补全 ${updatedCount} 条，清理 ${removedCount} 条过期编码，${failed} 条可稍后重试。`);
    } else if (updatedCount > 0 && removedCount > 0) {
      showSuccess(`联网查询完成：已补全 ${updatedCount} 条，并清理 ${removedCount} 条过期编码。`);
    } else if (updatedCount > 0 && failed > 0) {
      showSuccess(`联网查询完成：已补全 ${updatedCount} 条详情，${failed} 条可稍后手动重试。`);
    } else if (updatedCount > 0) {
      showSuccess(`联网查询完成：已补全 ${updatedCount} 条详情。`);
    } else if (removedCount > 0) {
      showSuccess(`联网查询完成：已清理 ${removedCount} 条过期编码。`);
    } else if (failed > 0) {
      showSuccess(`联网查询完成：${items.length} 条，远程详情暂时未能补全，可点刷新重试。`);
    }
  }

  return (
    <section className="form-section hs-code-tools" aria-label="HS 编码导入与联网查询">
      <div className="section-header">
        <div>
          <h2>HS 编码工具</h2>
          <span>{remoteResultStatus}</span>
        </div>
      </div>

      {message ? <div className={messageType === "error" ? "alert" : "success-alert"}>{message}</div> : null}

      <div className="hs-code-tool-grid">
        <div className="hs-code-import-actions">
          {desktopAvailable ? (
            <button className="command-button secondary" type="button" disabled={isBusy} onClick={() => void importFromDesktopPath()}>
              <FileSpreadsheet size={17} aria-hidden="true" />
              <span>选择 Excel 导入</span>
            </button>
          ) : null}
          <input
            ref={uploadInputRef}
            type="file"
            accept=".xlsx,.xlsm"
            className="visually-hidden"
            onChange={handleUploadFile}
          />
          <button className="command-button secondary" type="button" disabled={isBusy} onClick={chooseUploadFile}>
            <Upload size={17} aria-hidden="true" />
            <span>上传 Excel 导入</span>
          </button>
          <button className="command-button secondary danger" type="button" disabled={isBusy} onClick={clearAllLocalHsCodes}>
            <Trash2 size={17} aria-hidden="true" />
            <span>清空本地库</span>
          </button>
        </div>

        <form className="hs-code-remote-form" onSubmit={handleRemoteSearch}>
          <div className="search-form hs-code-remote-search">
            <Search size={17} aria-hidden="true" />
            <input
              aria-label="联网查询 HS 编码"
              value={remoteKeyword}
              onChange={(event) => setRemoteKeyword(event.target.value)}
              placeholder="HS 编码、品名或关键词"
            />
          </div>
          <button className="command-button" type="submit" disabled={isBusy || !remoteKeyword.trim()}>
            <Globe2 size={17} aria-hidden="true" />
            <span>联网查询</span>
          </button>
        </form>
      </div>

      {clearAllConfirmOpen ? (
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

      {remoteResults.length > 0 ? (
        <div className="table-frame hs-code-remote-table-frame">
          <table className="hs-code-remote-table" aria-label="HS 编码联网结果">
            <thead>
              <tr>
                <th>编码</th>
                <th>名称</th>
                <th>单位</th>
                <th>退税率</th>
                <th>监管条件</th>
                <th className="row-actions-cell">操作</th>
              </tr>
            </thead>
            <tbody>
              {remoteResults.map((item, index) => {
                const resultKey = buildHsCodeResultKey(item, index);
                const isExpanded = expandedResultKey === resultKey;
                return (
                  <Fragment key={resultKey}>
                    <tr>
                      <td className="strong-cell">{item.code || "-"}</td>
                      <td>{item.name || "-"}</td>
                      <td>{item.unit || "-"}</td>
                      <td>{item.rebateRate || "-"}</td>
                      <td>{item.supervisionConditions || "-"}</td>
                      <td className="row-actions-cell">
                        <button
                          className="icon-button compact-icon-button"
                          type="button"
                          title={isExpanded ? "收起详情" : "查看详情"}
                          aria-label={`${isExpanded ? "收起" : "查看"} HS 编码 ${item.code || index + 1} 详情`}
                          onClick={() => setExpandedResultKey(isExpanded ? null : resultKey)}
                        >
                          <Eye size={15} aria-hidden="true" />
                        </button>
                        <button
                          className="icon-button compact-icon-button"
                          type="button"
                          title="补全详情"
                          aria-label={`补全 HS 编码 ${item.code || index + 1} 详情`}
                          disabled={isBusy || !item.detailUrl}
                          onClick={() => fetchDetailMutation.mutate(item)}
                        >
                          <RefreshCw size={15} aria-hidden="true" />
                        </button>
                        <button
                          className="icon-button compact-icon-button"
                          type="button"
                          title="保存到本地库"
                          aria-label={`保存 HS 编码 ${item.code || index + 1} 到本地库`}
                          disabled={isBusy || !item.code}
                          onClick={() => saveRemoteMutation.mutate(item)}
                        >
                          <Save size={15} aria-hidden="true" />
                        </button>
                      </td>
                    </tr>
                    {isExpanded ? (
                      <tr className="hs-code-detail-row">
                        <td colSpan={6}>
                          <HsCodeRemoteDetail item={item} />
                        </td>
                      </tr>
                    ) : null}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}

function HsCodeRemoteDetail({ item }: { item: ApiHsCodeDto }) {
  return (
    <div className="hs-code-detail-panel">
      <div className="hs-code-detail-grid">
        <DetailField label="HS编码" value={item.code || item.normalizedCode} />
        <DetailField label="商品名称" value={item.name} />
        <DetailField label="法定单位" value={item.unit} />
        <DetailField label="退税率" value={item.rebateRate} />
        <DetailField label="监管条件" value={item.supervisionConditions} />
        <DetailField label="检验检疫" value={item.inspectionCategory} />
        <DetailField label="申报要素" value={item.elements} wide />
        <DetailField label="描述" value={item.description} wide />
      </div>
    </div>
  );
}

function DetailField({ label, value, wide = false }: { label: string; value?: string; wide?: boolean }) {
  return (
    <div className={wide ? "hs-code-detail-field hs-code-detail-wide" : "hs-code-detail-field"}>
      <span>{label}</span>
      <p>{value?.trim() || "-"}</p>
    </div>
  );
}

