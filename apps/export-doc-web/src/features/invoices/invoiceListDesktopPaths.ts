import type { ApiInvoiceListItemDto } from "../../api/index.ts";
import {
  isDesktopBridgeAvailable, selectInvoiceTransferPackageFile, selectSaveExcelPath,
  selectSaveInvoiceTransferPackagePath, selectSavePackagePath, selectSingleWindowPackageFile,
} from "../../desktop/desktopBridge.ts";
import { buildSingleWindowPackageDefaultFileName, type SingleWindowBusinessType } from "./invoiceListFileNames.ts";
import { formatSingleWindowBusinessType } from "./invoiceListModels.ts";

export function requestPackageSavePath(defaultFileName: string, defaultExportDirectory: string) {
  return requireDesktopPath("选择发票单据包保存位置", "当前环境不能打开本机保存对话框，请在 Tauri 桌面端导出发票单据包。", () => selectSaveInvoiceTransferPackagePath(defaultFileName, defaultExportDirectory));
}
export function requestPackageOpenPath() { return requireDesktopPath("选择发票单据包", "当前环境不能打开本机文件选择器，请在 Tauri 桌面端选择发票单据包。", selectInvoiceTransferPackageFile); }
export function requestExcelSavePath(defaultFileName: string, defaultExportDirectory: string) { return requireDesktopPath("选择托单 Excel 保存位置", "当前环境不能打开本机保存对话框，请在 Tauri 桌面端导出托单 Excel。", () => selectSaveExcelPath(defaultFileName, defaultExportDirectory)); }
export function requestSingleWindowPackageSavePath(invoice: ApiInvoiceListItemDto, businessType: SingleWindowBusinessType, defaultExportDirectory: string) {
  return requireDesktopPath("选择单一窗口提交包保存位置", `当前环境不能打开本机保存对话框，请在 Tauri 桌面端导出${formatSingleWindowBusinessType(businessType)}提交包。`, () => selectSavePackagePath(buildSingleWindowPackageDefaultFileName(invoice, businessType), defaultExportDirectory));
}
export function requestSingleWindowPackageOpenPath() { return requireDesktopPath("选择单一窗口回执包", "当前环境不能打开本机文件选择器，请在 Tauri 桌面端选择单一窗口回执包。", selectSingleWindowPackageFile); }

async function requireDesktopPath(label: string, unavailableMessage: string, selector: () => Promise<string | null>) {
  if (!isDesktopBridgeAvailable()) throw new Error(unavailableMessage);
  try { return (await selector())?.trim() || ""; }
  catch (error) { const detail = error instanceof Error && error.message.trim() ? error.message.trim() : "桌面桥接命令未完成。"; throw new Error(`${label}失败：${detail}`); }
}

export function readPathDialogError(error: unknown) {
  return error instanceof Error && error.message.trim() ? error.message : "桌面文件路径选择失败。";
}
