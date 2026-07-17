import path from "node:path";

export function createReportTemplateSmokeScene(runtime) {
  const {
    evaluate,
    redactDesktopAccessToken,
    waitFor,
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
          const select = document.querySelector(".template-select-field select");
          if (!select) {
            return false;
          }
          const selectedText = select.selectedOptions && select.selectedOptions[0] ? select.selectedOptions[0].textContent || "" : "";
          return (select.value || "").includes(${JSON.stringify(check.templateFileName)}) || selectedText.includes(${JSON.stringify(check.templateFileName)});
        })()`,
        timeoutMs,
        `Timed out waiting for selected report template: ${check.reportType}/${check.templateFileName}`,
      );
      const loadedDesignerCheck = await waitForPageExpression(
        page,
        `Boolean(document.querySelector(".new-report-designer")) && document.body && (document.body.innerText || "").includes("字段目录")`,
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
      const sourceFormatCheck = await waitForReportTemplateSourceFormatCheck(page, timeoutMs);

      results.push({
        reportType: check.reportType,
        templateFileName: check.templateFileName,
        expectedDesignerText: check.expectedFrameText,
        url: redactDesktopAccessToken(checkUrl),
        selectedTemplateCheck,
        loadedDesignerCheck,
        debugReadoutRemovedCheck,
        previewWorkspaceCheck,
        sourceFormatCheck,
      });
    }

    return results;
  }

  async function waitForReportTemplatePreviewWorkspaceCheck(page, timeoutMs) {
    await evaluate(
      page,
      `(() => {
        const workspaceTabs = document.querySelector('[aria-label="模板工作区"]');
        const previewButton = workspaceTabs
          ? Array.from(workspaceTabs.querySelectorAll('button')).find((button) => (button.innerText || '').trim().includes('预览'))
          : null;
        if (!previewButton) {
          throw new Error('Report template preview workspace tab was not found.');
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

  async function waitForReportTemplateSourceFormatCheck(page, timeoutMs) {
    await evaluate(
      page,
      `(() => {
        const buttons = Array.from(document.querySelectorAll('button'));
        const sourceTab = buttons.find((button) => (button.innerText || '').includes('源码'));
        if (!sourceTab) {
          throw new Error('Report template source tab was not found.');
        }

        sourceTab.click();
        window.__reportTemplateSourceFormatClicked = false;
        delete window.__reportTemplateSourceFormatOriginal;
        return true;
      })()`,
      true,
    );

    return waitFor(async () => {
      const state = await evaluate(
        page,
        `(() => {
          const textarea = document.querySelector('textarea[aria-label="模板源码"]');
          if (!textarea) {
            return null;
          }

          const notifyReactChange = (control) => {
            const reactPropsKey = Object.keys(control).find((key) => key.startsWith('__reactProps$'));
            const reactProps = reactPropsKey ? control[reactPropsKey] : null;
            if (reactProps && typeof reactProps.onChange === 'function') {
              reactProps.onChange({ target: control, currentTarget: control });
            }
          };
          const setNativeValue = (control, value) => {
            const prototype = Object.getPrototypeOf(control);
            const descriptor = Object.getOwnPropertyDescriptor(prototype, 'value');
            if (descriptor && typeof descriptor.set === 'function') {
              descriptor.set.call(control, value);
            } else {
              control.value = value;
            }
            control.focus();
            control.dispatchEvent(new Event('input', { bubbles: true }));
            control.dispatchEvent(new Event('change', { bubbles: true }));
            notifyReactChange(control);
          };

          if (typeof window.__reportTemplateSourceFormatOriginal !== 'string') {
            window.__reportTemplateSourceFormatOriginal = textarea.value || '';
          }

          if (!window.__reportTemplateSourceFormatClicked) {
            setNativeValue(textarea, '<div><span>{{\\n Invoice.InvoiceNo \\n}}</span></div>');
            const buttons = Array.from(document.querySelectorAll('button'));
            const formatButton = buttons.find((button) => (button.innerText || '').includes('格式化'));
            if (!formatButton || formatButton.disabled) {
              return null;
            }

            formatButton.click();
            window.__reportTemplateSourceFormatClicked = true;
            return null;
          }

          const formatted = textarea.value || '';
          const expected = '<div>\\n  <span>{{ Invoice.InvoiceNo }}</span>\\n</div>';
          if (!formatted.includes(expected)) {
            return null;
          }

          setNativeValue(textarea, window.__reportTemplateSourceFormatOriginal || '');
          return {
            sourceTabVisible: true,
            formatButtonFound: true,
            formattedIncludesExpected: true,
            expected,
            formatted,
            restoredOriginalDraft: true,
          };
        })()`,
        true,
      ).catch(() => ({ value: null }));

      return state.value ?? null;
    }, timeoutMs, () => "Timed out waiting for report template source formatter check.");
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
