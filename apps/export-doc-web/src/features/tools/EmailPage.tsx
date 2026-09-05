import { useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useLocation, useNavigate } from "react-router-dom";
import { Paperclip, RefreshCw, Send, Settings, Trash2 } from "lucide-react";
import {
  type ApiEmailDeliveryDto,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { usePermission, usePermissionCapabilities } from "../../app/PermissionAccessContext.tsx";
import { permissionActions, permissionResources } from "../../app/permissionCatalog.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { isDesktopBridgeAvailable, selectEmailAttachmentFiles } from "../../desktop/desktopBridge.ts";
import { readDesktopError, renderOpenPathAction } from "../../ui/DesktopPathActions.tsx";
import { PathTextAreaField } from "../../ui/PathField.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { InlineNotice, PermissionNotice } from "../../ui/PageState.tsx";
import { ResponsiveTableFrame } from "../../ui/ResponsiveTable.tsx";
import { readEmailDraftNavigationState } from "./emailDraftNavigation.ts";
import { EmailRichTextEditor } from "../../ui/EmailRichTextEditor.tsx";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";

type MessageState = {
  kind: "success" | "error";
  text: string;
};

export function EmailPage({ client }: { client: ExportDocManagerApiClient }) {
  const sendPermission = usePermission(permissionResources.emailDelivery, permissionActions.send);
  const deliveryViewPermission = usePermission(permissionResources.emailDelivery, permissionActions.viewDelivery);
  const { canManageSettings } = usePermissionCapabilities();
  const location = useLocation();
  const navigate = useNavigate();
  const [toAddress, setToAddress] = useState("");
  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [attachmentsText, setAttachmentsText] = useState("");
  const [message, setMessage] = useState<MessageState | null>(null);
  const [lastSentDraftSnapshot, setLastSentDraftSnapshot] = useState("");
  const submittedDraftSnapshotRef = useRef("");
  const deliveryAttemptRef = useRef({ snapshot: "", id: "" });
  const isDesktopRuntime = isDesktopBridgeAvailable();

  useEffect(() => {
    const draft = readEmailDraftNavigationState(location.state);
    if (!draft) return;
    if (draft.toAddress) setToAddress(draft.toAddress);
    setSubject(draft.subject); setBody(draft.body);
    setMessage({ kind: "success", text: "已套用邮件模板，请确认收件人和内容后发送。" });
    navigate(location.pathname, { replace: true, state: null });
  }, [location.pathname, location.state, navigate]);

  const statusQuery = useQuery({
    queryKey: queryKeys.emailStatus(),
    queryFn: ({ signal }) => client.getEmailToolStatus({ signal }),
    enabled: deliveryViewPermission.allowed,
  });
  const deliveriesQuery = useQuery({
    queryKey: queryKeys.emailDeliveries(),
    queryFn: ({ signal }) => client.listEmailDeliveries({ limit: 50 }, { signal }),
    enabled: deliveryViewPermission.allowed,
  });

  useEffect(() => {
    if (statusQuery.isError) {
      setMessage({ kind: "error", text: readApiError(statusQuery.error) });
    }
  }, [statusQuery.error, statusQuery.isError]);

  const sendMutation = useMutation({
    mutationFn: () =>
      client.sendEmail({
        "Idempotency-Key": deliveryAttemptRef.current.id,
        body: {
          toAddress: toAddress.trim(),
          subject: subject.trim(),
          body,
          attachmentPaths: isDesktopRuntime ? attachmentPaths : [],
        },
      }),
    onSuccess: (response) => {
      if (response.success) {
        setLastSentDraftSnapshot(submittedDraftSnapshotRef.current);
      }
      setMessage({
        kind: response.success ? "success" : "error",
        text: response.message || "邮件发送完成。",
      });
      if (deliveryViewPermission.allowed) void deliveriesQuery.refetch();
    },
    onError: (error) => {
      setMessage({ kind: "error", text: readApiError(error) });
      if (deliveryViewPermission.allowed) void deliveriesQuery.refetch();
    },
  });

  const status = statusQuery.data ?? null;
  const attachmentPaths = useMemo(
    () => isDesktopRuntime ? normalizeAttachmentPaths(attachmentsText) : [],
    [attachmentsText, isDesktopRuntime],
  );
  const currentDraftSnapshot = JSON.stringify({
    toAddress: toAddress.trim(),
    subject: subject.trim(),
    body,
    attachmentPaths,
  });
  const hasDraftContent = Boolean(toAddress.trim() || subject.trim() || body.trim() || attachmentPaths.length);
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: sendPermission.allowed && hasDraftContent && currentDraftSnapshot !== lastSentDraftSnapshot,
    message: "当前邮件有尚未发送的内容。",
  });
  const isBusy = statusQuery.isFetching || sendMutation.isPending;
  const canSend = sendPermission.allowed && Boolean(status?.isConfigured && toAddress.trim()) && !isBusy;
  const statusLabel = status ? (status.isConfigured ? "SMTP 已配置" : "SMTP 未配置") : "读取中";
  const statusSummary = !status
    ? "正在读取邮件服务状态"
    : status.isConfigured
      ? `${status.smtpHost}:${status.smtpPort} · ${status.fromAddress}`
      : "请先配置邮件服务器和发件人信息";

  async function pickAttachments() {
    try {
      const selected = await selectEmailAttachmentFiles();
      if (!selected.length) {
        return;
      }

      setAttachmentsText((current) => mergeAttachmentPaths(normalizeAttachmentPaths(current), selected).join("\n"));
      setMessage({ kind: "success", text: `已选择 ${selected.length} 个附件。` });
    } catch (error) {
      setMessage({ kind: "error", text: readDesktopError(error) });
    }
  }

  function removeAttachment(path: string) {
    setAttachmentsText((current) =>
      normalizeAttachmentPaths(current)
        .filter((item) => item !== path)
        .join("\n"),
    );
  }

  function showDesktopError(text: string) {
    setMessage({ kind: "error", text });
  }

  function handleSend() {
    if (!canSend) {
      return;
    }

    setMessage(null);
    submittedDraftSnapshotRef.current = currentDraftSnapshot;
    if (deliveryAttemptRef.current.snapshot !== currentDraftSnapshot) {
      deliveryAttemptRef.current = { snapshot: currentDraftSnapshot, id: crypto.randomUUID() };
    }
    sendMutation.mutate();
  }

  async function openEmailSettings() {
    if (!canManageSettings) return;
    if (await confirmDiscardChanges("打开邮件设置")) navigate("/settings?section=email");
  }

  return (
    <section className="work-surface email-tool-surface" aria-label="邮件发送">
      <div className="toolbar email-tool-toolbar">
        <div className="toolbar-summary">
          <strong>{statusLabel}</strong>
          <span>{statusSummary}</span>
        </div>
        <div className="toolbar-actions">
          {canManageSettings && status && !status.isConfigured ? (
            <button className="icon-button" type="button" title="配置邮件服务" aria-label="配置邮件服务" onClick={() => void openEmailSettings()}>
              <Settings size={18} aria-hidden="true" />
            </button>
          ) : null}
          <button
            className="icon-button"
            type="button"
            title="刷新状态" aria-label="刷新状态"
            disabled={isBusy}
            onClick={() => {
              setMessage(null);
              void statusQuery.refetch();
            }}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          {isDesktopRuntime ? (
            <button className="icon-button" type="button" title="选择附件" aria-label="选择附件" disabled={isBusy || !sendPermission.allowed} onClick={() => void pickAttachments()}>
              <Paperclip size={18} aria-hidden="true" />
            </button>
          ) : null}
          <button className="icon-button solid" type="button" title="发送邮件" aria-label="发送邮件" disabled={!canSend} onClick={handleSend}>
            <Send size={18} aria-hidden="true" />
          </button>
        </div>
      </div>

      {!sendPermission.allowed ? <PermissionNotice>当前模板仅允许查看邮件服务状态和授权范围内的投递记录，邮件编辑和发送已禁用。</PermissionNotice> : null}
      {message ? <InlineNotice tone={message.kind === "error" ? "error" : "success"}>{message.text}</InlineNotice> : null}
      <section className="form-section" aria-label="邮件状态">
        <div className="detail-grid email-status-detail-grid">
          <DetailItem label="SMTP 服务器" value={status?.isConfigured ? status.smtpHost : "-"} wide />
          <DetailItem label="端口" value={status?.isConfigured ? String(status.smtpPort) : "-"} />
          <DetailItem label="SSL" value={status?.isConfigured ? (status.enableSsl ? "启用" : "关闭") : "-"} />
          <DetailItem label="发件人地址" value={status?.isConfigured ? status.fromAddress : "-"} wide />
          <DetailItem label="发件人名称" value={status?.isConfigured ? (status.fromDisplayName || "-") : "-"} />
          <DetailItem label="附件数量" value={String(attachmentPaths.length)} />
        </div>
      </section>

      <div className="email-tool-layout">
        <section className="form-section email-compose-section" aria-label="邮件内容">
          <label>
            <span>收件人</span>
            <input
              value={toAddress}
              type="email"
              autoComplete="email"
              disabled={!sendPermission.allowed}
              onChange={(event) => setToAddress(event.target.value)}
            />
          </label>
          <label>
            <span>主题</span>
            <input value={subject} disabled={!sendPermission.allowed} onChange={(event) => setSubject(event.target.value)} />
          </label>
          <div className="email-body-field email-rich-text-field">
            <span className="email-rich-text-field-label">正文</span>
            <EmailRichTextEditor value={body} disabled={!sendPermission.allowed} ariaLabel="待发送邮件正文" onChange={setBody} />
            <details className="email-html-advanced">
              <summary>高级 HTML</summary>
              <textarea aria-label="待发送邮件高级 HTML" value={body} disabled={!sendPermission.allowed} onChange={(event) => setBody(event.target.value)} />
            </details>
          </div>
        </section>

        {isDesktopRuntime ? <section className="form-section email-attachment-section" aria-label="邮件附件">
          <div className="section-header">
            <div>
              <h2>附件</h2>
              <span>{attachmentPaths.length ? `${attachmentPaths.length} 个文件` : "未选择"}</span>
            </div>
          </div>
          <PathTextAreaField
            label="附件路径"
            value={attachmentsText}
            disabled={isBusy || !sendPermission.allowed}
            onChange={(value) => setAttachmentsText(value)}
            actions={
              <button className="icon-button" type="button" title="选择附件" aria-label="选择附件" disabled={isBusy || !sendPermission.allowed} onClick={() => void pickAttachments()}>
                <Paperclip size={16} aria-hidden="true" />
              </button>
            }
          />
          <ResponsiveTableFrame className="email-attachment-table-frame" label="邮件附件列表">
            <table className="email-attachment-table">
              <thead>
                <tr>
                  <th>路径</th>
                  <th>操作</th>
                </tr>
              </thead>
              <tbody>
                {attachmentPaths.map((path) => (
                  <tr key={path}>
                    <td className="path-cell" title={path}>
                      {path}
                    </td>
                    <td className="row-actions-cell">
                      {renderOpenPathAction(path, "打开附件", showDesktopError)}
                      <button
                        className="icon-button compact-icon-button"
                        type="button"
                        title="移除附件" aria-label="移除附件"
                        disabled={isBusy || !sendPermission.allowed}
                        onClick={() => removeAttachment(path)}
                      >
                        <Trash2 size={15} aria-hidden="true" />
                      </button>
                    </td>
                  </tr>
                ))}
                {!attachmentPaths.length ? (
                  <tr>
                    <td className="empty-cell small-empty" colSpan={2}>
                      暂无附件路径
                    </td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </ResponsiveTableFrame>
        </section> : (
          <section className="form-section email-attachment-section" aria-label="邮件附件">
            <div className="section-header">
              <div>
                <h2>附件</h2>
                <span>浏览器端不接受服务器文件路径</span>
              </div>
            </div>
            <InlineNotice tone="info">
              局域网和容器版的通用邮件页只发送正文。需要发送单据附件时，请从对应发票或报表输出页面发起，系统会按当前账号可访问的业务记录生成附件。
            </InlineNotice>
          </section>
        )}
      </div>

      {deliveryViewPermission.allowed ? (
        <EmailDeliveryHistory
          rows={deliveriesQuery.data ?? []}
          isLoading={deliveriesQuery.isFetching}
          onRefresh={() => void deliveriesQuery.refetch()}
        />
      ) : null}
    </section>
  );
}

function EmailDeliveryHistory({ rows, isLoading, onRefresh }: { rows: ApiEmailDeliveryDto[]; isLoading: boolean; onRefresh: () => void }) {
  return (
    <section className="form-section" aria-label="邮件投递记录">
      <div className="section-header">
        <div><h2>投递记录</h2><span>最近 {rows.length} 条</span></div>
        <button className="icon-button" type="button" title="刷新投递记录" aria-label="刷新投递记录" disabled={isLoading} onClick={onRefresh}>
          <RefreshCw size={17} aria-hidden="true" />
        </button>
      </div>
      <ResponsiveTableFrame label="邮件投递记录">
        <table>
          <thead><tr><th>时间</th><th>来源</th><th>收件人</th><th>主题</th><th>附件</th><th>状态</th></tr></thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.deliveryId}>
                <td>{formatDeliveryTime(row.createdAt)}</td>
                <td>{row.kind === "ReportDocumentEmail" ? "发票单据" : "通用邮件"}</td>
                <td>{row.recipient}</td>
                <td title={row.errorMessage || row.subject}>{row.subject || "-"}</td>
                <td>{row.attachmentCount}</td>
                <td title={row.errorMessage}>{formatDeliveryStatus(row.status)}</td>
              </tr>
            ))}
            {!rows.length ? <tr><td className="empty-cell small-empty" colSpan={6}>{isLoading ? "正在加载" : "暂无投递记录"}</td></tr> : null}
          </tbody>
        </table>
      </ResponsiveTableFrame>
    </section>
  );
}

function formatDeliveryStatus(status: string) {
  if (status === "Sent") return "已发送";
  if (status === "Uncertain") return "结果不确定";
  return "投递中";
}

function formatDeliveryTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "-" : date.toLocaleString();
}

function DetailItem({ label, value, wide }: { label: string; value: string; wide?: boolean }) {
  return (
    <div className={wide ? "detail-item detail-item-wide" : "detail-item"}>
      <span>{label}</span>
      <strong title={value}>{value}</strong>
    </div>
  );
}

function normalizeAttachmentPaths(text: string) {
  const seen = new Set<string>();
  const paths: string[] = [];
  for (const rawPath of text.split(/\r?\n/)) {
    const path = rawPath.trim();
    if (!path) {
      continue;
    }

    const key = path.toLocaleLowerCase();
    if (seen.has(key)) {
      continue;
    }

    seen.add(key);
    paths.push(path);
  }

  return paths;
}

function mergeAttachmentPaths(current: string[], selected: string[]) {
  return normalizeAttachmentPaths([...current, ...selected].join("\n"));
}
