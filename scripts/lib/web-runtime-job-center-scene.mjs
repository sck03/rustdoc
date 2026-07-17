import { existsSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";

export function createJobCenterSmokeScene(runtime) {
  const {
    authorizedHeaders,
    authorizedJsonHeaders,
    cleanupSmokeFile,
    createSmokeInvoice,
    deleteSmokeInvoice,
    ensureTrailingSlash,
    evaluate,
    includesText,
    normalizePathForCompare,
    readFileSize,
    redactDesktopAccessToken,
    waitFor,
    waitForPageExpression,
    waitForRuntimeDiagnostics,
  } = runtime;

  async function waitForJobCenterCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.jobCenterCheck) {
      return null;
    }

    const outputJob = options.mockTauriRuntimeContext
      ? await createSmokeJobCenterOutputJob(options, accessToken, tokenType, timeoutMs)
      : null;
    let result = null;
    let deletedOutputFile = false;

    const checkUrl = buildJobCenterCheckUrl(options.webUrl);
    try {
      await page.send("Page.navigate", { url: checkUrl });
      const expectedText = [
        "批量报表 ZIP",
        "PDF 合并",
        "状态",
        "任务",
        "类型",
        "进度",
        "输出",
      ];

      const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);

      await page.send("Page.navigate", { url: buildContainerPackingCheckUrl(options.webUrl) });
      await waitForRuntimeDiagnostics(page, ["装箱分析", "方案名称", "已存方案", "柜型", "保存方案", "加载", "保存柜型"], timeoutMs);
      const containerPanelCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="装箱分析"]');
          return Boolean(panel &&
            panel.querySelector('[aria-label="装柜方案管理"]') &&
            panel.querySelector('input[list="container-packing-type-options"]') &&
            panel.querySelector('#container-packing-type-options') &&
            panel.querySelector('.container-packing-cargo-table') &&
            panel.querySelector('[aria-label="装柜自动刷新"] input[type="checkbox"]') &&
            panel.querySelector('[aria-label="装柜分析状态"]') &&
            Array.from(panel.querySelectorAll('button')).some((button) => (button.innerText || '').includes('分析')) &&
            Array.from(panel.querySelectorAll('button')).some((button) => (button.innerText || '').includes('保存方案')) &&
            Array.from(panel.querySelectorAll('button')).some((button) => (button.innerText || '').includes('删除方案')) &&
            Array.from(panel.querySelectorAll('button')).some((button) => (button.innerText || '').includes('保存柜型')));
        })()`,
        timeoutMs,
        "Timed out waiting for the container packing panel.",
      );
      const containerProjectCrudCheck = await waitForContainerPackingProjectCrudCheck(page, timeoutMs);
      await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="装箱分析"]');
          const autoToggle = panel ? panel.querySelector('[aria-label="装柜自动刷新"] input[type="checkbox"]') : null;
          if (!autoToggle) throw new Error('Container packing auto analysis toggle is unavailable.');
          if (!autoToggle.checked) autoToggle.click();
          return true;
        })()`,
        true,
      );
      const containerAutoRefreshCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="装箱分析"]');
          const autoToggle = panel ? panel.querySelector('[aria-label="装柜自动刷新"] input[type="checkbox"]') : null;
          const status = panel ? panel.querySelector('[aria-label="装柜分析状态"]') : null;
          const summary = panel ? panel.querySelector('.container-packing-result .packing-summary-grid') : null;
          const statusText = status ? status.innerText || '' : '';
          return Boolean(autoToggle &&
            autoToggle.checked &&
            status &&
            status.dataset.autoRefresh === 'enabled' &&
            status.dataset.autoRefreshState === 'complete' &&
            statusText.includes('自动分析') &&
            statusText.includes('货物:') &&
            summary);
        })()`,
        timeoutMs,
        "Timed out waiting for the container packing auto refresh result.",
      );
      const containerEditingStabilityResult = await evaluate(
        page,
        `(async () => {
          const panel = document.querySelector('[aria-label="装箱分析"]');
          const row = panel?.querySelector('.container-packing-cargo-table tbody tr');
          const quantityInput = row?.querySelector('input[inputmode="numeric"]');
          const summary = panel?.querySelector('.container-packing-result .packing-summary-grid');
          const status = panel?.querySelector('[aria-label="装柜分析状态"]');
          if (!quantityInput || !summary || !status) return false;
          const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
          if (!valueSetter) return false;
          valueSetter.call(quantityInput, String(Number(quantityInput.value || '1') + 1));
          quantityInput.dispatchEvent(new Event('input', { bubbles: true }));
          await new Promise((resolve) => setTimeout(resolve, 250));
          return Boolean(
            panel.querySelector('.container-packing-result .packing-summary-grid') &&
            status.dataset.autoRefreshState === 'queued' &&
            (status.innerText || '').includes('停止输入后自动刷新')
          );
        })()`,
        true,
      );
      if (!containerEditingStabilityResult.value) {
        throw new Error("Container packing result did not remain stable while editing.");
      }
      const containerEditingStabilityCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="装箱分析"]');
          const status = panel?.querySelector('[aria-label="装柜分析状态"]');
          return Boolean(
            panel?.querySelector('.container-packing-result .packing-summary-grid') &&
            status?.dataset.autoRefreshState === 'complete' &&
            (status.innerText || '').includes('自动分析完成')
          );
        })()`,
        timeoutMs,
        "Timed out waiting for the deferred container packing refresh.",
      );
      const containerAnalyzeButtonCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="装箱分析"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('分析'));
          return Boolean(button && !button.disabled);
        })()`,
        timeoutMs,
        "Timed out waiting for the container packing analyze button.",
      );
      await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="装箱分析"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('分析'));
          if (!button || button.disabled) {
            throw new Error('Container packing analyze button is not available.');
          }

          button.click();
          return true;
        })()`,
        true,
      );
      const containerVisualizationCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="装箱分析"]');
          const visualization = panel ? panel.querySelector('[aria-label="装柜平面可视化"]') : null;
          const pseudo3d = visualization ? visualization.querySelector('svg[aria-label="装柜效果图"]') : null;
          const doorEdge = pseudo3d ? pseudo3d.querySelector('line[data-shell-edge="door-width-bottom"]') : null;
          const doorSlantsOutward = doorEdge
            ? Number(doorEdge.getAttribute('x2')) > Number(doorEdge.getAttribute('x1')) &&
              Number(doorEdge.getAttribute('y2')) > Number(doorEdge.getAttribute('y1'))
            : false;
          const itemFaces = pseudo3d ? Array.from(pseudo3d.querySelectorAll('.container-packing-pseudo3d-item polygon')) : [];
          const itemGroups = pseudo3d ? pseudo3d.querySelectorAll('.container-packing-pseudo3d-item') : [];
          const edgeLines = pseudo3d ? pseudo3d.querySelectorAll('.container-packing-pseudo3d-item-edge') : [];
          const edgesStayWithSolidItems =
            itemGroups.length > 0 &&
            Array.from(itemGroups).every((group) => {
              const lineCount = group.querySelectorAll('.container-packing-pseudo3d-item-edge').length;
              return lineCount >= 8 && lineCount < 12;
            }) &&
            !pseudo3d.querySelector('.container-packing-pseudo3d-item-outlines');
          const itemFacesSolid =
            itemFaces.length > 0 &&
            itemFaces.every((face) => {
              const opacity = face.getAttribute('opacity');
              return !opacity || Number(opacity) >= 0.99;
            }) &&
            Boolean(
              pseudo3d.querySelector('.container-packing-pseudo3d-item polygon[data-face="top"]') &&
                pseudo3d.querySelector('.container-packing-pseudo3d-item polygon[data-face="width-side"]') &&
                pseudo3d.querySelector('.container-packing-pseudo3d-item polygon[data-face="length-side"]'),
            );
          const outlineModeHasNoPseudoGrid = pseudo3d ? pseudo3d.querySelectorAll('.container-packing-pseudo3d-item-grid-line').length === 0 : false;
          const outlineEdgesCoverBoxes = itemGroups.length > 0 && edgeLines.length >= itemGroups.length * 8 && edgeLines.length < itemGroups.length * 12;
          return Boolean(visualization &&
            pseudo3d &&
            doorSlantsOutward &&
            itemFacesSolid &&
            outlineModeHasNoPseudoGrid &&
            outlineEdgesCoverBoxes &&
            edgesStayWithSolidItems &&
            visualization.querySelector('[data-view-kind="top"] svg[aria-label="俯视图"]') &&
            visualization.querySelector('[data-view-kind="side"] svg[aria-label="侧视图"]') &&
            visualization.querySelector('[data-view-kind="door"] svg[aria-label="柜门视图"]') &&
            visualization.querySelectorAll('.container-packing-item-rect').length > 0 &&
            visualization.querySelector('[aria-label="装柜颜色图例"]'));
        })()`,
        timeoutMs,
        "Timed out waiting for the container packing visualization.",
      );
      const containerPdfExportCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="装箱分析"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const exportButton = buttons.find((element) => (element.innerText || '').includes('导出 PDF'));
          const exportRoot = panel ? panel.querySelector('[data-container-packing-pdf]') : null;
          const cargoTable = exportRoot ? exportRoot.querySelector('.container-packing-pdf-cargo table') : null;
          if (!exportRoot) return false;
          return Boolean(exportButton && !exportButton.disabled && cargoTable);
        })()`,
        timeoutMs,
        "Timed out waiting for the container packing PDF export entry.",
      );
      await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="装箱分析"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const exportButton = buttons.find((element) => (element.innerText || '').includes('导出 PDF'));
          if (!exportButton || exportButton.disabled) throw new Error('Container packing PDF export button is not available.');
          window.__containerPackingPdfSmoke = {
            tauri: window.__TAURI__,
            invoke: window.__TAURI__?.core?.invoke,
            createObjectURL: URL.createObjectURL,
            revokeObjectURL: URL.revokeObjectURL,
            anchorClick: HTMLAnchorElement.prototype.click,
            downloads: [],
            savedPdf: null,
          };
          if (!window.__containerPackingPdfSmoke.invoke) throw new Error('Mock Tauri invoke is unavailable.');
          window.__TAURI__.core.invoke = async (command, args) => {
            if (command === 'select_save_pdf_path') return 'E:/Smoke/container-packing.pdf';
            if (command === 'save_pdf_file') {
              const binary = atob(args.base64Data || '');
              window.__containerPackingPdfSmoke.savedPdf = {
                path: args.path || '',
                type: 'application/pdf',
                size: binary.length,
                header: binary.slice(0, 5),
                pageCount: (binary.match(/\\/Type\\s*\\/Page\\b/g) || []).length,
              };
              return null;
            }
            return window.__containerPackingPdfSmoke.invoke(command, args);
          };
          URL.createObjectURL = (blob) => {
            window.__containerPackingPdfSmoke.downloads.push({ type: blob.type, size: blob.size, href: 'blob:container-packing-pdf-smoke' });
            return 'blob:container-packing-pdf-smoke';
          };
          URL.revokeObjectURL = () => {};
          HTMLAnchorElement.prototype.click = function () {
            const current = window.__containerPackingPdfSmoke.downloads.at(-1);
            if (current) current.fileName = this.download || '';
          };
          exportButton.click();
          return true;
        })()`,
        true,
      );
      const containerPdfGenerationCheck = await waitForPageExpression(
        page,
        `(() => {
          const smoke = window.__containerPackingPdfSmoke;
          const savedPdf = smoke?.savedPdf;
          const panel = document.querySelector('[aria-label="装箱分析"]');
          const text = panel?.innerText || '';
          return Boolean(savedPdf &&
            savedPdf.type === 'application/pdf' &&
            savedPdf.header === '%PDF-' &&
            savedPdf.size > 1000 &&
            savedPdf.size < 10 * 1024 * 1024 &&
            savedPdf.path.endsWith('.pdf') &&
            savedPdf.pageCount === 1 &&
            smoke.downloads.length === 0 &&
            text.includes('PDF 已保存到'));
        })()`,
        timeoutMs,
        "Timed out waiting for the generated container packing PDF.",
      );
      const containerPdfGenerationDetailsResult = await evaluate(
        page,
        `(() => {
          const savedPdf = window.__containerPackingPdfSmoke?.savedPdf;
          return savedPdf ? { type: savedPdf.type, size: savedPdf.size, fileName: savedPdf.path, pageCount: savedPdf.pageCount } : null;
        })()`,
        true,
      );
      const containerPdfGenerationDetails = containerPdfGenerationDetailsResult.value ?? null;
      await evaluate(
        page,
        `(() => {
          const smoke = window.__containerPackingPdfSmoke;
          if (!smoke) return false;
          window.__TAURI__ = smoke.tauri;
          window.__TAURI__.core.invoke = smoke.invoke;
          URL.createObjectURL = smoke.createObjectURL;
          URL.revokeObjectURL = smoke.revokeObjectURL;
          HTMLAnchorElement.prototype.click = smoke.anchorClick;
          delete window.__containerPackingPdfSmoke;
          return true;
        })()`,
        true,
      );
      await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="装箱分析"]');
          const buttons = panel ? Array.from(panel.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('完整分格'));
          if (!button) {
            throw new Error('Container packing full-grid render mode button is not available.');
          }

          button.click();
          return true;
        })()`,
        true,
      );
      const containerFullGridVisualizationCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="装箱分析"]');
          const visualization = panel ? panel.querySelector('[aria-label="装柜平面可视化"]') : null;
          const pseudo3d = visualization ? visualization.querySelector('svg[aria-label="装柜效果图"]') : null;
          const fullGridButton = panel
            ? Array.from(panel.querySelectorAll('button')).find((element) => (element.innerText || '').includes('完整分格'))
            : null;
          const pseudoGridLines = pseudo3d ? pseudo3d.querySelectorAll('.container-packing-pseudo3d-item-grid-line') : [];
          const edgeLines = pseudo3d ? pseudo3d.querySelectorAll('.container-packing-pseudo3d-item-edge') : [];
          const itemGroups = pseudo3d ? Array.from(pseudo3d.querySelectorAll('.container-packing-pseudo3d-item')) : [];
          const stackedItemGroups = itemGroups.filter((group) => Number(group.getAttribute('data-load-count') || '1') > 1);
          const fullGridHasExpectedInternalLines = stackedItemGroups.length === 0 || pseudoGridLines.length >= stackedItemGroups.length;
          const outlineEdgesCoverBoxes = itemGroups.length > 0 && edgeLines.length >= itemGroups.length * 8 && edgeLines.length < itemGroups.length * 12;
          const edgesStayWithSolidItems =
            itemGroups.length > 0 &&
            itemGroups.every((group) => {
              const lineCount = group.querySelectorAll('.container-packing-pseudo3d-item-edge').length;
              return lineCount >= 8 && lineCount < 12;
            }) &&
            !pseudo3d.querySelector('.container-packing-pseudo3d-item-outlines');
          return Boolean(
            pseudo3d &&
              fullGridButton &&
              fullGridButton.getAttribute('aria-pressed') === 'true' &&
              fullGridHasExpectedInternalLines &&
              outlineEdgesCoverBoxes &&
              edgesStayWithSolidItems,
          );
        })()`,
        timeoutMs,
        "Timed out waiting for the container packing full-grid visualization.",
      );
      const container3dCanvasCheck = await waitForContainerPacking3dCanvas(page, timeoutMs, "desktop");
      const container3dControlsCheck = await verifyContainerPacking3dControls(page, timeoutMs);
      let container3dMobileCanvasCheck;
      await page.send("Emulation.setDeviceMetricsOverride", {
        width: 390,
        height: 844,
        deviceScaleFactor: 2,
        mobile: true,
      });
      try {
        await evaluate(
          page,
          `(() => {
            window.dispatchEvent(new Event('resize'));
            document.querySelector('[aria-label="装柜三维可视化"]')?.scrollIntoView({ block: 'center' });
            return true;
          })()`,
          true,
        );
        container3dMobileCanvasCheck = await waitForContainerPacking3dCanvas(page, timeoutMs, "mobile");
      } finally {
        await page.send("Emulation.clearDeviceMetricsOverride").catch(() => undefined);
      }

      await page.send("Page.navigate", { url: buildExcelToolsCheckUrl(options.webUrl) });
      await waitForRuntimeDiagnostics(page, ["Excel 模板与托单", "导出导入模板", "导出空白托单", "选择 Excel 并转托单", "发票导出托单"], timeoutMs);
      const excelToolsCheck = await waitForPageExpression(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="Excel 模板与托单"]');
          return Boolean(panel &&
            Array.from(panel.querySelectorAll('button')).some((button) => (button.innerText || '').includes('导出导入模板')) &&
            Array.from(panel.querySelectorAll('button')).some((button) => (button.innerText || '').includes('导出空白托单')) &&
            Array.from(panel.querySelectorAll('button')).some((button) => (button.innerText || '').includes('选择 Excel 并转托单')) &&
            Array.from(panel.querySelectorAll('button')).some((button) => (button.innerText || '').includes('导出发票托单')));
        })()`,
        timeoutMs,
        "Timed out waiting for the Excel tools panel.",
      );
      const excelToolOutputJobsCheck = await waitForJobCenterExcelToolOutputJobsCheck(
        page,
        options,
        accessToken,
        tokenType,
        timeoutMs,
      );

      await page.send("Page.navigate", { url: checkUrl });
      await waitForRuntimeDiagnostics(page, ["批量报表 ZIP", "PDF 合并", "任务", "类型", "进度", "输出"], timeoutMs);
      await openJobCenterTaskDetails(page);
      const pathPanelsCheck = await waitForPageExpression(
        page,
        `(() => Boolean(
          document.querySelector('[aria-label="批量报表 ZIP 任务"]') &&
          document.querySelector('[aria-label="PDF 合并任务"]') &&
          document.querySelector('.job-table')
        ))()`,
        timeoutMs,
        "Timed out waiting for job center path/task panels.",
      );
      const outputOpenPathActionCheck = outputJob
        ? await waitForJobOutputOpenPathAction(page, outputJob.outputPath, timeoutMs)
        : null;
      const reportBatchZipJobCheck = await waitForJobCenterReportBatchZipJobCheck(
        page,
        options,
        accessToken,
        tokenType,
        timeoutMs,
      );
      const pdfMergeJobCheck = await waitForJobCenterPdfMergeJobCheck(
        page,
        options,
        accessToken,
        tokenType,
        timeoutMs,
      );

      result = {
        url: redactDesktopAccessToken(checkUrl),
        expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
        containerPanelCheck,
          containerProjectCrudCheck,
          containerAutoRefreshCheck,
          containerEditingStabilityCheck,
          containerAnalyzeButtonCheck,
        containerVisualizationCheck,
          containerPdfExportCheck,
          containerPdfGenerationCheck,
          containerPdfGenerationDetails,
        containerFullGridVisualizationCheck,
        container3dCanvasCheck,
        container3dControlsCheck,
        container3dMobileCanvasCheck,
        excelToolsCheck,
        excelToolOutputJobsCheck,
        pathPanelsCheck,
        reportBatchZipJobCheck,
        pdfMergeJobCheck,
        outputJob,
        outputOpenPathActionCheck,
        deletedOutputFile,
        textExcerpt: pageText.slice(0, 1200),
      };
      } finally {
        if (outputJob?.createdPath) {
        try {
          rmSync(outputJob.createdPath, { force: true });
          deletedOutputFile = !existsSync(outputJob.createdPath);
        } catch {
          deletedOutputFile = false;
        }
      }

      if (result) {
        result.deletedOutputFile = deletedOutputFile;
      }
    }

    return result;
  }

  async function openJobCenterTaskDetails(page) {
    await evaluate(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="新建任务"]');
        const details = Array.from(panel ? panel.querySelectorAll('details') : []);
        for (const item of details) {
          item.open = true;
        }

        return details.length;
      })()`,
      true,
    );
  }

  async function waitForJobCenterExcelToolOutputJobsCheck(page, options, accessToken, tokenType, timeoutMs) {
    const timestamp = Date.now();
    const jobs = [
      {
        key: "templateExport",
        stackTitle: "导入模板",
        fieldLabel: "模板输出",
        buttonText: "按路径导出",
        messageLabel: "Excel 模板导出",
        expectedKind: "ExcelTemplateExport",
        outputPath: path.join(options.userDataDir, `job-center-excel-template-${timestamp}.xlsx`),
      },
      {
        key: "blankBooking",
        stackTitle: "空白托单",
        fieldLabel: "托单输出",
        buttonText: "按路径导出",
        messageLabel: "空白托单导出",
        expectedKind: "BlankBookingSheetExport",
        outputPath: path.join(options.userDataDir, `job-center-blank-booking-${timestamp}.xlsx`),
      },
    ];
    const checks = {};

    for (const job of jobs) {
      checks[job.key] = await runJobCenterExcelOutputJobCheck(page, options, accessToken, tokenType, timeoutMs, job);
    }

    checks.bookingConvert = await runJobCenterExcelBookingConvertJobCheck(
      page,
      options,
      accessToken,
      tokenType,
      timeoutMs,
      timestamp,
    );
    checks.invoiceBooking = await runJobCenterInvoiceBookingExportJobCheck(
      page,
      options,
      accessToken,
      tokenType,
      timeoutMs,
      timestamp,
    );

    return {
      ...checks,
      allSucceeded: Object.values(checks).every((check) =>
        String(check?.status ?? "").toLowerCase() === "succeeded" &&
        check?.fileHeader === "PK" &&
        check?.cleanedOutputFile === true &&
        (check?.cleanedSourceFile === undefined || check?.cleanedSourceFile === true) &&
        (check?.deletedInvoice === undefined || check?.deletedInvoice === true)),
    };
  }

  async function runJobCenterExcelOutputJobCheck(page, options, accessToken, tokenType, timeoutMs, job) {
    let result = null;
    let cleanedOutputFile = false;
    let latestUiState = null;
    let latestJob = null;

    try {
      const outputState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const stackTitle = ${JSON.stringify(job.stackTitle)};
              const fieldLabel = ${JSON.stringify(job.fieldLabel)};
              const buttonText = ${JSON.stringify(job.buttonText)};
              const outputPath = ${JSON.stringify(job.outputPath)};
              const panel = document.querySelector('[aria-label="Excel 模板与托单"]');
              const notifyReactChange = (control) => {
                const reactPropsKey = Object.keys(control).find((key) => key.startsWith("__reactProps$"));
                const reactProps = reactPropsKey ? control[reactPropsKey] : null;
                if (reactProps && typeof reactProps.onChange === "function") {
                  reactProps.onChange({ target: control, currentTarget: control });
                }
              };
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
                notifyReactChange(control);
              };
              const fieldByLabel = (labelText) => {
                const fields = Array.from(stack ? stack.querySelectorAll(".path-field") : []);
                const field = fields.find((item) =>
                  ((item.querySelector(".path-field-label") || {}).innerText || "").trim() === labelText);
                return field ? field.querySelector("input") : null;
              };
              const stack = Array.from(panel ? panel.querySelectorAll(".job-tool-stack") : [])
                .find((item) => ((item.querySelector(".job-tool-stack-title") || {}).innerText || "").includes(stackTitle));

              if (!panel) {
                return { ready: false, reason: "Excel tools panel is not ready" };
              }
              if (!stack) {
                return { ready: false, reason: "Excel tool stack is not ready", stackTitle };
              }
              const advanced = stack.querySelector("details");
              if (advanced) {
                advanced.open = true;
              }

              const input = fieldByLabel(fieldLabel);
              const button = Array.from(stack.querySelectorAll("button"))
                .find((element) => (element.innerText || "").includes(buttonText));
              if (!input || !button) {
                return {
                  ready: false,
                  reason: "Excel output controls are not ready",
                  hasInput: Boolean(input),
                  hasButton: Boolean(button),
                };
              }

              if (input.value !== outputPath) {
                setNativeValue(input, outputPath);
              }

              return {
                ready: Boolean(!button.disabled && input.value === outputPath),
                reason: button.disabled ? "Excel output button is disabled" : "",
                fieldLabel,
                outputPath: input.value,
                buttonText: button.innerText || "",
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { ready: false, reason: String(error) } }));

        latestUiState = state.value ?? null;
        return latestUiState?.ready ? latestUiState : null;
      }, timeoutMs, () =>
        `Timed out preparing job center Excel output task ${job.key}. Latest UI state: ${JSON.stringify(latestUiState)}`);

      await evaluate(
        page,
        `(() => {
          const stackTitle = ${JSON.stringify(job.stackTitle)};
          const buttonText = ${JSON.stringify(job.buttonText)};
          const panel = document.querySelector('[aria-label="Excel 模板与托单"]');
          const stack = Array.from(panel ? panel.querySelectorAll(".job-tool-stack") : [])
            .find((item) => ((item.querySelector(".job-tool-stack-title") || {}).innerText || "").includes(stackTitle));
          const advanced = stack ? stack.querySelector("details") : null;
          if (advanced) {
            advanced.open = true;
          }
          const button = stack
            ? Array.from(stack.querySelectorAll("button")).find((element) => (element.innerText || "").includes(buttonText))
            : null;
          if (!button || button.disabled) {
            throw new Error("Excel output button is not available: " + buttonText);
          }

          button.click();
          return true;
        })()`,
        true,
      );

      const createdState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const messageLabel = ${JSON.stringify(job.messageLabel)};
              const text = document.body ? document.body.innerText || "" : "";
              const match = text.match(new RegExp("已创建" + messageLabel + "任务：([^\\\\s\\\\n]+)"));
              return {
                jobId: match ? match[1] : "",
                textExcerpt: text.slice(0, 1200),
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { jobId: "", textExcerpt: String(error) } }));

        return state.value?.jobId ? state.value : null;
      }, timeoutMs, () => `Timed out waiting for the job center Excel output job creation message: ${job.key}.`);

      const completed = await waitFor(async () => {
        const response = await fetch(new URL(`/api/jobs/${encodeURIComponent(createdState.jobId)}`, ensureTrailingSlash(options.apiBaseUrl)), {
          headers: authorizedHeaders(options, accessToken, tokenType),
        });
        if (!response.ok) {
          throw new Error(`GET Excel output job ${createdState.jobId} failed with HTTP ${response.status}: ${await response.text()}`);
        }

        latestJob = await response.json();
        const status = String(latestJob.status ?? "").toLowerCase();
        if (status === "succeeded") {
          return latestJob;
        }

        if (status === "failed" || status === "canceled") {
          throw new Error(`Excel output job ${createdState.jobId} ended as ${latestJob.status}: ${latestJob.errorMessage || latestJob.detailText || ""}`);
        }

        return null;
      }, timeoutMs, () => `Timed out waiting for Excel output job to finish. Latest: ${JSON.stringify(latestJob)}`);

      const outputExists = existsSync(job.outputPath);
      const outputSize = outputExists ? await readFileSize(job.outputPath) : 0;
      const header = outputExists ? readFileSync(job.outputPath).subarray(0, 2).toString("ascii") : "";
      if (!outputExists || outputSize <= 0 || header !== "PK") {
        throw new Error(
          [
            "Job center Excel output smoke did not create the expected .xlsx output.",
            `key=${job.key}`,
            `exists=${outputExists}`,
            `size=${outputSize}`,
            `header=${header}`,
            `outputPath=${job.outputPath}`,
          ].join(" "),
        );
      }

      result = {
        outputState,
        jobId: completed.jobId,
        kind: completed.kind,
        expectedKind: job.expectedKind,
        status: completed.status,
        outputPath: completed.outputPath || job.outputPath,
        outputExists,
        outputSize,
        fileHeader: header,
        cleanedOutputFile,
        storagePolicy: "Excel 工具 smoke 只读取程序根 Resources/ExcelTemplates 内置模板，并只写运行数据根 smoke profile 下的显式临时 .xlsx 输出；任务完成后删除输出文件。",
      };
    } finally {
      if (existsSync(job.outputPath)) {
        cleanedOutputFile = cleanupSmokeFile(job.outputPath, options.userDataDir);
      } else {
        cleanedOutputFile = true;
      }

      if (result) {
        result.cleanedOutputFile = cleanedOutputFile;
      }
    }

    return result;
  }

  async function runJobCenterExcelBookingConvertJobCheck(page, options, accessToken, tokenType, timeoutMs, timestamp) {
    const outputPath = path.join(options.userDataDir, `job-center-booking-convert-${timestamp}.xlsx`);
    let sourceJob = null;
    let result = null;
    let cleanedOutputFile = false;
    let cleanedSourceFile = false;
    let latestUiState = null;
    let latestJob = null;

    try {
      sourceJob = await createSmokeJobCenterOutputJob(options, accessToken, tokenType, timeoutMs);
      const sourcePath = sourceJob.createdPath || sourceJob.outputPath;
      const sourceExists = existsSync(sourcePath);
      const sourceSize = sourceExists ? await readFileSize(sourcePath) : 0;
      const sourceHeader = sourceExists ? readFileSync(sourcePath).subarray(0, 2).toString("ascii") : "";
      if (!sourceExists || sourceSize <= 0 || sourceHeader !== "PK") {
        throw new Error(
          [
            "Job center booking convert smoke could not prepare a valid source .xlsx.",
            `exists=${sourceExists}`,
            `size=${sourceSize}`,
            `header=${sourceHeader}`,
            `sourcePath=${sourcePath}`,
          ].join(" "),
        );
      }

      const outputState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const sourcePath = ${JSON.stringify(sourcePath)};
              const outputPath = ${JSON.stringify(outputPath)};
              const panel = document.querySelector('[aria-label="Excel 模板与托单"]');
              const notifyReactChange = (control) => {
                const reactPropsKey = Object.keys(control).find((key) => key.startsWith("__reactProps$"));
                const reactProps = reactPropsKey ? control[reactPropsKey] : null;
                if (reactProps && typeof reactProps.onChange === "function") {
                  reactProps.onChange({ target: control, currentTarget: control });
                }
              };
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
                notifyReactChange(control);
              };
              const fieldByLabel = (labelText) => {
                const fields = Array.from(stack ? stack.querySelectorAll(".path-field") : []);
                const field = fields.find((item) =>
                  ((item.querySelector(".path-field-label") || {}).innerText || "").trim() === labelText);
                return field ? field.querySelector("input") : null;
              };
              const stack = Array.from(panel ? panel.querySelectorAll(".job-tool-stack") : [])
                .find((item) => ((item.querySelector(".job-tool-stack-title") || {}).innerText || "").includes("Excel 转托单"));

              if (!panel) {
                return { ready: false, reason: "Excel tools panel is not ready" };
              }
              if (!stack) {
                return { ready: false, reason: "Booking convert stack is not ready" };
              }
              const advanced = stack.querySelector("details");
              if (advanced) {
                advanced.open = true;
              }

              const sourceInput = fieldByLabel("转换源");
              const destinationInput = fieldByLabel("转换输出");
              const button = Array.from(stack.querySelectorAll("button"))
                .find((element) => (element.innerText || "").includes("按路径转换"));
              if (!sourceInput || !destinationInput || !button) {
                return {
                  ready: false,
                  reason: "Booking convert controls are not ready",
                  hasSourceInput: Boolean(sourceInput),
                  hasDestinationInput: Boolean(destinationInput),
                  hasButton: Boolean(button),
                };
              }

              if (sourceInput.value !== sourcePath) {
                setNativeValue(sourceInput, sourcePath);
              }
              if (destinationInput.value !== outputPath) {
                setNativeValue(destinationInput, outputPath);
              }

              return {
                ready: Boolean(!button.disabled && sourceInput.value === sourcePath && destinationInput.value === outputPath),
                reason: button.disabled ? "Booking convert button is disabled" : "",
                sourcePath: sourceInput.value,
                outputPath: destinationInput.value,
                buttonText: button.innerText || "",
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { ready: false, reason: String(error) } }));

        latestUiState = state.value ?? null;
        return latestUiState?.ready ? latestUiState : null;
      }, timeoutMs, () =>
        `Timed out preparing job center booking convert task. Latest UI state: ${JSON.stringify(latestUiState)}`);

      await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="Excel 模板与托单"]');
          const stack = Array.from(panel ? panel.querySelectorAll(".job-tool-stack") : [])
            .find((item) => ((item.querySelector(".job-tool-stack-title") || {}).innerText || "").includes("Excel 转托单"));
          const advanced = stack ? stack.querySelector("details") : null;
          if (advanced) {
            advanced.open = true;
          }
          const button = stack
            ? Array.from(stack.querySelectorAll("button")).find((element) => (element.innerText || "").includes("按路径转换"))
            : null;
          if (!button || button.disabled) {
            throw new Error("Booking convert button is not available.");
          }

          button.click();
          return true;
        })()`,
        true,
      );

      const createdState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const text = document.body ? document.body.innerText || "" : "";
              const match = text.match(/已创建托单转换任务：([^\\s\\n]+)/);
              return {
                jobId: match ? match[1] : "",
                textExcerpt: text.slice(0, 1200),
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { jobId: "", textExcerpt: String(error) } }));

        return state.value?.jobId ? state.value : null;
      }, timeoutMs, "Timed out waiting for the job center booking convert job creation message.");

      const completed = await waitFor(async () => {
        const response = await fetch(new URL(`/api/jobs/${encodeURIComponent(createdState.jobId)}`, ensureTrailingSlash(options.apiBaseUrl)), {
          headers: authorizedHeaders(options, accessToken, tokenType),
        });
        if (!response.ok) {
          throw new Error(`GET booking convert job ${createdState.jobId} failed with HTTP ${response.status}: ${await response.text()}`);
        }

        latestJob = await response.json();
        const status = String(latestJob.status ?? "").toLowerCase();
        if (status === "succeeded") {
          return latestJob;
        }

        if (status === "failed" || status === "canceled") {
          throw new Error(`Booking convert job ${createdState.jobId} ended as ${latestJob.status}: ${latestJob.errorMessage || latestJob.detailText || ""}`);
        }

        return null;
      }, timeoutMs, () => `Timed out waiting for booking convert job to finish. Latest: ${JSON.stringify(latestJob)}`);

      const outputExists = existsSync(outputPath);
      const outputSize = outputExists ? await readFileSize(outputPath) : 0;
      const header = outputExists ? readFileSync(outputPath).subarray(0, 2).toString("ascii") : "";
      if (!outputExists || outputSize <= 0 || header !== "PK") {
        throw new Error(
          [
            "Job center booking convert smoke did not create the expected .xlsx output.",
            `exists=${outputExists}`,
            `size=${outputSize}`,
            `header=${header}`,
            `outputPath=${outputPath}`,
          ].join(" "),
        );
      }

      result = {
        sourceJobId: sourceJob.jobId,
        sourceKind: sourceJob.kind,
        sourceStatus: sourceJob.status,
        sourcePath,
        sourceSize,
        sourceFileHeader: sourceHeader,
        outputState,
        jobId: completed.jobId,
        kind: completed.kind,
        expectedKind: "BookingSheetConvert",
        status: completed.status,
        outputPath: completed.outputPath || outputPath,
        outputExists,
        outputSize,
        fileHeader: header,
        cleanedSourceFile,
        cleanedOutputFile,
        storagePolicy: "托单转换 smoke 使用任务生成的运行数据根临时 Excel 作为源文件，并只写运行数据根下显式临时 .xlsx 输出；任务完成后删除源文件和输出文件。",
      };
    } finally {
      if (existsSync(outputPath)) {
        cleanedOutputFile = cleanupSmokeFile(outputPath, options.userDataDir);
      } else {
        cleanedOutputFile = true;
      }

      const sourcePath = sourceJob?.createdPath || sourceJob?.outputPath;
      if (sourcePath && existsSync(sourcePath)) {
        cleanedSourceFile = cleanupSmokeFile(sourcePath, options.userDataDir);
      } else {
        cleanedSourceFile = Boolean(sourcePath);
      }

      if (result) {
        result.cleanedSourceFile = cleanedSourceFile;
        result.cleanedOutputFile = cleanedOutputFile;
      }
    }

    return result;
  }

  async function runJobCenterInvoiceBookingExportJobCheck(page, options, accessToken, tokenType, timeoutMs, timestamp) {
    const outputPath = path.join(options.userDataDir, `job-center-invoice-booking-${timestamp}.xlsx`);
    let invoice = null;
    let result = null;
    let cleanedOutputFile = false;
    let deletedInvoice = false;
    let latestUiState = null;
    let latestJob = null;

    try {
      invoice = await createSmokeInvoice(options, accessToken, tokenType);
      if (!invoice?.id) {
        throw new Error(`Invoice booking smoke create response did not include invoice id: ${JSON.stringify(invoice)}`);
      }

      const invoiceId = String(invoice.id);
      await page.send("Page.navigate", { url: buildExcelToolsCheckUrl(options.webUrl) });
      await page.send("Page.reload", { ignoreCache: true });
      await waitForRuntimeDiagnostics(page, ["Excel 模板与托单", "发票导出托单"], timeoutMs);
      const outputState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const invoiceId = ${JSON.stringify(invoiceId)};
              const outputPath = ${JSON.stringify(outputPath)};
              const panel = document.querySelector('[aria-label="Excel 模板与托单"]');
              const notifyReactChange = (control) => {
                const reactPropsKey = Object.keys(control).find((key) => key.startsWith("__reactProps$"));
                const reactProps = reactPropsKey ? control[reactPropsKey] : null;
                if (reactProps && typeof reactProps.onChange === "function") {
                  reactProps.onChange({ target: control, currentTarget: control });
                }
              };
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
                notifyReactChange(control);
              };
              const fieldByLabel = (labelText) => {
                const fields = Array.from(stack ? stack.querySelectorAll(".path-field") : []);
                const field = fields.find((item) =>
                  ((item.querySelector(".path-field-label") || {}).innerText || "").trim() === labelText);
                return field ? field.querySelector("input") : null;
              };
              const labelSelectByText = (labelText) => {
                const labels = Array.from(stack ? stack.querySelectorAll("label") : []);
                const label = labels.find((item) => ((item.querySelector("span") || {}).innerText || "").trim() === labelText);
                return label ? label.querySelector("select") : null;
              };
              const stack = Array.from(panel ? panel.querySelectorAll(".job-tool-stack") : [])
                .find((item) => ((item.querySelector(".job-tool-stack-title") || {}).innerText || "").includes("发票导出托单"));

              if (!panel) {
                return { ready: false, reason: "Excel tools panel is not ready" };
              }
              if (!stack) {
                return { ready: false, reason: "Invoice booking stack is not ready" };
              }
              const advanced = stack.querySelector("details");
              if (advanced) {
                advanced.open = true;
              }

              const invoiceSelect = labelSelectByText("发票");
              const destinationInput = fieldByLabel("托单输出");
              const button = Array.from(stack.querySelectorAll("button"))
                .find((element) => (element.innerText || "").includes("按路径导出"));
              const hasInvoiceOption = Boolean(invoiceSelect && Array.from(invoiceSelect.options).some((option) => option.value === invoiceId));
              if (!invoiceSelect || !destinationInput || !button || !hasInvoiceOption) {
                return {
                  ready: false,
                  reason: "Invoice booking export controls are not ready",
                  hasInvoiceSelect: Boolean(invoiceSelect),
                  hasInvoiceOption,
                  hasDestinationInput: Boolean(destinationInput),
                  hasButton: Boolean(button),
                };
              }

              if (invoiceSelect.value !== invoiceId) {
                setNativeValue(invoiceSelect, invoiceId);
              }
              if (destinationInput.value !== outputPath) {
                setNativeValue(destinationInput, outputPath);
              }

              return {
                ready: Boolean(!button.disabled && invoiceSelect.value === invoiceId && destinationInput.value === outputPath),
                reason: button.disabled ? "Invoice booking export button is disabled" : "",
                invoiceId: invoiceSelect.value,
                outputPath: destinationInput.value,
                buttonText: button.innerText || "",
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { ready: false, reason: String(error) } }));

        latestUiState = state.value ?? null;
        return latestUiState?.ready ? latestUiState : null;
      }, timeoutMs, () =>
        `Timed out preparing job center invoice booking export task. Latest UI state: ${JSON.stringify(latestUiState)}`);

      await evaluate(
        page,
        `(() => {
          const panel = document.querySelector('[aria-label="Excel 模板与托单"]');
          const stack = Array.from(panel ? panel.querySelectorAll(".job-tool-stack") : [])
            .find((item) => ((item.querySelector(".job-tool-stack-title") || {}).innerText || "").includes("发票导出托单"));
          const advanced = stack ? stack.querySelector("details") : null;
          if (advanced) {
            advanced.open = true;
          }
          const button = stack
            ? Array.from(stack.querySelectorAll("button")).find((element) => (element.innerText || "").includes("按路径导出"))
            : null;
          if (!button || button.disabled) {
            throw new Error("Invoice booking export button is not available.");
          }

          button.click();
          return true;
        })()`,
        true,
      );

      const createdState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const text = document.body ? document.body.innerText || "" : "";
              const match = text.match(/已创建发票托单导出任务：([^\\s\\n]+)/);
              return {
                jobId: match ? match[1] : "",
                textExcerpt: text.slice(0, 1200),
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { jobId: "", textExcerpt: String(error) } }));

        return state.value?.jobId ? state.value : null;
      }, timeoutMs, "Timed out waiting for the job center invoice booking export job creation message.");

      const completed = await waitFor(async () => {
        const response = await fetch(new URL(`/api/jobs/${encodeURIComponent(createdState.jobId)}`, ensureTrailingSlash(options.apiBaseUrl)), {
          headers: authorizedHeaders(options, accessToken, tokenType),
        });
        if (!response.ok) {
          throw new Error(`GET invoice booking export job ${createdState.jobId} failed with HTTP ${response.status}: ${await response.text()}`);
        }

        latestJob = await response.json();
        const status = String(latestJob.status ?? "").toLowerCase();
        if (status === "succeeded") {
          return latestJob;
        }

        if (status === "failed" || status === "canceled") {
          throw new Error(`Invoice booking export job ${createdState.jobId} ended as ${latestJob.status}: ${latestJob.errorMessage || latestJob.detailText || ""}`);
        }

        return null;
      }, timeoutMs, () => `Timed out waiting for invoice booking export job to finish. Latest: ${JSON.stringify(latestJob)}`);

      const outputExists = existsSync(outputPath);
      const outputSize = outputExists ? await readFileSize(outputPath) : 0;
      const header = outputExists ? readFileSync(outputPath).subarray(0, 2).toString("ascii") : "";
      if (!outputExists || outputSize <= 0 || header !== "PK") {
        throw new Error(
          [
            "Job center invoice booking export smoke did not create the expected .xlsx output.",
            `exists=${outputExists}`,
            `size=${outputSize}`,
            `header=${header}`,
            `outputPath=${outputPath}`,
          ].join(" "),
        );
      }

      result = {
        outputState,
        invoiceId: invoice.id,
        invoiceNo: invoice.invoiceNo,
        jobId: completed.jobId,
        kind: completed.kind,
        expectedKind: "InvoiceBookingSheetExport",
        status: completed.status,
        outputPath: completed.outputPath || outputPath,
        outputExists,
        outputSize,
        fileHeader: header,
        cleanedOutputFile,
        deletedInvoice,
        dataBoundary: "发票托单导出 smoke 只使用 invoice.id 读取单据数据；发票号仅作为结果记录，不作为跨域或付款/报销查询键。",
        storagePolicy: "发票托单导出 smoke 只写运行数据根 smoke profile 下的显式临时 .xlsx 输出；任务完成后删除输出文件和临时发票。",
      };
    } finally {
      if (existsSync(outputPath)) {
        cleanedOutputFile = cleanupSmokeFile(outputPath, options.userDataDir);
      } else {
        cleanedOutputFile = true;
      }

      if (invoice?.id) {
        deletedInvoice = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
      } else {
        deletedInvoice = true;
      }

      if (result) {
        result.cleanedOutputFile = cleanedOutputFile;
        result.deletedInvoice = deletedInvoice;
      }
    }

    return result;
  }

  async function waitForJobCenterPdfMergeJobCheck(page, options, accessToken, tokenType, timeoutMs) {
    const timestamp = Date.now();
    const sourcePaths = [
      path.join(options.userDataDir, `job-center-pdf-merge-source-a-${timestamp}.pdf`),
      path.join(options.userDataDir, `job-center-pdf-merge-source-b-${timestamp}.pdf`),
    ];
    const outputPath = path.join(options.userDataDir, `job-center-pdf-merge-output-${timestamp}.pdf`);
    let result = null;
    let cleanedOutputFile = false;
    let cleanedSourceFiles = [];
    let latestUiState = null;
    let latestJob = null;

    try {
      writeFileSync(sourcePaths[0], createSmokePdfBytes(`Job Center PDF Merge A ${timestamp}`));
      writeFileSync(sourcePaths[1], createSmokePdfBytes(`Job Center PDF Merge B ${timestamp}`));

      const sourcePathsText = sourcePaths.join("\n");
      const outputState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const sourcePathsText = ${JSON.stringify(sourcePathsText)};
              const outputPath = ${JSON.stringify(outputPath)};
              const form = document.querySelector('[aria-label="PDF 合并任务"]');
              const notifyReactChange = (control) => {
                const reactPropsKey = Object.keys(control).find((key) => key.startsWith("__reactProps$"));
                const reactProps = reactPropsKey ? control[reactPropsKey] : null;
                if (reactProps && typeof reactProps.onChange === "function") {
                  reactProps.onChange({ target: control, currentTarget: control });
                }
              };
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
                notifyReactChange(control);
              };
              const fieldByLabel = (labelText, selector) => {
                const fields = Array.from(form ? form.querySelectorAll(".path-field") : []);
                const field = fields.find((item) =>
                  ((item.querySelector(".path-field-label") || {}).innerText || "").trim() === labelText);
                return field ? field.querySelector(selector) : null;
              };

              if (!form) {
                return { ready: false, reason: "PDF merge form is not ready" };
              }

              const sourceInput = fieldByLabel("源 PDF", "textarea");
              const destination = fieldByLabel("输出 PDF", "input");
              const button = Array.from(form.querySelectorAll("button"))
                .find((element) => (element.innerText || "").includes("开始"));
              if (!sourceInput || !destination || !button) {
                return {
                  ready: false,
                  reason: "PDF merge inputs are not ready",
                  hasSourceInput: Boolean(sourceInput),
                  hasDestination: Boolean(destination),
                  hasButton: Boolean(button),
                };
              }

              if (sourceInput.value !== sourcePathsText) {
                setNativeValue(sourceInput, sourcePathsText);
              }
              if (destination.value !== outputPath) {
                setNativeValue(destination, outputPath);
              }

              return {
                ready: Boolean(!button.disabled && sourceInput.value === sourcePathsText && destination.value === outputPath),
                reason: button.disabled ? "PDF merge start button is disabled" : "",
                sourcePaths: sourceInput.value,
                destinationPath: destination.value,
                sourceCountText: ((form.querySelector(".tool-panel-heading span") || {}).innerText || "").trim(),
                buttonText: button.innerText || "",
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { ready: false, reason: String(error) } }));

        latestUiState = state.value ?? null;
        return latestUiState?.ready ? latestUiState : null;
      }, timeoutMs, () =>
        `Timed out preparing job center PDF merge task. Latest UI state: ${JSON.stringify(latestUiState)}`);

      await evaluate(
        page,
        `(() => {
          const form = document.querySelector('[aria-label="PDF 合并任务"]');
          const button = form
            ? Array.from(form.querySelectorAll("button")).find((element) => (element.innerText || "").includes("开始"))
            : null;
          if (!button || button.disabled) {
            throw new Error("PDF merge start button is not available.");
          }

          button.click();
          return true;
        })()`,
        true,
      );

      const createdState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const text = document.body ? document.body.innerText || "" : "";
              const match = text.match(/已创建 PDF 合并任务：([^\\s\\n]+)/);
              return {
                jobId: match ? match[1] : "",
                textExcerpt: text.slice(0, 1200),
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { jobId: "", textExcerpt: String(error) } }));

        return state.value?.jobId ? state.value : null;
      }, timeoutMs, () => "Timed out waiting for the job center PDF merge job creation message.");

      const completed = await waitFor(async () => {
        const response = await fetch(new URL(`/api/jobs/${encodeURIComponent(createdState.jobId)}`, ensureTrailingSlash(options.apiBaseUrl)), {
          headers: authorizedHeaders(options, accessToken, tokenType),
        });
        if (!response.ok) {
          throw new Error(`GET PDF merge job ${createdState.jobId} failed with HTTP ${response.status}: ${await response.text()}`);
        }

        latestJob = await response.json();
        const status = String(latestJob.status ?? "").toLowerCase();
        if (status === "succeeded") {
          return latestJob;
        }

        if (status === "failed" || status === "canceled") {
          throw new Error(`PDF merge job ${createdState.jobId} ended as ${latestJob.status}: ${latestJob.errorMessage || latestJob.detailText || ""}`);
        }

        return null;
      }, timeoutMs, () => `Timed out waiting for PDF merge job to finish. Latest: ${JSON.stringify(latestJob)}`);

      const outputExists = existsSync(outputPath);
      const outputSize = outputExists ? await readFileSize(outputPath) : 0;
      const header = outputExists ? readFileSync(outputPath).subarray(0, 4).toString("ascii") : "";
      if (!outputExists || outputSize <= 0 || header !== "%PDF") {
        throw new Error(
          [
            "Job center PDF merge smoke did not create the expected PDF output.",
            `exists=${outputExists}`,
            `size=${outputSize}`,
            `header=${header}`,
            `outputPath=${outputPath}`,
          ].join(" "),
        );
      }

      result = {
        outputState,
        jobId: completed.jobId,
        kind: completed.kind,
        status: completed.status,
        sourcePaths,
        outputPath: completed.outputPath || outputPath,
        outputExists,
        outputSize,
        pdfHeader: header,
        cleanedOutputFile,
        cleanedSourceFiles,
        storagePolicy: "PDF 合并 smoke 只读取运行数据根 smoke profile 下的显式源 PDF，并只写同一 profile 下的显式临时输出 PDF；任务完成后删除源文件和输出文件。",
      };
    } finally {
      if (existsSync(outputPath)) {
        cleanedOutputFile = cleanupSmokeFile(outputPath, options.userDataDir);
      } else {
        cleanedOutputFile = true;
      }

      cleanedSourceFiles = sourcePaths.map((sourcePath) => {
        if (!existsSync(sourcePath)) {
          return { path: sourcePath, cleaned: true };
        }

        return { path: sourcePath, cleaned: cleanupSmokeFile(sourcePath, options.userDataDir) };
      });

      if (result) {
        result.cleanedOutputFile = cleanedOutputFile;
        result.cleanedSourceFiles = cleanedSourceFiles;
      }
    }

    return result;
  }

  function createSmokePdfBytes(title) {
    const text = escapePdfText(title || "ExportDocManager smoke PDF");
    const stream = `BT /F1 12 Tf 36 120 Td (${text}) Tj ET`;
    const objects = [
      "<< /Type /Catalog /Pages 2 0 R >>",
      "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
      "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 180] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
      "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
      `<< /Length ${Buffer.byteLength(stream, "ascii")} >>\nstream\n${stream}\nendstream`,
    ];

    let pdf = "%PDF-1.4\n";
    const offsets = [0];
    for (let index = 0; index < objects.length; index++) {
      offsets.push(Buffer.byteLength(pdf, "ascii"));
      pdf += `${index + 1} 0 obj\n${objects[index]}\nendobj\n`;
    }

    const xrefOffset = Buffer.byteLength(pdf, "ascii");
    pdf += `xref\n0 ${objects.length + 1}\n`;
    pdf += "0000000000 65535 f \n";
    for (let index = 1; index < offsets.length; index++) {
      pdf += `${String(offsets[index]).padStart(10, "0")} 00000 n \n`;
    }

    pdf += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\n`;
    pdf += `startxref\n${xrefOffset}\n%%EOF\n`;
    return Buffer.from(pdf, "ascii");
  }

  function escapePdfText(value) {
    return String(value ?? "")
      .replace(/[^\x20-\x7e]/g, " ")
      .replace(/\\/g, "\\\\")
      .replace(/\(/g, "\\(")
      .replace(/\)/g, "\\)");
  }

  async function waitForJobCenterReportBatchZipJobCheck(page, options, accessToken, tokenType, timeoutMs) {
    const invoices = [];
    const outputPath = path.join(options.userDataDir, `job-center-report-batch-${Date.now()}.zip`);
    let result = null;
    let cleanedOutputFile = false;
    let latestUiState = null;
    let latestJob = null;
    let deletedInvoices = [];

    try {
      invoices.push(await createSmokeInvoice(options, accessToken, tokenType));
      invoices.push(await createSmokeInvoice(options, accessToken, tokenType));

      const invoiceIdsText = invoices.map((invoice) => String(invoice.id)).join("\n");
      const outputState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const invoiceIdsText = ${JSON.stringify(invoiceIdsText)};
              const outputPath = ${JSON.stringify(outputPath)};
              const form = document.querySelector('[aria-label="批量报表 ZIP 任务"]');
              const notifyReactChange = (control) => {
                const reactPropsKey = Object.keys(control).find((key) => key.startsWith("__reactProps$"));
                const reactProps = reactPropsKey ? control[reactPropsKey] : null;
                if (reactProps && typeof reactProps.onChange === "function") {
                  reactProps.onChange({ target: control, currentTarget: control });
                }
              };
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
                notifyReactChange(control);
              };
              const fieldByLabel = (labelText, selector) => {
                const fields = Array.from(form ? form.querySelectorAll(".path-field") : []);
                const field = fields.find((item) =>
                  ((item.querySelector(".path-field-label") || {}).innerText || "").trim() === labelText);
                return field ? field.querySelector(selector) : null;
              };

              if (!form) {
                return { ready: false, reason: "batch report ZIP form is not ready" };
              }

              const invoiceIds = fieldByLabel("发票 ID", "textarea");
              const destination = fieldByLabel("输出 ZIP", "input");
              const template = form.querySelector("select");
              const button = Array.from(form.querySelectorAll("button"))
                .find((element) => (element.innerText || "").includes("开始"));
              if (!invoiceIds || !destination || !template || !button) {
                return {
                  ready: false,
                  reason: "batch report ZIP inputs are not ready",
                  hasInvoiceIds: Boolean(invoiceIds),
                  hasDestination: Boolean(destination),
                  hasTemplate: Boolean(template),
                  hasButton: Boolean(button),
                };
              }

              if (invoiceIds.value !== invoiceIdsText) {
                setNativeValue(invoiceIds, invoiceIdsText);
              }
              if (destination.value !== outputPath) {
                setNativeValue(destination, outputPath);
              }

              return {
                ready: Boolean(!button.disabled && invoiceIds.value === invoiceIdsText && destination.value === outputPath && template.value),
                reason: button.disabled ? "batch report ZIP start button is disabled" : "",
                invoiceIds: invoiceIds.value,
                destinationPath: destination.value,
                templatePath: template.value,
                invoiceCountText: ((form.querySelector(".tool-panel-heading span") || {}).innerText || "").trim(),
                buttonText: button.innerText || "",
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { ready: false, reason: String(error) } }));

        latestUiState = state.value ?? null;
        return latestUiState?.ready ? latestUiState : null;
      }, timeoutMs, () =>
        `Timed out preparing job center batch report ZIP task. Latest UI state: ${JSON.stringify(latestUiState)}`);

      await evaluate(
        page,
        `(() => {
          const form = document.querySelector('[aria-label="批量报表 ZIP 任务"]');
          const button = form
            ? Array.from(form.querySelectorAll("button")).find((element) => (element.innerText || "").includes("开始"))
            : null;
          if (!button || button.disabled) {
            throw new Error("Batch report ZIP start button is not available.");
          }

          button.click();
          return true;
        })()`,
        true,
      );

      const createdState = await waitFor(async () => {
        const state = await evaluate(
            page,
            `(() => {
              const text = document.body ? document.body.innerText || "" : "";
              const match = text.match(/已创建批量报表 ZIP 任务：([^\\s\\n]+)/);
              return {
                jobId: match ? match[1] : "",
                textExcerpt: text.slice(0, 1200),
              };
            })()`,
            true,
          )
          .catch((error) => ({ value: { jobId: "", textExcerpt: String(error) } }));

        return state.value?.jobId ? state.value : null;
      }, timeoutMs, () => "Timed out waiting for the job center batch report ZIP job creation message.");

      const completed = await waitFor(async () => {
        const response = await fetch(new URL(`/api/jobs/${encodeURIComponent(createdState.jobId)}`, ensureTrailingSlash(options.apiBaseUrl)), {
          headers: authorizedHeaders(options, accessToken, tokenType),
        });
        if (!response.ok) {
          throw new Error(`GET batch report ZIP job ${createdState.jobId} failed with HTTP ${response.status}: ${await response.text()}`);
        }

        latestJob = await response.json();
        const status = String(latestJob.status ?? "").toLowerCase();
        if (status === "succeeded") {
          return latestJob;
        }

        if (status === "failed" || status === "canceled") {
          throw new Error(`Batch report ZIP job ${createdState.jobId} ended as ${latestJob.status}: ${latestJob.errorMessage || latestJob.detailText || ""}`);
        }

        return null;
      }, timeoutMs, () => `Timed out waiting for batch report ZIP job to finish. Latest: ${JSON.stringify(latestJob)}`);

      const outputExists = existsSync(outputPath);
      const outputSize = outputExists ? await readFileSize(outputPath) : 0;
      const header = outputExists ? readFileSync(outputPath).subarray(0, 2).toString("ascii") : "";
      const cacheRoot = path.join(path.dirname(options.userDataDir), "Cache", "ReportBatchZip", completed.jobId);
      const tempCacheCleaned = !existsSync(cacheRoot);
      if (!outputExists || outputSize <= 0 || header !== "PK" || !tempCacheCleaned) {
        throw new Error(
          [
            "Job center batch report ZIP smoke did not create the expected ZIP output.",
            `exists=${outputExists}`,
            `size=${outputSize}`,
            `header=${header}`,
            `tempCacheCleaned=${tempCacheCleaned}`,
            `outputPath=${outputPath}`,
          ].join(" "),
        );
      }

      result = {
        outputState,
        jobId: completed.jobId,
        kind: completed.kind,
        status: completed.status,
        invoiceIds: invoices.map((invoice) => invoice.id),
        invoiceNos: invoices.map((invoice) => invoice.invoiceNo),
        outputPath: completed.outputPath || outputPath,
        outputExists,
        outputSize,
        zipHeader: header,
        cacheRoot,
        tempCacheCleaned,
        cleanedOutputFile,
        deletedInvoices,
        dataBoundary: "批量报表 ZIP smoke 仅按 invoice.id 读取发票/报关单据数据，不用 InvoiceNo 关联付款/报销单据。",
        storagePolicy: "批量报表 ZIP smoke 只写运行数据根 smoke profile 下的显式临时 .zip；中间 PDF 缓存在运行缓存根 Cache/ReportBatchZip/{jobId}，任务完成后必须清理。",
      };
    } finally {
      if (existsSync(outputPath)) {
        cleanedOutputFile = cleanupSmokeFile(outputPath, options.userDataDir);
      } else {
        cleanedOutputFile = true;
      }

      deletedInvoices = [];
      for (const invoice of invoices) {
        const deleted = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
        deletedInvoices.push({ id: invoice.id, invoiceNo: invoice.invoiceNo, deleted });
      }

      if (result) {
        result.cleanedOutputFile = cleanedOutputFile;
        result.deletedInvoices = deletedInvoices;
      }
    }

    return result;
  }

  async function waitForContainerPackingProjectCrudCheck(page, timeoutMs) {
    const payload = {
      projectName: `Tauri Container Project Smoke ${Date.now()}-${Math.floor(Math.random() * 100000)}`,
      changedProjectName: `Unsaved Container Project ${Date.now()}`,
      cargoName: `Smoke Cargo ${Math.floor(Math.random() * 100000)}`,
      changedCargoName: `Unsaved Cargo ${Math.floor(Math.random() * 100000)}`,
    };

    const action = await evaluate(
      page,
      `(async (payload) => {
        const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
        const panel = document.querySelector('[aria-label="装箱分析"]');
        if (!panel) {
          throw new Error("装箱分析区域未找到。");
        }

        const notifyReactChange = (control) => {
          const reactPropsKey = Object.keys(control).find((key) => key.startsWith("__reactProps$"));
          const reactProps = reactPropsKey ? control[reactPropsKey] : null;
          if (reactProps && typeof reactProps.onChange === "function") {
            reactProps.onChange({ target: control, currentTarget: control });
          }
        };

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
          notifyReactChange(control);
        };

        const fieldByLabel = (labelText, selector = "input, select, textarea") => {
          const labels = Array.from(panel.querySelectorAll("label"));
          const label = labels.find((item) =>
            Array.from(item.querySelectorAll("span")).some((span) => (span.textContent || "").trim() === labelText));
          if (!label) {
            throw new Error("字段未找到: " + labelText);
          }

          const control = label.querySelector(selector);
          if (!control) {
            throw new Error("字段没有控件: " + labelText);
          }

          return control;
        };

        const firstCargoRow = () => {
          const row = panel.querySelector(".container-packing-cargo-table tbody tr:not(:has(.empty-cell))");
          if (!row) {
            throw new Error("装柜货物行未找到。");
          }

          return row;
        };

        const firstCargoNameInput = () => {
          const input = firstCargoRow().querySelector("td:nth-child(2) input");
          if (!input) {
            throw new Error("装柜货物名称输入框未找到。");
          }

          return input;
        };

        const firstCargoQuantityInput = () => {
          const input = firstCargoRow().querySelector("td:nth-child(7) input");
          if (!input) {
            throw new Error("装柜货物数量输入框未找到。");
          }

          return input;
        };

        const firstCargoMaxTopLoadInput = () => {
          const input = firstCargoRow().querySelector("td:nth-child(11) input");
          if (!input) {
            throw new Error("装柜货物顶载输入框未找到。");
          }

          return input;
        };

        const buttons = () => Array.from(panel.querySelectorAll("button"));
        const findButton = (text) => buttons().find((button) => (button.innerText || "").includes(text));
        const clickButton = (text) => {
          const button = findButton(text);
          if (!button) {
            throw new Error("按钮未找到: " + text);
          }
          if (button.disabled) {
            throw new Error("按钮不可用: " + text);
          }

          button.click();
          return button.innerText || text;
        };

        const waitForCondition = async (predicate, description) => {
          const deadline = Date.now() + ${Number(timeoutMs)};
          let latest = {};
          while (Date.now() < deadline) {
            latest = {
              projectName: fieldByLabel("方案名称").value || "",
              cargoName: firstCargoNameInput().value || "",
              selectedProjectId: fieldByLabel("已存方案", "select").value || "",
              message: Array.from(panel.querySelectorAll(".success-alert, .alert")).map((item) => item.innerText || "").join("\\n"),
              optionText: Array.from(fieldByLabel("已存方案", "select").options).map((option) => option.textContent || "").join("\\n"),
            };

            if (predicate(latest)) {
              return latest;
            }

            await delay(120);
          }

          throw new Error("等待装柜方案状态超时: " + description + " latest=" + JSON.stringify(latest));
        };

        const projectNameInput = fieldByLabel("方案名称");
        const projectSelect = fieldByLabel("已存方案", "select");
        setNativeValue(projectNameInput, payload.projectName);
        setNativeValue(firstCargoNameInput(), payload.cargoName);
        setNativeValue(firstCargoQuantityInput(), "10");
        setNativeValue(firstCargoMaxTopLoadInput(), "1000");
        await delay(100);

        const saveButtonText = clickButton("保存方案");
        const savedState = await waitForCondition(
          (state) => state.optionText.includes(payload.projectName) && state.message.includes("装柜方案已保存"),
          "保存装柜方案",
        );
        const savedOption = Array.from(projectSelect.options).find((option) => (option.textContent || "").includes(payload.projectName));
        if (!savedOption) {
          throw new Error("保存后的装柜方案选项未找到。");
        }

        setNativeValue(projectSelect, savedOption.value);
        setNativeValue(projectNameInput, payload.changedProjectName);
        setNativeValue(firstCargoNameInput(), payload.changedCargoName);
        await delay(100);

        const loadButtonText = clickButton("加载方案");
        const loadedState = await waitForCondition(
          (state) => state.projectName === payload.projectName && state.cargoName === payload.cargoName && state.message.includes("装柜方案已加载"),
          "加载装柜方案",
        );

        let confirmMessage = "";
        const originalConfirm = window.confirm;
        window.confirm = (message) => {
          confirmMessage = String(message || "");
          return true;
        };
        try {
          const refreshedOption = Array.from(projectSelect.options).find((option) => (option.textContent || "").includes(payload.projectName));
          if (refreshedOption) {
            setNativeValue(projectSelect, refreshedOption.value);
          }
          const deleteButtonText = clickButton("删除方案");
          const deletedState = await waitForCondition(
            (state) => !state.optionText.includes(payload.projectName) && state.message.includes("装柜方案已删除"),
            "删除装柜方案",
          );

          return {
            projectName: payload.projectName,
            savedProjectId: Number(savedOption.value || 0),
            saveButtonText,
            loadButtonText,
            deleteButtonText,
            confirmMessage,
            savedState,
            loadedState,
            deletedState,
            saved: true,
            loaded: true,
            deleted: true,
          };
        } finally {
          window.confirm = originalConfirm;
        }
      })(${JSON.stringify(payload)})`,
      true,
    );

    return action.value ?? {
      projectName: payload.projectName,
      saved: true,
      loaded: true,
      deleted: true,
    };
  }

  async function createSmokeJobCenterOutputJob(options, accessToken, tokenType, timeoutMs) {
    const outputPath = path.join(options.userDataDir, `job-center-output-${Date.now()}.xlsx`);
    const response = await fetch(new URL("/api/tools/excel/template/save-to-path", ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify({
        destinationPath: outputPath,
      }),
    });

    if (!response.ok) {
      throw new Error(`Job center smoke output job create failed with HTTP ${response.status}: ${await response.text()}`);
    }

    const accepted = await response.json();
    if (!accepted?.jobId) {
      throw new Error(`Job center smoke output job response did not include jobId: ${JSON.stringify(accepted)}`);
    }

    let latestJob = accepted;
    const completed = await waitFor(async () => {
      const jobResponse = await fetch(new URL(`/api/jobs/${encodeURIComponent(accepted.jobId)}`, ensureTrailingSlash(options.apiBaseUrl)), {
        headers: authorizedHeaders(options, accessToken, tokenType),
      });
      if (!jobResponse.ok) {
        throw new Error(`GET smoke job ${accepted.jobId} failed with HTTP ${jobResponse.status}: ${await jobResponse.text()}`);
      }

      latestJob = await jobResponse.json();
      const status = String(latestJob.status ?? "").toLowerCase();
      if (status === "succeeded") {
        return latestJob;
      }

      if (status === "failed" || status === "canceled") {
        throw new Error(`Smoke job ${accepted.jobId} ended as ${latestJob.status}: ${latestJob.errorMessage || latestJob.detailText || ""}`);
      }

      return null;
    }, timeoutMs, () => `Timed out waiting for job center smoke output job to finish. Latest: ${JSON.stringify(latestJob)}`);

    return {
      jobId: completed.jobId,
      kind: completed.kind,
      status: completed.status,
      outputPath: completed.outputPath || outputPath,
      createdPath: outputPath,
    };
  }

  async function waitForJobOutputOpenPathAction(page, expectedPath, timeoutMs) {
    let latestClickResult = null;
    let latestInvocations = [];
    const expectedPathKey = normalizePathForCompare(expectedPath);

    const clickResult = await waitFor(async () => {
      const result = await evaluate(
        page,
        `(() => {
          const expectedPath = ${JSON.stringify(expectedPath)};
          const rows = Array.from(document.querySelectorAll('.job-table tbody tr'));
          const row = rows.find((candidate) => {
            const pathCell = candidate.querySelector('.path-cell');
            const title = pathCell ? pathCell.getAttribute('title') || '' : '';
            const text = pathCell ? pathCell.innerText || '' : '';
            return title === expectedPath || text.includes(expectedPath);
          });
          if (!row) {
            return {
              found: false,
              reason: 'missing output job row',
              pathTitles: rows.map((candidate) => candidate.querySelector('.path-cell')?.getAttribute('title') || '')
            };
          }

          const button = Array.from(row.querySelectorAll('button')).find((candidate) => (candidate.title || '') === '打开任务输出');
          if (!button || button.disabled) {
            return {
              found: false,
              reason: 'missing enabled output open button',
              rowText: row.innerText || '',
              buttonTitles: Array.from(row.querySelectorAll('button')).map((candidate) => candidate.title || candidate.getAttribute('aria-label') || '')
            };
          }

          window.__exportDocManagerSmokeTauriInvocations = [];
          button.click();
          return {
            found: true,
            title: button.title || button.getAttribute('aria-label') || '',
            rowText: row.innerText || ''
          };
        })()`,
        true,
      ).catch((error) => ({ value: { found: false, reason: error.message } }));
      latestClickResult = result.value ?? null;
      return latestClickResult?.found ? latestClickResult : null;
    }, timeoutMs, () => `Timed out waiting for job output open path button. Latest: ${JSON.stringify(latestClickResult)}`);

    const invocations = await waitFor(async () => {
      const result = await evaluate(
        page,
        "window.__exportDocManagerSmokeTauriInvocations || []",
        true,
      ).catch(() => ({ value: [] }));
      latestInvocations = Array.isArray(result.value) ? result.value : [];
      const matched = latestInvocations.find((item) =>
        item?.command === "open_path" &&
        normalizePathForCompare(item?.args?.path) === expectedPathKey);
      return matched ? [matched] : null;
    }, timeoutMs, () => {
      const opened = latestInvocations
        .filter((item) => item?.command === "open_path")
        .map((item) => item?.args?.path)
        .filter(Boolean);
      return [
        "Timed out waiting for mocked Tauri open_path call from job output action.",
        `Expected: ${JSON.stringify(expectedPath)}`,
        `Opened: ${JSON.stringify(opened)}`,
      ].join("\n");
    });

    return {
      clickedTitle: clickResult.title,
      expectedPath,
      invocations: invocations.map((item) => ({
        command: item.command,
        path: item.args?.path ?? "",
      })),
    };
  }

  async function waitForContainerPacking3dCanvas(page, timeoutMs, viewportName) {
    return waitForPageExpression(
      page,
      `(() => {
        const panel = document.querySelector('[aria-label="装箱分析"]');
        const visualization = panel ? panel.querySelector('[aria-label="装柜三维可视化"]') : null;
        const canvas = visualization ? visualization.querySelector('canvas[aria-label="装柜三维画布"]') : null;
        if (!canvas || canvas.dataset.sceneReady !== 'true' || Number(canvas.dataset.packedItems || '0') <= 0) {
          return false;
        }

        const rect = canvas.getBoundingClientRect();
        if (rect.width < 240 || rect.height < 220 || canvas.width < 120 || canvas.height < 120) {
          return false;
        }

        const snapshot = document.createElement('canvas');
        snapshot.width = 56;
        snapshot.height = 40;
        const context = snapshot.getContext('2d', { willReadFrequently: true });
        if (!context) {
          return false;
        }

        context.drawImage(canvas, 0, 0, snapshot.width, snapshot.height);
        const image = context.getImageData(0, 0, snapshot.width, snapshot.height).data;
        const colors = new Set();
        let nonTransparentPixels = 0;
        for (let index = 0; index < image.length; index += 16) {
          const alpha = image[index + 3];
          if (alpha > 0) {
            nonTransparentPixels += 1;
            colors.add(\`\${image[index]},\${image[index + 1]},\${image[index + 2]}\`);
          }
        }

        return nonTransparentPixels > 80 && colors.size >= 5;
      })()`,
      timeoutMs,
      `Timed out waiting for the container packing 3D canvas (${viewportName}).`,
    );
  }

  async function verifyContainerPacking3dControls(page, timeoutMs) {
    const controlsVisibleCheck = await waitForPageExpression(
      page,
      `(() => {
        const section = document.querySelector('[aria-label="装柜三维可视化"]');
        const controls = section ? section.querySelector('[aria-label="装柜三维视角控制"]') : null;
        return Boolean(controls &&
          controls.querySelector('button[aria-label="暂停三维自动旋转"]') &&
          controls.querySelector('button[aria-label="重置三维视角"]') &&
          Array.from(controls.querySelectorAll('button')).some((button) => (button.innerText || '').includes('等轴')) &&
          Array.from(controls.querySelectorAll('button')).some((button) => (button.innerText || '').includes('俯视')) &&
          Array.from(controls.querySelectorAll('button')).some((button) => (button.innerText || '').includes('柜门')));
      })()`,
      timeoutMs,
      "Timed out waiting for the container packing 3D controls.",
    );

    await evaluate(
      page,
      `(() => {
        const section = document.querySelector('[aria-label="装柜三维可视化"]');
        const pauseButton = section ? section.querySelector('button[aria-label="暂停三维自动旋转"]') : null;
        if (!pauseButton || pauseButton.disabled) {
          throw new Error('Container packing 3D pause button is not available.');
        }

        pauseButton.click();
        return true;
      })()`,
      true,
    );
    const pauseCheck = await waitForPageExpression(
      page,
      `(() => {
        const canvas = document.querySelector('[aria-label="装柜三维可视化"] canvas[aria-label="装柜三维画布"]');
        const resumeButton = document.querySelector('[aria-label="装柜三维可视化"] button[aria-label="恢复三维自动旋转"]');
        return Boolean(canvas && canvas.dataset.autoRotate === 'false' && resumeButton);
      })()`,
      timeoutMs,
      "Timed out waiting for the container packing 3D pause action.",
    );

    await evaluate(
      page,
      `(() => {
        const controls = document.querySelector('[aria-label="装柜三维视角控制"]');
        const doorButton = controls ? Array.from(controls.querySelectorAll('button')).find((button) => (button.innerText || '').includes('柜门')) : null;
        if (!doorButton || doorButton.disabled) {
          throw new Error('Container packing 3D door view button is not available.');
        }

        doorButton.click();
        return true;
      })()`,
      true,
    );
    const doorViewCheck = await waitForPageExpression(
      page,
      `(() => {
        const canvas = document.querySelector('[aria-label="装柜三维可视化"] canvas[aria-label="装柜三维画布"]');
        const controls = document.querySelector('[aria-label="装柜三维视角控制"]');
        const doorButton = controls ? Array.from(controls.querySelectorAll('button')).find((button) => (button.innerText || '').includes('柜门')) : null;
        return Boolean(canvas && canvas.dataset.viewPreset === 'door' && doorButton && doorButton.getAttribute('aria-pressed') === 'true');
      })()`,
      timeoutMs,
      "Timed out waiting for the container packing 3D door view action.",
    );

    await evaluate(
      page,
      `(() => {
        const resetButton = document.querySelector('[aria-label="装柜三维可视化"] button[aria-label="重置三维视角"]');
        if (!resetButton || resetButton.disabled) {
          throw new Error('Container packing 3D reset view button is not available.');
        }

        resetButton.click();
        return true;
      })()`,
      true,
    );
    const resetViewCheck = await waitForPageExpression(
      page,
      `(() => {
        const canvas = document.querySelector('[aria-label="装柜三维可视化"] canvas[aria-label="装柜三维画布"]');
        const controls = document.querySelector('[aria-label="装柜三维视角控制"]');
        const isometricButton = controls ? Array.from(controls.querySelectorAll('button')).find((button) => (button.innerText || '').includes('等轴')) : null;
        return Boolean(canvas && canvas.dataset.viewPreset === 'isometric' && isometricButton && isometricButton.getAttribute('aria-pressed') === 'true');
      })()`,
      timeoutMs,
      "Timed out waiting for the container packing 3D reset view action.",
    );

    return {
      controlsVisibleCheck,
      pauseCheck,
      doorViewCheck,
      resetViewCheck,
    };
  }

  function buildJobCenterCheckUrl(webUrl) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeJobCenter", "1");
    url.hash = "/jobs";
    return url.toString();
  }

  function buildExcelToolsCheckUrl(webUrl) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeJobCenterExcel", "1");
    url.hash = "/tools/excel";
    return url.toString();
  }

  function buildContainerPackingCheckUrl(webUrl) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeJobCenterPacking", "1");
    url.hash = "/tools/container-packing";
    return url.toString();
  }

  return { run: waitForJobCenterCheck };
}
