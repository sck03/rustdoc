import { delay } from "./chromium-cdp.mjs";

export async function attachPdfViewerSession(browser, pageTargetId, testCase) {
  let sessionId;
  try {
    for (let attempt = 0; attempt < 100; attempt += 1) {
      const targets = await browser.send("Target.getTargets");
      const viewerTarget = targets.targetInfos?.find(
        (target) =>
          target.type === "iframe" &&
          target.parentId === pageTargetId &&
          target.url?.startsWith("chrome-extension://") &&
          target.url.endsWith("/index.html"),
      );
      if (!viewerTarget) {
        await delay(100);
        continue;
      }

      const attached = await browser.send("Target.attachToTarget", {
        targetId: viewerTarget.targetId,
        flatten: true,
      });
      sessionId = attached.sessionId;
      assertCapture(sessionId, `${testCase.slug}: Chrome did not return a PDF viewer session.`);
      await browser.send("Runtime.enable", {}, sessionId);
      break;
    }

    assertCapture(sessionId, `${testCase.slug}: Chrome did not expose its PDF viewer frame.`);
    for (let attempt = 0; attempt < 150; attempt += 1) {
      const state = await evaluatePdfViewer(browser, sessionId, `(() => {
        const viewer = document.querySelector("pdf-viewer");
        const pageCount = viewer?.documentDimensions?.pageDimensions?.length ?? 0;
        return {
          loadState: viewer?.loadState_ ?? null,
          pageCount,
          hasPageNavigation:
            typeof viewer?.goToPageAndXy_ === "function" ||
            typeof viewer?.viewport_?.goToPageAndXy === "function",
        };
      })()`);
      if (
        state?.loadState === "success" &&
        state.pageCount === testCase.expectedPages &&
        state.hasPageNavigation
      ) {
        return sessionId;
      }
      await delay(100);
    }

    throw new Error(
      `${testCase.slug}: PDF viewer did not finish loading ${testCase.expectedPages} page(s).`,
    );
  } catch (error) {
    if (sessionId) {
      await browser.send("Target.detachFromTarget", { sessionId }).catch(() => null);
    }
    throw error;
  }
}

async function evaluatePdfViewer(browser, sessionId, expression) {
  const response = await browser.send(
    "Runtime.evaluate",
    {
      expression,
      returnByValue: true,
      awaitPromise: true,
    },
    sessionId,
  );
  if (response.exceptionDetails) {
    const message = response.exceptionDetails.exception?.description ?? response.exceptionDetails.text;
    throw new Error(`PDF viewer evaluation failed: ${message}`);
  }
  return response.result?.value;
}

function assertCapture(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
