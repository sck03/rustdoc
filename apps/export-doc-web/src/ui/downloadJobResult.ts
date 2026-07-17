import { BackgroundJobSnapshot, ExportDocManagerApiClient } from "../api/index.ts";
import { downloadBlob } from "./downloadBlob.ts";

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

  const blob = await client.downloadJobResult({ jobId: job.jobId });
  downloadBlob(blob, fileName);
  return job;
}

function delay(milliseconds: number) {
  return new Promise<void>((resolve) => window.setTimeout(resolve, milliseconds));
}
