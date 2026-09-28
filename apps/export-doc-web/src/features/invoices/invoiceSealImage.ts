/** Keep the upload bounded before encoding; the server validates image bytes. */
export async function invoiceSealDataUrl(file: File) {
  if (!file.size || file.size > 5 * 1024 * 1024) throw new Error("印章图片须大于 0 且不超过 5 MiB。");
  if (!["image/png", "image/jpeg"].includes(file.type)) throw new Error("印章仅支持 PNG 或 JPEG 图片。");
  const bytes = new Uint8Array(await file.arrayBuffer());
  let binary = "";
  for (let offset = 0; offset < bytes.length; offset += 8192) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 8192));
  }
  return `data:${file.type};base64,${btoa(binary)}`;
}
