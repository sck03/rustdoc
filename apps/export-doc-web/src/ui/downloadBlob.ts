export function downloadBlob(blob: Blob, fileName: string) {
  const objectUrl = window.URL.createObjectURL(blob);
  try {
    startBrowserDownload(objectUrl, fileName.trim() || "download");
  } finally {
    window.setTimeout(() => window.URL.revokeObjectURL(objectUrl), 1000);
  }
}

export function startBrowserDownload(url: string, fileName?: string) {
  const link = document.createElement("a");
  link.href = url;
  link.rel = "noopener";
  link.referrerPolicy = "no-referrer";
  if (fileName?.trim()) link.download = fileName.trim();
  link.hidden = true;
  document.body.appendChild(link);
  try { link.click(); } finally { link.remove(); }
}
