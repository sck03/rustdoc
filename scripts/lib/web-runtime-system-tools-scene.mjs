import { createHash } from "node:crypto";
import { existsSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";

export function createSystemToolsSmokeScene(runtime) {
  const {
    authorizedHeaders,
    authorizedJsonHeaders,
    cleanupSmokeDirectory,
    cleanupSmokeFile,
    ensureTrailingSlash,
    evaluate,
    fetchJson,
    includesText,
    readFileSize,
    redactDesktopAccessToken,
    waitFor,
    waitForPageExpression,
    waitForRuntimeDiagnostics,
  } = runtime;

  async function run(page, options, accessToken, tokenType, timeoutMs) {
    const updateCheck = await waitForUpdateCheck(page, options, accessToken, tokenType, timeoutMs);
    const smartOcrCheck = await waitForSmartOcrCheck(page, options, accessToken, tokenType, timeoutMs);
    const exchangeRateCheck = await waitForExchangeRateCheck(page, options, timeoutMs);
    const emailCheck = await waitForEmailCheck(page, options, timeoutMs);
    const auditLogCheck = await waitForAuditLogCheck(page, options, accessToken, tokenType, timeoutMs);
    const licenseCheck = await waitForLicenseCheck(page, options, timeoutMs);

    return {
      updateCheck,
      smartOcrCheck,
      exchangeRateCheck,
      emailCheck,
      auditLogCheck,
      licenseCheck,
    };
  }

  async function waitForUpdateCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.updateCheck) {
      return null;
    }

    const updateSource = options.updateStageCheck
      ? createSmokeUpdateSource(options)
      : null;
    let cleanedUpdateSource = false;
    let result = null;

    try {
    const checkUrl = buildUpdateCheckUrl(options.webUrl);
    await page.send("Page.navigate", { url: checkUrl });
    const expectedText = [
      "软件更新",
      "检查并安装新版本",
      "更新源配置",
      "当前版本",
      "最新版本",
      "目标平台",
      "更新日志",
      "下载地址",
      "检查更新",
      "下载并安装",
    ];

    const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
    const updatePageCheck = await waitForPageExpression(
      page,
      `(() => {
        const page = document.querySelector('[aria-label="软件更新"]');
        return Boolean(page &&
          page.querySelector('[aria-label="更新状态"] .update-center-detail-grid') &&
          page.querySelector('[aria-label="更新日志"]') &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.innerText || '').includes('检查更新')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.innerText || '').includes('下载并安装')));
      })()`,
      timeoutMs,
      "Timed out waiting for the update center page.",
    );

      if (updateSource && options.mockTauriRuntimeContext) {
        await evaluate(
          page,
          `((payload) => {
            window.__exportDocManagerSmokeTauriUpdateResult = payload;
            return true;
          })(${JSON.stringify({
            supported: true,
            configured: true,
            updateAvailable: true,
            currentVersion: "0.1.0",
            latestVersion: updateSource.version,
            target: "windows-x86_64-nsis",
            downloadUrl: updateSource.packagePath,
            body: updateSource.marker,
            date: new Date().toISOString(),
            statusText: "Tauri updater 发现可安装的新版本。",
            errorMessage: "",
            storagePolicy: "多平台安装由 Tauri updater 插件处理；业务数据库、授权文件和运行数据仍留在运行目录 App_Data。",
          })})`,
          true,
        );
      }

      const updateStageCheck = options.updateStageCheck
        ? await waitForUpdateStageCheck(page, options, updateSource, timeoutMs)
        : null;

      result = {
      url: redactDesktopAccessToken(checkUrl),
      expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
      updatePageCheck,
        updateStageCheck,
      mandatoryStartupCheck: options.updateMandatoryCheck ? { skipped: true, reason: "旧 API 启动强制更新门禁已移除，更新改由 Tauri updater 主线处理。" } : null,
      textExcerpt: pageText.slice(0, 1200),
    };
    } finally {
      if (updateSource?.sourceRoot && existsSync(updateSource.sourceRoot)) {
        cleanedUpdateSource = cleanupSmokeDirectory(updateSource.sourceRoot, options.userDataDir);
      }

      if (result?.updateStageCheck) {
        result.updateStageCheck.cleanedUpdateSource = cleanedUpdateSource;
      }
    }

    return result;
  }

  function createSmokeUpdateSource(options) {
    const suffix = `${Date.now()}-${Math.floor(Math.random() * 100000)}`;
    const sourceRoot = path.join(options.userDataDir, `update-stage-source-${suffix}`);
    mkdirSync(sourceRoot, { recursive: true });

    const version = "99.0.0.0";
    const marker = `Tauri update stage smoke ${suffix}`;
    const packageFileName = "package.zip";
    const packageContent = `fake package for ${marker}`;
    const packagePath = path.join(sourceRoot, packageFileName);
    writeFileSync(packagePath, packageContent, "utf8");

    const sha256 = createHash("sha256").update(packageContent, "utf8").digest("hex");
    const manifestPath = path.join(sourceRoot, "update.json");
    writeFileSync(
      manifestPath,
      JSON.stringify({
        version,
        notes: marker,
        pub_date: new Date().toISOString(),
        platforms: {
          "windows-x86_64-nsis": {
            signature: "smoke-signature",
            url: packageFileName,
          },
        },
      }, null, 2),
      "utf8",
    );

    return {
      sourceRoot,
      manifestPath,
      packagePath,
      packageFileName,
      packageContent,
      version,
      marker,
      sha256,
    };
  }


  async function waitForUpdateStageCheck(page, options, updateSource, timeoutMs) {
    const checkAction = await runUpdateCenterUiAction(page, "检查更新");
    const checkedState = await waitFor(async () => {
      const state = await readUpdateCenterState(page);
      const canInstall = state.buttons.some((button) =>
        includesText(button.text, "下载并安装") && !button.disabled);
      return state.statusDetails["最新版本"] === `v${updateSource.version}` &&
        state.statusDetails["更新可用"] === "是" &&
        includesText(state.statusDetails["下载地址"], updateSource.packagePath) &&
        includesText(state.releaseNotes, updateSource.marker) &&
        canInstall
        ? state
        : null;
    }, timeoutMs, `Timed out waiting for Tauri updater source to become available: ${updateSource.version}`);

    const installAction = options.mockTauriRuntimeContext
      ? await runUpdateCenterUiAction(page, "下载并安装")
      : null;
    const installedState = options.mockTauriRuntimeContext
      ? await waitFor(async () => {
        const state = await readUpdateCenterState(page);
        const invoked = state.invocations.some((item) => item.command === "install_tauri_update");
        return invoked && state.statusDetails["安装版本"] === `v${updateSource.version}` ? state : null;
      }, timeoutMs, "Timed out waiting for Tauri updater install invocation.")
      : checkedState;

    return {
      updateSource: {
        version: updateSource.version,
        marker: updateSource.marker,
        manifestPath: updateSource.manifestPath,
        packagePath: updateSource.packagePath,
        sha256: updateSource.sha256,
      },
      checkAction,
      checkedState,
      installAction,
      installedState,
    };
  }

  async function readUpdateCenterState(page) {
    const result = await evaluate(
      page,
      `(() => {
        const readDetails = (section) => {
          const details = {};
          for (const item of Array.from(section?.querySelectorAll('.detail-item') || [])) {
            const label = (item.querySelector('span')?.textContent || '').trim();
            const valueElement = item.querySelector('strong');
            const value = (valueElement?.getAttribute('title') || valueElement?.textContent || '').trim();
            if (label) {
              details[label] = value;
            }
          }
          return details;
        };
        const page = document.querySelector('[aria-label="软件更新"]');
        const statusSection = page?.querySelector('[aria-label="更新状态"]');
        return {
          alerts: Array.from(page?.querySelectorAll('.alert, .success-alert, .info-alert') || []).map((item) => item.innerText || ''),
          releaseNotes: page?.querySelector('[aria-label="更新日志"] .update-release-notes')?.innerText || '',
          statusDetails: readDetails(statusSection),
          invocations: window.__exportDocManagerSmokeTauriInvocations || [],
          buttons: Array.from(page?.querySelectorAll('button') || []).map((button) => ({
            title: button.title || '',
            text: button.innerText || button.textContent || '',
            disabled: Boolean(button.disabled)
          }))
        };
      })()`,
      true,
    );

    const value = result.value ?? {};
    return {
      alerts: Array.isArray(value.alerts) ? value.alerts : [],
      releaseNotes: String(value.releaseNotes ?? ""),
      statusDetails: value.statusDetails ?? {},
      invocations: Array.isArray(value.invocations) ? value.invocations : [],
      buttons: Array.isArray(value.buttons) ? value.buttons : [],
    };
  }

  async function runUpdateCenterUiAction(page, titlePart) {
    const result = await evaluate(
      page,
      `(async (payload) => {
        const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
        const page = document.querySelector('[aria-label="软件更新"]');
        if (!page) {
          throw new Error("软件更新页面未找到。");
        }

        const deadline = Date.now() + 10000;
        let latestReason = "";
        while (Date.now() < deadline) {
          const button = Array.from(page.querySelectorAll("button"))
            .find((item) => (item.title || item.innerText || item.textContent || "").includes(payload.titlePart));
          if (button && !button.disabled) {
            button.click();
            return { action: payload.titlePart, submitted: true };
          }

          latestReason = button ? "按钮仍不可用: " + payload.titlePart : "按钮未找到: " + payload.titlePart;
          await delay(100);
        }

        throw new Error(latestReason || "等待按钮超时: " + payload.titlePart);
      })(${JSON.stringify({ titlePart })})`,
      true,
    );

    return result.value ?? { action: titlePart, submitted: true };
  }


  async function waitForSmartOcrCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.smartOcrCheck) {
      return null;
    }

    const checkUrl = buildSmartOcrCheckUrl(options.webUrl);
    await page.send("Page.navigate", { url: checkUrl });
    const expectedText = [
      "智能 OCR",
      "图片路径",
      "图片预览",
      "识别结果",
      "未载入图片预览",
      "等待识别",
    ];

    const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
    const smartOcrPageCheck = await waitForPageExpression(
      page,
      `(() => {
        const page = document.querySelector('[aria-label="智能 OCR"]');
        return Boolean(page &&
          page.querySelector('[aria-label="图片预览"] .smart-ocr-preview-viewport') &&
          page.querySelector('[aria-label="识别结果"] textarea') &&
          page.querySelector('.smart-ocr-side-panel') &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('粘贴图片')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('放大')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('缩小')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('重置缩放')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('开始识别')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('复制文本')));
      })()`,
      timeoutMs,
      "Timed out waiting for the Smart OCR page.",
    );
    const realSampleCheck = await waitForSmartOcrRealSampleCheck(
      page,
      options,
      accessToken,
      tokenType,
      timeoutMs,
    );

    return {
      url: redactDesktopAccessToken(checkUrl),
      expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
      smartOcrPageCheck,
      realSampleCheck,
      textExcerpt: pageText.slice(0, 1200),
    };
  }

  async function waitForSmartOcrRealSampleCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.smartOcrRealSampleCheck) {
      return null;
    }

    const health = await fetchJson(new URL("/healthz", ensureTrailingSlash(options.apiBaseUrl)));
    const modelBundle = inspectPaddleOcrModelBundle(health.ocrModelRoot);
    if (!modelBundle.available) {
      return {
        skipped: true,
        reason: "PaddleOCR model bundle was not found under the program OCR model root.",
        modelRoot: health.ocrModelRoot ?? "",
        missingFiles: modelBundle.missingFiles,
      };
    }

    if (process.platform !== "win32") {
      return {
        skipped: true,
        reason: "Current API auto-enables PaddleOCR only on Windows; non-Windows native OCR runtime is still pending.",
        modelRoot: health.ocrModelRoot ?? "",
        missingFiles: [],
      };
    }

    const sample = await createSmartOcrRealSample(page);
    const startedAt = Date.now();
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), timeoutMs);
    let response;
    try {
      response = await fetch(new URL("/api/tools/ocr/recognize-image-content", ensureTrailingSlash(options.apiBaseUrl)), {
        method: "POST",
        headers: authorizedJsonHeaders(options, accessToken, tokenType),
        body: JSON.stringify({
          imageContentBase64: sample.imageContentBase64,
          sourceName: sample.sourceName,
          sourceMimeType: sample.sourceMimeType,
        }),
        signal: controller.signal,
      });
    } catch (error) {
      if (error?.name === "AbortError") {
        throw new Error(`Smart OCR real sample recognition timed out after ${timeoutMs} ms.`);
      }

      throw error;
    } finally {
      clearTimeout(timer);
    }

    const responseText = await response.text();
    if (!response.ok) {
      throw new Error(`Smart OCR real sample recognition failed with HTTP ${response.status}: ${responseText}`);
    }

    const result = JSON.parse(responseText);
    const fullText = readOcrFullText(result);
    const normalizedText = normalizeOcrTextForTokenMatch(fullText);
    const matchedTokens = sample.expectedTokens.filter((token) =>
      normalizedText.includes(normalizeOcrTextForTokenMatch(token)),
    );
    const requiredMatchedTokenCount = 3;
    if (matchedTokens.length < requiredMatchedTokenCount) {
      throw new Error([
        "Smart OCR real sample quality check did not meet the token threshold.",
        `Required: ${requiredMatchedTokenCount}/${sample.expectedTokens.length}`,
        `Expected tokens: ${sample.expectedTokens.join(", ")}`,
        `Matched tokens: ${matchedTokens.join(", ") || "<none>"}`,
        `Recognized text: ${fullText || "<empty>"}`,
      ].join("\n"));
    }

    return {
      skipped: false,
      modelRoot: health.ocrModelRoot ?? "",
      sourceName: sample.sourceName,
      sourceMimeType: sample.sourceMimeType,
      expectedTokens: sample.expectedTokens,
      matchedTokens,
      lineCount: Array.isArray(result.lines) ? result.lines.length : 0,
      durationMs: Date.now() - startedAt,
      fullText,
      storagePolicy: result.storagePolicy ?? "",
    };
  }

  async function createSmartOcrRealSample(page) {
    const result = await evaluate(
      page,
      `(() => {
        const canvas = document.createElement("canvas");
        canvas.width = 1200;
        canvas.height = 360;
        const ctx = canvas.getContext("2d");
        if (!ctx) {
          throw new Error("Canvas 2D context is unavailable.");
        }

        ctx.fillStyle = "#ffffff";
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        ctx.strokeStyle = "#111827";
        ctx.lineWidth = 3;
        ctx.strokeRect(44, 40, 1112, 280);
        ctx.fillStyle = "#111827";
        ctx.textBaseline = "alphabetic";
        ctx.font = "700 72px Arial, Microsoft YaHei, sans-serif";
        ctx.fillText("INVOICE NO ABC123", 82, 122);
        ctx.font = "500 56px Arial, Microsoft YaHei, sans-serif";
        ctx.fillText("TOTAL USD 456.78", 82, 215);
        ctx.fillText("CUSTOMER ACME EXPORT", 82, 292);

        const dataUrl = canvas.toDataURL("image/png");
        const separatorIndex = dataUrl.indexOf(",");
        if (separatorIndex < 0) {
          throw new Error("Canvas did not return a PNG data URL.");
        }

        return {
          imageContentBase64: dataUrl.slice(separatorIndex + 1),
          sourceName: "smart-ocr-smoke-real-sample.png",
          sourceMimeType: "image/png",
          expectedTokens: ["INVOICE", "ABC123", "TOTAL", "USD", "45678", "ACME"]
        };
      })()`,
      true,
    );

    if (!result.value?.imageContentBase64) {
      throw new Error("Smart OCR smoke failed to generate an in-memory PNG sample.");
    }

    return result.value;
  }

  function inspectPaddleOcrModelBundle(ocrModelRoot) {
    const modelRoot = String(ocrModelRoot ?? "");
    const requiredFiles = [
      path.join("PaddleOCR", "V6", "det", "inference.onnx"),
      path.join("PaddleOCR", "V6", "det", "inference.yml"),
      path.join("PaddleOCR", "V6", "rec", "inference.onnx"),
      path.join("PaddleOCR", "V6", "rec", "inference.yml"),
    ];
    const missingFiles = requiredFiles
      .map((relativePath) => path.join(modelRoot, relativePath))
      .filter((absolutePath) => !existsSync(absolutePath));

    return {
      available: modelRoot.length > 0 && missingFiles.length === 0,
      missingFiles,
    };
  }

  function readOcrFullText(result) {
    const fullText = String(result?.fullText ?? "").trim();
    if (fullText) {
      return fullText;
    }

    return Array.isArray(result?.lines)
      ? result.lines.map((line) => String(line?.text ?? "")).filter(Boolean).join("\n")
      : "";
  }

  function normalizeOcrTextForTokenMatch(value) {
    return String(value ?? "").toLocaleUpperCase().replace(/[^A-Z0-9]/g, "");
  }


  async function waitForExchangeRateCheck(page, options, timeoutMs) {
    if (!options.exchangeRateCheck) {
      return null;
    }

    const checkUrl = buildExchangeRateCheckUrl(options.webUrl);
    await page.send("Page.navigate", { url: checkUrl });
    const expectedText = [
      "今日汇率",
      "汇率列表",
      "可用货币",
      "汇率源",
      "缓存分钟",
      "暂无汇率数据",
    ];

    const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
    const exchangeRatePageCheck = await waitForPageExpression(
      page,
      `(() => {
        const page = document.querySelector('[aria-label="今日汇率"]');
        return Boolean(page &&
          page.querySelector('.exchange-rate-table') &&
          page.querySelector('[aria-label="可用货币"]') &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('刷新汇率')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('强制刷新汇率')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('读取可用货币')));
      })()`,
      timeoutMs,
      "Timed out waiting for the exchange-rate page.",
    );

    return {
      url: redactDesktopAccessToken(checkUrl),
      expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
      exchangeRatePageCheck,
      textExcerpt: pageText.slice(0, 1200),
    };
  }

  async function waitForEmailCheck(page, options, timeoutMs) {
    if (!options.emailCheck) {
      return null;
    }

    const checkUrl = buildEmailCheckUrl(options.webUrl);
    await page.send("Page.navigate", { url: checkUrl });
    const expectedText = [
      "邮件发送",
      "收件人",
      "主题",
      "正文",
      "附件路径",
      "附件",
    ];

    const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
    const emailPageCheck = await waitForPageExpression(
      page,
      `(() => {
        const page = document.querySelector('[aria-label="邮件发送"]');
        const toolbarText = page?.querySelector('.email-tool-toolbar')?.innerText || '';
        const isUnconfigured = toolbarText.includes('SMTP 未配置');
        const statusValues = page ? Array.from(page.querySelectorAll('[aria-label="邮件状态"] .detail-item strong')).map((item) => item.textContent || '') : [];
        return Boolean(page &&
          page.querySelector('[aria-label="邮件状态"] .email-status-detail-grid') &&
          page.querySelector('[aria-label="邮件内容"] input[type="email"]') &&
          page.querySelector('[aria-label="邮件内容"] textarea') &&
          page.querySelector('[aria-label="邮件附件"] .email-attachment-table') &&
          page.querySelector('[aria-label="邮件附件"] textarea') &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('刷新状态')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('选择附件')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('发送邮件')) &&
          !toolbarText.includes('-:587 / -') &&
          (!isUnconfigured || (toolbarText.includes('请先配置邮件服务器和发件人信息') && statusValues.slice(0, 5).every((value) => value === '-'))));
      })()`,
      timeoutMs,
      "Timed out waiting for the email page.",
    );

    return {
      url: redactDesktopAccessToken(checkUrl),
      expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
      emailPageCheck,
      textExcerpt: pageText.slice(0, 1200),
    };
  }


  async function waitForAuditLogCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.auditLogCheck) {
      return null;
    }

    const auditSource = options.auditLogExportCheck
      ? await createSmokeAuditLogSource(options, accessToken, tokenType, timeoutMs)
      : null;
    const checkUrl = buildAuditLogCheckUrl(options.webUrl);
    await page.send("Page.navigate", { url: checkUrl });
    const expectedText = [
      "审计日志",
      "发票",
      "实体",
      "动作",
      "操作人",
      "关键字",
      "时间范围",
      "导出与维护",
      "Excel 保存位置",
      "按保留期清理",
      "高级操作：删除筛选结果",
      "详情",
    ];

    const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
    const auditLogPageCheck = await waitForPageExpression(
      page,
      `(() => {
        const page = document.querySelector('[aria-label="审计日志"]');
        return Boolean(page &&
          page.querySelector('[aria-label="审计日志导出与维护"]') &&
          page.querySelector('.audit-log-table') &&
          page.querySelector('[aria-label="审计日志详情"]') &&
          page.querySelector('.audit-log-maintenance-grid') &&
          page.querySelector('input[placeholder="AuditLogs.xlsx"]') &&
          !page.querySelector('input[placeholder="DELETE"]') &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('查询')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('刷新')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('导出 Excel')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('删除当前筛选结果')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('清理过期审计日志')));
      })()`,
      timeoutMs,
      "Timed out waiting for the audit log page.",
    );

    const auditLogExportCheck = options.auditLogExportCheck
      ? await waitForAuditLogExportCheck(page, options, auditSource, timeoutMs)
      : null;

    return {
      url: redactDesktopAccessToken(checkUrl),
      expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
      auditLogPageCheck,
      auditLogExportCheck,
      textExcerpt: pageText.slice(0, 1200),
    };
  }

  async function createSmokeAuditLogSource(options, accessToken, tokenType, timeoutMs) {
    const suffix = `${Date.now()}-${Math.floor(Math.random() * 100000)}`;
    const username = `audit-smoke-${suffix}`;
    const password = `Audit-${suffix}!`;
    const body = {
      username,
      fullName: `Audit Smoke User ${suffix}`,
      role: "User",
      departmentId: "AUDIT-SMOKE",
      companyScope: "SMOKE",
      isActive: true,
      resetPassword: password,
    };

    const createResponse = await fetch(new URL("/api/users", ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify(body),
    });

    if (!createResponse.ok) {
      throw new Error(`Audit smoke user create failed with HTTP ${createResponse.status}: ${await createResponse.text()}`);
    }

    const created = await createResponse.json();
    const userId = created?.user?.id;
    let deleted = false;
    if (userId) {
      const deleteResponse = await fetch(new URL(`/api/users/${userId}`, ensureTrailingSlash(options.apiBaseUrl)), {
        method: "DELETE",
        headers: authorizedHeaders(options, accessToken, tokenType),
      });
      if (!deleteResponse.ok) {
        throw new Error(`Audit smoke user delete failed with HTTP ${deleteResponse.status}: ${await deleteResponse.text()}`);
      }

      deleted = true;
    }

    const auditRows = await waitFor(async () => {
      const url = new URL("/api/audit-logs", ensureTrailingSlash(options.apiBaseUrl));
      url.searchParams.set("pageNumber", "1");
      url.searchParams.set("pageSize", "10");
      url.searchParams.set("entityName", "User");
      url.searchParams.set("keyword", username);
      const response = await fetch(url, {
        headers: authorizedHeaders(options, accessToken, tokenType),
      });
      if (!response.ok) {
        throw new Error(`Audit smoke log query failed with HTTP ${response.status}: ${await response.text()}`);
      }

      const page = await response.json();
      return page?.totalCount > 0 ? page : null;
    }, timeoutMs, `Timed out waiting for audit rows for smoke user ${username}.`);

    return {
      username,
      userId: userId ?? null,
      created: true,
      deleted,
      auditRowCount: auditRows.totalCount ?? 0,
    };
  }

  async function waitForAuditLogExportCheck(page, options, auditSource, timeoutMs) {
    const exportPath = path.join(options.userDataDir, `audit-log-export-smoke-${Date.now()}.xlsx`);
    rmSync(exportPath, { force: true });

    let cleanedExportFile = false;
    try {
      const exportAction = await runAuditLogExportUiAction(page, exportPath);
      const exportResult = await waitFor(async () => {
        const state = await readAuditLogExportState(page);
        const fileExists = existsSync(exportPath);
        const hasSuccess = state.alerts.some((text) =>
          includesText(text, "审计日志已导出") &&
          includesText(text, exportPath),
        );

        return fileExists && hasSuccess ? state : null;
      }, timeoutMs, `Timed out waiting for audit log export file: ${exportPath}`);

      const openPathAction = await runAuditLogOpenExportPathAction(page, exportPath);
      return {
        auditSource,
        exportPath,
        exportAction,
        exportResult,
        exportFileExists: existsSync(exportPath),
        exportFileSize: existsSync(exportPath) ? await readFileSize(exportPath) : 0,
        openPathAction,
        cleanedExportFile: cleanupSmokeFile(exportPath, options.userDataDir),
      };
    } finally {
      if (existsSync(exportPath)) {
        cleanedExportFile = cleanupSmokeFile(exportPath, options.userDataDir);
      }
    }
  }

  async function readAuditLogExportState(page) {
    const result = await evaluate(
      page,
      `(() => {
        const section = document.querySelector('[aria-label="审计日志"]');
        const rowCount = Array.from(section?.querySelectorAll('.audit-log-table tbody tr') || [])
          .filter((row) => !((row.innerText || '').includes('暂无审计日志') || (row.innerText || '').includes('加载中')))
          .length;
        return {
          alerts: Array.from(section?.querySelectorAll('.alert, .success-alert') || []).map((item) => item.innerText || ''),
          rowCount,
          outputPath: section?.querySelector('input[placeholder="AuditLogs.xlsx"]')?.value || '',
          buttons: Array.from(section?.querySelectorAll('button') || []).map((button) => ({
            title: button.title || '',
            text: button.innerText || button.textContent || '',
            disabled: Boolean(button.disabled)
          }))
        };
      })()`,
      true,
    );

    const value = result.value ?? {};
    return {
      alerts: Array.isArray(value.alerts) ? value.alerts : [],
      rowCount: Number(value.rowCount ?? 0),
      outputPath: String(value.outputPath ?? ""),
      buttons: Array.isArray(value.buttons) ? value.buttons : [],
    };
  }

  async function runAuditLogExportUiAction(page, exportPath) {
    const result = await evaluate(
      page,
      `(async (payload) => {
        const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
        const section = document.querySelector('[aria-label="审计日志"]');
        if (!section) {
          throw new Error("审计日志页面未找到。");
        }

        const setNativeValue = (control, value) => {
          const prototype = Object.getPrototypeOf(control);
          const descriptor = Object.getOwnPropertyDescriptor(prototype, "value");
          if (descriptor && typeof descriptor.set === "function") {
            descriptor.set.call(control, value);
          } else {
            control.value = value;
          }
          control.focus();
          control.dispatchEvent(new Event("input", { bubbles: true }));
          control.dispatchEvent(new Event("change", { bubbles: true }));
          const reactPropsKey = Object.keys(control).find((key) => key.startsWith("__reactProps$"));
          const reactProps = reactPropsKey ? control[reactPropsKey] : null;
          if (reactProps && typeof reactProps.onChange === "function") {
            reactProps.onChange({ target: control, currentTarget: control });
          }
        };

        const outputInput = section.querySelector('input[placeholder="AuditLogs.xlsx"]');
        if (!outputInput) {
          throw new Error("审计日志导出路径输入框未找到。");
        }

        setNativeValue(outputInput, payload.exportPath);

        const deadline = Date.now() + 8000;
        let latestReason = "";
        while (Date.now() < deadline) {
          const button = Array.from(section.querySelectorAll("button")).find((item) => (item.title || "").includes("导出 Excel"));
          if (button && !button.disabled) {
            button.click();
            return { action: "export", submitted: true, exportPath: payload.exportPath };
          }

          latestReason = button ? "导出按钮仍不可用" : "导出按钮未找到";
          await delay(100);
        }

        throw new Error(latestReason || "等待导出按钮超时");
      })(${JSON.stringify({ exportPath })})`,
      true,
    );

    return result.value ?? { action: "export", submitted: true, exportPath };
  }

  async function runAuditLogOpenExportPathAction(page, exportPath) {
    const result = await evaluate(
      page,
      `(async (payload) => {
        const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
        const section = document.querySelector('[aria-label="审计日志"]');
        if (!section) {
          throw new Error("审计日志页面未找到。");
        }

        window.__exportDocManagerSmokeTauriInvocations = [];
        const deadline = Date.now() + 8000;
        let latestReason = "";
        while (Date.now() < deadline) {
          const button = Array.from(section.querySelectorAll("button")).find((item) => (item.title || "").includes("打开导出文件"));
          if (button && !button.disabled) {
            button.click();
            break;
          }

          latestReason = button ? "打开导出文件按钮仍不可用" : "打开导出文件按钮未找到";
          await delay(100);
        }

        if (latestReason && !Array.from(section.querySelectorAll("button")).some((item) => (item.title || "").includes("打开导出文件") && !item.disabled)) {
          throw new Error(latestReason);
        }

        const invocationDeadline = Date.now() + 8000;
        while (Date.now() < invocationDeadline) {
          const invocations = window.__exportDocManagerSmokeTauriInvocations || [];
          const matched = invocations.find((item) => item.command === "open_path" && item.args && item.args.path === payload.exportPath);
          if (matched) {
            return { action: "open_path", submitted: true, path: matched.args.path, invocations };
          }

          await delay(100);
        }

        throw new Error("等待打开导出文件 open_path 调用超时");
      })(${JSON.stringify({ exportPath })})`,
      true,
    );

    return result.value ?? { action: "open_path", submitted: true, path: exportPath };
  }


  async function waitForLicenseCheck(page, options, timeoutMs) {
    if (!options.licenseCheck) {
      return null;
    }

    const checkUrl = buildLicenseCheckUrl(options.webUrl);
    await page.send("Page.navigate", { url: checkUrl });
    const expectedText = [
      "授权注册",
      "机器码",
      "试用天数",
      "剩余天数",
      "到期日期",
      "注册码",
    ];

    const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
    const licensePageCheck = await waitForPageExpression(
      page,
      `(() => {
        const page = document.querySelector('[aria-label="授权注册"]');
        const labels = Array.from(page?.querySelectorAll('.detail-item span') || []).map((element) => (element.textContent || '').trim());
        return Boolean(page &&
          page.querySelector('[aria-label="授权状态"] .license-detail-grid') &&
          page.querySelector('[aria-label="注册"] textarea') &&
          labels.includes('机器码') &&
          labels.includes('试用天数') &&
          labels.includes('剩余天数') &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('刷新授权状态')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('复制机器码')) &&
          Array.from(page.querySelectorAll('button')).some((button) => (button.title || '').includes('注册')));
      })()`,
      timeoutMs,
      "Timed out waiting for the license page.",
    );

    return {
      url: redactDesktopAccessToken(checkUrl),
      expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
      licensePageCheck,
      textExcerpt: pageText.slice(0, 1200),
    };
  }

  function buildUpdateCheckUrl(webUrl) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeUpdate", "1");
    url.hash = "/system/update";
    return url.toString();
  }

  function buildSmartOcrCheckUrl(webUrl) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeSmartOcr", "1");
    url.hash = "/tools/ocr";
    return url.toString();
  }

  function buildExchangeRateCheckUrl(webUrl) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeExchangeRates", "1");
    url.hash = "/tools/exchange-rates";
    return url.toString();
  }

  function buildEmailCheckUrl(webUrl) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeEmail", "1");
    url.hash = "/tools/email";
    return url.toString();
  }

  function buildAuditLogCheckUrl(webUrl) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeAuditLogs", "1");
    url.hash = "/audit-logs";
    return url.toString();
  }

  function buildLicenseCheckUrl(webUrl) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeLicense", "1");
    url.hash = "/system/license";
    return url.toString();
  }

  return { run };
}
