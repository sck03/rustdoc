import { existsSync } from "node:fs";
import path from "node:path";

export function createInvoiceShippingMarkSmokeScene(runtime) {
  const {
    authorizedHeaders,
    ensureTrailingSlash,
    evaluate,
    waitFor,
  } = runtime;

  async function run(page, options, accessToken, tokenType, invoice, timeoutMs) {
    const marker = `SMOKE-MARK-${Date.now()}`;
    const editedMarker = `${marker}-EDIT`;
    const readStateExpression = `(() => {
      const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
      const dialog = document.querySelector('[aria-labelledby="shipping-mark-editor-title"]');
      const modeControl = section ? section.querySelector('[aria-label="唛头类型"]') : null;
      const modeButtons = modeControl ? Array.from(modeControl.querySelectorAll('button')).map((button) => ({
        text: button.innerText || '',
        disabled: Boolean(button.disabled),
        active: button.classList.contains('segmented-active'),
      })) : [];
      const imagePanel = section ? section.querySelector('.shipping-mark-image-panel') : null;
      const preview = imagePanel ? imagePanel.querySelector('.shipping-mark-preview-frame') : null;
      const previewImage = preview ? preview.querySelector('img') : null;
      const imagePath = imagePanel ? imagePanel.querySelector('.shipping-mark-image-path') : null;
      const message = section ? section.innerText || '' : '';
      const canvas = dialog ? dialog.querySelector('.shipping-mark-canvas') : null;
      const input = dialog ? dialog.querySelector('input[aria-label="唛头文字内容"]') : null;
      const toolbar = dialog ? dialog.querySelector('[aria-label="唛头绘图工具"]') : null;
      const toolButtons = toolbar ? Array.from(toolbar.querySelectorAll('button')).map((button) => ({
        label: button.getAttribute('aria-label') || button.title || '',
        pressed: button.getAttribute('aria-pressed') === 'true',
        disabled: Boolean(button.disabled),
      })) : [];
      let nonWhitePixelCount = 0;
      if (canvas) {
        const context = canvas.getContext('2d');
        if (context) {
          const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
          for (let index = 0; index < pixels.length; index += 16) {
            const red = pixels[index];
            const green = pixels[index + 1];
            const blue = pixels[index + 2];
            const alpha = pixels[index + 3];
            if (alpha > 0 && (red < 245 || green < 245 || blue < 245)) {
              nonWhitePixelCount += 1;
            }
          }
        }
      }
  
      return {
        sectionFound: Boolean(section),
        dialogFound: Boolean(dialog),
        modeButtons,
        imagePanelFound: Boolean(imagePanel),
        previewImageSrc: previewImage ? previewImage.src || '' : '',
        previewImageComplete: previewImage ? Boolean(previewImage.complete) : false,
        imagePathText: imagePath ? imagePath.innerText || imagePath.textContent || '' : '',
        message,
        canvasFound: Boolean(canvas),
        inputFound: Boolean(input),
        inputValue: input ? input.value || '' : '',
        toolButtons,
        nonWhitePixelCount,
        saveButtonFound: Boolean(dialog && Array.from(dialog.querySelectorAll('button')).some((button) => (button.innerText || '').trim() === '保存')),
        submitFound: Boolean(document.querySelector('.invoice-form button[type="submit"]')),
        success: document.body ? (document.body.innerText || '').includes('发票已保存') : false,
      };
    })()`;
  
    const initial = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.sectionFound && value.modeButtons.some((button) => button.text.includes("图片") && !button.disabled)
        ? value
        : null;
    }, timeoutMs, () => "Timed out waiting for shipping mark mode controls.");
  
    await evaluate(
      page,
      `(() => {
        const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
        if (!section) {
          throw new Error('Shipping mark section is not available.');
        }
  
        section.scrollIntoView({ block: 'center' });
        const buttons = Array.from(section.querySelectorAll('[aria-label="唛头类型"] button'));
        const imageButton = buttons.find((button) => (button.innerText || '').includes('图片'));
        if (!imageButton || imageButton.disabled) {
          throw new Error('Shipping mark image mode button is not available.');
        }
  
        imageButton.click();
        return true;
      })()`,
      true,
    );
  
    const imageMode = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.imagePanelFound && value.modeButtons.some((button) => button.text.includes("图片") && button.active)
        ? value
        : null;
    }, timeoutMs, () => "Timed out waiting for shipping mark image mode panel.");
  
    await evaluate(
      page,
      `(() => {
        const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
        const buttons = section ? Array.from(section.querySelectorAll('button')) : [];
        const editButton = buttons.find((button) => (button.innerText || '').includes('编辑图片'));
        if (!editButton || editButton.disabled) {
          throw new Error('Shipping mark image editor button is not available.');
        }
  
        editButton.click();
        return true;
      })()`,
      true,
    );
  
    await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.dialogFound && value.canvasFound && value.inputFound && value.saveButtonFound ? value : null;
    }, timeoutMs, () => "Timed out waiting for shipping mark designer dialog.");
  
    await evaluate(
      page,
      `(() => {
        const dialog = document.querySelector('[aria-labelledby="shipping-mark-editor-title"]');
        const input = dialog ? dialog.querySelector('input[aria-label="唛头文字内容"]') : null;
        if (!input || input.disabled) {
          throw new Error('Shipping mark text input is not available.');
        }
  
        input.focus();
        const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
        if (valueSetter) {
          valueSetter.call(input, ${JSON.stringify(marker)});
        } else {
          input.value = ${JSON.stringify(marker)};
        }
        input.dispatchEvent(new Event('input', { bubbles: true }));
  
        const textButton = Array.from(dialog.querySelectorAll('[aria-label="唛头绘图工具"] button'))
          .find((button) => (button.getAttribute('aria-label') || button.title || '') === '文字');
        if (!textButton || textButton.disabled) {
          throw new Error('Shipping mark text tool button is not available.');
        }
  
        textButton.click();
        return input.value;
      })()`,
      true,
    );
  
    await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.inputValue === marker && value.toolButtons.some((button) => button.label === "文字" && button.pressed)
        ? value
        : null;
    }, timeoutMs, () => "Timed out waiting for shipping mark text tool to become active.");
  
    await dispatchShippingMarkCanvasMouse(page, 0.22, 0.24);
  
    const afterText = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.nonWhitePixelCount > 20 ? value : null;
    }, timeoutMs, () => "Timed out waiting for shipping mark text to render on canvas.");
  
    await evaluate(
      page,
      `(() => {
        const dialog = document.querySelector('[aria-labelledby="shipping-mark-editor-title"]');
        const input = dialog ? dialog.querySelector('input[aria-label="唛头文字内容"]') : null;
        if (!input || input.disabled) {
          throw new Error('Shipping mark text input is not editable after adding text.');
        }
  
        const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
        if (valueSetter) {
          valueSetter.call(input, ${JSON.stringify(editedMarker)});
        } else {
          input.value = ${JSON.stringify(editedMarker)};
        }
        input.dispatchEvent(new Event('input', { bubbles: true }));
        return input.value;
      })()`,
      true,
    );
  
    const afterTextEdit = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.inputValue === editedMarker && value.nonWhitePixelCount >= afterText.nonWhitePixelCount ? value : null;
    }, timeoutMs, () => "Timed out waiting for selected shipping mark text to update.");
  
    await evaluate(
      page,
      `(() => {
        const dialog = document.querySelector('[aria-labelledby="shipping-mark-editor-title"]');
        const rectangleButton = dialog
          ? Array.from(dialog.querySelectorAll('[aria-label="唛头绘图工具"] button')).find((button) => (button.getAttribute('aria-label') || button.title || '') === '矩形')
          : null;
        if (!rectangleButton || rectangleButton.disabled) {
          throw new Error('Shipping mark rectangle tool button is not available.');
        }
  
        rectangleButton.click();
        return true;
      })()`,
      true,
    );
  
    await dispatchShippingMarkCanvasMouse(page, 0.52, 0.42, 0.78, 0.66);
  
    const afterRectangle = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.nonWhitePixelCount > afterTextEdit.nonWhitePixelCount ? value : null;
    }, timeoutMs, () => "Timed out waiting for shipping mark rectangle to render on canvas.");
  
    await evaluate(
      page,
      `(() => {
        const dialog = document.querySelector('[aria-labelledby="shipping-mark-editor-title"]');
        const button = dialog ? Array.from(dialog.querySelectorAll('button')).find((element) => (element.innerText || '').trim() === '保存') : null;
        if (!button || button.disabled) {
          throw new Error('Shipping mark designer save button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    let latestSavedPreviewState = null;
    const savedPreview = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      latestSavedPreviewState = value;
      return !value.dialogFound &&
        value.previewImageSrc.startsWith("data:image/png") &&
        value.previewImageComplete &&
        value.imagePathText.includes("Marks")
        ? value
        : null;
    }, timeoutMs, () =>
      [
        "Timed out waiting for shipping mark image save and preview.",
        latestSavedPreviewState ? JSON.stringify(latestSavedPreviewState, null, 2) : "<empty state>",
      ].join("\n"),
    );
  
    await evaluate(
      page,
      `(() => {
        const button = document.querySelector('.invoice-form button[type="submit"]');
        if (!button || button.disabled) {
          throw new Error('Invoice save button is not available after shipping mark image save.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const invoiceSaved = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.success ? value : null;
    }, timeoutMs, () => "Timed out waiting for invoice save after shipping mark image update.");
  
    const detailResponse = await fetch(new URL(`/api/invoices/${invoice.id}`, ensureTrailingSlash(options.apiBaseUrl)), {
      method: "GET",
      headers: authorizedHeaders(options, accessToken, tokenType),
    });
    if (!detailResponse.ok) {
      throw new Error(`Shipping mark designer smoke could not reload invoice ${invoice.id}: HTTP ${detailResponse.status}.`);
    }
  
    const persisted = await detailResponse.json();
    if (persisted.shippingMarksType !== "Image" || persisted.shippingMarks || !String(persisted.shippingMarksImage || "").includes("Marks")) {
      throw new Error(`Shipping mark designer smoke persisted unexpected invoice state: ${JSON.stringify({
        shippingMarksType: persisted.shippingMarksType,
        shippingMarks: persisted.shippingMarks,
        shippingMarksImage: persisted.shippingMarksImage,
      })}`);
    }
  
    const imagePath = String(persisted.shippingMarksImage || "");
    return {
      found: true,
      invoiceId: invoice.id,
      invoiceNo: invoice.invoiceNo,
      marker: editedMarker,
      initialModeButtons: initial.modeButtons,
      imageModeActive: imageMode.modeButtons.some((button) => button.text.includes("图片") && button.active),
      textPixelCount: afterText.nonWhitePixelCount,
      editedTextPixelCount: afterTextEdit.nonWhitePixelCount,
      rectanglePixelCount: afterRectangle.nonWhitePixelCount,
      previewDataUrlHeader: savedPreview.previewImageSrc.slice(0, 22),
      saveMessageObserved: savedPreview.message.includes("唛头图片已保存"),
      imagePath,
      imageFileExists: path.isAbsolute(imagePath) ? existsSync(imagePath) : null,
      invoiceSaved: invoiceSaved.success,
      persistedType: persisted.shippingMarksType,
      persistedTextCleared: persisted.shippingMarks === "",
      storagePolicyEvidence: "唛头图片由发票编辑页保存到运行数据根 Marks，并由受控预览读取；smoke 未写入系统用户配置目录、系统级数据目录或系统盘默认目录。",
      dataBoundaryEvidence: "唛头图片 smoke 只通过当前 invoice.id 更新发票草稿和发票记录，不读取付款/报销单据，也不按 InvoiceNo 合并实际数据/报关数据。",
    };
  }
  
  async function dispatchShippingMarkCanvasMouse(page, startXRatio, startYRatio, endXRatio = startXRatio, endYRatio = startYRatio) {
    const canvasBox = await waitFor(async () => {
      const state = await evaluate(
        page,
        `(() => {
          const canvas = document.querySelector('[aria-labelledby="shipping-mark-editor-title"] .shipping-mark-canvas');
          if (!canvas) {
            return null;
          }
  
          canvas.scrollIntoView({ block: 'center', inline: 'center' });
          const rect = canvas.getBoundingClientRect();
          return {
            left: rect.left,
            top: rect.top,
            width: rect.width,
            height: rect.height,
            visible: rect.width > 0 &&
              rect.height > 0 &&
              rect.bottom > 0 &&
              rect.right > 0 &&
              rect.left < window.innerWidth &&
              rect.top < window.innerHeight,
          };
        })()`,
        true,
      ).catch(() => ({ value: null }));
      const value = state.value ?? null;
      return value && value.visible ? value : null;
    }, 5000, () => "Timed out waiting for shipping mark canvas bounds.");
  
    const startX = canvasBox.left + canvasBox.width * startXRatio;
    const startY = canvasBox.top + canvasBox.height * startYRatio;
    const endX = canvasBox.left + canvasBox.width * endXRatio;
    const endY = canvasBox.top + canvasBox.height * endYRatio;
  
    await page.send("Input.dispatchMouseEvent", {
      type: "mouseMoved",
      x: startX,
      y: startY,
      button: "none",
      buttons: 0,
    });
    await page.send("Input.dispatchMouseEvent", {
      type: "mousePressed",
      x: startX,
      y: startY,
      button: "left",
      buttons: 1,
      clickCount: 1,
    });
    if (startX !== endX || startY !== endY) {
      await page.send("Input.dispatchMouseEvent", {
        type: "mouseMoved",
        x: endX,
        y: endY,
        button: "left",
        buttons: 1,
      });
    }
    await page.send("Input.dispatchMouseEvent", {
      type: "mouseReleased",
      x: endX,
      y: endY,
      button: "left",
      buttons: 0,
      clickCount: 1,
    });
  }

  return { run };
}
