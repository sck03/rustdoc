import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Copy, KeyRound, RefreshCw } from "lucide-react";
import {
  type ApiLicenseStatusResponse,
  ExportDocManagerApiClient,
} from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";

export function LicensePage({ client }: { client: ExportDocManagerApiClient }) {
  const queryClient = useQueryClient();
  const [licenseKey, setLicenseKey] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [messageType, setMessageType] = useState<"success" | "error">("success");

  const statusQuery = useQuery({
    queryKey: queryKeys.licenseStatus(),
    queryFn: () => client.getLicenseStatus(),
  });

  useEffect(() => {
    if (statusQuery.isError) {
      setMessage(readApiError(statusQuery.error));
      setMessageType("error");
    }
  }, [statusQuery.error, statusQuery.isError]);

  const registerMutation = useMutation({
    mutationFn: () =>
      client.registerLicense({
        body: {
          licenseKey: licenseKey.trim(),
        },
      }),
    onSuccess: (response) => {
      queryClient.setQueryData<ApiLicenseStatusResponse>(queryKeys.licenseStatus(), response.status);
      setMessage(response.message || response.status.message || "注册成功。");
      setMessageType(response.success ? "success" : "error");
      if (response.success) {
        setLicenseKey("");
      }
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setMessageType("error");
    },
  });

  const status = statusQuery.data ?? null;
  const isBusy = statusQuery.isFetching || registerMutation.isPending;
  const canRegister = Boolean(licenseKey.trim()) && !isBusy;
  const statusText = status ? readLicenseState(status) : "读取中";

  async function copyMachineId() {
    if (!status?.machineId) {
      return;
    }

    try {
      await navigator.clipboard.writeText(status.machineId);
      setMessage("机器码已复制。");
      setMessageType("success");
    } catch {
      setMessage("复制机器码失败。");
      setMessageType("error");
    }
  }

  function handleRegister() {
    if (!canRegister) {
      return;
    }

    setMessage(null);
    registerMutation.mutate();
  }

  return (
    <section className="work-surface license-surface" aria-label="授权注册">
      <div className="toolbar license-toolbar">
        <div className="toolbar-summary">
          <strong>{statusText}</strong>
          <span>{status?.message || "正在读取授权状态"}</span>
        </div>
        <div className="toolbar-actions">
          <button
            className="icon-button"
            type="button"
            title="刷新授权状态"
            disabled={isBusy}
            onClick={() => {
              setMessage(null);
              void statusQuery.refetch();
            }}
          >
            <RefreshCw size={18} aria-hidden="true" />
          </button>
          <button
            className="icon-button"
            type="button"
            title="复制机器码"
            disabled={!status?.machineId}
            onClick={() => void copyMachineId()}
          >
            <Copy size={18} aria-hidden="true" />
          </button>
          <button
            className="icon-button solid"
            type="button"
            title="注册"
            disabled={!canRegister}
            onClick={handleRegister}
          >
            <KeyRound size={18} aria-hidden="true" />
          </button>
        </div>
      </div>

      {message ? <div className={messageType === "error" ? "alert" : "success-alert"}>{message}</div> : null}

      <section className="form-section" aria-label="授权状态">
        <div className="detail-grid license-detail-grid">
          <DetailItem label="状态" value={statusText} />
          <DetailItem label="试用天数" value={status ? `${status.trialDays}` : "-"} />
          <DetailItem label="剩余天数" value={status ? `${status.daysRemaining}` : "-"} />
          <DetailItem label="到期日期" value={formatExpireDate(status)} />
          <DetailItem label="机器码" value={status?.machineId || "-"} wide />
        </div>
      </section>

      <section className="form-section license-register-section" aria-label="注册">
        <div className="section-header">
          <div>
            <h2>注册</h2>
            <span>{status?.isRegistered ? "当前设备已注册" : "输入当前机器码对应的注册码"}</span>
          </div>
        </div>
        <label className="textarea-field">
          <span>注册码</span>
          <textarea
            value={licenseKey}
            placeholder="粘贴 EDM2 注册码"
            disabled={isBusy || status?.isRegistered === true}
            onChange={(event) => setLicenseKey(event.target.value)}
          />
        </label>
        <div className="license-register-actions">
          <button
            className="command-button license-register-button"
            type="button"
            disabled={!canRegister}
            onClick={handleRegister}
          >
            <KeyRound size={18} aria-hidden="true" />
            <span>{status?.isRegistered ? "已注册" : registerMutation.isPending ? "注册中" : "注册授权"}</span>
          </button>
        </div>
      </section>
    </section>
  );
}

function DetailItem({ label, value, wide }: { label: string; value: string; wide?: boolean }) {
  return (
    <div className={wide ? "detail-item detail-item-wide" : "detail-item"}>
      <span>{label}</span>
      <strong title={value}>{value}</strong>
    </div>
  );
}

function readLicenseState(status: ApiLicenseStatusResponse) {
  if (status.isRegistered) {
    return "已注册";
  }

  return status.isTrialExpired ? "试用到期" : "试用中";
}

function formatExpireDate(status: ApiLicenseStatusResponse | null) {
  if (!status?.isRegistered || !status.expireDate) {
    return "-";
  }

  const date = new Date(status.expireDate);
  if (Number.isNaN(date.getTime())) {
    return status.expireDate;
  }

  return date.getFullYear() >= 9999 ? "终身授权" : date.toLocaleDateString();
}
