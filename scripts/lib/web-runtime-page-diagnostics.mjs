import { evaluate } from "./web-runtime-browser-session.mjs";
import {
  includesText,
  normalizePathForCompare,
  redactDesktopAccessToken,
  waitFor,
} from "./web-runtime-smoke-common.mjs";

export async function waitForRuntimeDiagnostics(page, expectedText, timeoutMs) {
  let latestText = "";
  let latestHref = "";
  return waitFor(async () => {
    const result = await evaluate(page, "document.body ? document.body.innerText : ''", true).catch(() => ({ value: "" }));
    const text = result.value ?? "";
    latestText = text;
    const href = await evaluate(page, "window.location.href", true).catch(() => ({ value: "" }));
    latestHref = href.value ?? "";
    return expectedText.every((value) => includesText(text, value)) ? text : null;
  }, timeoutMs, () => {
    const missing = expectedText.filter((value) => !includesText(latestText, value));
    return [
      `Timed out waiting for page text: ${expectedText.join(", ")}`,
      latestHref ? `Location: ${redactDesktopAccessToken(latestHref)}` : "",
      missing.length > 0 ? `Missing: ${missing.join(", ")}` : "",
      latestText ? `Text excerpt: ${latestText.slice(0, 1600)}` : "Text excerpt: <empty>",
    ].filter(Boolean).join("\n");
  });
}

export async function waitForRuntimePathActionsCheck(page, options, timeoutMs) {
  if (!options.runtimePathActionsCheck) {
    return null;
  }

  const expectedOpenPaths = options.expectedOpenPaths.filter((value) => String(value ?? "").trim());
  const expectedPathKeys = expectedOpenPaths.map(normalizePathForCompare);
  let latestClickResult = null;
  let latestInvocations = [];

  const clickResult = await waitFor(async () => {
    const result = await evaluate(
      page,
      `(() => {
        const section = document.querySelector('[aria-label="运行诊断"]');
        if (!section) return { found: false, reason: "missing runtime diagnostics section" };
        const buttons = Array.from(section.querySelectorAll('button'))
          .filter((button) => (button.title || "").startsWith("打开") && !button.disabled);
        if (buttons.length < ${expectedOpenPaths.length}) {
          return { found: false, reason: "missing open path buttons", buttonTitles: buttons.map((button) => button.title || button.getAttribute("aria-label") || "") };
        }
        window.__exportDocManagerSmokeTauriInvocations = [];
        for (const button of buttons) button.click();
        return { found: true, clickedTitles: buttons.map((button) => button.title || button.getAttribute("aria-label") || "") };
      })()`,
      true,
    ).catch((error) => ({ value: { found: false, reason: error.message } }));
    latestClickResult = result.value ?? null;
    return latestClickResult?.found ? latestClickResult : null;
  }, timeoutMs, () => `Timed out waiting for runtime diagnostics open path buttons. Latest: ${JSON.stringify(latestClickResult)}`);

  const invocations = await waitFor(async () => {
    const result = await evaluate(page, "window.__exportDocManagerSmokeTauriInvocations || []", true).catch(() => ({ value: [] }));
    latestInvocations = Array.isArray(result.value) ? result.value : [];
    const openPathInvocations = latestInvocations.filter((item) => item?.command === "open_path");
    const openedPathKeys = openPathInvocations.map((item) => normalizePathForCompare(item?.args?.path)).filter(Boolean);
    return expectedPathKeys.every((expectedPath) => openedPathKeys.includes(expectedPath)) ? openPathInvocations : null;
  }, timeoutMs, () => {
    const opened = latestInvocations.filter((item) => item?.command === "open_path").map((item) => item?.args?.path).filter(Boolean);
    return [
      "Timed out waiting for mocked Tauri open_path calls from runtime diagnostics.",
      `Expected: ${JSON.stringify(expectedOpenPaths)}`,
      `Opened: ${JSON.stringify(opened)}`,
    ].join("\n");
  });

  return {
    clickedTitles: clickResult.clickedTitles,
    expectedOpenPaths: expectedOpenPaths.map((value) => ({ value, found: true })),
    invocations: invocations.map((item) => ({ command: item.command, path: item.args?.path ?? "" })),
  };
}

export async function waitForRuntimeDependencyClassification(page, options, timeoutMs) {
  if (!options.runtimePathActionsCheck) {
    return null;
  }

  let latest = null;
  return waitFor(async () => {
    const result = await evaluate(
      page,
      `(() => {
        const section = document.querySelector('[aria-label="运行诊断"]');
        if (!section) return { found: false, reason: 'missing runtime diagnostics section' };
        const text = section.innerText || '';
        const count = (selector) => section.querySelectorAll('.runtime-path-row ' + selector).length;
        const classification = {
          core: count('.runtime-path-requirement-core'),
          feature: count('.runtime-path-requirement-feature'),
          optional: count('.runtime-path-requirement-optional'),
        };
        const dependencyCards = Array.from(section.querySelectorAll('.runtime-dependency-card'));
        const dependencies = dependencyCards.map((card) => ({
          label: card.getAttribute('aria-label') || '',
          status: card.querySelector('.runtime-dependency-status')?.textContent?.trim() || '',
        }));
        const dependencyLabels = dependencies.map((item) => item.label);
        return {
          found: text.includes('核心路径正常')
            && !text.includes('核心路径需处理')
            && classification.core > 0
            && classification.feature > 0
            && classification.optional > 0
            && dependencies.length === 3
            && ['报表 PDF 浏览器', '智能 OCR', 'PostgreSQL 维护工具'].every((label) => dependencyLabels.includes(label)),
          classification,
          dependencies,
          textExcerpt: text.slice(0, 1600),
        };
      })()`,
      true,
    ).catch((error) => ({ value: { found: false, reason: error.message } }));
    latest = result.value ?? null;
    return latest?.found ? latest : null;
  }, timeoutMs, () => `Timed out waiting for runtime dependency classification. Latest: ${JSON.stringify(latest)}`);
}

export async function waitForTemplateStorageCheck(page, options, timeoutMs) {
  if (!options.runtimePathActionsCheck) {
    return null;
  }

  let latest = null;
  const clicked = await waitFor(async () => {
    const result = await evaluate(
      page,
      `(() => {
        const section = document.querySelector('[aria-label="运行诊断"]');
        if (!section) return { found: false, reason: 'missing runtime diagnostics section' };
        const button = Array.from(section.querySelectorAll('button'))
          .find((candidate) => (candidate.innerText || '').includes('检查模板目录'));
        if (!button || button.disabled) return { found: false, reason: 'template storage button unavailable' };
        button.click();
        return { found: true, title: button.title || '', text: button.innerText || '' };
      })()`,
      true,
    ).catch((error) => ({ value: { found: false, reason: error.message } }));
    latest = result.value ?? null;
    return latest?.found ? latest : null;
  }, timeoutMs, () => `Timed out clicking template storage diagnostics. Latest: ${JSON.stringify(latest)}`);

  const completed = await waitFor(async () => {
    const result = await evaluate(
      page,
      `(() => {
        const section = document.querySelector('[aria-label="运行诊断"]');
        const text = section ? section.innerText || '' : '';
        const path = section?.querySelector('.runtime-template-storage-path code')?.textContent || '';
        return {
          found: text.includes('模板目录可用') && text.includes('新建、编辑和导入模板可继续使用 Templates 目录'),
          path,
          textExcerpt: text.slice(0, 1400),
        };
      })()`,
      true,
    ).catch((error) => ({ value: { found: false, reason: error.message } }));
    latest = result.value ?? null;
    return latest?.found ? latest : null;
  }, timeoutMs, () => `Timed out waiting for template storage diagnostics. Latest: ${JSON.stringify(latest)}`);

  return {
    clicked,
    templateRoot: completed.path,
    writable: true,
    textExcerpt: completed.textExcerpt,
  };
}

export async function waitForFrameDiagnostics(page, options, timeoutMs, readPageDiagnostics = null) {
  if (!options.expectedFrameUrl) {
    return null;
  }

  let latestFrameUrls = [];
  let latestText = "";
  let latestSelectorChecks = [];
  let latestExpressionChecks = [];
  let latestPageDiagnostics = null;
  let latestFrameDataset = null;

  return waitFor(async () => {
    latestPageDiagnostics = readPageDiagnostics ? await readPageDiagnostics(page).catch(() => null) : null;
    const frameTree = await page.send("Page.getFrameTree");
    const frames = flattenFrameTree(frameTree.frameTree);
    latestFrameUrls = frames.map((frame) => frame.url).filter(Boolean);
    const frame = frames.find((candidate) => isExpectedFrameUrl(candidate.url, options.expectedFrameUrl));
    if (!frame) return null;

    const context = await page.send("Page.createIsolatedWorld", { frameId: frame.id, worldName: "exportdocmanager-smoke-frame" });
    const contextId = context.executionContextId;
    const textResult = await evaluate(page, "document.body ? document.body.innerText : ''", true, contextId).catch(() => ({ value: "" }));
    const text = textResult.value ?? "";
    latestText = text;
    const datasetResult = await evaluate(page, `(() => { const dataset = document.body && document.body.dataset ? Object.assign({}, document.body.dataset) : {}; delete dataset.currentStateJson; return dataset; })()`, true, contextId).catch(() => ({ value: {} }));
    latestFrameDataset = datasetResult.value ?? {};

    latestSelectorChecks = [];
    for (const selector of options.expectedFrameSelectors) {
      const result = await evaluate(page, `Boolean(document.querySelector(${JSON.stringify(selector)}))`, true, contextId).catch(() => ({ value: false }));
      latestSelectorChecks.push({ selector, found: Boolean(result.value) });
    }

    latestExpressionChecks = [];
    for (const expression of options.expectedFrameExpressions) {
      const result = await evaluate(page, `Boolean((${expression}))`, true, contextId).catch((error) => ({ value: false, error: error.message }));
      latestExpressionChecks.push({ expression, found: Boolean(result.value), error: result.error || null });
    }

    const textOk = options.expectedFrameText.every((value) => includesText(text, value));
    if (!textOk || !latestSelectorChecks.every((check) => check.found) || !latestExpressionChecks.every((check) => check.found)) return null;
    return {
      frameId: frame.id,
      url: frame.url,
      checks: options.expectedFrameText.map((value) => ({ value, found: includesText(text, value) })),
      selectorChecks: latestSelectorChecks,
      expressionChecks: latestExpressionChecks,
      frameDataset: latestFrameDataset,
      textExcerpt: text.slice(0, 1200),
    };
  }, timeoutMs, () => {
    const missingText = options.expectedFrameText.filter((value) => !includesText(latestText, value));
    const missingSelectors = latestSelectorChecks.filter((check) => !check.found).map((check) => check.selector);
    const missingExpressions = latestExpressionChecks.filter((check) => !check.found).map((check) => check.expression);
    return [
      `Timed out waiting for frame checks: ${options.expectedFrameUrl}`,
      latestFrameUrls.length > 0 ? `Frames: ${latestFrameUrls.join(", ")}` : "Frames: <none>",
      missingText.length > 0 ? `Missing frame text: ${missingText.join(", ")}` : "",
      missingSelectors.length > 0 ? `Missing frame selectors: ${missingSelectors.join(", ")}` : "",
      missingExpressions.length > 0 ? `Missing frame expressions: ${missingExpressions.join(", ")}` : "",
      latestPageDiagnostics ? `Page diagnostics: ${JSON.stringify(latestPageDiagnostics)}` : "",
      latestFrameDataset ? `Frame dataset: ${JSON.stringify(latestFrameDataset)}` : "",
      latestText ? `Frame text excerpt: ${latestText.slice(0, 1600)}` : "Frame text excerpt: <empty>",
    ].filter(Boolean).join("\n");
  });
}

export async function waitForPageExpression(page, expression, timeoutMs, description) {
  let latestHref = "";
  let latestText = "";
  let latestResult = false;
  return waitFor(async () => {
    const result = await evaluate(page, `Boolean((${expression}))`, true).catch(() => ({ value: false }));
    latestResult = Boolean(result.value);
    const href = await evaluate(page, "window.location.href", true).catch(() => ({ value: "" }));
    latestHref = href.value ?? "";
    const text = await evaluate(page, "document.body ? document.body.innerText : ''", true).catch(() => ({ value: "" }));
    latestText = text.value ?? "";
    return latestResult ? { expression, found: true, location: redactDesktopAccessToken(latestHref), textExcerpt: latestText.slice(0, 1200) } : null;
  }, timeoutMs, () => [
    description,
    `Expression: ${expression}`,
    latestHref ? `Location: ${redactDesktopAccessToken(latestHref)}` : "",
    `Found: ${latestResult}`,
    latestText ? `Text excerpt: ${latestText.slice(0, 1600)}` : "Text excerpt: <empty>",
  ].filter(Boolean).join("\n"));
}

export async function waitForTauriCommandInvocation(page, command, expectedPath, timeoutMs, description) {
  const expectedPathKey = normalizePathForCompare(expectedPath).toLowerCase();
  return waitForPageExpression(page, `(() => {
    const expected = ${JSON.stringify(expectedPathKey)};
    const normalize = (value) => {
      let normalized = String(value || '').split('\\\\').join('/').toLowerCase();
      while (normalized.endsWith('/')) normalized = normalized.slice(0, -1);
      return normalized;
    };
    const invocations = window.__exportDocManagerSmokeTauriInvocations || [];
    return invocations.some((entry) => {
      if (!entry || entry.command !== ${JSON.stringify(command)}) return false;
      const values = [
        entry.args && entry.args.path, entry.args && entry.args.defaultDirectory, entry.args && entry.args.defaultFileName,
        window.__exportDocManagerSmokeSavePackagePath, window.__exportDocManagerSmokeSaveInvoiceTransferPackagePath,
        window.__exportDocManagerSmokeInvoiceTransferPackagePath, window.__exportDocManagerSmokeSaveExcelPath,
        window.__exportDocManagerSmokeSingleWindowPackagePath,
      ];
      return values.some((value) => normalize(value) === expected || normalize(value).endsWith('/' + expected.split('/').pop()));
    });
  })()`, timeoutMs, description);
}

export function flattenFrameTree(frameTree) {
  if (!frameTree?.frame) return [];
  const frames = [frameTree.frame];
  for (const child of frameTree.childFrames || []) frames.push(...flattenFrameTree(child));
  return frames;
}

export function isExpectedFrameUrl(actual, expected) {
  if (!actual || !expected) return false;
  const normalizedActual = normalizeFrameUrl(actual);
  const normalizedExpected = normalizeFrameUrl(expected);
  return normalizedActual === normalizedExpected || normalizedActual.startsWith(`${normalizedExpected}?`);
}

export function normalizeFrameUrl(value) {
  return String(value).trim().replace(/\/$/, "").toLocaleLowerCase();
}
