import { BackgroundJobSnapshot, ExportDocManagerApiClient } from "../api/index.ts";

const terminalStatuses = new Set(["succeeded", "failed", "canceled"]);

export async function downloadJobResultWhenReady(
  client: ExportDocManagerApiClient,
  acceptedJob: BackgroundJobSnapshot,
  fileName: string,
  timeoutMs = 180_000,
) {
  const startedAt = Date.now();
  let job = acceptedJob;

  while (!terminalStatuses.has(job.status.toLowerCase())) {
    if (Date.now() - startedAt >= timeoutMs) {
      throw new Error("文件仍在后台生成，可稍后在任务中心下载。");
    }

    await delay(500);
    job = await client.getJob({ jobId: job.jobId });
  }

  if (job.status.toLowerCase() !== "succeeded") {
    throw new Error(job.errorMessage || job.detailText || "文件生成失败。");
  }

  await downloadCompletedJobResult(client, job, fileName);
  return job;
}

export async function downloadCompletedJobResult(
  client: ExportDocManagerApiClient,
  job: BackgroundJobSnapshot,
  fileName?: string,
) {
  const ticket = await client.createJobDownloadTicket({ jobId: job.jobId });
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

function delay(milliseconds: number) {
  return new Promise<void>((resolve) => window.setTimeout(resolve, milliseconds));
}
