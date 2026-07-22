import { useEffect, useMemo, useState } from "react";
import { CheckSquare, RefreshCw } from "lucide-react";
import {
  SingleWindowEditorNavigationTarget,
  SingleWindowExportIssueGroup,
  SingleWindowExportReview,
} from "../../api/index.ts";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { PageState } from "../../ui/PageState.tsx";

export function SingleWindowExportReviewPanel({
  review,
  isBusy,
  isActionDisabled = false,
  repairBusy = false,
  onRepairGroups,
}: {
  review: SingleWindowExportReview | null;
  isBusy: boolean;
  isActionDisabled?: boolean;
  repairBusy?: boolean;
  onRepairGroups?: (groupKeys: string[]) => void;
}) {
  const repairableGroupKeys = useMemo(
    () => (review ? review.groups.filter((group) => group.canAutoRepair).map((group) => group.groupKey) : []),
    [review],
  );
  const [selectedGroupKey, setSelectedGroupKey] = useState<string>("");
  const [selectedRepairKeys, setSelectedRepairKeys] = useState<Set<string>>(() => new Set());

  useEffect(() => {
    setSelectedGroupKey(review?.groups[0]?.groupKey ?? "");
    setSelectedRepairKeys(new Set(repairableGroupKeys));
  }, [repairableGroupKeys, review]);

  if (!review && isBusy) {
    return <PageState tone="loading" title="正在加载预检结果" description="系统正在整理字段分组和需要处理的问题。" />;
  }

  if (!review) {
    return <div className="info-alert">暂无预检结果。</div>;
  }

  const selectedGroup = review.groups.find((group) => group.groupKey === selectedGroupKey) ?? review.groups[0] ?? null;
  const selectedRepairGroupKeys = repairableGroupKeys.filter((key) => selectedRepairKeys.has(key));
  const allRepairableSelected =
    repairableGroupKeys.length > 0 && repairableGroupKeys.every((key) => selectedRepairKeys.has(key));
  const canRepair = Boolean(onRepairGroups) && selectedRepairGroupKeys.length > 0 && !isActionDisabled && !repairBusy;

  function toggleRepairGroup(group: SingleWindowExportIssueGroup, checked: boolean) {
    if (!group.canAutoRepair) {
      return;
    }

    setSelectedRepairKeys((current) => {
      const next = new Set(current);
      if (checked) {
        next.add(group.groupKey);
      } else {
        next.delete(group.groupKey);
      }
      return next;
    });
  }

  function toggleAllRepairableGroups() {
    setSelectedRepairKeys(allRepairableSelected ? new Set() : new Set(repairableGroupKeys));
  }

  return (
    <div className="single-window-export-review-panel">
      <div className="detail-grid single-window-document-summary-grid">
        <SummaryItem label="发票号" value={review.invoiceNo} />
        <SummaryItem label="合同号" value={review.contractNo} />
        <SummaryItem label="草稿版本" value={review.draftRevision} />
        <SummaryItem label="人工锁定字段" value={review.manualLockedFieldCount} />
        <SummaryItem label="来源差异" value={review.sourceDiffCount} />
        <SummaryItem label="预检分组" value={review.groups.length} />
        <SummaryItem label="错误" value={review.totalErrorCount} />
        <SummaryItem label="警告" value={review.totalWarningCount} />
      </div>

      <div className={review.sourceDiffCount > 0 ? "single-window-review-source-diff has-diff" : "single-window-review-source-diff"}>
        <span>
          源资料变化 {review.sourceDiffCount} 项
          {review.totalErrorCount > 0 ? "，请先处理错误后再导出。" : "，确认无误后可继续导出。"}
        </span>
        {review.sourceDiffSummary ? (
          <details>
            <summary>查看源资料变化</summary>
            <pre>{review.sourceDiffSummary}</pre>
          </details>
        ) : null}
      </div>

      <div className="section-header single-window-review-subheader">
        <div>
          <h3>预检分组</h3>
          <span>
            可修复 {repairableGroupKeys.length} / 已选择 {selectedRepairGroupKeys.length}
          </span>
        </div>
        <div className="toolbar-actions">
          <button
            className="command-button secondary"
            type="button"
            disabled={repairableGroupKeys.length === 0 || isActionDisabled || repairBusy}
            onClick={toggleAllRepairableGroups}
          >
            <CheckSquare size={17} aria-hidden="true" />
            <span>{allRepairableSelected ? "清空选择" : "全选可修复"}</span>
          </button>
          <button className="command-button secondary" type="button" disabled={!canRepair} onClick={() => onRepairGroups?.(selectedRepairGroupKeys)}>
            <RefreshCw size={17} aria-hidden="true" />
            <span>{repairBusy ? "修复中" : "修复所选分组"}</span>
          </button>
        </div>
      </div>

      <div className="single-window-export-review-layout">
        <ResponsiveTableFrame label="单一窗口预检分组" className="compact-table" mobileLayout="scroll">
          <table className="single-window-export-review-group-table">
            <thead>
              <tr>
                <th>修复</th>
                <th>分组</th>
                <th>错误</th>
                <th>警告</th>
                <th>可修复</th>
              </tr>
            </thead>
            <tbody>
              {review.groups.length === 0 ? (
                <tr>
                  <td colSpan={5} className="empty-cell small-empty">
                    未发现导出问题
                  </td>
                </tr>
              ) : (
                review.groups.map((group) => (
                  <tr
                    key={group.groupKey}
                    className={group.groupKey === selectedGroup?.groupKey ? "selected-row" : undefined}
                    onClick={() => setSelectedGroupKey(group.groupKey)}
                  >
                    <td>
                      <input
                        type="checkbox"
                        aria-label={`选择修复分组 ${group.groupDisplayName || group.groupKey}`}
                        checked={selectedRepairKeys.has(group.groupKey)}
                        disabled={!group.canAutoRepair || isActionDisabled || repairBusy}
                        onChange={(event) => toggleRepairGroup(group, event.target.checked)}
                        onClick={(event) => event.stopPropagation()}
                      />
                    </td>
                    <td>{group.groupDisplayName || group.groupKey}</td>
                    <td>{group.errorCount}</td>
                    <td>{group.warningCount}</td>
                    <td>{group.canAutoRepair ? "是" : "否"}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </ResponsiveTableFrame>

        <ResponsiveTableFrame label="单一窗口预检问题" className="compact-table" mobileLayout="scroll">
          <table className="single-window-export-review-issue-table">
            <thead>
              <tr>
                <th>级别</th>
                <th>定位</th>
                <th>消息</th>
                <th>可修复</th>
              </tr>
            </thead>
            <tbody>
              {!selectedGroup || selectedGroup.issues.length === 0 ? (
                <tr>
                  <td colSpan={4} className="empty-cell small-empty">
                    当前分组没有具体问题
                  </td>
                </tr>
              ) : (
                selectedGroup.issues.map((issue, index) => (
                  <tr key={`${selectedGroup.groupKey}-${issue.navigationTarget.propertyKey}-${index}`}>
                    <td>
                      <span className={`review-severity review-severity-${formatSeverityKey(issue.severity)}`}>
                        {formatSeverity(issue.severity)}
                      </span>
                    </td>
                    <td>{formatNavigationTarget(issue.navigationTarget)}</td>
                    <td className="message-cell" title={issue.message}>
                      {issue.message}
                    </td>
                    <td>{issue.canAutoRepair || selectedGroup.canAutoRepair ? "是" : "否"}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </ResponsiveTableFrame>
      </div>
    </div>
  );
}

function SummaryItem({ label, value }: { label: string; value?: string | number }) {
  const displayValue = readDisplayValue(value);

  return (
    <div className="detail-item">
      <span>{label}</span>
      <strong title={displayValue}>{displayValue}</strong>
    </div>
  );
}

function formatNavigationTarget(target?: SingleWindowEditorNavigationTarget) {
  if (!target) {
    return "-";
  }

  const field = target.propertyKey || target.groupKey;
  return target.goodsLineNo > 0 ? `第 ${target.goodsLineNo} 行 · ${field}` : field || "-";
}

function formatSeverity(severity: number) {
  if (severity >= 2) {
    return "错误";
  }

  if (severity === 1) {
    return "警告";
  }

  return "提示";
}

function formatSeverityKey(severity: number) {
  if (severity >= 2) {
    return "error";
  }

  if (severity === 1) {
    return "warning";
  }

  return "info";
}

function readDisplayValue(value?: string | number) {
  if (typeof value === "number") {
    return Number.isFinite(value) ? String(value) : "-";
  }

  return value?.trim() ? value : "-";
}
