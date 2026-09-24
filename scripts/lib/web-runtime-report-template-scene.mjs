export function createReportTemplateSmokeScene(runtime) {
  const {
    evaluate,
    redactDesktopAccessToken,
    waitForPageExpression,
    waitForRuntimeDiagnostics,
  } = runtime;

  async function readPageTemplateDiagnostics(page) {
    const result = await evaluate(
      page,
      `(() => ({
        location: window.location.href,
        templateTypeSelect: (() => {
          const select = document.querySelector(".template-type-field select");
          return select ? { value: select.value || "", selectedText: select.selectedOptions && select.selectedOptions[0] ? select.selectedOptions[0].textContent || "" : "" } : null;
        })(),
        templateSelect: (() => {
          const select = document.querySelector(".template-select-field select");
          return select ? { value: select.value || "", selectedText: select.selectedOptions && select.selectedOptions[0] ? select.selectedOptions[0].textContent || "" : "" } : null;
        })(),
        templateSelectValues: Array.from(document.querySelectorAll("select")).map((element) => ({
          value: element.value || "",
          selectedText: element.selectedOptions && element.selectedOptions[0] ? element.selectedOptions[0].textContent || "" : ""
        }))
      }))()`,
      true,
    );
    return result.value ?? null;
  }

  async function waitForReportTemplateChecks(page, options, timeoutMs) {
    if (!Array.isArray(options.reportTemplateChecks) || options.reportTemplateChecks.length === 0) {
      return [];
    }

    const results = [];
    for (const check of options.reportTemplateChecks) {
      const checkUrl = buildReportTemplateCheckUrl(options.webUrl, check);
      await page.send("Page.navigate", { url: checkUrl });
      await waitForRuntimeDiagnostics(page, options.expectedText, timeoutMs);
      const selectedTemplateCheck = await waitForPageExpression(
        page,
        `(() => {
          const title = (document.querySelector(".report-template-current-title small")?.textContent || "").trim();
          const params = new URLSearchParams(window.location.hash.split("?")[1] || "");
          return params.get("template") === ${JSON.stringify(check.templateFileName)} && title.startsWith("当前模板：") && title !== "当前模板：报表模板";
        })()`,
        timeoutMs,
        `Timed out waiting for selected report template: ${check.reportType}/${check.templateFileName}`,
      );
      const loadedDesignerCheck = await waitForPageExpression(
        page,
        `Boolean(document.querySelector('.report-designer-v3-workspace [data-v3-element-id]'))`,
        timeoutMs,
        `Timed out waiting for structured report designer: ${check.reportType}/${check.templateFileName}`,
      );
      const debugReadoutRemovedCheck = await waitForPageExpression(
        page,
        `!document.querySelector(".template-path-readout") && !document.querySelector(".template-runtime-panel")`,
        timeoutMs,
        `Timed out waiting for report template debug readouts to be absent: ${check.reportType}/${check.templateFileName}`,
      );
      const previewWorkspaceCheck = await waitForReportTemplatePreviewWorkspaceCheck(page, timeoutMs);
      const singleFormatCheck = await waitForPageExpression(page, `!document.querySelector('textarea[aria-label="模板高级 HTML"]') && !Array.from(document.querySelectorAll('button')).some(button => button.textContent.includes('高级 HTML'))`, timeoutMs, 'Legacy HTML template controls must remain absent.');

      results.push({
        reportType: check.reportType,
        templateFileName: check.templateFileName,
        expectedDesignerText: check.expectedFrameText,
        url: redactDesktopAccessToken(checkUrl),
        selectedTemplateCheck,
        loadedDesignerCheck,
        debugReadoutRemovedCheck,
        previewWorkspaceCheck,
        singleFormatCheck,
      });
    }

    return results;
  }

  async function waitForReportTemplatePreviewWorkspaceCheck(page, timeoutMs) {
    await evaluate(
      page,
      `(() => {
        const workspaceTabs = document.querySelector('[aria-label="报表设计视图"]');
        const previewButton = workspaceTabs
          ? Array.from(workspaceTabs.querySelectorAll('button')).find((button) => (button.innerText || '').trim().includes('预览'))
          : null;
        if (!previewButton) {
          throw new Error('Report template preview view was not found.');
        }

        previewButton.click();
        return true;
      })()`,
      true,
    );

    await waitForPageExpression(
      page,
      `Boolean(document.querySelector('.report-template-preview-workspace') &&
        document.querySelector('[aria-label="模板预览数据"]') &&
        (document.body.innerText || '').includes('样例数据') &&
        (document.body.innerText || '').includes('当前单据') &&
        (document.body.innerText || '').includes('样例档案'))`,
      timeoutMs,
      "Timed out waiting for the report template preview workspace.",
    );

    await evaluate(
      page,
      `(() => {
        const previewModeTabs = document.querySelector('[aria-label="模板预览数据"]');
        const currentDocumentButton = previewModeTabs
          ? Array.from(previewModeTabs.querySelectorAll('button')).find((button) => (button.innerText || '').includes('当前单据'))
          : null;
        if (!currentDocumentButton) {
          throw new Error('Current document preview mode was not found.');
        }

        currentDocumentButton.click();
        return true;
      })()`,
      true,
    );

    return waitForPageExpression(
      page,
      `Array.from(document.querySelectorAll('label')).some((label) =>
        (label.innerText || '').includes('预览单据') && Boolean(label.querySelector('select')))` ,
      timeoutMs,
      "Timed out waiting for the current document preview selector.",
    );
  }


  function buildReportTemplateCheckUrl(webUrl, check) {
    const url = new URL(webUrl);
    const hash = url.hash && url.hash !== "#" ? url.hash.slice(1) : "/reports/templates";
    const path = hash.split("?")[0] || "/reports/templates";
    const search = new URLSearchParams();
    search.set("reportType", check.reportType);
    search.set("template", check.templateFileName);
    url.searchParams.set("smokeReportTemplate", `${check.reportType}-${check.templateFileName}`);
    url.hash = `${path}?${search.toString()}`;
    return url.toString();
  }

  return { readPageTemplateDiagnostics, run: waitForReportTemplateChecks };
}
