import { isDesktopBridgeAvailable, savePdfFile, selectSavePdfPath } from "../../../desktop/desktopBridge.ts";
import { downloadBlob } from "../../../ui/downloadBlob.ts";

type ContainerPackingPdfExportOptions = {
  root: HTMLElement;
  projectName: string;
  containerType: string;
};

const A4_CAPTURE_WIDTH_PX = 960;
const PDF_IMAGE_QUALITY = 0.8;

export async function exportContainerPackingPdf(options: ContainerPackingPdfExportOptions) {
  const [{ default: html2canvas }, { jsPDF }] = await Promise.all([
    import("html2canvas"),
    import("jspdf"),
  ]);
  const generatedAt = new Date();
  const fileName = buildContainerPackingPdfFileName(options.projectName, generatedAt);
  const exportRoot = buildPdfExportRoot(options.root, options.projectName, options.containerType, generatedAt);
  let canvas: HTMLCanvasElement;
  try {
    document.body.append(exportRoot);
    await document.fonts?.ready;
    const captureWidth = Math.max(A4_CAPTURE_WIDTH_PX, Math.ceil(exportRoot.scrollWidth));
    exportRoot.style.width = `${captureWidth}px`;
    canvas = await html2canvas(exportRoot, {
      backgroundColor: "#ffffff",
      width: captureWidth,
      windowWidth: captureWidth,
      logging: false,
      scale: 1.15,
      useCORS: true,
    });
  } finally {
    exportRoot.remove();
  }
  const pdf = new jsPDF({ orientation: "portrait", unit: "mm", format: "a4", compress: true });
  const pageCount = addCanvasPages(pdf, canvas);
  const blob = pdf.output("blob");

  if (isDesktopBridgeAvailable()) {
    const selectedPath = await selectSavePdfPath(fileName);
    if (!selectedPath) return { status: "cancelled" as const, fileName };

    try {
      await savePdfFile(selectedPath, await blobToBase64(blob));
      return { status: "saved" as const, mode: "desktop" as const, fileName, path: selectedPath, sizeBytes: blob.size, pageCount };
    } catch (error) {
      return {
        status: "save-failed" as const,
        fileName,
        sizeBytes: blob.size,
        pageCount,
        error: readUnknownError(error),
      };
    }
  }

  downloadBlob(blob, fileName);
  return { status: "saved" as const, mode: "browser" as const, fileName, sizeBytes: blob.size, pageCount };
}

export function buildContainerPackingPdfFileName(projectName: string, generatedAt = new Date()) {
  const safeName = (projectName.trim() || "未命名方案")
    .replace(/[<>:"/\\|?*\u0000-\u001f]/g, "-")
    .replace(/\s+/g, " ")
    .slice(0, 60);
  const timestamp = [
    generatedAt.getFullYear(),
    String(generatedAt.getMonth() + 1).padStart(2, "0"),
    String(generatedAt.getDate()).padStart(2, "0"),
    "-",
    String(generatedAt.getHours()).padStart(2, "0"),
    String(generatedAt.getMinutes()).padStart(2, "0"),
  ].join("");
  return `装柜方案-${safeName}-${timestamp}.pdf`;
}

function buildPdfHeading(documentClone: Document, projectName: string, containerType: string, generatedAt: Date) {
  const heading = documentClone.createElement("header");
  heading.className = "container-packing-pdf-heading";
  const titleGroup = documentClone.createElement("div");
  const eyebrow = documentClone.createElement("span");
  eyebrow.textContent = "现场装柜作业单";
  const title = documentClone.createElement("h1");
  title.textContent = projectName.trim() || "未命名方案";
  titleGroup.append(eyebrow, title);
  const meta = documentClone.createElement("div");
  const containerLabel = documentClone.createElement("strong");
  containerLabel.textContent = `柜型：${containerType || "自定义柜型"}`;
  const timeLabel = documentClone.createElement("span");
  timeLabel.textContent = `生成时间：${generatedAt.toLocaleString("zh-CN", { hour12: false })}`;
  meta.append(containerLabel, timeLabel);
  heading.append(titleGroup, meta);
  return heading;
}

function buildPdfExportRoot(sourceRoot: HTMLElement, projectName: string, containerType: string, generatedAt: Date) {
  const exportRoot = sourceRoot.cloneNode(true) as HTMLElement;
  exportRoot.classList.add("container-packing-pdf-export");
  exportRoot.style.position = "fixed";
  exportRoot.style.left = "-100000px";
  exportRoot.style.top = "0";
  exportRoot.style.width = `${A4_CAPTURE_WIDTH_PX}px`;
  exportRoot.style.maxWidth = "none";
  exportRoot.style.gridTemplateColumns = "minmax(0, 1fr)";
  exportRoot.style.pointerEvents = "none";
  exportRoot.prepend(buildPdfHeading(document, projectName, containerType, generatedAt));
  return exportRoot;
}

function addCanvasPages(pdf: InstanceType<(typeof import("jspdf"))["jsPDF"]>, canvas: HTMLCanvasElement) {
  const pageWidth = 210;
  const pageHeight = 297;
  const margin = 8;
  const contentWidth = pageWidth - margin * 2;
  const contentHeight = pageHeight - margin * 2;
  const maximumSliceHeight = Math.max(1, Math.floor((contentHeight / contentWidth) * canvas.width));
  const widthScale = contentWidth / canvas.width;
  const fitScale = Math.min(widthScale, contentHeight / canvas.height);

  if (fitScale >= widthScale * 0.92) {
    const imageWidth = canvas.width * fitScale;
    const imageHeight = canvas.height * fitScale;
    const imageX = (pageWidth - imageWidth) / 2;
    const imageY = (pageHeight - imageHeight) / 2;
    pdf.addImage(canvas.toDataURL("image/jpeg", PDF_IMAGE_QUALITY), "JPEG", imageX, imageY, imageWidth, imageHeight, undefined, "FAST");
    return 1;
  }

  const pageCount = Math.ceil(canvas.height / maximumSliceHeight);
  const sliceHeight = Math.ceil(canvas.height / pageCount);
  let offsetY = 0;
  let pageIndex = 0;

  while (offsetY < canvas.height) {
    const currentSliceHeight = Math.min(sliceHeight, canvas.height - offsetY);
    const pageCanvas = document.createElement("canvas");
    pageCanvas.width = canvas.width;
    pageCanvas.height = currentSliceHeight;
    const context = pageCanvas.getContext("2d");
    if (!context) throw new Error("无法创建 PDF 页面画布。");
    context.fillStyle = "#ffffff";
    context.fillRect(0, 0, pageCanvas.width, pageCanvas.height);
    context.drawImage(canvas, 0, offsetY, canvas.width, currentSliceHeight, 0, 0, canvas.width, currentSliceHeight);

    if (pageIndex > 0) pdf.addPage();
    const imageHeight = (currentSliceHeight * contentWidth) / canvas.width;
    pdf.addImage(pageCanvas.toDataURL("image/jpeg", PDF_IMAGE_QUALITY), "JPEG", margin, margin, contentWidth, imageHeight, undefined, "FAST");
    offsetY += currentSliceHeight;
    pageIndex += 1;
  }

  return pageIndex;
}

function readUnknownError(error: unknown) {
  if (error instanceof Error && error.message.trim()) return error.message.trim();
  if (typeof error === "string" && error.trim()) return error.trim();
  return "桌面保存命令暂不可用";
}

function blobToBase64(blob: Blob) {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(new Error("无法读取生成的 PDF。"));
    reader.onload = () => {
      const result = typeof reader.result === "string" ? reader.result : "";
      const separatorIndex = result.indexOf(",");
      if (separatorIndex < 0) {
        reject(new Error("生成的 PDF 数据无效。"));
        return;
      }
      resolve(result.slice(separatorIndex + 1));
    };
    reader.readAsDataURL(blob);
  });
}
