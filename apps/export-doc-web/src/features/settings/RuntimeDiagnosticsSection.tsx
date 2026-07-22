import { useMutation } from "@tanstack/react-query";
import { Database, FolderCheck, HardDrive, PackageOpen, RefreshCw } from "lucide-react";
import type { ApiHealthResponse, ExportDocManagerApiClient } from "../../api/index.ts";
import { renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { formatRuntimeDate } from "./settingsFormatters.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { PageState } from "../../ui/PageState.tsx";
import {
  buildRuntimePathGroups,
  runtimePathAccessModeLabel,
  runtimePathRequirementLabel,
  summarizeRuntimePathGroups,
  type RuntimePathAvailability,
  type RuntimePathGroup,
  type RuntimePathItem,
} from "./runtimeDiagnosticsModel.ts";
import {
  buildRuntimeDependencyItems,
  runtimeDependencyStatusLabel,
  summarizeRuntimeDependencies,
  type RuntimeDependencyItem,
} from "./runtimeDependencyDiagnosticsModel.ts";

export function RuntimeDiagnosticsSection({
  client,
  canManageSettings,
  health,
  isBusy,
  errorMessage,
  onRefresh,
  onPathError,
}: {
  client: ExportDocManagerApiClient;
  canManageSettings: boolean;
  health: ApiHealthResponse | null;
  isBusy: boolean;
  errorMessage: string | null;
  onRefresh: () => void;
  onPathError: (message: string) => void;
}) {
  const templateStorageMutation = useMutation({
    mutationFn: () => client.checkReportTemplateStorage(),
  });
  const groups = buildRuntimePathGroups(health);
  const summary = summarizeRuntimePathGroups(groups);
  const dependencies = buildRuntimeDependencyItems(health);
  const dependencySummary = summarizeRuntimeDependencies(dependencies);
  const directoryCheckValue = summary.total === 0
    ? "-"
    : summary.coreMissing > 0
      ? `${summary.coreMissing} 项核心路径需处理`
      : `${summary.coreAvailable}/${summary.coreTotal} 核心路径正常`;
  const directoryCheckTone = summary.coreMissing > 0 ? "warning" : "positive";

  return (
    <section className="form-section runtime-diagnostics-section" aria-label="运行诊断">
      <div className="section-header">
        <div>
          <h2>运行诊断</h2>
          <p className="section-description">集中查看程序资源和业务可写目录，便于确认数据库、日志与缓存是否跟随运行目录。</p>
        </div>
        <div className="toolbar-actions">
          <button
            className="command-button secondary"
            type="button"
            disabled={!canManageSettings || templateStorageMutation.isPending}
            onClick={() => templateStorageMutation.mutate()}
          >
            <FolderCheck size={17} aria-hidden="true" />
            <span>{templateStorageMutation.isPending ? "正在检查" : "检查模板目录"}</span>
          </button>
          <button className="icon-button" type="button" title="刷新运行诊断" aria-label="刷新运行诊断" disabled={isBusy} onClick={onRefresh}>
            <RefreshCw size={18} aria-hidden="true" />
          </button>
        </div>
      </div>
      {health?.storagePolicy ? <div className="info-alert">{health.storagePolicy}</div> : null}
      {errorMessage ? <div className="alert">{errorMessage}</div> : null}
      {templateStorageMutation.isError ? <div className="alert">{readApiError(templateStorageMutation.error)}</div> : null}
      {templateStorageMutation.data ? (
        <div className={templateStorageMutation.data.writable ? "success-alert runtime-template-storage-result" : "alert runtime-template-storage-result"}>
          <div>
            <strong>{templateStorageMutation.data.writable ? "模板目录可用" : "模板目录需要处理"}</strong>
            <span>{templateStorageMutation.data.message}</span>
          </div>
          <div className="runtime-template-storage-path">
            <code title={templateStorageMutation.data.templateRoot}>{templateStorageMutation.data.templateRoot}</code>
            {templateStorageMutation.data.exists
              ? renderOpenPathAction(templateStorageMutation.data.templateRoot, "打开模板目录", onPathError)
              : null}
          </div>
        </div>
      ) : null}

      <div className="runtime-overview-grid" aria-label="运行状态摘要">
        <RuntimeOverviewItem label="API 状态" value={health?.status || "-"} tone={health?.status === "ok" ? "positive" : "neutral"} />
        <RuntimeOverviewItem label="数据库类型" value={health?.databaseProvider || "-"} tone="neutral" />
        <RuntimeOverviewItem label="核心目录" value={directoryCheckValue} tone={directoryCheckTone} />
        <RuntimeOverviewItem
          label="功能依赖"
          value={dependencySummary.featureUnavailable > 0 ? "报表输出需处理" : "核心功能依赖正常"}
          tone={dependencySummary.featureUnavailable > 0 ? "warning" : dependencies.length > 0 ? "positive" : "neutral"}
        />
        <RuntimeOverviewItem label="检查时间" value={formatRuntimeDate(health?.checkedAt)} tone="neutral" />
      </div>

      {summary.coreMissing === 0 && summary.featureMissing > 0 ? (
        <div className="info-alert runtime-feature-dependency-note">
          有 {summary.featureMissing} 项按功能需要的资源未安装，只影响对应功能，不影响数据库和基本运行。
        </div>
      ) : null}

      {dependencies.length > 0 ? (
        <div className="runtime-dependency-section" aria-label="功能依赖检查">
          <div className="runtime-dependency-heading">
            <div>
              <h3>功能依赖</h3>
              <p>只读取运行目录中的可执行文件和模型，不启动外部程序，也不会自动下载。</p>
            </div>
            <span>{dependencySummary.ready}/{dependencySummary.total} 已就绪</span>
          </div>
          <div className="runtime-dependency-grid">
            {dependencies.map((item) => (
              <RuntimeDependencyCard key={item.key} item={item} onPathError={onPathError} />
            ))}
          </div>
        </div>
      ) : null}

      {groups.length > 0 ? (
        <div className="runtime-path-groups">
          {groups.map((group) => (
            <RuntimePathGroupCard key={group.key} group={group} onPathError={onPathError} />
          ))}
        </div>
      ) : !errorMessage ? (
        <PageState tone={isBusy ? "loading" : "empty"} title={isBusy ? "正在读取运行目录" : "暂无运行目录信息"} description={isBusy ? "系统正在检查数据、日志、缓存和依赖目录。" : "刷新诊断信息后可查看运行目录可用性。"} />
      ) : null}
    </section>
  );
}

function RuntimeDependencyCard({
  item,
  onPathError,
}: {
  item: RuntimeDependencyItem;
  onPathError: (message: string) => void;
}) {
  return (
    <section className={`runtime-dependency-card runtime-dependency-card-${item.status}`} aria-label={item.label}>
      <div className="runtime-dependency-card-header">
        <div>
          <strong>{item.label}</strong>
          <span className={`runtime-path-requirement runtime-path-requirement-${item.requirement}`}>
            {runtimePathRequirementLabel(item.requirement)}
          </span>
        </div>
        <span className={`runtime-dependency-status runtime-dependency-status-${item.status}`}>
          {runtimeDependencyStatusLabel(item.status)}
        </span>
      </div>
      <p>{item.message}</p>
      <div className="runtime-dependency-path" title={item.resolvedPath}>{item.resolvedPath}</div>
      {item.ready ? renderOpenPathAction(item.resolvedPath, `打开${item.label}`, onPathError) : null}
    </section>
  );
}

function RuntimeOverviewItem({
  label,
  value,
  tone,
}: {
  label: string;
  value: string;
  tone: "positive" | "warning" | "neutral";
}) {
  return (
    <div className={`runtime-overview-item runtime-overview-${tone}`}>
      <span>{label}</span>
      <strong title={value}>{value}</strong>
    </div>
  );
}

function RuntimePathGroupCard({
  group,
  onPathError,
}: {
  group: RuntimePathGroup;
  onPathError: (message: string) => void;
}) {
  const Icon = group.key === "program-resource" ? PackageOpen : group.key === "database-file" ? Database : HardDrive;

  return (
    <section className="runtime-path-group" aria-label={group.label}>
      <div className="runtime-path-group-header">
        <div className="runtime-path-group-title">
          <span className="runtime-path-group-icon" aria-hidden="true">
            <Icon size={18} />
          </span>
          <div>
            <h3>{group.label}</h3>
            <p>{group.description}</p>
          </div>
        </div>
        <span className="runtime-path-group-count">{group.items.length} 项</span>
      </div>
      <div className="runtime-path-list">
        {group.items.map((item) => (
          <RuntimePathRow key={item.key} item={item} onPathError={onPathError} />
        ))}
      </div>
    </section>
  );
}

function RuntimePathRow({
  item,
  onPathError,
}: {
  item: RuntimePathItem;
  onPathError: (message: string) => void;
}) {
  return (
    <div className="runtime-path-row">
      <div className="runtime-path-name">
        <strong>{item.label}</strong>
        <span>{item.description || "运行目录"}</span>
      </div>
      <div className="runtime-path-value" title={item.path}>{item.path}</div>
      <div className="runtime-path-meta">
        <span className={`runtime-path-requirement runtime-path-requirement-${item.requirement}`}>
          {runtimePathRequirementLabel(item.requirement)}
        </span>
        <span className="runtime-path-access">{runtimePathAccessModeLabel(item.accessMode)}</span>
        <RuntimePathStatus availability={item.availability} requirement={item.requirement} />
        {item.availability === "available"
          ? renderOpenPathAction(item.path, `打开${item.label}`, onPathError)
          : null}
      </div>
    </div>
  );
}

function RuntimePathStatus({
  availability,
  requirement,
}: {
  availability: RuntimePathAvailability;
  requirement: RuntimePathItem["requirement"];
}) {
  const label = availability === "available"
    ? "已找到"
    : requirement === "core"
      ? "需要处理"
      : requirement === "feature"
        ? "功能未安装"
        : "未安装（可选）";
  return (
    <span className={`runtime-path-status runtime-path-status-${availability} runtime-path-status-${availability}-${requirement}`}>
      {label}
    </span>
  );
}
