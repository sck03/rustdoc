export const documentAccept = ".pdf,.png,.jpg,.jpeg,.doc,.docx,.xls,.xlsx,.ofd,.xml";
export const documentExtension = /\.(pdf|png|jpe?g|docx?|xlsx?|ofd|xml)$/iu;
export const documentDescription = "PDF、PNG、JPEG、Word、Excel、OFD、XML；每个 10 MiB，最多 20 个、合计 50 MiB。OFD/XML 保留原文件供下载核验。";

export function attachmentUploadForm(file: File, expectedVersion: number) {
  const body = new FormData();
  body.set("file", file); body.set("expectedVersion", String(expectedVersion));
  return body;
}
