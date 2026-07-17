import { rmSync, writeFileSync } from "node:fs";
import path from "node:path";

export function createSingleWindowEditorToolsSmokeScene(runtime) {
  const {
    createSmokeInvoice,
    deleteSmokeInvoice,
    evaluate,
    includesText,
    normalizePathForCompare,
    redactDesktopAccessToken,
    waitFor,
    waitForPageExpression,
    waitForRuntimeDiagnostics,
  } = runtime;

  async function waitForSingleWindowEditorToolsCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.singleWindowEditorToolsCheck) {
      return null;
    }

    let invoice = null;
    let cleanupDeleted = false;
    let result = null;
    let cooAttachmentPath = "";
    const pages = [
      {
        routeKey: "coo",
        resultKey: "customsCoo",
        ariaLabel: "海关原产地证草稿",
        expectedTitle: "COO 草稿",
        lockDialogTitle: "原产地证字段锁定",
      },
      {
        routeKey: "acd",
        resultKey: "agentConsignment",
        ariaLabel: "报关代理委托草稿",
        expectedTitle: "ACD 草稿",
        lockDialogTitle: "代理委托字段锁定",
      },
    ];

    try {
      invoice = await createSmokeInvoice(options, accessToken, tokenType);
      const attachmentTimestamp = Date.now();
      cooAttachmentPath = path.join(options.userDataDir, `customs-coo-attachment-smoke-${attachmentTimestamp}.pdf`);
      writeFileSync(
        cooAttachmentPath,
        [
          `%PDF-1.4`,
          `% ExportDocManager COO attachment smoke ${attachmentTimestamp}`,
          `1 0 obj << /Type /Catalog >> endobj`,
          `trailer << /Root 1 0 R >>`,
          `%%EOF`,
        ].join("\n"),
        "utf8",
      );
      const pageResults = {};

      for (const config of pages) {
        const checkUrl = buildSingleWindowEditorToolsCheckUrl(options.webUrl, invoice.id, config.routeKey);
        await page.send("Page.navigate", { url: checkUrl });
        const expectedText = [
          config.expectedTitle,
          "取默认",
          "回填空白",
          "清覆盖",
          "字段锁定",
          "恢复",
          "分组",
          "类别",
          "预检",
          "保存草稿",
          invoice.invoiceNo,
        ];

        const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
        const buttonCheck = await waitForPageExpression(
          page,
          `(() => {
             const surface = document.querySelector('[aria-label="${config.ariaLabel}"]');
             const toolbar = surface ? surface.querySelector('.editor-toolbar') : null;
             const buttons = toolbar ? Array.from(toolbar.querySelectorAll('button')) : [];
             const labels = ['取默认', '回填空白', '清覆盖', '字段锁定', '预检', '保存草稿'];
             const byLabel = Object.fromEntries(labels.map((label) => {
               const button = buttons.find((element) => (element.innerText || '').includes(label));
               return [label, { found: Boolean(button), disabled: Boolean(button?.disabled) }];
             }));
             const undoButton = buttons.find((element) => (element.title || '').includes('撤销'));
             return labels.every((label) => byLabel[label].found) &&
               !byLabel['取默认'].disabled &&
               !byLabel['回填空白'].disabled &&
               !byLabel['清覆盖'].disabled &&
               !byLabel['字段锁定'].disabled &&
               Boolean(undoButton && undoButton.disabled) &&
               !byLabel['预检'].disabled &&
               !byLabel['保存草稿'].disabled;
          })()`,
          timeoutMs,
          `Timed out waiting for single window editor tool buttons on ${config.expectedTitle}.`,
        );
        const scopedClearCheck = await waitForPageExpression(
          page,
          `(() => {
             const surface = document.querySelector('[aria-label="${config.ariaLabel}"]');
             const tools = surface ? Array.from(surface.querySelectorAll('.single-window-section-tools')) : [];
             const buttons = tools.flatMap((tool) => Array.from(tool.querySelectorAll('button')));
             const groupButton = buttons.find((element) => (element.innerText || '').trim() === '分组');
             const categoryButton = buttons.find((element) => (element.innerText || '').trim() === '类别' && !element.disabled);
            const selects = tools.flatMap((tool) => Array.from(tool.querySelectorAll('select')));
            return Boolean(
              tools.length > 0 &&
              groupButton &&
              !groupButton.disabled &&
              categoryButton &&
              selects.length > 0
            );
          })()`,
          timeoutMs,
          `Timed out waiting for single window scoped clear controls on ${config.expectedTitle}.`,
        );
        const lockDialogOpened = await waitForPageExpression(
          page,
          `(() => {
            const surface = document.querySelector('[aria-label="${config.ariaLabel}"]');
            const toolbar = surface ? surface.querySelector('.editor-toolbar') : null;
            const buttons = toolbar ? Array.from(toolbar.querySelectorAll('button')) : [];
            const button = buttons.find((element) => (element.innerText || '').includes('字段锁定'));
            if (!button || button.disabled) {
              return false;
            }

            button.click();
            return true;
          })()`,
          timeoutMs,
          `Timed out opening single window locked field dialog on ${config.expectedTitle}.`,
        );
        const lockDialogCheck = await waitForPageExpression(
          page,
          `(() => {
            const dialog = document.querySelector('.single-window-lock-dialog[role="dialog"]');
            const text = dialog ? dialog.innerText || '' : '';
            return Boolean(dialog &&
              text.includes(${JSON.stringify(config.lockDialogTitle)}) &&
              text.includes('全选') &&
              text.includes('解锁选中'));
          })()`,
          timeoutMs,
          `Timed out waiting for single window locked field dialog on ${config.expectedTitle}.`,
        );
        await evaluate(
          page,
          `(() => {
            const dialog = document.querySelector('.single-window-lock-dialog[role="dialog"]');
            const buttons = dialog ? Array.from(dialog.querySelectorAll('button')) : [];
            const button = buttons.find((element) => (element.title || '').includes('关闭')) ||
              buttons.find((element) => (element.innerText || '').trim() === '关闭');
            if (button && !button.disabled) {
              button.click();
              return true;
            }

            return false;
          })()`,
          true,
        ).catch(() => undefined);

        const attachmentCheck = config.routeKey === "coo"
          ? await waitForCustomsCooAttachmentPersistenceCheck(page, checkUrl, cooAttachmentPath, timeoutMs)
          : null;

        pageResults[config.resultKey] = {
          url: redactDesktopAccessToken(checkUrl),
          expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
          buttonCheck,
          scopedClearCheck,
          lockDialogOpened,
          lockDialogCheck,
          attachmentCheck,
        };
      }

      pageResults.referenceCatalog = await waitForSingleWindowReferenceCatalogGridCheck(page, options, timeoutMs);

      cleanupDeleted = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
      result = {
        invoiceId: invoice.id,
        invoiceNo: invoice.invoiceNo,
        cleanupDeleted,
        ...pageResults,
      };
    } finally {
      try {
        if (typeof cooAttachmentPath === "string") {
          rmSync(cooAttachmentPath, { force: true });
        }
      } catch {
        // best-effort smoke cleanup
      }

      if (invoice?.id && !cleanupDeleted) {
        cleanupDeleted = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
        if (result) {
          result.cleanupDeleted = cleanupDeleted;
        }
      }
    }

    return result;
  }

  async function waitForCustomsCooAttachmentPersistenceCheck(page, checkUrl, attachmentPath, timeoutMs) {
    const fileName = path.basename(attachmentPath);
    const note = `SMOKE-COO-ATTACHMENT-${Date.now()}`;
    const normalizedAttachmentPath = normalizePathForCompare(attachmentPath);

    await evaluate(
      page,
      `(() => {
        window.__exportDocManagerSmokeCooAttachmentPath = ${JSON.stringify(attachmentPath)};
        return true;
      })()`,
      true,
    );

    const controlsReady = await waitForPageExpression(
      page,
      `(() => {
        const section = document.querySelector('[aria-label="附件"]');
        const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.title || '').includes('选择附件')) : null;
        return Boolean(section && section.querySelector('.coo-attachment-table') && button && !button.disabled);
      })()`,
      timeoutMs,
      "Timed out waiting for COO attachment controls.",
    );

    await evaluate(
      page,
      `(() => {
        const section = document.querySelector('[aria-label="附件"]');
        const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.title || '').includes('选择附件')) : null;
        if (!button || button.disabled) {
          throw new Error('COO attachment selection button is not available.');
        }

        button.click();
        return true;
      })()`,
      true,
    );

    const added = await waitForCustomsCooAttachmentRowState(
      page,
      attachmentPath,
      note,
      { requireNote: false },
      timeoutMs,
      `Timed out waiting for selected COO attachment row: ${attachmentPath}`,
    );

    await evaluate(
      page,
      `(() => {
        const expectedPath = ${JSON.stringify(attachmentPath)};
        const normalize = (value) => String(value || '').replace(/\\\\/g, '/').toLowerCase();
        const rows = Array.from(document.querySelectorAll('[aria-label="附件"] .coo-attachment-table tbody tr'));
        const row = rows.find((candidate) => Array.from(candidate.querySelectorAll('input')).some((input) => normalize(input.value) === normalize(expectedPath)));
        const noteInput = row ? Array.from(row.querySelectorAll('input')).find((input) => (input.getAttribute('aria-label') || '').includes('附件说明')) : null;
        if (!noteInput) {
          throw new Error('COO attachment note input was not found.');
        }

        const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        setter.call(noteInput, ${JSON.stringify(note)});
        noteInput.dispatchEvent(new Event('input', { bubbles: true }));
        return true;
      })()`,
      true,
    );

    const noteApplied = await waitForCustomsCooAttachmentRowState(
      page,
      attachmentPath,
      note,
      { requireNote: true },
      timeoutMs,
      `Timed out waiting for COO attachment note to apply: ${note}`,
    );

    await evaluate(
      page,
      `(() => {
        const button = document.querySelector('button[type="submit"][form="customs-coo-form"]');
        if (!button || button.disabled) {
          throw new Error('COO save button is not available for attachment persistence check.');
        }

        button.click();
        return true;
      })()`,
      true,
    );

    const saved = await waitForPageExpression(
      page,
      `(() => {
        const bodyText = document.body ? document.body.innerText || '' : '';
        if (!bodyText.includes('原产地证草稿已保存')) {
          return false;
        }

        return (${buildCustomsCooAttachmentRowStateExpression(attachmentPath, note, { requireNote: true, expressionOnly: true })}).found;
      })()`,
      timeoutMs,
      "Timed out waiting for COO attachment save confirmation.",
    );

    await page.send("Page.navigate", { url: checkUrl });
    await waitForRuntimeDiagnostics(page, ["COO 草稿", "附件", "保存草稿"], timeoutMs);

    const reloaded = await waitForCustomsCooAttachmentRowState(
      page,
      attachmentPath,
      note,
      { requireNote: true },
      timeoutMs,
      `Timed out waiting for persisted COO attachment after reload: ${attachmentPath}`,
    );

    await evaluate(
      page,
      `(() => {
        const expectedPath = ${JSON.stringify(attachmentPath)};
        const normalize = (value) => String(value || '').replace(/\\\\/g, '/').toLowerCase();
        const rows = Array.from(document.querySelectorAll('[aria-label="附件"] .coo-attachment-table tbody tr'));
        const row = rows.find((candidate) => Array.from(candidate.querySelectorAll('input')).some((input) => normalize(input.value) === normalize(expectedPath)));
        const button = row ? Array.from(row.querySelectorAll('button')).find((element) => (element.title || '').includes('打开附件')) : null;
        if (!button || button.disabled) {
          throw new Error('COO attachment open button is not available.');
        }

        button.click();
        return true;
      })()`,
      true,
    );

    const openPathCheck = await waitForPageExpression(
      page,
      `(() => {
        const expected = ${JSON.stringify(normalizedAttachmentPath)};
        const normalize = (value) => String(value || '').replace(/\\\\/g, '/').replace(/\\/+$/, '').toLowerCase();
        const invocations = window.__exportDocManagerSmokeTauriInvocations || [];
        return invocations.some((entry) => entry &&
          entry.command === 'open_path' &&
          normalize(entry.args && entry.args.path) === expected);
      })()`,
      timeoutMs,
      `Timed out waiting for COO attachment open_path invocation: ${attachmentPath}`,
    );

    await waitForPageExpression(
      page,
      `(() => {
        const expectedPath = ${JSON.stringify(attachmentPath)};
        const normalize = (value) => String(value || '').replace(/\\\\/g, '/').toLowerCase();
        const rows = Array.from(document.querySelectorAll('[aria-label="附件"] .coo-attachment-table tbody tr'));
        const row = rows.find((candidate) => Array.from(candidate.querySelectorAll('input')).some((input) => normalize(input.value) === normalize(expectedPath)));
        const button = row ? Array.from(row.querySelectorAll('button')).find((element) => (element.title || '').includes('删除附件')) : null;
        return Boolean(button && !button.disabled);
      })()`,
      timeoutMs,
      "Timed out waiting for the COO attachment delete button to become available.",
    );

    await evaluate(
      page,
      `(() => {
        const expectedPath = ${JSON.stringify(attachmentPath)};
        const normalize = (value) => String(value || '').replace(/\\\\/g, '/').toLowerCase();
        const rows = Array.from(document.querySelectorAll('[aria-label="附件"] .coo-attachment-table tbody tr'));
        const row = rows.find((candidate) => Array.from(candidate.querySelectorAll('input')).some((input) => normalize(input.value) === normalize(expectedPath)));
        const button = row ? Array.from(row.querySelectorAll('button')).find((element) => (element.title || '').includes('删除附件')) : null;
        if (!button || button.disabled) {
          throw new Error('COO attachment delete button is not available.');
        }

        button.click();
        return true;
      })()`,
      true,
    );

    const removedInEditor = await waitForCustomsCooAttachmentRowMissing(
      page,
      attachmentPath,
      note,
      timeoutMs,
      `Timed out waiting for COO attachment row to be removed in editor: ${attachmentPath}`,
    );

    await evaluate(
      page,
      `(() => {
        const button = document.querySelector('button[type="submit"][form="customs-coo-form"]');
        if (!button || button.disabled) {
          throw new Error('COO save button is not available for attachment deletion check.');
        }

        button.click();
        return true;
      })()`,
      true,
    );

    const deletedAfterSave = await waitForPageExpression(
      page,
      `(() => {
        const bodyText = document.body ? document.body.innerText || '' : '';
        if (!bodyText.includes('原产地证草稿已保存')) {
          return false;
        }

        return !(${buildCustomsCooAttachmentRowStateExpression(attachmentPath, note, { requireNote: true, expressionOnly: true })}).found;
      })()`,
      timeoutMs,
      "Timed out waiting for COO attachment deletion save confirmation.",
    );

    await page.send("Page.navigate", { url: checkUrl });
    await waitForRuntimeDiagnostics(page, ["海关原产地证草稿", "附件", "保存"], timeoutMs);

    const deletedAfterReload = await waitForCustomsCooAttachmentRowMissing(
      page,
      attachmentPath,
      note,
      timeoutMs,
      `Timed out waiting for deleted COO attachment to stay absent after reload: ${attachmentPath}`,
    );

    return {
      found: true,
      fileName,
      attachmentPath,
      note,
      controlsReady: Boolean(controlsReady?.found),
      addedRowCount: added?.rowCount ?? 0,
      addedFileName: added?.fileName || "",
      noteApplied: Boolean(noteApplied?.noteMatched),
      saved: Boolean(saved?.found),
      reloaded: Boolean(reloaded?.found),
      reloadedRowCount: reloaded?.rowCount ?? 0,
      openPathCheck: Boolean(openPathCheck?.found),
      removedInEditor: Boolean(removedInEditor?.missing),
      removedRowCount: removedInEditor?.rowCount ?? 0,
      deletedAfterSave: Boolean(deletedAfterSave?.found),
      deletedAfterReload: Boolean(deletedAfterReload?.missing),
      deletedReloadRowCount: deletedAfterReload?.rowCount ?? 0,
    };
  }

  async function waitForCustomsCooAttachmentRowState(page, attachmentPath, note, options, timeoutMs, description) {
    const stateExpression = buildCustomsCooAttachmentRowStateExpression(attachmentPath, note, options);
    await waitForPageExpression(
      page,
      `(${stateExpression}).found`,
      timeoutMs,
      description,
    );

    const result = await evaluate(page, stateExpression, true).catch(() => ({ value: null }));
    return result.value ?? null;
  }

  async function waitForCustomsCooAttachmentRowMissing(page, attachmentPath, note, timeoutMs, description) {
    const stateExpression = buildCustomsCooAttachmentRowStateExpression(attachmentPath, note, { requireNote: true });
    await waitForPageExpression(
      page,
      `!(${stateExpression}).found`,
      timeoutMs,
      description,
    );

    const result = await evaluate(
      page,
      `(() => {
        const state = (${stateExpression});
        return { ...state, missing: !state.found };
      })()`,
      true,
    ).catch(() => ({ value: null }));
    return result.value ?? null;
  }

  function buildCustomsCooAttachmentRowStateExpression(attachmentPath, note, options = {}) {
    const requireNote = Boolean(options.requireNote);
    const expressionOnly = Boolean(options.expressionOnly);
    const source = `(() => {
      const expectedPath = ${JSON.stringify(attachmentPath)};
      const expectedNote = ${JSON.stringify(note)};
      const normalize = (value) => String(value || '').replace(/\\\\/g, '/').toLowerCase();
      const section = document.querySelector('[aria-label="附件"]');
      const table = section ? section.querySelector('.coo-attachment-table') : null;
      const rows = table ? Array.from(table.querySelectorAll('tbody tr')).filter((row) => !row.querySelector('.empty-cell')) : [];
      const row = rows.find((candidate) => Array.from(candidate.querySelectorAll('input')).some((input) => normalize(input.value) === normalize(expectedPath)));
      const fileNameInput = row ? Array.from(row.querySelectorAll('input')).find((input) => (input.getAttribute('aria-label') || '').includes('附件文件名')) : null;
      const noteInput = row ? Array.from(row.querySelectorAll('input')).find((input) => (input.getAttribute('aria-label') || '').includes('附件说明')) : null;
      const noteMatched = Boolean(noteInput && noteInput.value === expectedNote);
      return {
        found: Boolean(section && table && row && (!${JSON.stringify(requireNote)} || noteMatched)),
        rowCount: rows.length,
        fileName: fileNameInput ? fileNameInput.value || '' : '',
        note: noteInput ? noteInput.value || '' : '',
        noteMatched,
      };
    })()`;

    return expressionOnly ? source : source;
  }

  async function waitForSingleWindowReferenceCatalogGridCheck(page, options, timeoutMs) {
    const timestamp = Date.now();
    const duplicateCode = `ZX${String(timestamp).slice(-8)}`;
    const englishName = `SMOKE COUNTRY ${timestamp}`;
    const chineseName = `烟测国家${String(timestamp).slice(-4)}`;
    const firstAlias = `ALIAS-A-${timestamp}`;
    const secondAlias = `ALIAS-B-${timestamp}`;
    const appliedAlias = `ALIAS-APPLIED-${timestamp}`;
    const checkUrl = buildSingleWindowReferenceCatalogCheckUrl(options.webUrl);

    await page.send("Page.navigate", { url: checkUrl });
    const expectedText = [
      "参考词典",
      "国家/地区(COO)",
      "批量粘贴",
      "去重",
      "保存",
    ];
    const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
    const tableReady = await waitForPageExpression(
      page,
      `(() => {
        const surface = document.querySelector('[aria-label="单一窗口参考词典"]');
        return Boolean(surface &&
          surface.querySelector('.reference-catalog-table') &&
          surface.querySelector('[data-catalog-row="0"][data-catalog-column="0"]'));
      })()`,
      timeoutMs,
      "Timed out waiting for the single-window reference catalog table.",
    );

    await evaluate(
      page,
      `(() => {
        const surface = document.querySelector('[aria-label="单一窗口参考词典"]');
        const input = surface ? surface.querySelector('[data-catalog-row="0"][data-catalog-column="0"]') : null;
        if (!input) {
          throw new Error('Reference catalog first editable cell is not available.');
        }

        input.focus();
        const transfer = new DataTransfer();
        const text = ${JSON.stringify(
          `${duplicateCode}\t${englishName}\t${chineseName}\t${firstAlias}\n${duplicateCode}\t${englishName} DUP\t${chineseName}二\t${secondAlias}`,
        )};
        transfer.setData('text', text);
        transfer.setData('text/plain', text);
        input.dispatchEvent(new ClipboardEvent('paste', {
          bubbles: true,
          cancelable: true,
          clipboardData: transfer,
        }));
        return true;
      })()`,
      true,
    );

    const pasteCheck = await waitFor(async () => {
      const state = await evaluate(
        page,
        `(() => {
          const surface = document.querySelector('[aria-label="单一窗口参考词典"]');
          const read = (row, column) => {
            const element = surface ? surface.querySelector('[data-catalog-row="' + row + '"][data-catalog-column="' + column + '"]') : null;
            return element ? element.value || '' : '';
          };
          const text = surface ? surface.innerText || '' : '';
          return {
            firstCode: read(0, 0),
            firstAlias: read(0, 3),
            secondCode: read(1, 0),
            secondAlias: read(1, 3),
            success: text.includes('已批量粘贴'),
          };
        })()`,
        true,
      ).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.firstCode === duplicateCode &&
        value.secondCode === duplicateCode &&
        value.firstAlias.includes(firstAlias) &&
        value.secondAlias.includes(secondAlias) &&
        value.success
        ? value
        : null;
    }, timeoutMs, () => "Timed out waiting for reference catalog table paste to update the draft.");

    await evaluate(
      page,
      `(() => {
        const alias = document.querySelector('[aria-label="单一窗口参考词典"] [data-catalog-row="0"][data-catalog-column="3"]');
        if (!alias) {
          throw new Error('Reference catalog alias cell is not available.');
        }

        alias.focus();
        alias.dispatchEvent(new KeyboardEvent('keydown', {
          key: 'F4',
          bubbles: true,
          cancelable: true,
        }));
        return true;
      })()`,
      true,
    );

    const aliasDialogCheck = await waitForPageExpression(
      page,
      `(() => {
        const dialog = document.querySelector('.reference-catalog-alias-dialog[role="dialog"]');
        return Boolean(dialog && (dialog.innerText || '').includes('编辑别名') && dialog.querySelector('textarea'));
      })()`,
      timeoutMs,
      "Timed out waiting for reference catalog alias dialog.",
    );

    await evaluate(
      page,
      `(() => {
        const dialog = document.querySelector('.reference-catalog-alias-dialog[role="dialog"]');
        const textarea = dialog ? dialog.querySelector('textarea') : null;
        const apply = dialog ? Array.from(dialog.querySelectorAll('button')).find((button) => (button.innerText || '').includes('应用')) : null;
        if (!textarea || !apply) {
          throw new Error('Reference catalog alias dialog controls are not available.');
        }

        const descriptor = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value');
        descriptor.set.call(textarea, ${JSON.stringify(`${appliedAlias}\n${firstAlias}`)});
        textarea.dispatchEvent(new Event('input', { bubbles: true }));
        apply.click();
        return true;
      })()`,
      true,
    );

    const aliasApplyCheck = await waitFor(async () => {
      const state = await evaluate(
        page,
        `(() => {
          const alias = document.querySelector('[aria-label="单一窗口参考词典"] [data-catalog-row="0"][data-catalog-column="3"]');
          return { value: alias ? alias.value || '' : '' };
        })()`,
        true,
      ).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.value.includes(appliedAlias) && value.value.includes(firstAlias) ? value : null;
    }, timeoutMs, () => "Timed out waiting for reference catalog alias dialog changes to apply.");

    await evaluate(
      page,
      `(() => {
        const input = document.querySelector('[aria-label="单一窗口参考词典"] [data-catalog-row="0"][data-catalog-column="0"]');
        if (!input) {
          throw new Error('Reference catalog first cell is not available for context menu.');
        }

        input.focus();
        input.dispatchEvent(new MouseEvent('contextmenu', {
          bubbles: true,
          cancelable: true,
          clientX: 220,
          clientY: 220,
        }));
        return true;
      })()`,
      true,
    );

    const contextMenuCheck = await waitFor(async () => {
      const state = await evaluate(
        page,
        `(() => {
          const menu = document.querySelector('.reference-catalog-context-menu');
          const text = menu ? menu.innerText || '' : '';
          return {
            found: Boolean(menu),
            text,
            hasAdd: text.includes('新增一行'),
            hasPaste: text.includes('批量粘贴'),
            hasDeduplicate: text.includes('批量去重'),
            hasAlias: text.includes('编辑别名'),
          };
        })()`,
        true,
      ).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.found && value.hasAdd && value.hasPaste && value.hasDeduplicate && value.hasAlias ? value : null;
    }, timeoutMs, () => "Timed out waiting for reference catalog context menu.");

    await evaluate(
      page,
      `(() => {
        const input = document.querySelector('[aria-label="单一窗口参考词典"] [data-catalog-row="0"][data-catalog-column="0"]');
        if (!input) {
          throw new Error('Reference catalog first cell is not available for Ctrl+D.');
        }

        input.focus();
        input.dispatchEvent(new KeyboardEvent('keydown', {
          key: 'd',
          ctrlKey: true,
          bubbles: true,
          cancelable: true,
        }));
        return true;
      })()`,
      true,
    );

    const ctrlDDeduplicateCheck = await waitFor(async () => {
      const state = await evaluate(
        page,
        `(() => {
          const surface = document.querySelector('[aria-label="单一窗口参考词典"]');
          const alias = surface ? surface.querySelector('[data-catalog-row="0"][data-catalog-column="3"]') : null;
          const text = surface ? surface.innerText || '' : '';
          return {
            messageFound: text.includes('当前页重复项已合并'),
            aliasValue: alias ? alias.value || '' : '',
          };
        })()`,
        true,
      ).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.messageFound && value.aliasValue.includes(appliedAlias) && value.aliasValue.includes(secondAlias)
        ? value
        : null;
    }, timeoutMs, () => "Timed out waiting for reference catalog Ctrl+D deduplicate.");

    return {
      url: redactDesktopAccessToken(checkUrl),
      expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
      tableReady,
      pasteCheck,
      aliasDialogCheck,
      aliasApplyCheck,
      contextMenuCheck,
      ctrlDDeduplicateCheck,
    };
  }

  function buildSingleWindowEditorToolsCheckUrl(webUrl, invoiceId, routeKey) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeSingleWindowEditorTools", routeKey);
    url.hash = `/single-window/${routeKey}/${invoiceId}`;
    return url.toString();
  }

  function buildSingleWindowReferenceCatalogCheckUrl(webUrl) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeSingleWindowReferenceCatalog", "1");
    url.hash = "/single-window/reference-catalog";
    return url.toString();
  }

  return { run: waitForSingleWindowEditorToolsCheck };
}
