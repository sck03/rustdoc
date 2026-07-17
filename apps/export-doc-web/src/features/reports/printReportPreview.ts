import { buildPrintSourceHtml } from "./printReportPreviewDocument.ts";

export function printReportPreviewHtml(html: string, title: string) {
  return new Promise<void>((resolve, reject) => {
    if (!html.trim()) {
      reject(new Error("当前没有可打印的预览内容。"));
      return;
    }

    const frame = document.createElement("iframe");
    let settled = false;
    let cleanupTimer = 0;
    let timeoutTimer = 0;
    let fallbackLoadTimer = 0;

    function cleanup() {
      window.clearTimeout(cleanupTimer);
      window.clearTimeout(timeoutTimer);
      window.clearTimeout(fallbackLoadTimer);
      frame.remove();
    }

    function settle(error?: unknown) {
      if (settled) {
        return;
      }

      settled = true;
      cleanupTimer = window.setTimeout(cleanup, 1000);
      if (error) {
        reject(error);
      } else {
        resolve();
      }
    }

    frame.title = title;
    frame.setAttribute("aria-hidden", "true");
    frame.setAttribute("sandbox", "allow-same-origin allow-modals");
    frame.style.position = "fixed";
    frame.style.right = "0";
    frame.style.bottom = "0";
    frame.style.width = "1px";
    frame.style.height = "1px";
    frame.style.border = "0";
    frame.style.opacity = "0";
    frame.style.pointerEvents = "none";

    const printLoadedFrame = () => {
      if (settled) {
        return;
      }

      window.clearTimeout(fallbackLoadTimer);
      void withTimeout(waitForPrintReady(frame), 5000)
        .catch(() => delay(300))
        .then(() => {
          try {
            const previewWindow = frame.contentWindow;
            if (!previewWindow) {
              throw new Error("无法打开打印窗口。");
            }

            previewWindow.focus();
            previewWindow.print();
            settle();
          } catch (error) {
            settle(error);
          }
        });
    };

    frame.addEventListener("load", printLoadedFrame, { once: true });

    timeoutTimer = window.setTimeout(() => {
      settle(new Error("打印预览载入超时。"));
    }, 10000);

    document.body.appendChild(frame);
    frame.srcdoc = buildPrintSourceHtml(html);
    fallbackLoadTimer = window.setTimeout(printLoadedFrame, 800);
  });
}

async function waitForPrintReady(frame: HTMLIFrameElement) {
  const previewWindow = frame.contentWindow;
  const previewDocument = frame.contentDocument;
  if (!previewWindow || !previewDocument) {
    await delay(50);
    return;
  }

  previewDocument.title = "";

  if (previewDocument.readyState !== "complete") {
    await new Promise<void>((resolve) => {
      previewWindow.addEventListener("load", () => resolve(), { once: true });
    });
  }

  try {
    await previewDocument.fonts?.ready;
  } catch {
    // Printing can continue even if a browser rejects font readiness.
  }

  const imageTasks = Array.from(previewDocument.images || []).map((image) => waitForImageReady(image));
  await Promise.all(imageTasks);
  await new Promise<void>((resolve) => previewWindow.requestAnimationFrame(() => previewWindow.requestAnimationFrame(() => resolve())));
}

function waitForImageReady(image: HTMLImageElement) {
  if (image.complete && image.naturalWidth > 0) {
    return typeof image.decode === "function" ? image.decode().catch(() => undefined) : Promise.resolve();
  }

  return new Promise<void>((resolve) => {
    let settled = false;
    let timer = 0;

    function finish() {
      if (settled) {
        return;
      }

      settled = true;
      window.clearTimeout(timer);
      image.removeEventListener("load", finish);
      image.removeEventListener("error", finish);
      resolve();
    }

    image.addEventListener("load", finish, { once: true });
    image.addEventListener("error", finish, { once: true });
    timer = window.setTimeout(finish, 1500);
  });
}

function delay(ms: number) {
  return new Promise<void>((resolve) => window.setTimeout(resolve, ms));
}

function withTimeout<T>(task: Promise<T>, ms: number) {
  return new Promise<T>((resolve, reject) => {
    const timer = window.setTimeout(() => reject(new Error("等待打印预览资源超时。")), ms);
    task.then(
      (value) => {
        window.clearTimeout(timer);
        resolve(value);
      },
      (error) => {
        window.clearTimeout(timer);
        reject(error);
      },
    );
  });
}
