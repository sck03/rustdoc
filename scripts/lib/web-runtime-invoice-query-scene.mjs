export function createInvoiceQuerySmokeScene(runtime) {
  const {
    authorizedHeaders,
    createSmokeInvoice,
    deleteSmokeInvoice,
    dispatchActiveElementKey,
    ensureTrailingSlash,
    evaluate,
    includesText,
    redactDesktopAccessToken,
    waitFor,
    waitForPageExpression,
    waitForRuntimeDiagnostics,
  } = runtime;

  async function waitForInvoiceDeleteCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.invoiceDeleteCheck) {
      return null;
    }

    let invoice = null;
    let detailStatus = null;
    let cleanupDeleted = false;
    let result = null;

    try {
      invoice = await createSmokeInvoice(options, accessToken, tokenType);
      const checkUrl = buildInvoiceDeleteCheckUrl(options.webUrl, invoice.id);
      await page.send("Page.navigate", { url: checkUrl });
      const expectedText = [
        "基础信息",
        "客户与出口商",
        "运输与条款",
        "商品明细",
        "删除",
        invoice.invoiceNo,
      ];

      const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
      const deleteButtonCheck = await waitForPageExpression(
        page,
        `(() => {
          const toolbar = document.querySelector('[aria-label="编辑发票"] .editor-toolbar');
          const buttons = toolbar ? Array.from(toolbar.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('删除'));
          return Boolean(button && !button.disabled);
        })()`,
        timeoutMs,
        "Timed out waiting for the invoice delete button to become available.",
      );

      await evaluate(
        page,
        `(() => {
          window.__invoiceDeleteConfirmMessages = [];
          window.confirm = (message) => {
            window.__invoiceDeleteConfirmMessages.push(String(message || ''));
            return true;
          };

          const toolbar = document.querySelector('[aria-label="编辑发票"] .editor-toolbar');
          const buttons = toolbar ? Array.from(toolbar.querySelectorAll('button')) : [];
          const button = buttons.find((element) => (element.innerText || '').includes('删除'));
          if (!button || button.disabled) {
            throw new Error('Invoice delete button is not available.');
          }

          button.click();
          return true;
        })()`,
        true,
      );

      const deletedState = await waitFor(async () => {
        const state = await evaluate(
          page,
          `(() => ({
            hash: window.location.hash || '',
            text: document.body ? document.body.innerText || '' : '',
            confirmMessages: Array.isArray(window.__invoiceDeleteConfirmMessages)
              ? window.__invoiceDeleteConfirmMessages.slice()
              : [],
          }))()`,
          true,
        ).catch(() => ({ value: null }));
        const value = state.value ?? {};
        const text = value.text || "";
        return value.hash.includes("/invoices") &&
          !value.hash.includes(`/invoices/${invoice.id}`) &&
          text.includes("发票已删除")
          ? value
          : null;
      }, timeoutMs, () => `Timed out waiting for invoice delete success message: ${invoice.invoiceNo}`);

      const detailResponse = await fetch(new URL(`/api/invoices/${invoice.id}`, ensureTrailingSlash(options.apiBaseUrl)), {
        method: "GET",
        headers: authorizedHeaders(options, accessToken, tokenType),
      });
      detailStatus = detailResponse.status;
      if (detailStatus !== 404) {
        throw new Error(`Invoice delete UI did not remove invoice ${invoice.id}; detail status was ${detailStatus}.`);
      }

      cleanupDeleted = true;
      result = {
        invoiceId: invoice.id,
        invoiceNo: invoice.invoiceNo,
        url: redactDesktopAccessToken(checkUrl),
        expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
        deleteButtonCheck,
        confirmMessages: deletedState.confirmMessages,
        redirectedToList: deletedState.hash.includes("/invoices") && !deletedState.hash.includes(`/invoices/${invoice.id}`),
        successMessageFound: includesText(deletedState.text || "", "发票已删除"),
        detailStatus,
        cleanupDeleted,
      };
    } finally {
      if (invoice?.id && detailStatus !== 404) {
        cleanupDeleted = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
        if (result) {
          result.cleanupDeleted = cleanupDeleted;
        }
      }
    }

    return result;
  }

  async function waitForQueryKeyboardCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.queryKeyboardCheck) {
      return null;
    }

    let invoice = null;
    let cleanupDeleted = false;
    let result = null;

    try {
      invoice = await createSmokeInvoice(options, accessToken, tokenType);
      const checkUrl = buildQueryKeyboardCheckUrl(options.webUrl);
      await page.send("Page.navigate", { url: checkUrl });
      const expectedText = [
        "导出",
        "输出路径",
        "发票号",
        "合同号",
        "客户",
        "类型",
      ];

      const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
      const controlsReady = await waitForPageExpression(
        page,
        `(() => {
          const page = document.querySelector('[aria-label="单据查询"]');
          const required = ['startDate', 'endDate', 'customerId', 'exporterId', 'invoiceType', 'transportMode', 'keyword'];
          return Boolean(page && required.every((name) => page.querySelector('[data-query-filter="' + name + '"]')));
        })()`,
        timeoutMs,
        "Timed out waiting for query filter controls.",
      );

      const today = new Date().toISOString().slice(0, 10);
      const monthStart = `${today.slice(0, 8)}01`;
      await evaluate(
        page,
        `(() => {
          const values = {
            startDate: ${JSON.stringify(monthStart)},
            endDate: ${JSON.stringify(today)},
            customerId: '0',
            exporterId: '0',
            invoiceType: '',
            transportMode: '',
            keyword: ${JSON.stringify(invoice.invoiceNo)}
          };
          const setNativeValue = (control, value) => {
            const proto = control instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
            const descriptor = Object.getOwnPropertyDescriptor(proto, 'value');
            descriptor.set.call(control, value);
            control.dispatchEvent(new Event('input', { bubbles: true }));
            control.dispatchEvent(new Event('change', { bubbles: true }));
          };
          for (const [name, value] of Object.entries(values)) {
            const control = document.querySelector('[data-query-filter="' + name + '"]');
            if (!control) {
              throw new Error('Missing query filter control: ' + name);
            }
            setNativeValue(control, value);
          }
          document.querySelector('[data-query-filter="startDate"]').focus();
          return true;
        })()`,
        true,
      );

      await waitForPageExpression(
        page,
        `(() => {
          const keyword = document.querySelector('[data-query-filter="keyword"]');
          const startDate = document.querySelector('[data-query-filter="startDate"]');
          const active = document.activeElement;
          return Boolean(keyword && keyword.value === ${JSON.stringify(invoice.invoiceNo)} &&
            startDate && startDate.value === ${JSON.stringify(monthStart)} &&
            active && active.getAttribute('data-query-filter') === 'startDate');
        })()`,
        timeoutMs,
        "Timed out waiting for query filters to receive smoke values.",
      );

      await dispatchActiveElementKey(page, "Enter");
      const enterFlowCheck = await waitForPageExpression(
        page,
        `document.activeElement && document.activeElement.getAttribute('data-query-filter') === 'endDate'`,
        timeoutMs,
        "Timed out waiting for query Enter flow to move from start date to end date.",
      );

      await dispatchActiveElementKey(page, "Enter", { shiftKey: true });
      const shiftEnterFlowCheck = await waitForPageExpression(
        page,
        `document.activeElement && document.activeElement.getAttribute('data-query-filter') === 'startDate'`,
        timeoutMs,
        "Timed out waiting for query Shift+Enter flow to move back to start date.",
      );

      const ctrlFState = await evaluate(
        page,
        `(() => {
          const event = new KeyboardEvent('keydown', {
            key: 'f',
            ctrlKey: true,
            bubbles: true,
            cancelable: true
          });
          const dispatchResult = window.dispatchEvent(event);
          const active = document.activeElement;
          return {
            defaultPrevented: event.defaultPrevented,
            dispatchResult,
            activeFilter: active ? active.getAttribute('data-query-filter') || '' : ''
          };
        })()`,
        true,
      );

      if (!ctrlFState.value?.defaultPrevented || ctrlFState.value?.activeFilter !== "keyword") {
        throw new Error(`Query Ctrl+F did not focus keyword: ${JSON.stringify(ctrlFState.value)}`);
      }

      await dispatchActiveElementKey(page, "Enter");
      const keywordEnterSearchCheck = await waitFor(async () => {
        const state = await evaluate(
          page,
          `(() => {
            const active = document.activeElement;
            const rows = Array.from(document.querySelectorAll('[aria-label="单据查询"] .query-table tbody tr'))
              .filter((row) => !row.querySelector('.empty-cell'))
              .map((row) => ({
                text: row.innerText || '',
                invoiceNo: row.cells && row.cells[0] ? row.cells[0].innerText.trim() : '',
                type: row.cells && row.cells[12] ? row.cells[12].innerText.trim() : ''
              }));
            return {
              activeFilter: active ? active.getAttribute('data-query-filter') || '' : '',
              rows,
              totalText: document.querySelector('.pagination-bar')?.innerText || ''
            };
          })()`,
          true,
        ).catch(() => ({ value: null }));
        const value = state.value ?? {};
        const rows = Array.isArray(value.rows) ? value.rows : [];
        return value.activeFilter === "startDate" &&
          rows.some((row) => row.invoiceNo === invoice.invoiceNo && row.text.includes("实际数据"))
          ? value
          : null;
      }, timeoutMs, () => `Timed out waiting for query keyword Enter search result: ${invoice.invoiceNo}`);

      const f5State = await evaluate(
        page,
        `(() => {
          const event = new KeyboardEvent('keydown', {
            key: 'F5',
            bubbles: true,
            cancelable: true
          });
          const dispatchResult = window.dispatchEvent(event);
          return {
            defaultPrevented: event.defaultPrevented,
            dispatchResult,
            href: window.location.href,
            rows: Array.from(document.querySelectorAll('[aria-label="单据查询"] .query-table tbody tr'))
              .filter((row) => !row.querySelector('.empty-cell'))
              .map((row) => row.innerText || '')
          };
        })()`,
        true,
      );

      if (!f5State.value?.defaultPrevented) {
        throw new Error(`Query F5 refresh shortcut did not prevent browser reload: ${JSON.stringify(f5State.value)}`);
      }

      await waitForPageExpression(
        page,
        `Array.from(document.querySelectorAll('[aria-label="单据查询"] .query-table tbody tr'))
          .some((row) => (row.innerText || '').includes(${JSON.stringify(invoice.invoiceNo)}))`,
        timeoutMs,
        "Timed out waiting for query row to remain visible after F5 refresh.",
      );

      await evaluate(
        page,
        `(() => {
          const row = Array.from(document.querySelectorAll('[aria-label="单据查询"] .query-table tbody tr'))
            .find((item) => (item.innerText || '').includes(${JSON.stringify(invoice.invoiceNo)}));
          if (!row) {
            throw new Error('Query result row is not available for keyboard open.');
          }

          row.focus();
          row.dispatchEvent(new KeyboardEvent('keydown', {
            key: 'Enter',
            bubbles: true,
            cancelable: true
          }));
          return true;
        })()`,
        true,
      );

      const openedState = await waitFor(async () => {
        const state = await evaluate(
          page,
          `(() => ({
            hash: window.location.hash || '',
            text: document.body ? document.body.innerText || '' : ''
          }))()`,
          true,
        ).catch(() => ({ value: null }));
        const value = state.value ?? {};
        return value.hash.includes(`/invoices/${invoice.id}`) && includesText(value.text || "", invoice.invoiceNo)
          ? value
          : null;
      }, timeoutMs, () => `Timed out waiting for query row keyboard open: ${invoice.invoiceNo}`);

      result = {
        invoiceId: invoice.id,
        invoiceNo: invoice.invoiceNo,
        url: redactDesktopAccessToken(checkUrl),
        expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
        controlsReady,
        enterFlowCheck,
        shiftEnterFlowCheck,
        ctrlFCheck: {
          defaultPrevented: Boolean(ctrlFState.value?.defaultPrevented),
          activeFilter: ctrlFState.value?.activeFilter ?? "",
        },
        keywordEnterSearchCheck: {
          activeFilter: keywordEnterSearchCheck.activeFilter,
          rows: keywordEnterSearchCheck.rows,
          totalText: keywordEnterSearchCheck.totalText,
        },
        f5RefreshCheck: {
          defaultPrevented: Boolean(f5State.value?.defaultPrevented),
          rowVisible: Array.isArray(f5State.value?.rows) &&
            f5State.value.rows.some((row) => includesText(row, invoice.invoiceNo)),
        },
        openedInvoice: openedState.hash.includes(`/invoices/${invoice.id}`),
        cleanupDeleted,
      };
    } finally {
      if (invoice?.id) {
        cleanupDeleted = await deleteSmokeInvoice(options, accessToken, tokenType, invoice.id).catch(() => false);
        if (result) {
          result.cleanupDeleted = cleanupDeleted;
        }
      }
    }

    return result;
  }

  function buildInvoiceDeleteCheckUrl(webUrl, invoiceId) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeInvoiceDelete", String(invoiceId));
    url.hash = `/invoices/${invoiceId}`;
    return url.toString();
  }

  function buildQueryKeyboardCheckUrl(webUrl) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeQueryKeyboard", "1");
    url.hash = "/query/invoices";
    return url.toString();
  }

  return {
    runDelete: waitForInvoiceDeleteCheck,
    runQuery: waitForQueryKeyboardCheck,
  };
}
