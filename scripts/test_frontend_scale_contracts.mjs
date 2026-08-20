import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { CdpClient, closeChrome, delay } from "./lib/chromium-cdp.mjs";
import { spawnProcessTree, stopProcessTree } from "./lib/child-process-tree.mjs";
import { locateChromeForTesting } from "./lib/report-regression-common.mjs";
import { captureScreenshot, createPageSession, evaluate, getFreePort, startChrome } from "./lib/web-runtime-browser-session.mjs";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const webRoot = path.join(repositoryRoot, "apps", "export-doc-web");
const webRequire = createRequire(path.join(webRoot, "package.json"));
const viteCli = path.join(path.dirname(webRequire.resolve("vite/package.json")), "bin", "vite.js");
const outputRoot = path.join(repositoryRoot, "artifacts", "frontend-scale-contracts");
const profileRoot = path.join(repositoryRoot, ".codex-runtime", "frontend-scale-contracts-chrome");
const browserExecutable = locateChromeForTesting(repositoryRoot, "headless-shell");
const pages = ["login", "dashboard", "query", "invoice", "invoiceParties", "hs", "report", "singleWindow", "globalStyles"];
const densities = ["comfortable", "compact"];
const allProfiles = [
  { name: "windows-125", width: 1366, height: 768, deviceScaleFactor: 1.25, mobile: false },
  { name: "windows-150", width: 1920, height: 1080, deviceScaleFactor: 1.5, mobile: false },
  { name: "windows-4k", width: 3840, height: 2160, deviceScaleFactor: 1.5, mobile: false },
  { name: "macos-retina", width: 1440, height: 900, deviceScaleFactor: 2, mobile: false },
  { name: "linux-100", width: 1366, height: 768, deviceScaleFactor: 1, mobile: false },
  { name: "tablet-1024", width: 1024, height: 768, deviceScaleFactor: 1, mobile: false },
  { name: "desktop-zoom-200", width: 683, height: 384, deviceScaleFactor: 2, mobile: false },
  { name: "mobile-320", width: 320, height: 568, deviceScaleFactor: 2, mobile: true },
  { name: "mobile-375", width: 375, height: 667, deviceScaleFactor: 3, mobile: true },
  { name: "mobile-keyboard-390", width: 390, height: 430, deviceScaleFactor: 3, mobile: true },
  { name: "mobile-safari-contract", width: 390, height: 844, deviceScaleFactor: 3, mobile: true },
];
const requestedProfileNames = new Set(
  String(process.env.EXPORTDOCMANAGER_SCALE_PROFILE_FILTER || "")
    .split(",")
    .map((value) => value.trim())
    .filter(Boolean),
);
const profiles = requestedProfileNames.size > 0
  ? allProfiles.filter((profile) => requestedProfileNames.has(profile.name))
  : allProfiles;
if (profiles.length === 0) {
  throw new Error(`No scale profiles matched EXPORTDOCMANAGER_SCALE_PROFILE_FILTER: ${[...requestedProfileNames].join(", ")}`);
}

rmSync(outputRoot, { recursive: true, force: true });
rmSync(profileRoot, { recursive: true, force: true });
mkdirSync(outputRoot, { recursive: true });
mkdirSync(profileRoot, { recursive: true });

const port = await getFreePort();
const vite = spawnProcessTree(process.execPath, [
  viteCli,
  "--host", "127.0.0.1",
  "--port", String(port),
  "--strictPort",
], { cwd: webRoot, stdio: ["ignore", "pipe", "pipe"], windowsHide: true });
let viteOutput = "";
vite.stdout.on("data", (chunk) => { viteOutput += chunk.toString(); });
vite.stderr.on("data", (chunk) => { viteOutput += chunk.toString(); });

let chrome;
let cdp;
const results = [];
try {
  await waitForHttp(`http://127.0.0.1:${port}/visual-baseline.html`);
  chrome = await startChrome({ browserExecutable, userDataDir: profileRoot, timeoutMs: 60000 });
  cdp = await CdpClient.connect(chrome.browserWebSocketUrl);

  for (const profile of profiles) {
    process.stdout.write(`[scale] Starting profile ${profile.name}.\n`);
    const page = await createPageSession(cdp);
    try {
      await page.send("Emulation.setDeviceMetricsOverride", {
        width: profile.width,
        height: profile.height,
        deviceScaleFactor: profile.deviceScaleFactor,
        mobile: profile.mobile,
      });
      await page.send("Emulation.setEmulatedMedia", {
        media: "screen",
        features: [{ name: "prefers-reduced-motion", value: "reduce" }],
      });

      for (const density of densities) {
        for (const pageName of pages) {
          const scene = `${profile.name}/${density}/${pageName}`;
          process.stdout.write(`[scale] Running ${scene}.\n`);
          const url = pageName === "globalStyles"
            ? `http://127.0.0.1:${port}/global-style-contract.html?density=${density}`
            : `http://127.0.0.1:${port}/visual-baseline.html?page=${pageName}&density=${density}`;
          await page.send("Page.navigate", { url });
          await waitForReady(page);
          await evaluate(page, "document.fonts?.ready ?? Promise.resolve()", false);
          const audit = await evaluate(page, buildAuditExpression(profile.mobile), true);
          const value = audit.value;
          const expectedFieldGridColumns = profile.width <= 860 ? 1 : profile.width <= 1180 ? 2 : 4;
          const passed = !value.horizontalOverflow
            && value.truncatedCriticalText.length === 0
            && value.reportPanelOverlapCount === 0
            && value.reportMinimumSelectionFieldWidth >= (profile.mobile ? 180 : 128)
            && value.mobileInputFontFailures.length === 0
            && value.mobileTouchTargetFailures.length === 0
            && (pageName !== "globalStyles" || value.fieldGridMaximumColumnCount === expectedFieldGridColumns)
            && (pageName !== "invoiceParties" || value.invoicePartyGridContractPassed)
            && (pageName !== "query" || (value.queryFilterContractPassed && value.queryExportSummaryCompact))
            && (pageName !== "globalStyles" || value.globalStyleContractPassed);

          let screenshotPath = null;
          if (pageName === "dashboard" || pageName === "query" || pageName === "report" || pageName === "globalStyles") {
            screenshotPath = path.join(outputRoot, `${pageName}-${profile.name}-${density}.png`);
            await captureScreenshot(page, screenshotPath, { captureBeyondViewport: false });
          }

          results.push({ page: pageName, density, profile, url, screenshotPath, passed, ...value });
          process.stdout.write(`[scale] ${passed ? "Passed" : "Failed"} ${scene}.\n`);
        }
      }
    } finally {
      await cdp.send("Target.closeTarget", { targetId: page.targetId }).catch(() => {});
    }
  }

  const summary = {
    generatedAt: new Date().toISOString(),
    browserExecutable,
    passed: results.every((result) => result.passed),
    results,
  };
  writeFileSync(path.join(outputRoot, "summary.json"), `${JSON.stringify(summary, null, 2)}\n`, "utf8");
  if (!summary.passed) {
    const failures = results.filter((result) => !result.passed).map((result) => `${result.profile.name}/${result.density}/${result.page}: ${JSON.stringify(result)}`);
    throw new Error(`Frontend scale contracts failed:\n${failures.join("\n")}`);
  }
  process.stdout.write(`Frontend scale contracts passed (${results.length} scenes).\n`);
} finally {
  cdp?.close();
  if (chrome) await closeChrome(chrome.browserWebSocketUrl, chrome.process);
  await stopProcessTree(vite);
  await delay(250);
  rmSync(profileRoot, { recursive: true, force: true });
}

function buildAuditExpression(isMobile) {
  return `(() => {
    const root = document.documentElement;
    const visible = (element) => {
      if (!(element instanceof HTMLElement)) return false;
      const style = getComputedStyle(element);
      const rect = element.getBoundingClientRect();
      return style.display !== "none" && style.visibility !== "hidden" && rect.width > 0 && rect.height > 0;
    };
    const truncatedCriticalText = [...document.querySelectorAll("[data-visual-critical-text]")]
      .filter((element) => element.scrollWidth > element.clientWidth + 1)
      .map((element) => (element.textContent || "").trim());
    const reportPanels = [...document.querySelectorAll(".template-selection-panel, .template-user-panel, .template-admin-panel, .template-package-panel")]
      .filter(visible)
      .map((element) => element.getBoundingClientRect());
    const reportPanelOverlapCount = reportPanels.flatMap((rect, index) => reportPanels.slice(index + 1).map((other) => ({ rect, other })))
      .filter(({ rect, other }) => Math.min(rect.right, other.right) - Math.max(rect.left, other.left) > 1
        && Math.min(rect.bottom, other.bottom) - Math.max(rect.top, other.top) > 1).length;
    const reportSelectionWidths = [...document.querySelectorAll(".template-selection-panel > label")]
      .filter(visible)
      .map((element) => element.getBoundingClientRect().width);
    const fieldGridLayouts = [...document.querySelectorAll(".field-grid")]
      .filter(visible)
      .map((element) => {
        const style = getComputedStyle(element);
        const columns = style.gridTemplateColumns.split(/\\s+/u).filter(Boolean);
        return {
          className: element.className,
          parentClassName: element.parentElement?.className || "",
          display: style.display,
          gridTemplateColumns: style.gridTemplateColumns,
          columnCount: columns.length,
        };
      });
    const fieldGridMaximumColumnCount = fieldGridLayouts.length
      ? Math.max(...fieldGridLayouts.map((layout) => layout.columnCount))
      : 0;
    const invoicePartyGridLayouts = fieldGridLayouts.filter((layout) => layout.parentClassName.includes("invoice-party-group"));
    const invoicePartyGridContractPassed = invoicePartyGridLayouts.length === 0 || invoicePartyGridLayouts.every((layout) => {
      const exporter = layout.parentClassName.includes("invoice-party-group-exporter");
      const expectedColumns = innerWidth <= 860 ? 1 : exporter ? 4 : 2;
      return layout.display === "grid" && layout.columnCount === expectedColumns;
    });
    const queryFilterContractDetails = (() => {
      const filterStack = document.querySelector(".query-filter-stack");
      const commonGrid = document.querySelector(".query-common-filter-grid");
      const advancedGrid = document.querySelector(".query-advanced-filter-grid");
      const advancedDetails = document.querySelector(".query-advanced-filters");
      const advancedSummary = document.querySelector(".query-advanced-filter-summary");
      if (!(filterStack instanceof HTMLElement)
        || !(commonGrid instanceof HTMLElement)
        || !(advancedGrid instanceof HTMLElement)
        || !(advancedDetails instanceof HTMLDetailsElement)
        || !(advancedSummary instanceof HTMLElement)) return null;
      const dateRange = commonGrid.querySelector(".query-date-range");
      const actions = document.querySelector(".query-toolbar-actions");
      const allFields = [...filterStack.querySelectorAll(".query-filter-field")];
      const labels = [...filterStack.querySelectorAll(".query-filter-field > span, .query-filter-field > .form-field-label, .query-filter-field > .form-field-label > span:first-child")].filter(visible);
      const controls = [...filterStack.querySelectorAll("input, select")].filter(visible);
      const commonGridStyle = getComputedStyle(commonGrid);
      const advancedGridStyle = getComputedStyle(advancedGrid);
      const dateRangeStyle = dateRange instanceof HTMLElement ? getComputedStyle(dateRange) : null;
      const commonGridColumnCount = commonGridStyle.gridTemplateColumns.split(/\\s+/u).filter(Boolean).length;
      const advancedGridColumnCount = advancedGridStyle.gridTemplateColumns.split(/\\s+/u).filter(Boolean).length;
      const dateRangeColumnCount = dateRangeStyle?.gridTemplateColumns.split(/\\s+/u).filter(Boolean).length ?? 0;
      const expectedCommonGridColumnCount = innerWidth <= 860 ? 1 : 2;
      const expectedAdvancedGridColumnCount = innerWidth <= 620 ? 1 : innerWidth <= 1180 ? 2 : 4;
      const expectedDateRangeColumnCount = innerWidth <= 620 ? 1 : 2;
      const expectedFieldCount = 7;
      const expectedAdvancedOpen = innerWidth > 1180;
      const advancedSummaryVisible = visible(advancedSummary);
      const advancedStateMatches = advancedDetails.open === expectedAdvancedOpen
        && advancedSummaryVisible === !expectedAdvancedOpen;
      const labelClipping = labels.filter((label) => label.scrollWidth > label.clientWidth + 1 || label.scrollHeight > label.clientHeight + 1)
        .map((label) => (label.textContent || "").trim());
      const controlOverflow = controls.filter((control) => {
        const field = control.closest(".query-filter-field");
        if (!(field instanceof HTMLElement)) return true;
        const controlRect = control.getBoundingClientRect();
        const fieldRect = field.getBoundingClientRect();
        return controlRect.left < fieldRect.left - 1 || controlRect.right > fieldRect.right + 1
          || controlRect.top < fieldRect.top - 1 || controlRect.bottom > fieldRect.bottom + 1;
      }).map((control) => ({ tag: control.tagName, className: control.className }));
      const fieldRects = allFields.map((field) => ({
        className: field.className,
        rect: field.getBoundingClientRect(),
      }));
      const fieldOverlapCount = fieldRects.flatMap((entry, index) => fieldRects.slice(index + 1).map((other) => ({ entry, other })))
        .filter(({ entry, other }) => Math.min(entry.rect.right, other.rect.right) - Math.max(entry.rect.left, other.rect.left) > 1
          && Math.min(entry.rect.bottom, other.rect.bottom) - Math.max(entry.rect.top, other.rect.top) > 1).length;
      const filterRect = filterStack.getBoundingClientRect();
      const actionsRect = actions instanceof HTMLElement ? actions.getBoundingClientRect() : null;
      const gridActionsOverlap = actionsRect
        ? Math.min(filterRect.right, actionsRect.right) - Math.max(filterRect.left, actionsRect.left) > 1
          && Math.min(filterRect.bottom, actionsRect.bottom) - Math.max(filterRect.top, actionsRect.top) > 1
        : true;
      const actionsPlacementMatches = actionsRect
        ? innerWidth <= 860
          ? actionsRect.top >= filterRect.bottom - 1
          : actionsRect.left >= filterRect.right - 1
        : false;
      return {
        commonGridColumnCount,
        expectedCommonGridColumnCount,
        advancedGridColumnCount,
        expectedAdvancedGridColumnCount,
        dateRangeColumnCount,
        expectedDateRangeColumnCount,
        fieldCount: allFields.length,
        expectedFieldCount,
        advancedOpen: advancedDetails.open,
        advancedSummaryVisible,
        advancedStateMatches,
        labelClipping,
        controlOverflow,
        fieldOverlapCount,
        gridActionsOverlap,
        actionsPlacementMatches,
        passed: commonGridStyle.display === "grid"
          && advancedGridStyle.display === "grid"
          && dateRangeStyle?.display === "grid"
          && commonGridColumnCount === expectedCommonGridColumnCount
          && advancedGridColumnCount === expectedAdvancedGridColumnCount
          && dateRangeColumnCount === expectedDateRangeColumnCount
          && allFields.length === expectedFieldCount
          && advancedStateMatches
          && labelClipping.length === 0
          && controlOverflow.length === 0
          && fieldOverlapCount === 0
          && !gridActionsOverlap
          && actionsPlacementMatches,
      };
    })();
    const queryFilterContractPassed = queryFilterContractDetails?.passed ?? true;
    const queryExportSummary = document.querySelector(".query-export-panel:not([open]) > .query-export-summary");
    const queryExportSummaryHeight = queryExportSummary instanceof HTMLElement
      ? queryExportSummary.getBoundingClientRect().height
      : null;
    const queryExportSummaryCompact = queryExportSummaryHeight === null || queryExportSummaryHeight <= 36;
    const contractStyle = (name) => {
      const element = document.querySelector('[data-style-contract="' + name + '"]');
      return element ? getComputedStyle(element) : null;
    };
    const contractGridColumnCount = (name) => {
      const style = contractStyle(name);
      return style ? style.gridTemplateColumns.split(/\\s+/u).filter(Boolean).length : 0;
    };
    const sectionHeaderContract = (headerName, actionName) => {
      const header = document.querySelector('[data-style-contract="' + headerName + '"]');
      const action = document.querySelector('[data-style-contract="' + actionName + '"]');
      const title = header?.querySelector("h2, h3");
      if (!(header instanceof HTMLElement) || !(title instanceof HTMLElement) || !(action instanceof HTMLElement)) return null;
      const style = getComputedStyle(header);
      const headerRect = header.getBoundingClientRect();
      const titleRect = title.getBoundingClientRect();
      const actionRect = action.getBoundingClientRect();
      const narrow = innerWidth <= 860;
      return {
        display: style.display,
        justifyContent: style.justifyContent,
        flexDirection: style.flexDirection,
        actionRightGap: Math.abs(headerRect.right - actionRect.right),
        actionSeparatedFromTitle: actionRect.left >= titleRect.right + 8,
        actionStackedBelowTitle: actionRect.top >= titleRect.bottom - 1,
        passed: style.display === "flex"
          && style.justifyContent === "space-between"
          && (narrow
            ? style.flexDirection === "column" && actionRect.top >= titleRect.bottom - 1
            : style.flexDirection === "row" && Math.abs(headerRect.right - actionRect.right) <= 1.5 && actionRect.left >= titleRect.right + 8),
      };
    };
    const hiddenStyle = contractStyle("visually-hidden");
    const expectedFieldGridColumns = innerWidth <= 860 ? 1 : innerWidth <= 1180 ? 2 : 4;
    const expectedDetailGridColumns = innerWidth <= 860 ? 1 : 4;
    const textSectionHeader = sectionHeaderContract("section-header-text", "section-header-text-action");
    const iconSectionHeader = sectionHeaderContract("section-header-icon", "section-header-icon-action");
    const globalStyleContractDetails = {
      hiddenPosition: hiddenStyle?.position || null,
      hiddenWidth: hiddenStyle?.width || null,
      hiddenHeight: hiddenStyle?.height || null,
      filterBarDisplay: contractStyle("filter-bar")?.display || null,
      inlineFilterDisplay: contractStyle("inline-filter")?.display || null,
      inlineCheckDisplay: contractStyle("inline-check")?.display || null,
      fieldGridColumnCount: contractGridColumnCount("field-grid"),
      expectedFieldGridColumns,
      detailGridDisplay: contractStyle("detail-grid")?.display || null,
      detailGridColumnCount: contractGridColumnCount("detail-grid"),
      expectedDetailGridColumns,
      rowActionsDisplay: contractStyle("row-actions-cell")?.display || null,
      jobTitleDisplay: contractStyle("job-title-cell")?.display || null,
      reviewSeverityDisplay: contractStyle("review-severity")?.display || null,
      textSectionHeader,
      iconSectionHeader,
    };
    const globalStyleContractPassed = !document.querySelector('[data-style-contract="field-grid"]') || (
      globalStyleContractDetails.hiddenPosition === "absolute"
      && Math.round(Number.parseFloat(globalStyleContractDetails.hiddenWidth)) === 1
      && Math.round(Number.parseFloat(globalStyleContractDetails.hiddenHeight)) === 1
      && globalStyleContractDetails.filterBarDisplay === "flex"
      && globalStyleContractDetails.inlineFilterDisplay === "flex"
      && ["flex", "inline-flex"].includes(globalStyleContractDetails.inlineCheckDisplay)
      && globalStyleContractDetails.fieldGridColumnCount === expectedFieldGridColumns
      && globalStyleContractDetails.detailGridDisplay === "grid"
      && globalStyleContractDetails.detailGridColumnCount === expectedDetailGridColumns
      && globalStyleContractDetails.rowActionsDisplay === "flex"
      && globalStyleContractDetails.jobTitleDisplay === "grid"
      && globalStyleContractDetails.reviewSeverityDisplay === "inline-flex"
      && globalStyleContractDetails.textSectionHeader?.passed === true
      && globalStyleContractDetails.iconSectionHeader?.passed === true
    );
    const mobileInputFontFailures = ${isMobile}
      ? [...document.querySelectorAll('input:not([type="checkbox"]):not([type="radio"]), select, textarea')].filter(visible)
        .filter((element) => Number.parseFloat(getComputedStyle(element).fontSize) < 16)
        .map((element) => ({ tag: element.tagName, className: element.className, fontSize: getComputedStyle(element).fontSize }))
      : [];
    const frequentTargetSelector = ".command-button, .primary-button, .secondary-button, .icon-button, .nav-group-button, .nav-item, .settings-category-item, .master-data-tab, .report-template-workspace-tabs button, .segmented-control button, .density-toggle-button, .login-submit-button, .login-connection-settings summary";
    const mobileTouchTargetFailures = ${isMobile}
      ? [...document.querySelectorAll(frequentTargetSelector)].filter(visible)
        .filter((element) => {
          const rect = element.getBoundingClientRect();
          return rect.width < 43.5 || rect.height < 43.5;
        })
        .map((element) => ({ text: (element.textContent || element.getAttribute("aria-label") || "").trim(), width: element.getBoundingClientRect().width, height: element.getBoundingClientRect().height }))
      : [];
    return {
      horizontalOverflow: root.scrollWidth > root.clientWidth + 1,
      scrollWidth: root.scrollWidth,
      clientWidth: root.clientWidth,
      truncatedCriticalText,
      reportPanelOverlapCount,
      reportMinimumSelectionFieldWidth: reportSelectionWidths.length ? Math.min(...reportSelectionWidths) : 999,
      fieldGridLayouts,
      fieldGridMaximumColumnCount,
      invoicePartyGridLayouts,
      invoicePartyGridContractPassed,
      queryFilterContractPassed,
      queryFilterContractDetails,
      queryExportSummaryHeight,
      queryExportSummaryCompact,
      globalStyleContractPassed,
      globalStyleContractDetails,
      narrowWorkspaceMediaMatched: matchMedia("(max-width: 860px)").matches,
      mobileInputFontFailures,
      mobileTouchTargetFailures,
      computedBodyFont: getComputedStyle(document.body).fontFamily,
      computedBodyFontSize: getComputedStyle(document.body).fontSize,
      interfaceDensity: root.dataset.interfaceDensity,
    };
  })()`;
}

async function waitForHttp(url) {
  for (let attempt = 0; attempt < 120; attempt += 1) {
    if (vite.exitCode !== null) throw new Error(`Vite exited before startup.\n${viteOutput}`);
    const response = await fetch(url).catch(() => null);
    if (response?.ok) return;
    await delay(100);
  }
  throw new Error(`Timed out waiting for Vite.\n${viteOutput}`);
}

async function waitForReady(page) {
  for (let attempt = 0; attempt < 150; attempt += 1) {
    const result = await evaluate(page, "document.documentElement.dataset.visualBaselineReady === 'true'", true).catch(() => null);
    if (result?.value === true) {
      await delay(120);
      return;
    }
    await delay(100);
  }
  throw new Error("Visual scale page did not become ready.");
}
