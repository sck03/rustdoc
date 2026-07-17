import { existsSync, mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from "node:fs";
import path from "node:path";

export function createSingleWindowOperationCenterSmokeScene(runtime) {
  const {
    authorizedHeaders,
    authorizedJsonHeaders,
    buildSmokeAgentConsignmentReceiptXml,
    buildSmokeCustomsCooReceiptXml,
    createSmokeInvoice,
    deleteSmokeInvoice,
    ensureTrailingSlash,
    evaluate,
    getSingleWindowBatchDetail,
    normalizePathForCompare,
    redactDesktopAccessToken,
    tryRemoveDirectory,
    waitFor,
    waitForPageExpression,
    waitForRuntimeDiagnostics,
  } = runtime;

  async function waitForSingleWindowOperationCenterCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.singleWindowOperationCenterCheck) {
      return null;
    }

    const customsCoo = await waitForSingleWindowOperationCenterCustomsCooCheck(page, options, accessToken, tokenType, timeoutMs);
    const agentConsignment = await waitForSingleWindowOperationCenterAgentConsignmentCheck(page, options, accessToken, tokenType, timeoutMs);

    return {
      ...customsCoo,
      customsCoo,
      agentConsignment,
      allBusinessesSucceeded: Boolean(
        customsCoo?.detailStatus === "Approved" &&
        agentConsignment?.detailStatus === "Accepted" &&
        customsCoo?.detailReceiptRecordCount > 0 &&
        agentConsignment?.detailReceiptRecordCount > 0,
      ),
    };
  }

  async function waitForSingleWindowOperationCenterCustomsCooCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.singleWindowOperationCenterCheck) {
      return null;
    }

    const timestamp = Date.now();
    const smokeRoot = path.join(options.userDataDir, `single-window-operation-center-${timestamp}`);
    const clientRoot = path.join(smokeRoot, "ClientRoot");
    const outBoxPath = path.join(clientRoot, "OutBox");
    const inBoxPath = path.join(clientRoot, "InBox");
    const submitPackagePath = path.join(smokeRoot, `submit-package-${timestamp}.swpkg`);
    const receiptPackagePath = path.join(smokeRoot, `receipt-package-${timestamp}.swpkg`);
    let invoice = null;
    let submitPackage = null;
    let receiptFilePath = "";
    let cleanupDeleted = false;
    let cleanedClientRoot = false;
    let result = null;

    try {
      mkdirSync(inBoxPath, { recursive: true });
      invoice = await createSmokeInvoice(options, accessToken, tokenType);
      submitPackage = await exportSmokeCustomsCooSubmitPackage(
        options,
        accessToken,
        tokenType,
        invoice.id,
        submitPackagePath,
      );

      const batchId = submitPackage.trackingBatchId;
      const batchReference = submitPackage.manifest?.batchReference ?? "";
      if (!batchId || !batchReference) {
        throw new Error(`Single Window submit package response did not include trackingBatchId/batchReference: ${JSON.stringify(submitPackage)}`);
      }

      const checkUrl = buildSingleWindowOperationCenterCheckUrl(options.webUrl);
      await page.send("Page.navigate", { url: checkUrl });
      await waitForRuntimeDiagnostics(page, ["操作中心", "提交包导入", "批次快捷操作"], timeoutMs);

      await evaluate(
        page,
        `(() => {
          const input = document.querySelector('input[aria-label="搜索单一窗口批次"]');
          if (!input) {
            throw new Error('Operation center search input was not found.');
          }

          const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
          setter.call(input, ${JSON.stringify(invoice.invoiceNo)});
          input.dispatchEvent(new Event('input', { bubbles: true }));
          return true;
        })()`,
        true,
      );

      await waitForPageExpression(
        page,
        `(() => {
          const input = document.querySelector('input[aria-label="搜索单一窗口批次"]');
          return Boolean(input && input.value === ${JSON.stringify(invoice.invoiceNo)});
        })()`,
        timeoutMs,
        "Timed out waiting for operation center search input to receive the smoke invoice number.",
      );

      await evaluate(
        page,
        `(() => {
          const input = document.querySelector('input[aria-label="搜索单一窗口批次"]');
          if (!input) {
            throw new Error('Operation center search input was not found before submit.');
          }

          const form = input.closest('form');
          if (form) {
            form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
          }
          return true;
        })()`,
        true,
      );

      const rowReady = await waitForPageExpression(
        page,
        `(() => {
          const rows = Array.from(document.querySelectorAll('.single-window-operation-table tbody tr'));
          const row = rows.find((candidate) =>
            (candidate.innerText || '').includes(${JSON.stringify(invoice.invoiceNo)}) &&
            (candidate.innerText || '').includes(${JSON.stringify(batchReference)}));
          if (!row) {
            return false;
          }

          row.click();
          return true;
        })()`,
        timeoutMs,
        `Timed out waiting for operation center smoke batch row: ${invoice.invoiceNo} / ${batchReference}`,
      );

      const actionPanelReady = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const text = section ? section.innerText || '' : '';
          return Boolean(section &&
            text.includes(${JSON.stringify(batchReference)}) &&
            text.includes(${JSON.stringify(invoice.invoiceNo)}) &&
            text.includes('保存目录根') &&
            text.includes('发送到 OutBox') &&
            text.includes('自动收件打包') &&
            text.includes('打包并导入'));
        })()`,
        timeoutMs,
        "Timed out waiting for operation center list action panel.",
      );

      await waitForPageExpression(
        page,
        `(() => {
          window.__exportDocManagerSmokeDirectoryPath = ${JSON.stringify(clientRoot)};
          window.__exportDocManagerSmokeTauriInvocations = [];
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.title || '').includes('选择业务目录根')) : null;
          if (!button || button.disabled) {
            return false;
          }

          button.click();
          return true;
        })()`,
        timeoutMs,
        "Timed out waiting for operation center directory picker button to become available.",
      );

      const directoryPicked = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const input = section ? section.querySelector('.path-field input') : null;
          const invocations = window.__exportDocManagerSmokeTauriInvocations || [];
          return Boolean(input &&
            input.value === ${JSON.stringify(clientRoot)} &&
            invocations.some((entry) => entry.command === 'select_directory'));
        })()`,
        timeoutMs,
        "Timed out waiting for operation center directory picker to apply the smoke client root.",
      );

      await evaluate(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.innerText || '').includes('保存目录根')) : null;
          if (!button || button.disabled) {
            throw new Error('Operation center save directory root button is not available.');
          }

          button.click();
          return true;
        })()`,
        true,
      );

      const savedProfile = await waitFor(async () => {
        const candidate = await getSingleWindowClientProfile(options, accessToken, tokenType);
        return singleWindowProfileContainsPath(candidate.profile, clientRoot) ? candidate : null;
      }, timeoutMs, "Timed out waiting for operation center client root to persist in the API profile.");

      await evaluate(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.innerText || '').includes('发送到 OutBox')) : null;
          if (!button || button.disabled) {
            throw new Error('Operation center dispatch button is not available.');
          }

          button.click();
          return true;
        })()`,
        true,
      );

      const dispatchUi = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const text = section ? section.innerText || '' : '';
          return Boolean(text.includes('当前批次已发送到默认导入目录') &&
            text.includes('目标目录') &&
            text.includes('报文数'));
        })()`,
        timeoutMs,
        "Timed out waiting for operation center dispatch UI result.",
      );

      const outBoxFiles = await waitFor(async () => {
        if (!existsSync(outBoxPath)) {
          return null;
        }

        const files = readdirSync(outBoxPath)
          .filter((fileName) => fileName.toLowerCase().endsWith(".xml"))
          .map((fileName) => path.join(outBoxPath, fileName));
        return files.length > 0 ? files : null;
      }, timeoutMs, `Timed out waiting for dispatched Single Window XML files in ${outBoxPath}.`);

      receiptFilePath = path.join(inBoxPath, `Successed_${batchReference}_${invoice.invoiceNo}.xml`);
      writeFileSync(receiptFilePath, buildSmokeCustomsCooReceiptXml(batchReference), "utf8");

      await evaluate(
        page,
        `(() => {
          window.__exportDocManagerSmokeSavePackagePath = ${JSON.stringify(receiptPackagePath)};
          window.__exportDocManagerSmokeTauriInvocations = [];
          return true;
        })()`,
        true,
      );

      await evaluate(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.innerText || '').includes('打包并导入')) : null;
          if (!button || button.disabled) {
            throw new Error('Operation center receipt package import button is not available.');
          }

          button.click();
          return true;
        })()`,
        true,
      );

      const autoReceiptUi = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const text = section ? section.innerText || '' : '';
          return Boolean(
            text.includes('回执包已导出并导入') &&
            text.includes('回执文件') &&
            text.includes('回执包') &&
            text.includes('解析回执') &&
            text.includes('写入回执') &&
            text.includes('Smoke approved'));
        })()`,
        timeoutMs,
        "Timed out waiting for operation center receipt package import result.",
      );

      const packageFile = await waitFor(async () => {
        if (!existsSync(receiptPackagePath)) {
          return null;
        }

        const size = statSync(receiptPackagePath).size;
        const header = size > 0 ? readFileSync(receiptPackagePath).subarray(0, 2).toString("ascii") : "";
        return header === "PK" ? { size, header } : null;
      }, timeoutMs, `Timed out waiting for receipt package file: ${receiptPackagePath}`);

      const savePackageInvocation = await waitForPageExpression(
        page,
        `(() => {
          const expected = ${JSON.stringify(normalizePathForCompare(receiptPackagePath))};
          const normalize = (value) => String(value || '').replace(/\\\\/g, '/').replace(/\\/+$/, '').toLowerCase();
          const invocations = window.__exportDocManagerSmokeTauriInvocations || [];
          return invocations.some((entry) => entry &&
            entry.command === 'select_save_package_path' &&
            normalize(window.__exportDocManagerSmokeSavePackagePath) === expected);
        })()`,
        timeoutMs,
        "Timed out waiting for select_save_package_path invocation.",
      );

      const detail = await waitFor(async () => {
        const candidate = await getSingleWindowBatchDetail(options, accessToken, tokenType, batchId);
        const receiptRecords = Array.isArray(candidate.receiptRecords) ? candidate.receiptRecords : [];
        return normalizePathForCompare(candidate.lastReceiptPackagePath) === normalizePathForCompare(receiptPackagePath) &&
          candidate.status === "Approved" &&
          receiptRecords.some((record) => String(record.receiptMessage || "").includes("Smoke approved"))
          ? candidate
          : null;
      }, timeoutMs, "Timed out waiting for operation center detail to record imported receipt package and receipt log.");

      const submitPackageHeader = existsSync(submitPackagePath)
        ? readFileSync(submitPackagePath).subarray(0, 2).toString("ascii")
        : "";
      result = {
        invoiceId: invoice.id,
        invoiceNo: invoice.invoiceNo,
        batchId,
        batchReference,
        url: redactDesktopAccessToken(checkUrl),
        submitPackagePath,
        submitPackageHeader,
        clientRoot,
        outBoxPath,
        outBoxXmlCount: outBoxFiles.length,
        dispatchedXmlFiles: outBoxFiles.map((filePath) => path.basename(filePath)),
        receiptFilePath,
        receiptPackagePath,
        receiptPackageHeader: packageFile.header,
        receiptPackageSize: packageFile.size,
        rowReady: Boolean(rowReady?.found),
        actionPanelReady: Boolean(actionPanelReady?.found),
        directoryPicked: Boolean(directoryPicked?.found),
        savedProfile: singleWindowProfileContainsPath(savedProfile?.profile, clientRoot),
        dispatchUi: Boolean(dispatchUi?.found),
        autoReceiptUi: Boolean(autoReceiptUi?.found),
        savePackageInvocation: Boolean(savePackageInvocation?.found),
        detailStatus: detail.status,
        detailLastReceiptPackagePath: detail.lastReceiptPackagePath,
        detailClientDispatchPath: detail.clientDispatchPath,
        detailPackageRecordCount: Array.isArray(detail.packageRecords) ? detail.packageRecords.length : 0,
        detailReceiptRecordCount: Array.isArray(detail.receiptRecords) ? detail.receiptRecords.length : 0,
        detailReceiptMessages: Array.isArray(detail.receiptRecords)
          ? detail.receiptRecords.map((record) => record.receiptMessage).filter(Boolean)
          : [],
        deletedInvoice: false,
        cleanedClientRoot: false,
      };

      cleanupDeleted = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
      result.deletedInvoice = cleanupDeleted;
      cleanedClientRoot = tryRemoveDirectory(smokeRoot);
      result.cleanedClientRoot = cleanedClientRoot;
    } finally {
      if (!cleanedClientRoot) {
        cleanedClientRoot = tryRemoveDirectory(smokeRoot);
        if (result) {
          result.cleanedClientRoot = cleanedClientRoot;
        }
      }

      if (invoice?.id && !cleanupDeleted) {
        cleanupDeleted = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
        if (result) {
          result.deletedInvoice = cleanupDeleted;
        }
      }
    }

    return result;
  }

  async function waitForSingleWindowOperationCenterAgentConsignmentCheck(page, options, accessToken, tokenType, timeoutMs) {
    const timestamp = Date.now();
    const smokeRoot = path.join(options.userDataDir, `single-window-operation-center-acd-${timestamp}`);
    const clientRoot = path.join(smokeRoot, "ClientRoot");
    const outBoxPath = path.join(clientRoot, "OutBox");
    const inBoxPath = path.join(clientRoot, "InBox");
    const submitPackagePath = path.join(smokeRoot, `acd-submit-package-${timestamp}.swpkg`);
    const receiptPackagePath = path.join(smokeRoot, `acd-receipt-package-${timestamp}.swpkg`);
    let invoice = null;
    let cleanupDeleted = false;
    let cleanedClientRoot = false;
    let result = null;

    try {
      mkdirSync(inBoxPath, { recursive: true });
      invoice = await createSmokeInvoice(options, accessToken, tokenType);
      const submitPackage = await exportSmokeAgentConsignmentSubmitPackage(
        options,
        accessToken,
        tokenType,
        invoice.id,
        submitPackagePath,
      );

      const batchId = submitPackage.trackingBatchId;
      const batchReference = submitPackage.manifest?.batchReference ?? "";
      if (!batchId || !batchReference) {
        throw new Error(`Single Window ACD submit package response did not include trackingBatchId/batchReference: ${JSON.stringify(submitPackage)}`);
      }

      const checkUrl = buildSingleWindowOperationCenterCheckUrl(options.webUrl);
      await page.send("Page.navigate", { url: checkUrl });
      await waitForRuntimeDiagnostics(page, ["操作中心", "提交包导入", "批次快捷操作"], timeoutMs);

      await evaluate(
        page,
        `(() => {
          const input = document.querySelector('input[aria-label="搜索单一窗口批次"]');
          if (!input) {
            throw new Error('Operation center search input was not found for ACD smoke.');
          }

          const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
          setter.call(input, ${JSON.stringify(invoice.invoiceNo)});
          input.dispatchEvent(new Event('input', { bubbles: true }));
          const form = input.closest('form');
          if (form) {
            form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
          }
          return true;
        })()`,
        true,
      );

      const rowReady = await waitForPageExpression(
        page,
        `(() => {
          const rows = Array.from(document.querySelectorAll('.single-window-operation-table tbody tr'));
          const row = rows.find((candidate) =>
            (candidate.innerText || '').includes(${JSON.stringify(invoice.invoiceNo)}) &&
            (candidate.innerText || '').includes(${JSON.stringify(batchReference)}) &&
            ((candidate.innerText || '').includes('报关代理委托') || (candidate.innerText || '').includes('AgentConsignment')));
          if (!row) {
            return false;
          }

          row.click();
          return true;
        })()`,
        timeoutMs,
        `Timed out waiting for operation center ACD smoke batch row: ${invoice.invoiceNo} / ${batchReference}`,
      );

      const actionPanelReady = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const text = section ? section.innerText || '' : '';
          return Boolean(section &&
            text.includes(${JSON.stringify(batchReference)}) &&
            text.includes(${JSON.stringify(invoice.invoiceNo)}) &&
            text.includes('保存目录根') &&
            text.includes('发送到 OutBox') &&
            text.includes('打包并导入'));
        })()`,
        timeoutMs,
        "Timed out waiting for operation center ACD list action panel.",
      );

      await waitForPageExpression(
        page,
        `(() => {
          window.__exportDocManagerSmokeDirectoryPath = ${JSON.stringify(clientRoot)};
          window.__exportDocManagerSmokeTauriInvocations = [];
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.title || '').includes('选择业务目录根')) : null;
          if (!button || button.disabled) {
            return false;
          }

          button.click();
          return true;
        })()`,
        timeoutMs,
        "Timed out waiting for operation center ACD directory picker button to become available.",
      );

      const directoryPicked = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const input = section ? section.querySelector('.path-field input') : null;
          const invocations = window.__exportDocManagerSmokeTauriInvocations || [];
          return Boolean(input &&
            input.value === ${JSON.stringify(clientRoot)} &&
            invocations.some((entry) => entry.command === 'select_directory'));
        })()`,
        timeoutMs,
        "Timed out waiting for operation center ACD directory picker.",
      );

      await evaluate(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.innerText || '').includes('保存目录根')) : null;
          if (!button || button.disabled) {
            throw new Error('Operation center ACD save directory root button is not available.');
          }

          button.click();
          return true;
        })()`,
        true,
      );

      const savedProfile = await waitFor(async () => {
        const candidate = await getSingleWindowClientProfile(options, accessToken, tokenType);
        return singleWindowProfileContainsPath(candidate.profile, clientRoot) ? candidate : null;
      }, timeoutMs, "Timed out waiting for operation center ACD client root to persist in the API profile.");

      await evaluate(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.innerText || '').includes('发送到 OutBox')) : null;
          if (!button || button.disabled) {
            throw new Error('Operation center ACD dispatch button is not available.');
          }

          button.click();
          return true;
        })()`,
        true,
      );

      const dispatchUi = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const text = section ? section.innerText || '' : '';
          return Boolean(text.includes('当前批次已发送到默认导入目录') &&
            text.includes('目标目录') &&
            text.includes('报文数'));
        })()`,
        timeoutMs,
        "Timed out waiting for operation center ACD dispatch UI result.",
      );

      const outBoxFiles = await waitFor(async () => {
        if (!existsSync(outBoxPath)) {
          return null;
        }

        const files = readdirSync(outBoxPath)
          .filter((fileName) => fileName.toLowerCase().endsWith(".xml"))
          .map((fileName) => path.join(outBoxPath, fileName));
        return files.length > 0 ? files : null;
      }, timeoutMs, `Timed out waiting for dispatched ACD Single Window XML files in ${outBoxPath}.`);

      const receiptFilePath = path.join(inBoxPath, `Successed_${batchReference}_${invoice.invoiceNo}.xml`);
      writeFileSync(receiptFilePath, buildSmokeAgentConsignmentReceiptXml(batchReference), "utf8");

      await evaluate(
        page,
        `(() => {
          window.__exportDocManagerSmokeSavePackagePath = ${JSON.stringify(receiptPackagePath)};
          window.__exportDocManagerSmokeTauriInvocations = [];
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.innerText || '').includes('打包并导入')) : null;
          if (!button || button.disabled) {
            throw new Error('Operation center ACD receipt package import button is not available.');
          }

          button.click();
          return true;
        })()`,
        true,
      );

      const autoReceiptUi = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const text = section ? section.innerText || '' : '';
          return Boolean(
            text.includes('回执包已导出并导入') &&
            text.includes('代理委托导入响应') &&
            text.includes('Smoke ACD accepted') &&
            text.includes('写入回执'));
        })()`,
        timeoutMs,
        "Timed out waiting for operation center ACD receipt package import result.",
      );

      const packageFile = await waitFor(async () => {
        if (!existsSync(receiptPackagePath)) {
          return null;
        }

        const size = statSync(receiptPackagePath).size;
        const header = size > 0 ? readFileSync(receiptPackagePath).subarray(0, 2).toString("ascii") : "";
        return header === "PK" ? { size, header } : null;
      }, timeoutMs, `Timed out waiting for ACD receipt package file: ${receiptPackagePath}`);

      const detail = await waitFor(async () => {
        const candidate = await getSingleWindowBatchDetail(options, accessToken, tokenType, batchId);
        const receiptRecords = Array.isArray(candidate.receiptRecords) ? candidate.receiptRecords : [];
        return normalizePathForCompare(candidate.lastReceiptPackagePath) === normalizePathForCompare(receiptPackagePath) &&
          candidate.status === "Accepted" &&
          receiptRecords.some((record) => String(record.receiptMessage || "").includes("Smoke ACD accepted"))
          ? candidate
          : null;
      }, timeoutMs, "Timed out waiting for operation center ACD detail to record imported receipt package and receipt log.");

      const submitPackageHeader = existsSync(submitPackagePath)
        ? readFileSync(submitPackagePath).subarray(0, 2).toString("ascii")
        : "";
      result = {
        invoiceId: invoice.id,
        invoiceNo: invoice.invoiceNo,
        batchId,
        batchReference,
        businessType: "AgentConsignment",
        url: redactDesktopAccessToken(checkUrl),
        submitPackagePath,
        submitPackageHeader,
        clientRoot,
        outBoxPath,
        outBoxXmlCount: outBoxFiles.length,
        dispatchedXmlFiles: outBoxFiles.map((filePath) => path.basename(filePath)),
        receiptFilePath,
        receiptPackagePath,
        receiptPackageHeader: packageFile.header,
        receiptPackageSize: packageFile.size,
        rowReady: Boolean(rowReady?.found),
        actionPanelReady: Boolean(actionPanelReady?.found),
        directoryPicked: Boolean(directoryPicked?.found),
        savedProfile: singleWindowProfileContainsPath(savedProfile?.profile, clientRoot),
        dispatchUi: Boolean(dispatchUi?.found),
        autoReceiptUi: Boolean(autoReceiptUi?.found),
        detailStatus: detail.status,
        detailLastReceiptPackagePath: detail.lastReceiptPackagePath,
        detailClientDispatchPath: detail.clientDispatchPath,
        detailPackageRecordCount: Array.isArray(detail.packageRecords) ? detail.packageRecords.length : 0,
        detailReceiptRecordCount: Array.isArray(detail.receiptRecords) ? detail.receiptRecords.length : 0,
        detailReceiptMessages: Array.isArray(detail.receiptRecords)
          ? detail.receiptRecords.map((record) => record.receiptMessage).filter(Boolean)
          : [],
        deletedInvoice: false,
        cleanedClientRoot: false,
      };

      cleanupDeleted = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
      result.deletedInvoice = cleanupDeleted;
      cleanedClientRoot = tryRemoveDirectory(smokeRoot);
      result.cleanedClientRoot = cleanedClientRoot;
    } finally {
      if (!cleanedClientRoot) {
        cleanedClientRoot = tryRemoveDirectory(smokeRoot);
        if (result) {
          result.cleanedClientRoot = cleanedClientRoot;
        }
      }

      if (invoice?.id && !cleanupDeleted) {
        cleanupDeleted = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
        if (result) {
          result.deletedInvoice = cleanupDeleted;
        }
      }
    }

    return result;
  }

  async function exportSmokeCustomsCooSubmitPackage(options, accessToken, tokenType, invoiceId, packagePath) {
    const response = await fetch(new URL(`/api/single-window/coo/${encodeURIComponent(String(invoiceId))}/submit-package`, ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify({ packagePath }),
    });

    if (!response.ok) {
      throw new Error(`Single Window submit package smoke export failed with HTTP ${response.status}: ${await response.text()}`);
    }

    const payload = await response.json();
    if (!payload?.success || !payload?.trackingBatchId || !payload?.manifest?.batchReference) {
      throw new Error(`Single Window submit package response did not include success/trackingBatchId/manifest: ${JSON.stringify(payload)}`);
    }

    return payload;
  }

  async function exportSmokeAgentConsignmentSubmitPackage(options, accessToken, tokenType, invoiceId, packagePath) {
    const response = await fetch(new URL(`/api/single-window/acd/${encodeURIComponent(String(invoiceId))}/submit-package`, ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify({ packagePath }),
    });

    if (!response.ok) {
      throw new Error(`Single Window ACD submit package smoke export failed with HTTP ${response.status}: ${await response.text()}`);
    }

    const payload = await response.json();
    if (!payload?.success || !payload?.trackingBatchId || !payload?.manifest?.batchReference) {
      throw new Error(`Single Window ACD submit package response did not include success/trackingBatchId/manifest: ${JSON.stringify(payload)}`);
    }

    return payload;
  }

  async function getSingleWindowClientProfile(options, accessToken, tokenType) {
    const response = await fetch(new URL("/api/single-window/client-profile/default", ensureTrailingSlash(options.apiBaseUrl)), {
      headers: authorizedHeaders(options, accessToken, tokenType),
    });

    if (!response.ok) {
      throw new Error(`Single Window client profile failed with HTTP ${response.status}: ${await response.text()}`);
    }

    return response.json();
  }

  function singleWindowProfileContainsPath(profile, expectedPath) {
    const expected = normalizePathForCompare(expectedPath).toLowerCase();
    if (!profile || !expected) {
      return false;
    }

    const candidates = [
      profile.importRootPath,
      profile.receiptRootPath,
    ];
    try {
      const overrides = JSON.parse(profile.businessDirectoryOverridesJson || "{}");
      for (const item of overrides.businesses ?? overrides.Businesses ?? []) {
        candidates.push(item.importRootPath ?? item.ImportRootPath ?? "");
        candidates.push(item.receiptRootPath ?? item.ReceiptRootPath ?? "");
      }
    } catch {
      candidates.push(profile.businessDirectoryOverridesJson);
    }

    return candidates.some((candidate) => normalizePathForCompare(candidate).toLowerCase().includes(expected));
  }

  function buildSingleWindowOperationCenterCheckUrl(webUrl) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeSingleWindowOperationCenter", "1");
    url.hash = "/single-window/operation-center";
    return url.toString();
  }

  return { run: waitForSingleWindowOperationCenterCheck };
}
