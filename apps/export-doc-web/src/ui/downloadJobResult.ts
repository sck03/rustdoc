import { BackgroundJobSnapshot, ExportDocManagerApiClient } from "../api/index.ts";

const terminalStatuses = new Set(["succeeded", "failed", "canceled"]);

export async function downloadJobResultWhenReady(
  client: ExportDocManagerApiClient,
  acceptedJob: BackgroundJobSnapshot,
  fileName: string,
  options: { timeoutMs?: number; signal?: AbortSignal } = {},
) {
  const timeoutMs = options.timeoutMs ?? 180_000;
  const signal = options.signal;
  const startedAt = Date.now();
  let job = acceptedJob;

  while (!terminalStatuses.has(job.status.toLowerCase())) {
    throwIfAborted(signal);
    if (Date.now() - startedAt >= timeoutMs) {
      throw new Error("文件仍在后台生成，可稍后在任务中心下载。");
    }

    await delay(500, signal);
    job = await client.getJob({ jobId: job.jobId }, { signal });
  }

  if (job.status.toLowerCase() !== "succeeded") {
    throw new Error(job.errorMessage || job.detailText || "文件生成失败。");
  }

  await downloadCompletedJobResult(client, job, fileName, signal);
  return job;
}

export async function downloadCompletedJobResult(
  client: ExportDocManagerApiClient,
  job: BackgroundJobSnapshot,
  fileName?: string,
  signal?: AbortSignal,
) {
  throwIfAborted(signal);
  const ticket = await client.createJobDownloadTicket({ jobId: job.jobId }, { signal });
  const anchor = document.createElement("a");
  anchor.href = client.resolveUrl(ticket.downloadUrl);
  anchor.rel = "noopener";
  anchor.referrerPolicy = "no-referrer";
  if (fileName?.trim()) {
    anchor.download = fileName.trim();
  }
  anchor.hidden = true;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
}

function delay(milliseconds: number, signal?: AbortSignal) {
  return new Promise<void>((resolve, reject) => {
    throwIfAborted(signal);
    const handleAbort = () => {
      window.clearTimeout(timeoutId);
      reject(signal?.reason ?? createAbortError());
    };
    const timeoutId = window.setTimeout(() => {
      signal?.removeEventListener("abort", handleAbort);
      resolve();
    }, milliseconds);
    if (signal) {
      signal.addEventListener("abort", handleAbort, { once: true });
      if (signal.aborted) {
        handleAbort();
      }
    }
  });
}

function throwIfAborted(signal?: AbortSignal) {
  if (!signal?.aborted) return;
  throw signal.reason ?? createAbortError();
}

function createAbortError() {
  const error = new Error("操作已取消。");
  error.name = "AbortError";
  return error;
}
