export function downloadBlob(blob: Blob, fileName: string) {
  const normalizedFileName = fileName.trim() || "download";
  const objectUrl = window.URL.createObjectURL(blob);

  try {
    const link = document.createElement("a");
    link.href = objectUrl;
    link.download = normalizedFileName;
    link.rel = "noopener";
    document.body.appendChild(link);
    link.click();
    link.remove();
  } finally {
    window.setTimeout(() => window.URL.revokeObjectURL(objectUrl), 1000);
  }
}
