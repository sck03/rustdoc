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
    desktopAccessHeaders,
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
    if (!options.singleWindowOperationCenterCheck) return null;

    const customsCoo = await runBusinessCheck(page, options, accessToken, tokenType, timeoutMs, {
      key: "coo",
      displayName: "海关原产地证",
      minimumProfileCount: 1,
      expectedStatus: "Approved",
      expectedReceiptMessage: "Smoke approved",
      buildReceiptXml: buildSmokeCustomsCooReceiptXml,
      exportSubmitPackage: exportSmokeCustomsCooSubmitPackage,
    });
    const agentConsignment = await runBusinessCheck(page, options, accessToken, tokenType, timeoutMs, {
      key: "acd",
      displayName: "报关代理委托",
      minimumProfileCount: 2,
      expectedStatus: "Accepted",
      expectedReceiptMessage: "Smoke ACD accepted",
      buildReceiptXml: buildSmokeAgentConsignmentReceiptXml,
      exportSubmitPackage: exportSmokeAgentConsignmentSubmitPackage,
    });

    return {
      ...customsCoo,
      customsCoo,
      agentConsignment,
      allBusinessesSucceeded: Boolean(
        customsCoo?.detailStatus === "Approved" &&
        agentConsignment?.detailStatus === "Accepted" &&
        customsCoo?.detailReceiptRecordCount > 0 &&
        agentConsignment?.detailReceiptRecordCount > 0 &&
        customsCoo?.activeStationProfileCount === 1 &&
        agentConsignment?.activeStationProfileCount === 1 &&
        agentConsignment?.stationProfileCount >= 2 &&
        customsCoo?.companyScope !== agentConsignment?.companyScope &&
        customsCoo?.activeCardIdentifier !== agentConsignment?.activeCardIdentifier,
      ),
    };
  }

  async function runBusinessCheck(page, options, accessToken, tokenType, timeoutMs, definition) {
    const timestamp = Date.now();
    const smokeRoot = path.join(options.userDataDir, `single-window-operation-center-${definition.key}-${timestamp}`);
    const clientRoot = path.join(smokeRoot, "OfficialClient");
    const outBoxPath = path.join(clientRoot, "OutBox");
    const inBoxPath = path.join(clientRoot, "InBox");
    const submitPackagePath = path.join(smokeRoot, `${definition.key}-submit-${timestamp}.swpkg`);
    const receiptPackagePath = path.join(smokeRoot, `${definition.key}-receipt-${timestamp}.swpkg`);
    let invoice = null;
    let operatorUserId = 0;
    let operatorDeleted = false;
    let cleanupDeleted = false;
    let cleanedClientRoot = false;
    let result = null;

    try {
      mkdirSync(inBoxPath, { recursive: true });
      const companyScope = `Smoke Company ${definition.key.toUpperCase()} ${timestamp}`;
      const operator = await createScopedSmokeUser(
        options,
        accessToken,
        tokenType,
        definition,
        companyScope,
        timestamp,
      );
      operatorUserId = operator.userId;
      const operatorLogin = await loginScopedSmokeUser(options, operator.username, operator.password);
      invoice = await createSmokeInvoice(options, operatorLogin.accessToken, operatorLogin.tokenType);
      const submitPackage = await definition.exportSubmitPackage(
        options,
        operatorLogin.accessToken,
        operatorLogin.tokenType,
        invoice.id,
        submitPackagePath,
      );
      const batchId = submitPackage.trackingBatchId;
      const batchReference = submitPackage.manifest?.batchReference ?? "";
      if (!batchId || !batchReference) {
        throw new Error(`Single Window submit package response did not include trackingBatchId/batchReference: ${JSON.stringify(submitPackage)}`);
      }

      const profileResponse = await saveStationProfile(
        options,
        accessToken,
        tokenType,
        definition,
        submitPackage.manifest?.companyScope ?? "",
        clientRoot,
        timestamp,
      );
      const stationProfiles = Array.isArray(profileResponse.profiles) ? profileResponse.profiles : [];
      const activeProfiles = stationProfiles.filter((profile) => profile.isActive);
      const activeProfile = activeProfiles[0] ?? null;
      if (stationProfiles.length < definition.minimumProfileCount ||
          activeProfiles.length !== 1 ||
          !activeProfile ||
          !singleWindowProfileContainsPath(activeProfile, clientRoot)) {
        throw new Error(`Single Window station profile was not saved for ${clientRoot}: ${JSON.stringify(profileResponse)}`);
      }

      const checkUrl = buildSingleWindowOperationCenterCheckUrl(options.webUrl, timestamp);
      await page.send("Page.navigate", { url: checkUrl });
      await waitForRuntimeDiagnostics(page, ["操作中心", "公司与操作卡档案", "导入待办提交包"], timeoutMs);

      const profileReady = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="本机持卡机操作档案"]');
          const selector = section?.querySelector('select[aria-label="选择操作档案"]');
          const inputs = section ? Array.from(section.querySelectorAll('input')) : [];
          const inputValues = inputs.map((input) => input.value);
          return Boolean(section &&
            selector?.value === ${JSON.stringify(activeProfile.profileKey)} &&
            inputValues.includes(${JSON.stringify(activeProfile.profileName)}) &&
            inputValues.includes(${JSON.stringify(activeProfile.companyScope)}) &&
            inputValues.includes(${JSON.stringify(activeProfile.cardIdentifier)}));
        })()`,
        timeoutMs,
        "Timed out waiting for the active Single Window station profile.",
      );

      await waitForPageExpression(
        page,
        `(() => {
          window.__exportDocManagerSmokeSingleWindowPackagePath = ${JSON.stringify(submitPackagePath)};
          window.__exportDocManagerSmokeTauriInvocations = [];
          const section = document.querySelector('[aria-label="持卡机提交包导入"]');
          const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.title || '').includes('选择提交包')) : null;
          if (!button || button.disabled) return false;
          button.click();
          return true;
        })()`,
        timeoutMs,
        "Timed out waiting for the submit-package picker.",
      );

      const packagePicked = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="持卡机提交包导入"]');
          const input = section?.querySelector('.path-field input');
          return Boolean(input && input.value === ${JSON.stringify(submitPackagePath)});
        })()`,
        timeoutMs,
        "Timed out waiting for the submit package path to be selected.",
      );

      await evaluate(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="持卡机提交包导入"]');
          const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.innerText || '').includes('导入并绑定当前档案')) : null;
          if (!button || button.disabled) throw new Error('Submit-package import button is unavailable.');
          button.click();
          return true;
        })()`,
        true,
      );

      const submitImportUi = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="持卡机提交包导入"]');
          const text = section?.innerText || '';
          return Boolean(text.includes(${JSON.stringify(invoice.invoiceNo)}) && text.includes(${JSON.stringify(activeProfile.companyScope)}));
        })()`,
        timeoutMs,
        "Timed out waiting for the imported submit-package summary.",
      );

      await setOperationCenterSearch(page, invoice.invoiceNo);
      const rowReady = await waitForPageExpression(
        page,
        `(() => {
          const rows = Array.from(document.querySelectorAll('.single-window-operation-table tbody tr'));
          const row = rows.find((candidate) =>
            (candidate.innerText || '').includes(${JSON.stringify(invoice.invoiceNo)}) &&
            (candidate.innerText || '').includes(${JSON.stringify(batchReference)}) &&
            (candidate.innerText || '').includes(${JSON.stringify(definition.displayName)}));
          if (!row) return false;
          row.click();
          return true;
        })()`,
        timeoutMs,
        `Timed out waiting for operation center batch row: ${invoice.invoiceNo} / ${batchReference}`,
      );

      const actionPanelReady = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const text = section?.innerText || '';
          const dispatchButton = section
            ? Array.from(section.querySelectorAll('button')).find((element) => (element.innerText || '').includes('写入交接 OutBox'))
            : null;
          return Boolean(section &&
            text.includes(${JSON.stringify(batchReference)}) &&
            text.includes('写入交接 OutBox') &&
            text.includes('收集并导出回执') &&
            text.includes(${JSON.stringify(activeProfile.cardIdentifier)}) &&
            dispatchButton &&
            !dispatchButton.disabled);
        })()`,
        timeoutMs,
        "Timed out waiting for the operation center action panel and enabled dispatch action.",
      );

      await evaluate(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.innerText || '').includes('写入交接 OutBox')) : null;
          if (!button || button.disabled) throw new Error('Dispatch button is unavailable.');
          button.click();
          return true;
        })()`,
        true,
      );

      const dispatchUi = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const text = section?.innerText || '';
          return Boolean(text.includes('这不代表官方客户端已导入') && text.includes('已写入文件'));
        })()`,
        timeoutMs,
        "Timed out waiting for the dispatch result.",
      );

      const receiptActionReady = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const text = section?.innerText || '';
          const button = section
            ? Array.from(section.querySelectorAll('button')).find((element) => (element.innerText || '').includes('收集并导出回执'))
            : null;
          return Boolean(text.includes('已送入导入目录') && button && !button.disabled);
        })()`,
        timeoutMs,
        "Timed out waiting for the dispatched batch to become eligible for receipt collection.",
      );

      const outBoxFiles = await waitFor(async () => {
        if (!existsSync(outBoxPath)) return null;
        const files = readdirSync(outBoxPath)
          .filter((fileName) => fileName.toLowerCase().endsWith(".xml"))
          .map((fileName) => path.join(outBoxPath, fileName));
        return files.length > 0 ? files : null;
      }, timeoutMs, `Timed out waiting for dispatched XML files in ${outBoxPath}.`);

      const receiptFilePath = path.join(inBoxPath, `Successed_${batchReference}_${invoice.invoiceNo}.xml`);
      writeFileSync(receiptFilePath, definition.buildReceiptXml(batchReference), "utf8");

      await evaluate(
        page,
        `(() => {
          window.__exportDocManagerSmokeSavePackagePath = ${JSON.stringify(receiptPackagePath)};
          window.__exportDocManagerSmokeTauriInvocations = [];
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.innerText || '').includes('收集并导出回执')) : null;
          if (!button || button.disabled) throw new Error('Receipt export button is unavailable.');
          button.click();
          return true;
        })()`,
        true,
      );

      const receiptExportUi = await waitForPageExpression(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="选中批次快捷操作"]');
          const text = section?.innerText || '';
          return Boolean(text.includes('回执包已导出') && text.includes('已收集回执'));
        })()`,
        timeoutMs,
        "Timed out waiting for the receipt-package export result.",
      );

      const packageFile = await waitFor(async () => {
        if (!existsSync(receiptPackagePath)) return null;
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
          return invocations.some((entry) => entry?.command === 'select_save_package_path' && normalize(window.__exportDocManagerSmokeSavePackagePath) === expected);
        })()`,
        timeoutMs,
        "Timed out waiting for select_save_package_path invocation.",
      );

      await importReceiptPackage(options, accessToken, tokenType, receiptPackagePath);
      const detail = await waitFor(async () => {
        const candidate = await getSingleWindowBatchDetail(options, accessToken, tokenType, batchId);
        const receiptRecords = Array.isArray(candidate.receiptRecords) ? candidate.receiptRecords : [];
        return candidate.status === definition.expectedStatus &&
          receiptRecords.some((record) => String(record.receiptMessage || "").includes(definition.expectedReceiptMessage))
          ? candidate
          : null;
      }, timeoutMs, "Timed out waiting for the imported receipt to update the operation center detail.");

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
        profileReady: Boolean(profileReady?.found),
        packagePicked: Boolean(packagePicked?.found),
        submitImportUi: Boolean(submitImportUi?.found),
        rowReady: Boolean(rowReady?.found),
        actionPanelReady: Boolean(actionPanelReady?.found),
        savedProfile: true,
        stationProfileCount: stationProfiles.length,
        activeStationProfileCount: activeProfiles.length,
        activeStationProfileKey: activeProfile.profileKey,
        activeCardIdentifier: activeProfile.cardIdentifier,
        dispatchUi: Boolean(dispatchUi?.found),
        receiptActionReady: Boolean(receiptActionReady?.found),
        receiptExportUi: Boolean(receiptExportUi?.found),
        savePackageInvocation: Boolean(savePackageInvocation?.found),
        detailStatus: detail.status,
        detailPackageRecordCount: Array.isArray(detail.packageRecords) ? detail.packageRecords.length : 0,
        detailReceiptRecordCount: Array.isArray(detail.receiptRecords) ? detail.receiptRecords.length : 0,
        detailReceiptMessages: Array.isArray(detail.receiptRecords)
          ? detail.receiptRecords.map((record) => record.receiptMessage).filter(Boolean)
          : [],
        companyScope,
        operatorUsername: operator.username,
        deletedOperator: false,
        deletedInvoice: false,
        cleanedClientRoot: false,
      };

      cleanupDeleted = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
      result.deletedInvoice = cleanupDeleted;
      operatorDeleted = await deleteScopedSmokeUser(options, accessToken, tokenType, operatorUserId).catch(() => false);
      result.deletedOperator = operatorDeleted;
      cleanedClientRoot = tryRemoveDirectory(smokeRoot);
      result.cleanedClientRoot = cleanedClientRoot;
    } finally {
      if (!cleanedClientRoot) {
        cleanedClientRoot = tryRemoveDirectory(smokeRoot);
        if (result) result.cleanedClientRoot = cleanedClientRoot;
      }
      if (invoice?.id && !cleanupDeleted) {
        cleanupDeleted = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
        if (result) result.deletedInvoice = cleanupDeleted;
      }
      if (operatorUserId && !operatorDeleted) {
        operatorDeleted = await deleteScopedSmokeUser(options, accessToken, tokenType, operatorUserId).catch(() => false);
        if (result) result.deletedOperator = operatorDeleted;
      }
    }

    return result;
  }

  async function setOperationCenterSearch(page, invoiceNo) {
    await evaluate(
      page,
      `(() => {
        const input = document.querySelector('input[aria-label="搜索单一窗口批次"]');
        if (!input) throw new Error('Operation center search input was not found.');
        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        setter.call(input, ${JSON.stringify(invoiceNo)});
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.closest('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
        return true;
      })()`,
      true,
    );
  }

  async function saveStationProfile(options, accessToken, tokenType, definition, companyScope, clientRoot, timestamp) {
    const response = await fetch(new URL("/api/single-window/client-profiles", ensureTrailingSlash(options.apiBaseUrl)), {
      method: "PUT",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify({
        profileKey: "",
        profileName: `Smoke ${definition.displayName} ${timestamp}`,
        companyScope,
        cardIdentifier: `SMOKE-${definition.key.toUpperCase()}-${timestamp}`,
        customsCooClientRootPath: definition.key === "coo" ? clientRoot : "",
        agentConsignmentClientRootPath: definition.key === "acd" ? clientRoot : "",
        canSubmitCustomsCoo: definition.key === "coo",
        canSubmitAgentConsignment: definition.key === "acd",
      }),
    });
    if (!response.ok) {
      throw new Error(`Single Window station profile save failed with HTTP ${response.status}: ${await response.text()}`);
    }
    return response.json();
  }

  async function createScopedSmokeUser(options, accessToken, tokenType, definition, companyScope, timestamp) {
    const username = `sw-${definition.key}-${timestamp}`;
    const password = `Sw-${definition.key}-${timestamp}!`;
    const response = await fetch(new URL("/api/users", ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify({
        username,
        fullName: `Single Window ${definition.displayName} Smoke`,
        role: "Admin",
        departmentId: "SW-SMOKE",
        companyScope,
        isActive: true,
        resetPassword: password,
      }),
    });
    if (!response.ok) {
      throw new Error(`Single Window scoped smoke user create failed with HTTP ${response.status}: ${await response.text()}`);
    }

    const payload = await response.json();
    const userId = Number(payload?.user?.id || 0);
    if (!userId) {
      throw new Error(`Single Window scoped smoke user response is incomplete: ${JSON.stringify(payload)}`);
    }

    return { userId, username, password };
  }

  async function loginScopedSmokeUser(options, username, password) {
    const response = await fetch(new URL("/api/auth/login", ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: { "Content-Type": "application/json", ...desktopAccessHeaders(options) },
      body: JSON.stringify({ username, password }),
    });
    if (!response.ok) {
      throw new Error(`Single Window scoped smoke user login failed with HTTP ${response.status}: ${await response.text()}`);
    }

    const payload = await response.json();
    if (!payload?.accessToken) {
      throw new Error(`Single Window scoped smoke login response is incomplete: ${JSON.stringify(payload)}`);
    }

    return { accessToken: payload.accessToken, tokenType: payload.tokenType || "Bearer" };
  }

  async function deleteScopedSmokeUser(options, accessToken, tokenType, userId) {
    const response = await fetch(new URL(`/api/users/${encodeURIComponent(String(userId))}`, ensureTrailingSlash(options.apiBaseUrl)), {
      method: "DELETE",
      headers: authorizedHeaders(options, accessToken, tokenType),
    });
    return response.ok || response.status === 404;
  }

  async function importReceiptPackage(options, accessToken, tokenType, packagePath) {
    const response = await fetch(new URL("/api/single-window/receipts/import", ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify({ packagePath, workingDirectory: "", keepWorkingDirectory: false }),
    });
    if (!response.ok) {
      throw new Error(`Single Window receipt package import failed with HTTP ${response.status}: ${await response.text()}`);
    }
    return response.json();
  }

  async function exportSmokeCustomsCooSubmitPackage(options, accessToken, tokenType, invoiceId, packagePath) {
    return exportSubmitPackage(options, accessToken, tokenType, invoiceId, packagePath, "coo");
  }

  async function exportSmokeAgentConsignmentSubmitPackage(options, accessToken, tokenType, invoiceId, packagePath) {
    return exportSubmitPackage(options, accessToken, tokenType, invoiceId, packagePath, "acd");
  }

  async function exportSubmitPackage(options, accessToken, tokenType, invoiceId, packagePath, route) {
    const response = await fetch(new URL(`/api/single-window/${route}/${encodeURIComponent(String(invoiceId))}/submit-package/save-to-path`, ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify({ packagePath }),
    });
    if (!response.ok) {
      throw new Error(`Single Window ${route.toUpperCase()} submit package export failed with HTTP ${response.status}: ${await response.text()}`);
    }
    const payload = await response.json();
    if (!payload?.success || !payload?.trackingBatchId || !payload?.manifest?.batchReference) {
      throw new Error(`Single Window submit package response is incomplete: ${JSON.stringify(payload)}`);
    }
    return payload;
  }

  function singleWindowProfileContainsPath(profile, expectedPath) {
    const expected = normalizePathForCompare(expectedPath).toLowerCase();
    return Boolean(profile && expected && [
      profile.customsCooClientRootPath,
      profile.agentConsignmentClientRootPath,
    ].some((candidate) => normalizePathForCompare(candidate).toLowerCase() === expected));
  }

  function buildSingleWindowOperationCenterCheckUrl(webUrl, smokeRunId) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeSingleWindowOperationCenter", "1");
    url.searchParams.set("smokeSingleWindowRun", String(smokeRunId));
    url.hash = "/single-window/operation-center";
    return url.toString();
  }

  return { run: waitForSingleWindowOperationCenterCheck };
}
