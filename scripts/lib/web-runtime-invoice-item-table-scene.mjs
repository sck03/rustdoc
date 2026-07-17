export function createInvoiceItemTableSmokeScene(runtime) {
  const {
    evaluate,
    waitFor,
    waitForPageExpression,
  } = runtime;

  async function waitForInvoiceItemCellSelectionCheck(page, timeoutMs) {
    const readStateExpression = `(() => {
      const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
      const table = section ? section.querySelector('.item-editor-table') : null;
      const firstRow = table ? table.querySelector('tbody tr:not(:has(.empty-cell))') : null;
      const inputs = firstRow ? Array.from(firstRow.querySelectorAll('input')) : [];
      const toolbar = section ? section.querySelector('[aria-label="明细编辑工具"]') : null;
      const buttons = toolbar ? Array.from(toolbar.querySelectorAll('button')) : [];
      const copySelectionButton = buttons.find((button) => (button.title || '').includes('复制选中单元格'));
      const clearSelectionButton = buttons.find((button) => (button.title || '').includes('清空选中单元格'));
      const undoButton = buttons.find((button) => (button.title || '').includes('撤销明细编辑'));
      const selectedInputs = section ? Array.from(section.querySelectorAll('.item-cell-selected')) : [];
      const message = section ? section.innerText || '' : '';
      return {
        found: Boolean(section && table && firstRow && inputs.length >= 3 && copySelectionButton && clearSelectionButton && undoButton),
        styleNoValue: inputs[1] ? inputs[1].value : '',
        styleNameValue: inputs[2] ? inputs[2].value : '',
        selectedCount: selectedInputs.length,
        canCopySelection: Boolean(copySelectionButton && !copySelectionButton.disabled),
        canClearSelection: Boolean(clearSelectionButton && !clearSelectionButton.disabled),
        canUndo: Boolean(undoButton && !undoButton.disabled),
        copiedText: window.__invoiceItemCopiedText || '',
        message,
      };
    })()`;
  
    const initial = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.found && value.styleNoValue && value.styleNameValue ? value : null;
    }, timeoutMs, () => "Timed out waiting for invoice item cell-selection controls.");
  
    await evaluate(
      page,
      `(() => {
        const firstRow = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"]) .item-editor-table tbody tr:not(:has(.empty-cell))');
        const inputs = firstRow ? Array.from(firstRow.querySelectorAll('input')) : [];
        if (!inputs[1]) {
          throw new Error('Invoice item styleNo input is not available.');
        }
  
        inputs[1].focus();
        return true;
      })()`,
      true,
    );
  
    await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.selectedCount === 1 && value.canCopySelection && value.canClearSelection ? value : null;
    }, timeoutMs, () => "Timed out waiting for the first invoice item cell to be selected.");
  
    await evaluate(
      page,
      `(() => {
        const firstRow = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"]) .item-editor-table tbody tr:not(:has(.empty-cell))');
        const inputs = firstRow ? Array.from(firstRow.querySelectorAll('input')) : [];
        if (!inputs[2]) {
          throw new Error('Invoice item styleName input is not available.');
        }
  
        inputs[2].dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, ctrlKey: true }));
        return true;
      })()`,
      true,
    );
  
    const selected = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.selectedCount === 2 && value.canCopySelection && value.canClearSelection ? value : null;
    }, timeoutMs, () => "Timed out waiting for two invoice item cells to be selected.");
  
    await evaluate(
      page,
      `(() => {
        window.__invoiceItemCopiedText = '';
        const clipboardShim = {
          writeText: async (text) => {
            window.__invoiceItemCopiedText = String(text || '');
            return undefined;
          },
        };
  
        try {
          Object.defineProperty(navigator, 'clipboard', {
            configurable: true,
            value: clipboardShim,
          });
        } catch {
          Object.defineProperty(Navigator.prototype, 'clipboard', {
            configurable: true,
            get: () => clipboardShim,
          });
        }
  
        return true;
      })()`,
      true,
    );
  
    await evaluate(
      page,
      `(() => {
        const toolbar = document.querySelector('[aria-label="明细编辑工具"]');
        const button = toolbar ? Array.from(toolbar.querySelectorAll('button')).find((element) => (element.title || '').includes('复制选中单元格')) : null;
        if (!button || button.disabled) {
          throw new Error('Invoice item copy-selection button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    let latestCopyState = null;
    const copied = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      latestCopyState = value;
      return (value.message || '').includes('已复制 2 个单元格') &&
        (value.copiedText || '').includes(initial.styleNoValue) &&
        (value.copiedText || '').includes(initial.styleNameValue)
        ? value
        : null;
    }, timeoutMs, () =>
      [
        "Timed out waiting for invoice item selected-cell copy result.",
        latestCopyState ? JSON.stringify(latestCopyState, null, 2) : "<empty state>",
      ].join("\n"),
    );
  
    await evaluate(
      page,
      `(() => {
        const toolbar = document.querySelector('[aria-label="明细编辑工具"]');
        const button = toolbar ? Array.from(toolbar.querySelectorAll('button')).find((element) => (element.title || '').includes('清空选中单元格')) : null;
        if (!button || button.disabled) {
          throw new Error('Invoice item clear-selection button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const cleared = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.styleNoValue === '' && value.styleNameValue === '' && value.canUndo ? value : null;
    }, timeoutMs, () => "Timed out waiting for invoice item selected cells to clear.");
  
    await evaluate(
      page,
      `(() => {
        const toolbar = document.querySelector('[aria-label="明细编辑工具"]');
        const button = toolbar ? Array.from(toolbar.querySelectorAll('button')).find((element) => (element.title || '').includes('撤销明细编辑')) : null;
        if (!button || button.disabled) {
          throw new Error('Invoice item undo button is not available after clearing selected cells.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const restored = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.styleNoValue === initial.styleNoValue && value.styleNameValue === initial.styleNameValue ? value : null;
    }, timeoutMs, () => "Timed out waiting for invoice item selected-cell clear undo to restore values.");
  
    return {
      found: true,
      selectedCount: selected.selectedCount,
      copyMessageMatched: Boolean(copied.message),
      copiedText: copied.copiedText,
      clearedStyleNo: cleared.styleNoValue,
      restoredStyleNo: restored.styleNoValue,
    };
  }
  
  async function waitForInvoiceItemColumnVisibilityCheck(page, timeoutMs) {
    const timestamp = Date.now();
    const pastedStyleName = `VISIBLE-NAME-${timestamp}`;
    const pastedFabric = `VISIBLE-FABRIC-${timestamp}`;
    const readStateExpression = `(() => {
      const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
      const table = section ? section.querySelector('.item-editor-table') : null;
      const firstRow = table ? table.querySelector('tbody tr:not(:has(.empty-cell))') : null;
      const headers = table ? Array.from(table.querySelectorAll('thead th')).map((element) => (element.innerText || '').trim()) : [];
      const inputs = firstRow ? Array.from(firstRow.querySelectorAll('input')) : [];
      const toolbar = section ? section.querySelector('[aria-label="明细编辑工具"]') : null;
      const menu = section ? section.querySelector('.item-column-visibility-menu') : document.querySelector('.item-column-visibility-menu');
      const summary = menu ? menu.querySelector('summary[aria-label="显示/隐藏明细列"]') : null;
      const labels = menu ? Array.from(menu.querySelectorAll('.item-column-option')) : [];
      const chineseLabel = labels.find((label) => (label.textContent || '').includes('中文品名'));
      const chineseCheckbox = chineseLabel ? chineseLabel.querySelector('input[type="checkbox"]') : null;
      const buttons = section ? Array.from(section.querySelectorAll('button')) : [];
      const undoButton = buttons.find((button) => (button.title || '').includes('撤销明细编辑'));
      const styleNameInputIndex = headers.indexOf('英文品名') - 1;
      const styleNameCnInputIndex = headers.indexOf('中文品名') - 1;
      const fabricInputIndex = headers.indexOf('成分') - 1;
      const styleNameValue = styleNameInputIndex >= 0 && inputs[styleNameInputIndex] ? inputs[styleNameInputIndex].value : '';
      const styleNameCnValue = styleNameCnInputIndex >= 0 && inputs[styleNameCnInputIndex] ? inputs[styleNameCnInputIndex].value : '';
      const fabricValue = fabricInputIndex >= 0 && inputs[fabricInputIndex] ? inputs[fabricInputIndex].value : '';
      return {
        found: Boolean(section && table && firstRow && toolbar && menu && summary && chineseCheckbox && inputs.length >= 4),
        headers,
        inputCount: inputs.length,
        menuOpen: Boolean(menu && (menu.open || menu.hasAttribute('open'))),
        chineseChecked: chineseCheckbox ? chineseCheckbox.checked : null,
        canToggleChinese: Boolean(chineseCheckbox && !chineseCheckbox.disabled),
        styleNameValue,
        styleNameCnValue,
        fabricValue,
        canUndo: Boolean(undoButton && !undoButton.disabled),
        message: section ? section.innerText || '' : '',
        globalColumnMenuCount: document.querySelectorAll('.item-column-visibility-menu').length,
        globalMenuHtmlExcerpt: document.querySelector('.item-column-visibility-menu')?.outerHTML.slice(0, 1200) || '',
        toolbarHtmlExcerpt: toolbar ? toolbar.outerHTML.slice(0, 1800) : '',
      };
    })()`;
  
    let latestColumnVisibilityState = null;
    const initial = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      latestColumnVisibilityState = value;
      return value.found && value.headers.includes("中文品名") && value.headers.includes("成分") && value.canToggleChinese ? value : null;
    }, timeoutMs, () =>
      [
        "Timed out waiting for invoice item column-visibility controls.",
        latestColumnVisibilityState ? JSON.stringify(latestColumnVisibilityState, null, 2) : "<empty state>",
      ].join("\n"),
    );
  
    await evaluate(
      page,
      `(() => {
        const summary = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"]) .item-column-visibility-menu summary') ||
          document.querySelector('.item-column-visibility-menu summary');
        if (!summary) {
          throw new Error('Invoice item column visibility summary is not available.');
        }
  
        summary.click();
        return true;
      })()`,
      true,
    );
  
    await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.menuOpen ? value : null;
    }, timeoutMs, () => "Timed out waiting for invoice item column menu to open.");
  
    await evaluate(
      page,
      `(() => {
        const labels = Array.from(document.querySelectorAll(':is([aria-label="商品明细"], [aria-label="唛头和明细"]) .item-column-option, .item-column-option'));
        const label = labels.find((candidate) => (candidate.textContent || '').includes('中文品名'));
        const checkbox = label ? label.querySelector('input[type="checkbox"]') : null;
        if (!checkbox || checkbox.disabled || !checkbox.checked) {
          throw new Error('Invoice item Chinese name column checkbox is not available.');
        }
  
        checkbox.click();
        return true;
      })()`,
      true,
    );
  
    const hidden = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.found && !value.headers.includes("中文品名") && value.headers.includes("英文品名") && value.headers.includes("成分")
        ? value
        : null;
    }, timeoutMs, () => "Timed out waiting for invoice item Chinese name column to hide.");
  
    await evaluate(
      page,
      `(() => {
        const table = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"]) .item-editor-table');
        const firstRow = table ? table.querySelector('tbody tr:not(:has(.empty-cell))') : null;
        const headers = table ? Array.from(table.querySelectorAll('thead th')).map((element) => (element.innerText || '').trim()) : [];
        const inputs = firstRow ? Array.from(firstRow.querySelectorAll('input')) : [];
        const index = headers.indexOf('英文品名') - 1;
        const input = index >= 0 ? inputs[index] : null;
        if (!input) {
          throw new Error('Invoice item English style-name input is not available.');
        }
  
        input.focus();
        const transfer = new DataTransfer();
        transfer.setData('text', ${JSON.stringify(`${pastedStyleName}\t${pastedFabric}`)});
        input.dispatchEvent(new ClipboardEvent('paste', {
          bubbles: true,
          cancelable: true,
          clipboardData: transfer,
        }));
        return true;
      })()`,
      true,
    );
  
    const pasted = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.styleNameValue === pastedStyleName && value.fabricValue === pastedFabric && value.canUndo ? value : null;
    }, timeoutMs, () => "Timed out waiting for invoice item paste to follow visible columns.");
  
    await evaluate(
      page,
      `(() => {
        const labels = Array.from(document.querySelectorAll(':is([aria-label="商品明细"], [aria-label="唛头和明细"]) .item-column-option, .item-column-option'));
        const label = labels.find((candidate) => (candidate.textContent || '').includes('中文品名'));
        const checkbox = label ? label.querySelector('input[type="checkbox"]') : null;
        if (!checkbox || checkbox.disabled || checkbox.checked) {
          throw new Error('Invoice item Chinese name column checkbox is not available for restore.');
        }
  
        checkbox.click();
        return true;
      })()`,
      true,
    );
  
    const restoredColumn = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.headers.includes("中文品名") &&
        value.styleNameValue === pastedStyleName &&
        value.styleNameCnValue === initial.styleNameCnValue &&
        value.fabricValue === pastedFabric
        ? value
        : null;
    }, timeoutMs, () => "Timed out waiting for invoice item Chinese name column to restore without data drift.");
  
    await evaluate(
      page,
      `(() => {
        const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
        const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.title || '').includes('撤销明细编辑')) : null;
        if (!button || button.disabled) {
          throw new Error('Invoice item undo button is not available after visible-column paste.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const restoredValues = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.styleNameValue === initial.styleNameValue &&
        value.styleNameCnValue === initial.styleNameCnValue &&
        value.fabricValue === initial.fabricValue
        ? value
        : null;
    }, timeoutMs, () => "Timed out waiting for invoice item visible-column paste undo to restore values.");
  
    return {
      found: true,
      initialInputCount: initial.inputCount,
      hiddenInputCount: hidden.inputCount,
      pastedStyleName: pasted.styleNameValue,
      pastedFabric: pasted.fabricValue,
      restoredColumnChineseValue: restoredColumn.styleNameCnValue,
      restoredStyleName: restoredValues.styleNameValue,
    };
  }
  
  async function waitForInvoiceItemWorkbenchModeCheck(page, timeoutMs) {
    const initial = await waitForPageExpression(
      page,
      `(() => {
        const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
        const table = section ? section.querySelector('.item-editor-table') : null;
        const buttons = section ? Array.from(section.querySelectorAll('button')) : [];
        const workbenchButton = buttons.find((button) => (button.innerText || '').includes('明细工作台'));
        return Boolean(section && table && workbenchButton && !workbenchButton.disabled);
      })()`,
      timeoutMs,
      "Timed out waiting for invoice item workbench entry.",
    );
  
    await evaluate(
      page,
      `(() => {
        const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
        const buttons = section ? Array.from(section.querySelectorAll('button')) : [];
        const button = buttons.find((element) => (element.innerText || '').includes('明细工作台'));
        if (!button || button.disabled) {
          throw new Error('Invoice item workbench button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const focused = await waitFor(async () => {
      const state = await evaluate(
        page,
        `(() => {
          const shell = document.querySelector('[aria-label="商品明细工作台"]');
          const section = document.querySelector('[aria-label="商品明细"]');
          const tableFrame = section ? section.querySelector('.item-editor-frame') : null;
          const table = section ? section.querySelector('.item-editor-table') : null;
          const supportDetails = section ? section.querySelector('.invoice-items-support-details') : null;
          const nav = document.querySelector('[aria-label="发票编辑分区"]');
          const returnButton = shell
            ? Array.from(shell.querySelectorAll('button')).find((button) => (button.innerText || '').includes('返回发票'))
            : null;
          const rect = tableFrame ? tableFrame.getBoundingClientRect() : null;
          return {
            found: Boolean(shell && section && table && supportDetails && returnButton && !returnButton.disabled),
            href: window.location.href || '',
            hash: window.location.hash || '',
            navVisible: Boolean(nav),
            supportCollapsed: Boolean(supportDetails && !supportDetails.open),
            tableFrameHeight: rect ? Math.round(rect.height) : 0,
            tableFrameWidth: rect ? Math.round(rect.width) : 0,
          };
        })()`,
        true,
      ).catch((error) => ({ value: { found: false, error: String(error) } }));
      const value = state.value ?? {};
      return value.found &&
        value.hash.includes("workbench=items") &&
        !value.navVisible &&
        value.supportCollapsed &&
        value.tableFrameHeight >= 260
        ? value
        : null;
    }, timeoutMs, () => "Timed out waiting for invoice item focused workbench mode.");
  
    await evaluate(
      page,
      `(() => {
        const shell = document.querySelector('[aria-label="商品明细工作台"]');
        const buttons = shell ? Array.from(shell.querySelectorAll('button')) : [];
        const button = buttons.find((element) => (element.innerText || '').includes('返回发票'));
        if (!button || button.disabled) {
          throw new Error('Invoice item workbench return button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const restored = await waitFor(async () => {
      const state = await evaluate(
        page,
        `(() => {
          const nav = document.querySelector('[aria-label="发票编辑分区"]');
          const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
          const buttons = section ? Array.from(section.querySelectorAll('button')) : [];
          const workbenchButton = buttons.find((button) => (button.innerText || '').includes('明细工作台'));
          return {
            found: Boolean(nav && section && workbenchButton && !workbenchButton.disabled),
            hash: window.location.hash || '',
          };
        })()`,
        true,
      ).catch((error) => ({ value: { found: false, error: String(error) } }));
      const value = state.value ?? {};
      return value.found && !value.hash.includes("workbench=items") ? value : null;
    }, timeoutMs, () => "Timed out waiting for invoice item workbench mode to return to the invoice editor.");
  
    return {
      found: true,
      initial,
      focusedHeight: focused.tableFrameHeight,
      focusedWidth: focused.tableFrameWidth,
      supportCollapsed: focused.supportCollapsed,
      returnedToEditor: restored.found,
    };
  }
  
  async function waitForInvoiceItemProductLibraryCheck(page, product, timeoutMs) {
    const productId = Number(product.id);
    const productCodeLiteral = JSON.stringify(product.productCode);
    const productNameLiteral = JSON.stringify(product.nameEN);
    const readStateExpression = `(() => {
      const productId = ${productId};
      const productCode = ${productCodeLiteral};
      const productName = ${productNameLiteral};
      const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
      const toolbar = section ? section.querySelector('[aria-label="商品库工具"]') : null;
      const select = toolbar ? toolbar.querySelector('select[aria-label="商品库商品"]') : null;
      const productOption = select
        ? Array.from(select.options).find((option) => option.value === String(productId) || (option.textContent || '').includes(productCode))
        : null;
      const buttons = toolbar ? Array.from(toolbar.querySelectorAll('button')) : [];
      const searchButton = buttons.find((button) => (button.title || '').includes('搜索商品库'));
      const pickerButton = buttons.find((button) => (button.title || '').includes('打开商品库选择'));
      const applyButton = buttons.find((button) => (button.title || '').includes('从商品库新增明细'));
      const saveButton = buttons.find((button) => (button.title || '').includes('保存当前明细到商品库'));
      const refreshButton = buttons.find((button) => (button.title || '').includes('刷新商品库'));
      const undoButton = section
        ? Array.from(section.querySelectorAll('button')).find((button) => (button.title || '').includes('撤销明细编辑'))
        : null;
      const rows = section
        ? Array.from(section.querySelectorAll('.item-editor-table tbody tr')).filter((row) => !row.querySelector('.empty-cell'))
          .filter((row) => !row.classList.contains('item-placeholder-row'))
        : [];
      const rowText = rows.map((row) => row.innerText || '').join('\\n');
      const rowInputValues = rows
        .flatMap((row) => Array.from(row.querySelectorAll('input')).map((input) => input.value || ''))
        .join('\\n');
      const message = section ? section.innerText || '' : '';
      return {
        found: Boolean(section && toolbar && select && searchButton && pickerButton && applyButton && saveButton && refreshButton && undoButton),
        hasProductOption: Boolean(productOption),
        productOptionText: productOption ? productOption.textContent || '' : '',
        rowCount: rows.length,
        selectedProductId: select ? select.value : '',
        canOpenPicker: Boolean(pickerButton && !pickerButton.disabled),
        canApplyProduct: Boolean(applyButton && !applyButton.disabled),
        canSaveProduct: Boolean(saveButton && !saveButton.disabled),
        canUndo: Boolean(undoButton && !undoButton.disabled),
        rowHasProduct: rowText.includes(productCode) || rowText.includes(productName) || rowInputValues.includes(productCode) || rowInputValues.includes(productName),
        rowInputValues,
        message,
      };
    })()`;
  
    const initial = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.found && value.hasProductOption ? value : null;
    }, timeoutMs, () => `Timed out waiting for invoice item product-library controls: ${product.productCode}`);
  
    await evaluate(
      page,
      `(() => {
        const toolbar = document.querySelector('[aria-label="商品库工具"]');
        const select = toolbar ? toolbar.querySelector('select[aria-label="商品库商品"]') : null;
        if (!select) {
          throw new Error('Invoice product-library select is not available.');
        }
  
        select.value = String(${productId});
        select.dispatchEvent(new Event('change', { bubbles: true }));
        return true;
      })()`,
      true,
    );
  
    let latestSelectState = null;
    await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      latestSelectState = value;
      return value.canApplyProduct ? value : null;
    }, timeoutMs, () =>
      [
        `Timed out waiting for product apply button to become available: ${product.productCode}`,
        latestSelectState ? JSON.stringify(latestSelectState, null, 2) : "<empty state>",
      ].join("\n"),
    );
  
    await evaluate(
      page,
      `(() => {
        const toolbar = document.querySelector('[aria-label="商品库工具"]');
        const button = toolbar ? Array.from(toolbar.querySelectorAll('button')).find((element) => (element.title || '').includes('从商品库新增明细')) : null;
        if (!button || button.disabled) {
          throw new Error('Invoice product-library apply button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    let latestAppliedState = null;
    const applied = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      latestAppliedState = value;
      return value.rowCount === initial.rowCount + 1 && value.rowHasProduct ? value : null;
    }, timeoutMs, () =>
      [
        `Timed out waiting for product item row to be added: ${product.productCode}`,
        latestAppliedState ? JSON.stringify(latestAppliedState, null, 2) : "<empty state>",
      ].join("\n"),
    );
  
    let latestSaveButtonState = null;
    await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      latestSaveButtonState = value;
      return value.canSaveProduct ? value : null;
    }, timeoutMs, () =>
      [
        `Timed out waiting for product save button to become available: ${product.productCode}`,
        latestSaveButtonState ? JSON.stringify(latestSaveButtonState, null, 2) : "<empty state>",
      ].join("\n"),
    );
  
    await evaluate(
      page,
      `(() => {
        window.confirm = () => true;
        const toolbar = document.querySelector('[aria-label="商品库工具"]');
        const button = toolbar ? Array.from(toolbar.querySelectorAll('button')).find((element) => (element.title || '').includes('保存当前明细到商品库')) : null;
        if (!button || button.disabled) {
          throw new Error('Invoice product-library save button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const saved = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      const message = value.message || '';
      return message.includes('商品库已更新') || message.includes('商品已保存到商品库') ? value : null;
    }, timeoutMs, () => `Timed out waiting for product item save-to-library result: ${product.productCode}`);
  
    await evaluate(
      page,
      `(() => {
        const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
        const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.title || '').includes('撤销明细编辑')) : null;
        if (!button || button.disabled) {
          throw new Error('Invoice item undo button is not available after product-library apply.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const restored = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.rowCount === initial.rowCount ? value : null;
    }, timeoutMs, () => `Timed out waiting for product item row to restore after undo: ${product.productCode}`);
  
    await evaluate(
      page,
      `(() => {
        const toolbar = document.querySelector('[aria-label="商品库工具"]');
        const button = toolbar ? Array.from(toolbar.querySelectorAll('button')).find((element) => (element.title || '').includes('打开商品库选择')) : null;
        if (!button || button.disabled) {
          throw new Error('Invoice product-library picker button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const dialogStateExpression = `(() => {
      const productId = ${productId};
      const productCode = ${productCodeLiteral};
      const productName = ${productNameLiteral};
      const dialog = document.querySelector('[aria-labelledby="product-library-picker-title"]');
      const input = dialog ? dialog.querySelector('input[aria-label="商品库选择搜索"]') : null;
      const buttons = dialog ? Array.from(dialog.querySelectorAll('button')) : [];
      const searchButton = buttons.find((button) => (button.innerText || '').includes('搜索'));
      const applyButton = buttons.find((button) => (button.innerText || '').includes('套用'));
      const rows = dialog
        ? Array.from(dialog.querySelectorAll('.product-library-table tbody tr')).filter((row) => !row.querySelector('.empty-cell'))
        : [];
      const row = rows.find((candidate) => (candidate.innerText || '').includes(productCode) || (candidate.innerText || '').includes(productName)) || null;
      return {
        found: Boolean(dialog && input && searchButton && applyButton),
        inputValue: input ? input.value || '' : '',
        rowCount: rows.length,
        hasProductRow: Boolean(row),
        canApply: Boolean(applyButton && !applyButton.disabled),
        productRowText: row ? row.innerText || '' : '',
      };
    })()`;
  
    await waitFor(async () => {
      const state = await evaluate(page, dialogStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.found ? value : null;
    }, timeoutMs, () => "Timed out waiting for invoice product-library picker dialog.");
  
    await evaluate(
      page,
      `(() => {
        const dialog = document.querySelector('[aria-labelledby="product-library-picker-title"]');
        const input = dialog ? dialog.querySelector('input[aria-label="商品库选择搜索"]') : null;
        if (!input) {
          throw new Error('Invoice product-library picker search is not available.');
        }
  
        const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
        valueSetter.call(input, ${productCodeLiteral});
        input.dispatchEvent(new Event('input', { bubbles: true }));
        return true;
      })()`,
      true,
    );
  
    await waitFor(async () => {
      const state = await evaluate(page, dialogStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.inputValue === product.productCode ? value : null;
    }, timeoutMs, () => `Timed out waiting for product-library picker search input: ${product.productCode}`);
  
    let latestPickerSearchState = null;
    const pickerSearched = await waitFor(async () => {
      const state = await evaluate(
        page,
        `(() => {
          const productCode = ${productCodeLiteral};
          const productName = ${productNameLiteral};
          const dialog = document.querySelector('[aria-labelledby="product-library-picker-title"]');
          const input = dialog ? dialog.querySelector('input[aria-label="商品库选择搜索"]') : null;
          const buttons = dialog ? Array.from(dialog.querySelectorAll('button')) : [];
          const searchButton = buttons.find((button) => (button.innerText || '').includes('搜索'));
          const applyButton = buttons.find((button) => (button.innerText || '').includes('套用'));
          const rows = dialog
            ? Array.from(dialog.querySelectorAll('.product-library-table tbody tr')).filter((row) => !row.querySelector('.empty-cell'))
            : [];
          const row = rows.find((candidate) => (candidate.innerText || '').includes(productCode) || (candidate.innerText || '').includes(productName)) || null;
          const value = {
            found: Boolean(dialog && input && searchButton && applyButton),
            inputValue: input ? input.value || '' : '',
            rowCount: rows.length,
            hasProductRow: Boolean(row),
            canApply: Boolean(applyButton && !applyButton.disabled),
            productRowText: row ? row.innerText || '' : '',
            sent: false,
          };
          if (!row) {
            return value;
          }
  
          row.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true }));
          return { ...value, sent: true };
        })()`,
        true,
      ).catch(() => ({ value: null }));
      latestPickerSearchState = state.value ?? {};
      return latestPickerSearchState.sent ? latestPickerSearchState : null;
    }, timeoutMs, () =>
      [
        `Timed out waiting for product-library picker search result and row double-click: ${product.productCode}`,
        latestPickerSearchState ? JSON.stringify(latestPickerSearchState, null, 2) : "<empty state>",
      ].join("\n"),
    );
  
    let latestPickerAppliedState = null;
    const pickerApplied = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      latestPickerAppliedState = value;
      return value.rowCount === restored.rowCount + 1 && value.rowHasProduct ? value : null;
    }, timeoutMs, () =>
      [
        `Timed out waiting for product-library picker to add item row: ${product.productCode}`,
        latestPickerAppliedState ? JSON.stringify(latestPickerAppliedState, null, 2) : "<empty state>",
      ].join("\n"),
    );
  
    await evaluate(
      page,
      `(() => {
        const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
        const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.title || '').includes('撤销明细编辑')) : null;
        if (!button || button.disabled) {
          throw new Error('Invoice item undo button is not available after product-library picker apply.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const pickerRestored = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.rowCount === restored.rowCount ? value : null;
    }, timeoutMs, () => `Timed out waiting for product-library picker item row to restore after undo: ${product.productCode}`);
  
    return {
      found: true,
      productId,
      productCode: product.productCode,
      productOptionText: initial.productOptionText,
      initialRowCount: initial.rowCount,
      afterApplyRowCount: applied.rowCount,
      saveMessageMatched: Boolean(saved.message),
      restoredRowCount: restored.rowCount,
      pickerSearchRowCount: pickerSearched.rowCount,
      pickerAppliedRowCount: pickerApplied.rowCount,
      pickerRestoredRowCount: pickerRestored.rowCount,
    };
  }
  
  async function waitForInvoiceItemUndoRedoCheck(page, timeoutMs) {
    const readStateExpression = `(() => {
      const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
      const table = section ? section.querySelector('.item-editor-table') : null;
      const rows = table
        ? Array.from(table.querySelectorAll('tbody tr')).filter((row) => !row.querySelector('.empty-cell'))
          .filter((row) => !row.classList.contains('item-placeholder-row'))
        : [];
      const buttons = section ? Array.from(section.querySelectorAll('button')) : [];
      const duplicateButton = buttons.find((button) => (button.title || '').includes('复制新增明细行'));
      const undoButton = buttons.find((button) => (button.title || '').includes('撤销明细编辑'));
      const redoButton = buttons.find((button) => (button.title || '').includes('重做明细编辑'));
      return {
        found: Boolean(section && table && duplicateButton && undoButton && redoButton),
        rowCount: rows.length,
        canUndo: undoButton ? !undoButton.disabled : false,
        canRedo: redoButton ? !redoButton.disabled : false,
      };
    })()`;
  
    const initialState = await evaluate(page, readStateExpression, true);
    const initial = initialState.value ?? {};
    if (!initial.found || initial.rowCount < 1) {
      throw new Error(`Invoice item undo/redo controls are not ready: ${JSON.stringify(initial)}`);
    }
  
    await evaluate(
      page,
      `(() => {
        const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
        const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.title || '').includes('复制新增明细行')) : null;
        if (!button || button.disabled) {
          throw new Error('Duplicate invoice item button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const duplicated = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.rowCount === initial.rowCount + 1 && value.canUndo ? value : null;
    }, timeoutMs, () => "Timed out waiting for invoice item duplicate before undo.");
  
    await evaluate(
      page,
      `(() => {
        const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
        const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.title || '').includes('撤销明细编辑')) : null;
        if (!button || button.disabled) {
          throw new Error('Invoice item undo button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const undone = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.rowCount === initial.rowCount && value.canRedo ? value : null;
    }, timeoutMs, () => "Timed out waiting for invoice item undo.");
  
    await evaluate(
      page,
      `(() => {
        const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
        const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.title || '').includes('重做明细编辑')) : null;
        if (!button || button.disabled) {
          throw new Error('Invoice item redo button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const redone = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.rowCount === initial.rowCount + 1 && value.canUndo ? value : null;
    }, timeoutMs, () => "Timed out waiting for invoice item redo.");
  
    await evaluate(
      page,
      `(() => {
        const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
        const button = section ? Array.from(section.querySelectorAll('button')).find((element) => (element.title || '').includes('撤销明细编辑')) : null;
        if (!button || button.disabled) {
          throw new Error('Invoice item restore undo button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    const restored = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.rowCount === initial.rowCount ? value : null;
    }, timeoutMs, () => "Timed out waiting for invoice item row count to restore after undo/redo check.");
  
    return {
      found: true,
      initialRowCount: initial.rowCount,
      afterDuplicateRowCount: duplicated.rowCount,
      afterUndoRowCount: undone.rowCount,
      afterRedoRowCount: redone.rowCount,
      restoredRowCount: restored.rowCount,
    };
  }
  
  async function waitForInvoiceItemAutocompleteCheck(page, timeoutMs) {
    const probe = await evaluate(
      page,
      `(() => {
        const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
        const table = section ? section.querySelector('.item-editor-table') : null;
        const displayRows = table
          ? Array.from(table.querySelectorAll('tbody tr')).filter((row) => !row.querySelector('.empty-cell'))
          : [];
        const realRows = displayRows.filter((row) => !row.classList.contains('item-placeholder-row'));
        const placeholderRow = displayRows.find((row) => row.classList.contains('item-placeholder-row'));
        const candidateFields = ['styleNo', 'styleName', 'styleNameCN', 'fabricComposition', 'brand', 'origin', 'spare1', 'spare2', 'spare3'];
        let candidate = null;
  
        for (const field of candidateFields) {
          const fieldValues = realRows
            .map((row) => row.querySelector('input[data-invoice-item-field="' + field + '"]'))
            .map((input) => input ? String(input.value || '').trim() : '')
            .filter(Boolean);
          const distinctValues = Array.from(new Set(fieldValues));
          for (let valueIndex = fieldValues.length - 1; valueIndex >= 0 && !candidate; valueIndex -= 1) {
            const value = fieldValues[valueIndex];
            if (value.length < 2) {
              continue;
            }
  
            for (let prefixLength = 1; prefixLength < Math.min(value.length, 9); prefixLength += 1) {
              const prefix = value.slice(0, prefixLength);
              const prefixKey = prefix.toLocaleLowerCase();
              const matches = distinctValues.filter((entry) => {
                const entryKey = entry.toLocaleLowerCase();
                return entryKey.startsWith(prefixKey) && entryKey !== prefixKey;
              });
              if (matches.length === 1 && matches[0] === value) {
                candidate = { field, prefix, value };
                break;
              }
            }
          }
  
          if (candidate) {
            break;
          }
        }
  
        if (!candidate || !placeholderRow) {
          return { found: false };
        }
  
        const target = placeholderRow.querySelector('input[data-invoice-item-field="' + candidate.field + '"]');
        if (!target) {
          return { found: false };
        }
  
        const rowIndex = Number(target.getAttribute('data-invoice-item-row'));
        window.__invoiceItemAutocompleteProbe = {
          ...candidate,
          rowIndex,
          initialRealRowCount: realRows.length,
          startedAt: 0,
        };
        target.focus();
        return {
          found: true,
          field: candidate.field,
          prefix: candidate.prefix,
          expectedValue: candidate.value,
          rowIndex,
          initialRealRowCount: realRows.length,
        };
      })()`,
      true,
    );
  
    if (!probe.value?.found) {
      throw new Error("No unique invoice item value is available for the autocomplete smoke check.");
    }
  
    await waitFor(async () => {
      const state = await evaluate(
        page,
        `(() => {
          const probe = window.__invoiceItemAutocompleteProbe;
          const active = document.activeElement;
          return Boolean(probe && active &&
            active.getAttribute('data-invoice-item-row') === String(probe.rowIndex) &&
            active.getAttribute('data-invoice-item-field') === probe.field &&
            active.classList.contains('item-cell-selected'));
        })()`,
        true,
      ).catch(() => ({ value: false }));
      return state.value ? true : null;
    }, timeoutMs, () => "Timed out waiting for the invoice item autocomplete target to focus.");
  
    await evaluate(
      page,
      `(() => {
        const probe = window.__invoiceItemAutocompleteProbe;
        const active = document.activeElement;
        if (!probe || !active || !active.matches('input[data-invoice-item-row][data-invoice-item-field]')) {
          throw new Error('Invoice item autocomplete target is not active.');
        }
  
        probe.startedAt = performance.now();
        const descriptor = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
        descriptor.set.call(active, probe.prefix);
        active.dispatchEvent(new Event('input', { bubbles: true }));
        return true;
      })()`,
      true,
    );
  
    const completed = await waitFor(async () => {
      const state = await evaluate(
        page,
        `(() => {
          const probe = window.__invoiceItemAutocompleteProbe;
          if (!probe) {
            return null;
          }
  
          const input = document.querySelector(
            'input[data-invoice-item-row="' + probe.rowIndex + '"][data-invoice-item-field="' + probe.field + '"]'
          );
          if (!input || input.value !== probe.value) {
            return null;
          }
  
          return {
            elapsedMs: performance.now() - probe.startedAt,
            field: probe.field,
            prefix: probe.prefix,
            value: probe.value,
            selectionStart: input.selectionStart,
            selectionEnd: input.selectionEnd,
            initialRealRowCount: probe.initialRealRowCount,
          };
        })()`,
        true,
      ).catch(() => ({ value: null }));
      return state.value;
    }, timeoutMs, () => "Timed out waiting for the first invoice item autocomplete result.");
  
    if (completed.elapsedMs > 750) {
      throw new Error(`Invoice item first autocomplete took ${completed.elapsedMs.toFixed(1)}ms.`);
    }
  
    await evaluate(
      page,
      `(() => {
        const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
        const button = section
          ? Array.from(section.querySelectorAll('button')).find((element) => (element.title || '') === '撤销明细编辑')
          : null;
        if (!button || button.disabled) {
          throw new Error('Invoice item autocomplete cleanup undo button is not available.');
        }
  
        button.click();
        return true;
      })()`,
      true,
    );
  
    await waitFor(async () => {
      const state = await evaluate(
        page,
        `(() => {
          const probe = window.__invoiceItemAutocompleteProbe;
          const rows = Array.from(document.querySelectorAll(':is([aria-label="商品明细"], [aria-label="唛头和明细"]) .item-editor-table tbody tr'))
            .filter((row) => !row.querySelector('.empty-cell'))
            .filter((row) => !row.classList.contains('item-placeholder-row'));
          return probe && rows.length === probe.initialRealRowCount;
        })()`,
        true,
      ).catch(() => ({ value: false }));
      return state.value ? true : null;
    }, timeoutMs, () => "Timed out restoring invoice items after the autocomplete smoke check.");
  
    return {
      found: true,
      field: completed.field,
      prefix: completed.prefix,
      value: completed.value,
      selectionStart: completed.selectionStart,
      selectionEnd: completed.selectionEnd,
      elapsedMs: Number(completed.elapsedMs.toFixed(1)),
    };
  }
  
  async function waitForInvoiceItemKeyboardNavigationCheck(page, timeoutMs) {
    const readStateExpression = `(() => {
      const section = document.querySelector(':is([aria-label="商品明细"], [aria-label="唛头和明细"])');
      const table = section ? section.querySelector('.item-editor-table') : null;
      const displayRows = table
        ? Array.from(table.querySelectorAll('tbody tr')).filter((row) => !row.querySelector('.empty-cell'))
        : [];
      const rows = displayRows.filter((row) => !row.classList.contains('item-placeholder-row'));
      const placeholderRows = displayRows.filter((row) => row.classList.contains('item-placeholder-row'));
      const active = document.activeElement;
      const activeRow = active ? Number(active.getAttribute('data-invoice-item-row')) : NaN;
      const activeField = active ? active.getAttribute('data-invoice-item-field') || '' : '';
      const activeRowElement = Number.isFinite(activeRow) ? displayRows[activeRow] : null;
      const saveButton = document.querySelector('.invoice-form button[type="submit"]');
      const pageText = document.body ? document.body.innerText || '' : '';
      return {
        found: Boolean(section && table && rows.length > 0 && placeholderRows.length > 0 && rows.every((row) => row.querySelector('input[data-invoice-item-field="styleNo"]'))),
        rowCount: displayRows.length,
        realRowCount: rows.length,
        placeholderRowCount: placeholderRows.length,
        activeRow: Number.isFinite(activeRow) ? activeRow : null,
        activeField,
        activeIsPlaceholder: Boolean(activeRowElement && activeRowElement.classList.contains('item-placeholder-row')),
        activeSelected: Boolean(active && active.classList && active.classList.contains('item-cell-selected')),
        canSave: Boolean(saveButton && !saveButton.disabled),
        saved: pageText.includes('发票已保存'),
      };
    })()`;
  
    const initial = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.found && value.canSave ? value : null;
    }, timeoutMs, () => "Timed out waiting for invoice item keyboard navigation controls.");
  
    await evaluate(
      page,
      `(() => {
        const rows = Array.from(document.querySelectorAll(':is([aria-label="商品明细"], [aria-label="唛头和明细"]) .item-editor-table tbody tr'))
          .filter((row) => !row.querySelector('.empty-cell'))
          .filter((row) => !row.classList.contains('item-placeholder-row'));
        const lastRow = rows[rows.length - 1];
        const input = lastRow ? lastRow.querySelector('input[data-invoice-item-field="styleNo"]') : null;
        if (!input) {
          throw new Error('Last invoice item styleNo input is not available.');
        }
  
        input.focus();
        return true;
      })()`,
      true,
    );
  
    await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.activeRow === initial.realRowCount - 1 && value.activeField === 'styleNo' && value.activeSelected ? value : null;
    }, timeoutMs, () => "Timed out waiting for last invoice item styleNo cell to focus.");
  
    await dispatchInvoiceItemKey(page, "Enter");
  
    const afterEnter = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.realRowCount === initial.realRowCount &&
        value.activeRow === initial.realRowCount &&
        value.activeField === 'styleNo' &&
        value.activeIsPlaceholder
        ? value
        : null;
    }, timeoutMs, () => "Timed out waiting for Enter to focus the first reserved blank invoice item row.");
  
    await evaluate(
      page,
      `(() => {
        const active = document.activeElement;
        if (!active || !active.matches('input[data-invoice-item-row][data-invoice-item-field]')) {
          throw new Error('No active invoice item input is available for blank-row editing.');
        }
  
        const descriptor = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
        descriptor.set.call(active, 'KEYBOARD-BLANK-ROW');
        active.dispatchEvent(new Event('input', { bubbles: true }));
        return true;
      })()`,
      true,
    );
  
    const afterBlankInput = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.realRowCount === initial.realRowCount + 1 &&
        value.activeRow === initial.realRowCount &&
        value.activeField === 'styleNo' &&
        !value.activeIsPlaceholder
        ? value
        : null;
    }, timeoutMs, () => "Timed out waiting for reserved blank row input to become an invoice item draft row.");
  
    await dispatchInvoiceItemKey(page, "Enter", { shiftKey: true });
  
    const afterShiftEnter = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.realRowCount === initial.realRowCount + 1 && value.activeRow === initial.realRowCount - 1 && value.activeField === 'styleNo'
        ? value
        : null;
    }, timeoutMs, () => "Timed out waiting for Shift+Enter to focus the previous invoice item row.");
  
    await dispatchInvoiceItemKey(page, "Tab");
  
    const afterTab = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.realRowCount === initial.realRowCount + 1 && value.activeRow === initial.realRowCount && value.activeField === 'styleNo'
        ? value
        : null;
    }, timeoutMs, () => "Timed out waiting for Tab to focus the next invoice item row.");
  
    await dispatchInvoiceItemKey(page, "Tab", { shiftKey: true });
  
    const afterShiftTab = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.realRowCount === initial.realRowCount + 1 && value.activeRow === initial.realRowCount - 1 && value.activeField === 'styleNo'
        ? value
        : null;
    }, timeoutMs, () => "Timed out waiting for Shift+Tab to focus the previous invoice item row.");
  
    await evaluate(
      page,
      `(() => {
        window.dispatchEvent(new KeyboardEvent('keydown', {
          key: 's',
          ctrlKey: true,
          bubbles: true,
          cancelable: true,
        }));
        return true;
      })()`,
      true,
    );
  
    const saved = await waitFor(async () => {
      const state = await evaluate(page, readStateExpression, true).catch(() => ({ value: null }));
      const value = state.value ?? {};
      return value.saved ? value : null;
    }, timeoutMs, () => "Timed out waiting for Ctrl+S to save the invoice draft.");
  
    return {
      found: true,
      initialRowCount: initial.realRowCount,
      placeholderRowCount: initial.placeholderRowCount,
      afterEnterActiveRow: afterEnter.activeRow,
      afterBlankInputRowCount: afterBlankInput.realRowCount,
      afterShiftEnterActiveRow: afterShiftEnter.activeRow,
      afterTabActiveRow: afterTab.activeRow,
      afterShiftTabActiveRow: afterShiftTab.activeRow,
      ctrlSSaved: saved.saved,
    };
  }
  
  async function dispatchInvoiceItemKey(page, key, options = {}) {
    const shiftKey = Boolean(options.shiftKey);
    await evaluate(
      page,
      `(() => {
        const active = document.activeElement;
        if (!active || !active.matches('input[data-invoice-item-row][data-invoice-item-field]')) {
          throw new Error('No active invoice item input is available for key dispatch.');
        }
  
        active.dispatchEvent(new KeyboardEvent('keydown', {
          key: ${JSON.stringify(key)},
          shiftKey: ${JSON.stringify(shiftKey)},
          bubbles: true,
          cancelable: true,
        }));
        return true;
      })()`,
      true,
    );
  }

  async function run(page, product, timeoutMs) {
    const cellSelectionCheck = await waitForInvoiceItemCellSelectionCheck(page, timeoutMs);
    const columnVisibilityCheck = await waitForInvoiceItemColumnVisibilityCheck(page, timeoutMs);
    const workbenchModeCheck = await waitForInvoiceItemWorkbenchModeCheck(page, timeoutMs);
    const productLibraryCheck = await waitForInvoiceItemProductLibraryCheck(page, product, timeoutMs);
    const undoRedoCheck = await waitForInvoiceItemUndoRedoCheck(page, timeoutMs);
    const autocompleteCheck = await waitForInvoiceItemAutocompleteCheck(page, timeoutMs);
    const keyboardNavigationCheck = await waitForInvoiceItemKeyboardNavigationCheck(page, timeoutMs);

    return {
      autocompleteCheck,
      cellSelectionCheck,
      columnVisibilityCheck,
      keyboardNavigationCheck,
      productLibraryCheck,
      undoRedoCheck,
      workbenchModeCheck,
    };
  }

  return { run };
}
