export function createMasterDataSmokeScene(runtime) {
  const {
    authorizedHeaders,
    authorizedJsonHeaders,
    ensureTrailingSlash,
    evaluate,
    fetchJson,
    includesText,
    redactDesktopAccessToken,
    waitFor,
    waitForPageExpression,
    waitForRuntimeDiagnostics,
  } = runtime;

  async function waitForMasterDataKeyboardFlowCheck(page, options, accessToken, tokenType, customer, timeoutMs) {
    const contactPersonValue = `Tauri MasterData CtrlS ${Date.now()}`;
    const enterFlowCheck = await waitForPageExpression(
      page,
      `(() => {
        const surface = document.querySelector('[aria-label="编辑客户"]');
        if (!surface) {
          return false;
        }

        const fieldByLabel = (labelText) => {
          const labels = Array.from(surface.querySelectorAll('label'));
          const label = labels.find((item) =>
            Array.from(item.querySelectorAll('span')).some((span) => (span.textContent || '').trim() === labelText));
          return label ? label.querySelector('input, select, textarea') : null;
        };

        const customerNameEN = fieldByLabel('客户英文名');
        const notifyPartyName = fieldByLabel('通知人');
        if (!customerNameEN || !notifyPartyName) {
          return false;
        }

        customerNameEN.focus();
        customerNameEN.dispatchEvent(new KeyboardEvent('keydown', {
          key: 'Enter',
          bubbles: true,
          cancelable: true,
        }));
        return document.activeElement === notifyPartyName;
      })()`,
      timeoutMs,
      "Timed out waiting for master data editor Enter-as-Tab keyboard flow.",
    );

    await evaluate(
      page,
      `(() => {
        const surface = document.querySelector('[aria-label="编辑客户"]');
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
        const fieldByLabel = (labelText) => {
          const labels = Array.from(surface.querySelectorAll('label'));
          const label = labels.find((item) =>
            Array.from(item.querySelectorAll('span')).some((span) => (span.textContent || '').trim() === labelText));
          if (!label) {
            throw new Error('Master data field not found: ' + labelText);
          }

          const control = label.querySelector('input, select, textarea');
          if (!control) {
            throw new Error('Master data field has no editable control: ' + labelText);
          }

          return control;
        };

        setNativeValue(fieldByLabel('联系人'), ${JSON.stringify(contactPersonValue)});
        return true;
      })()`,
      true,
    );

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

    const saveUiCheck = await waitForPageExpression(
      page,
      `(() => {
        const surface = document.querySelector('[aria-label="编辑客户"]');
        const text = surface ? surface.innerText || '' : '';
        return text.includes('客户已保存');
      })()`,
      timeoutMs,
      "Timed out waiting for master data Ctrl+S save success message.",
    );

    const savedCustomer = await waitFor(async () => {
      const response = await fetch(new URL(`/api/master-data/customers/${customer.id}`, ensureTrailingSlash(options.apiBaseUrl)), {
        method: "GET",
        headers: authorizedHeaders(options, accessToken, tokenType),
      });
      if (!response.ok) {
        return null;
      }

      const payload = await response.json();
      return payload?.contactPerson === contactPersonValue ? payload : null;
    }, timeoutMs, () => `Timed out waiting for master data Ctrl+S persisted contact person: ${customer.customerNameEN}`);

    return {
      enterFlowCheck,
      ctrlSSaveUiCheck: saveUiCheck,
      persistedContactPerson: savedCustomer.contactPerson,
    };
  }

  async function waitForMasterDataDeleteCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.masterDataDeleteCheck) {
      return null;
    }

    const createdRecords = [];
    const entityDeleteChecks = [];
    let result = null;

    try {
      for (const definition of buildMasterDataDeleteDefinitions()) {
        const record = await createSmokeMasterDataRecord(options, accessToken, tokenType, definition);
        const deleteCase = buildMasterDataDeleteCase(definition, record);
        createdRecords.push(deleteCase);
        const check = await waitForMasterDataEntityDeleteCheck(
          page,
          options,
          accessToken,
          tokenType,
          deleteCase,
          timeoutMs,
        );
        deleteCase.deleted = check.detailStatus === 404;
        entityDeleteChecks.push(check);
      }

      const hsCodeClearAllCheck = await waitForMasterDataHsCodeClearAllCheck(
        page,
        options,
        accessToken,
        tokenType,
        timeoutMs,
      );
      const customerCheck = entityDeleteChecks.find((item) => item.entityKey === "customers") ?? null;
      result = {
        customerId: customerCheck?.recordId ?? null,
        customerNameEN: customerCheck?.displayName ?? "",
        url: customerCheck?.url ?? "",
        expectedText: customerCheck?.expectedText ?? [],
        keyboardFlowCheck: customerCheck?.keyboardFlowCheck ?? null,
        deleteButtonCheck: customerCheck?.deleteButtonCheck ?? null,
        confirmMessages: customerCheck?.confirmMessages ?? [],
        redirectedToList: Boolean(customerCheck?.redirectedToList),
        successMessageFound: Boolean(customerCheck?.successMessageFound),
        detailStatus: customerCheck?.detailStatus ?? null,
        cleanupDeleted: Boolean(customerCheck?.cleanupDeleted),
        entityDeleteChecks,
        entityCount: entityDeleteChecks.length,
        hsCodeClearAllCheck,
        allDeleted: entityDeleteChecks.every((item) =>
          item.redirectedToList && item.successMessageFound && item.detailStatus === 404 && item.cleanupDeleted),
      };
    } finally {
      for (const record of createdRecords.filter((item) => !item.deleted)) {
        await deleteSmokeMasterDataRecord(options, accessToken, tokenType, record).catch(() => false);
      }
    }

    return result;
  }

  async function waitForMasterDataHsCodeClearAllCheck(page, options, accessToken, tokenType, timeoutMs) {
    const health = await fetchJson(new URL("/healthz", ensureTrailingSlash(options.apiBaseUrl)));
    const dataRoot = String(health.dataRoot ?? "");
    if (!isSmokeRuntimeDataRoot(dataRoot)) {
      return {
        skipped: true,
        reason: "HS 编码清空验证只在 App_Data/Smoke 运行数据根中执行，避免误清真实本地库。",
        dataRoot,
      };
    }

    const hsCodeDefinition = buildMasterDataDeleteDefinitions().find((definition) => definition.entityKey === "hs-codes");
    if (!hsCodeDefinition) {
      throw new Error("HS code master data delete definition was not found.");
    }

    const createdCases = [];
    let cleared = false;
    try {
      for (let index = 0; index < 2; index += 1) {
        const record = await createSmokeMasterDataRecord(options, accessToken, tokenType, hsCodeDefinition);
        createdCases.push(buildMasterDataDeleteCase(hsCodeDefinition, record));
      }

      const checkUrl = buildMasterDataListCheckUrl(options.webUrl, "hs-codes", "smokeHsCodeClearAll");
      await page.send("Page.navigate", { url: checkUrl });
      const expectedText = ["HS 编码工具", "清空本地库", "联网查询"];
      const pageText = await waitForRuntimeDiagnostics(
        page,
        expectedText,
        timeoutMs,
      );
      const uiAction = await runHsCodeClearAllUiAction(page, timeoutMs);
      const successState = await waitFor(async () => {
        const state = await evaluate(
          page,
          `(() => {
            const text = document.body ? document.body.innerText || "" : "";
            const panel = document.querySelector('[aria-label="HS编码清空确认"]');
            return {
              text,
              confirmationPanelOpen: Boolean(panel),
            };
          })()`,
          true,
        ).catch(() => ({ value: null }));
        const value = state.value ?? {};
        const hasSuccess = String(value.text || "").includes("本地HS编码库已清空") ||
          String(value.text || "").includes("本地HS编码库已为空");
        return hasSuccess && !value.confirmationPanelOpen
          ? value
          : null;
      }, timeoutMs, () => "Timed out waiting for HS code clear-all success message.");

      const detailStatuses = [];
      for (const deleteCase of createdCases) {
        const detailResponse = await fetch(new URL(deleteCase.detailPath, ensureTrailingSlash(options.apiBaseUrl)), {
          method: "GET",
          headers: authorizedHeaders(options, accessToken, tokenType),
        });
        detailStatuses.push({
          code: deleteCase.displayName,
          status: detailResponse.status,
        });
      }

      cleared = detailStatuses.every((item) => item.status === 404);
      if (!cleared) {
        throw new Error(`HS code clear-all did not delete all smoke rows: ${JSON.stringify(detailStatuses)}`);
      }

      return {
        skipped: false,
        url: redactDesktopAccessToken(checkUrl),
        dataRoot,
        expectedText: expectedText.map((value) => ({
          value,
          found: includesText(pageText, value),
        })),
        uiAction,
        successMessageFound: includesText(successState.text || "", "本地HS编码库已清空") ||
          includesText(successState.text || "", "本地HS编码库已为空"),
        confirmationPanelClosed: !successState.confirmationPanelOpen,
        detailStatuses,
        allCleared: cleared,
        storagePolicy: "HS 编码清空 smoke 只在运行数据根 App_Data/Smoke 内执行；清空动作删除当前 smoke 数据库中的 HS 编码记录，不删除导入源文件、不创建默认目录。",
      };
    } finally {
      if (!cleared) {
        for (const deleteCase of createdCases) {
          await deleteSmokeMasterDataRecord(options, accessToken, tokenType, deleteCase).catch(() => false);
        }
      }
    }
  }

  function isSmokeRuntimeDataRoot(dataRoot) {
    const normalized = String(dataRoot ?? "").replace(/\\/g, "/").toLowerCase();
    return normalized.includes("/app_data/smoke");
  }

  async function runHsCodeClearAllUiAction(page, timeoutMs) {
    const result = await evaluate(
      page,
      `(async () => {
        const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
        const section = document.querySelector('[aria-label="HS 编码导入与联网查询"]');
        if (!section) {
          throw new Error("HS 编码工具区未找到。");
        }

        const setNativeValue = (control, value) => {
          const prototype = Object.getPrototypeOf(control);
          const descriptor = Object.getOwnPropertyDescriptor(prototype, "value");
          if (descriptor && typeof descriptor.set === "function") {
            descriptor.set.call(control, value);
          } else {
            control.value = value;
          }
          control.focus();
          control.dispatchEvent(new Event("input", { bubbles: true }));
          control.dispatchEvent(new Event("change", { bubbles: true }));
          const reactPropsKey = Object.keys(control).find((key) => key.startsWith("__reactProps$"));
          const reactProps = reactPropsKey ? control[reactPropsKey] : null;
          if (reactProps && typeof reactProps.onChange === "function") {
            reactProps.onChange({ target: control, currentTarget: control });
          }
        };

        const deadline = Date.now() + ${Math.min(8000, 30000)};
        let latestReason = "";
        while (Date.now() < deadline) {
          const clearButton = Array.from(section.querySelectorAll("button"))
            .find((button) => (button.innerText || "").includes("清空本地库"));
          if (clearButton && !clearButton.disabled) {
            clearButton.click();
            break;
          }

          latestReason = clearButton ? "清空本地库按钮仍不可用" : "清空本地库按钮未找到";
          await delay(100);
        }

        while (Date.now() < deadline) {
          const panel = document.querySelector('[aria-label="HS编码清空确认"]');
          const input = panel?.querySelector("input");
          const confirmButton = panel
            ? Array.from(panel.querySelectorAll("button")).find((button) => (button.innerText || "").includes("确认清空"))
            : null;
          if (panel && input && confirmButton) {
            setNativeValue(input, "CLEAR");
            await delay(100);
            if (!confirmButton.disabled) {
              confirmButton.click();
              return {
                panelFound: true,
                confirmationValue: input.value,
                submitted: true,
              };
            }

            latestReason = "确认清空按钮仍不可用";
          } else {
            latestReason = "清空确认面板尚未出现";
          }

          await delay(100);
        }

        throw new Error(latestReason || "等待 HS 编码清空确认面板超时");
      })()`,
      true,
    );

    return result.value ?? { panelFound: true, confirmationValue: "CLEAR", submitted: true };
  }

  async function waitForMasterDataEntityDeleteCheck(page, options, accessToken, tokenType, deleteCase, timeoutMs) {
    const checkUrl = buildMasterDataDeleteCheckUrl(options.webUrl, deleteCase.entityKey, deleteCase.routeId);
    await page.send("Page.navigate", { url: checkUrl });
    const expectedText = [
      "基础信息",
      "删除",
      deleteCase.displayName,
    ];

    const pageText = await waitForRuntimeDiagnostics(page, expectedText, timeoutMs);
    const keyboardFlowCheck = deleteCase.entityKey === "customers"
      ? await waitForMasterDataKeyboardFlowCheck(
          page,
          options,
          accessToken,
          tokenType,
          deleteCase.record,
          timeoutMs,
        )
      : null;
    const deleteButtonCheck = await waitForPageExpression(
      page,
      `(() => {
        const toolbar = document.querySelector(${JSON.stringify(`[aria-label="${deleteCase.editLabel}"] .editor-toolbar`)});
        const buttons = toolbar ? Array.from(toolbar.querySelectorAll('button')) : [];
        const button = buttons.find((element) => (element.innerText || '').includes('删除'));
        return Boolean(button && !button.disabled);
      })()`,
      timeoutMs,
      `Timed out waiting for the ${deleteCase.label} delete button to become available.`,
    );

    await evaluate(
      page,
      `(() => {
        window.__masterDataDeleteConfirmMessages = [];
        window.confirm = (message) => {
          window.__masterDataDeleteConfirmMessages.push(String(message || ''));
          return true;
        };

        const toolbar = document.querySelector(${JSON.stringify(`[aria-label="${deleteCase.editLabel}"] .editor-toolbar`)});
        const buttons = toolbar ? Array.from(toolbar.querySelectorAll('button')) : [];
        const button = buttons.find((element) => (element.innerText || '').includes('删除'));
        if (!button || button.disabled) {
          throw new Error(${JSON.stringify(`${deleteCase.label} delete button is not available.`)});
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
            confirmMessages: Array.isArray(window.__masterDataDeleteConfirmMessages)
              ? window.__masterDataDeleteConfirmMessages.slice()
              : [],
          }))()`,
          true,
        ).catch(() => ({ value: null }));
      const value = state.value ?? {};
      const text = value.text || "";
      return value.hash.includes(`/master-data/${deleteCase.entityKey}`) &&
        !value.hash.includes(`/master-data/${deleteCase.entityKey}/${deleteCase.routeId}`) &&
        text.includes(deleteCase.successText)
        ? value
        : null;
    }, timeoutMs, () => `Timed out waiting for ${deleteCase.label} delete success message: ${deleteCase.displayName}`);

    const detailResponse = await fetch(new URL(deleteCase.detailPath, ensureTrailingSlash(options.apiBaseUrl)), {
      method: "GET",
      headers: authorizedHeaders(options, accessToken, tokenType),
    });
    const detailStatus = detailResponse.status;
    if (detailStatus !== 404) {
      throw new Error(`Master data delete UI did not remove ${deleteCase.entityKey} ${deleteCase.routeId}; detail status was ${detailStatus}.`);
    }

    return {
      entityKey: deleteCase.entityKey,
      label: deleteCase.label,
      recordId: deleteCase.recordId,
      routeId: deleteCase.routeId,
      displayName: deleteCase.displayName,
      url: redactDesktopAccessToken(checkUrl),
      expectedText: expectedText.map((value) => ({ value, found: includesText(pageText, value) })),
      keyboardFlowCheck,
      deleteButtonCheck,
      confirmMessages: deletedState.confirmMessages,
      redirectedToList: deletedState.hash.includes(`/master-data/${deleteCase.entityKey}`) &&
        !deletedState.hash.includes(`/master-data/${deleteCase.entityKey}/${deleteCase.routeId}`),
      successText: deleteCase.successText,
      successMessageFound: includesText(deletedState.text || "", deleteCase.successText),
      detailStatus,
      cleanupDeleted: true,
    };
  }

  function buildMasterDataDeleteDefinitions() {
    return [
      {
        entityKey: "customers",
        label: "客户",
        editLabel: "编辑客户",
        createPath: "/api/master-data/customers",
        detailPath: (record) => `/api/master-data/customers/${record.id}`,
        deletePath: (record) => `/api/master-data/customers/${record.id}`,
        displayName: (record) => record.customerNameEN || record.displayName,
        routeId: (record) => String(record.id),
        body: (timestamp) => ({
          id: 0,
          customerNameEN: `Smoke Customer ${timestamp}`,
          displayName: `Smoke Customer ${timestamp}`,
          notifyPartyName: `Smoke Notify ${timestamp}`,
          addressEN: "Smoke Customer Address",
          notifyPartyAddress: "Smoke Notify Address",
          contactPerson: "Smoke Contact",
          phone: "13800000000",
          email: "smoke-customer@example.com",
          taxId: `SMOKE-TAX-${timestamp}`,
          notes: "Created by Tauri master data delete smoke and deleted after verification.",
          rowVersion: "",
        }),
      },
      {
        entityKey: "exporters",
        label: "出口商",
        editLabel: "编辑出口商",
        createPath: "/api/master-data/exporters",
        detailPath: (record) => `/api/master-data/exporters/${record.id}`,
        deletePath: (record) => `/api/master-data/exporters/${record.id}`,
        displayName: (record) => record.exporterNameEN,
        routeId: (record) => String(record.id),
        body: (timestamp) => ({
          id: 0,
          exporterNameEN: `Smoke Exporter ${timestamp}`,
          exporterNameCN: `烟测出口商 ${timestamp}`,
          addressEN: "Smoke Exporter Address",
          addressCN: "烟测出口商地址",
          contactPerson: "Smoke Contact",
          creditCode: `SMOKE-CREDIT-${timestamp}`,
          customsCode: `SMOKE-CUS-${String(timestamp).slice(-6)}`,
          phone: "13800000001",
          bankName: "Smoke Bank",
          bankAccount: `SMOKE-BANK-${timestamp}`,
          swiftCode: "SMOKECNS",
          notes: "Created by Tauri master data delete smoke and deleted after verification.",
          docSealPath: "",
          customsSealPath: "",
          rowVersion: "",
        }),
      },
      {
        entityKey: "payees",
        label: "收款对象",
        editLabel: "编辑收款对象",
        createPath: "/api/master-data/payees",
        detailPath: (record) => `/api/master-data/payees/${record.id}`,
        deletePath: (record) => `/api/master-data/payees/${record.id}`,
        displayName: (record) => record.name,
        routeId: (record) => String(record.id),
        body: (timestamp) => ({
          id: 0,
          category: "Smoke",
          name: `Smoke Payee Master ${timestamp}`,
          bankName: "Smoke Bank",
          rmbAccount: `RMB-${timestamp}`,
          usdAccount: `USD-${timestamp}`,
          contactPerson: "Smoke Contact",
          phone: "13800000002",
          notes: "Created by Tauri master data delete smoke and deleted after verification.",
        }),
      },
      {
        entityKey: "products",
        label: "商品",
        editLabel: "编辑商品",
        createPath: "/api/master-data/products",
        detailPath: (record) => `/api/master-data/products/${record.id}`,
        deletePath: (record) => `/api/master-data/products/${record.id}`,
        displayName: (record) => record.nameCN || record.productCode || record.nameEN,
        routeId: (record) => String(record.id),
        body: (timestamp) => ({
          id: 0,
          productCode: `SMOKE-MD-PRODUCT-${timestamp}`,
          nameEN: `Smoke Master Data Goods ${timestamp}`,
          nameCN: `烟测商品 ${timestamp}`,
          description: "Smoke master data delete product.",
          hsCode: "6217109000",
          elements: "Smoke elements",
          supervisionConditions: "",
          inspectionCategory: "",
          taxRebateRate: 13,
          material: "Cotton",
          brand: "SMOKE",
          origin: "CHINA",
          unitEN: "PCS",
          unitCN: "件",
          length: 10,
          width: 20,
          height: 30,
          gwPerCtn: 2,
          nwPerCtn: 1.8,
          pcsPerCtn: 12,
          packageUnitEN: "CTNS",
          packageUnitCN: "箱",
          defaultPrice: 19.88,
        }),
      },
      {
        entityKey: "ports",
        label: "港口",
        editLabel: "编辑港口",
        createPath: "/api/master-data/ports",
        detailPath: (record) => `/api/master-data/ports/${record.id}`,
        deletePath: (record) => `/api/master-data/ports/${record.id}`,
        displayName: (record) => record.nameEN,
        routeId: (record) => String(record.id),
        body: (timestamp) => ({
          id: 0,
          nameEN: `SMOKE PORT ${timestamp}`,
          nameCN: `烟测港口 ${timestamp}`,
          country: "CN",
          code: `SMP${String(timestamp).slice(-5)}`,
        }),
      },
      {
        entityKey: "units",
        label: "单位",
        editLabel: "编辑单位",
        createPath: "/api/master-data/units",
        detailPath: (record) => `/api/master-data/units/${record.id}`,
        deletePath: (record) => `/api/master-data/units/${record.id}`,
        displayName: (record) => record.nameEN,
        routeId: (record) => String(record.id),
        body: (timestamp) => ({
          id: 0,
          nameEN: `SMOKE-UNIT-${timestamp}`,
          nameCN: `烟测单位 ${timestamp}`,
          code: `SMU${String(timestamp).slice(-5)}`,
        }),
      },
      {
        entityKey: "hs-codes",
        label: "HS 编码",
        editLabel: "编辑 HS 编码",
        successText: "HS编码已删除",
        createPath: "/api/master-data/hs-codes",
        detailPath: (record) => `/api/master-data/hs-codes/${encodeURIComponent(record.code)}`,
        deletePath: (record) => `/api/master-data/hs-codes/by-id/${record.id}`,
        displayName: (record) => record.code,
        routeId: (record) => encodeURIComponent(record.code),
        body: (timestamp) => {
          const code = `99${String(timestamp).slice(-8)}`;
          return {
            id: 0,
            code,
            normalizedCode: code,
            name: `Smoke HS ${timestamp}`,
            unit: "千克",
            description: "Smoke HS code for Tauri master data delete smoke.",
            elements: "Smoke elements",
            supervisionConditions: "",
            inspectionCategory: "",
            rebateRate: "13%",
            detailUrl: "",
          };
        },
      },
    ];
  }

  async function createSmokeMasterDataRecord(options, accessToken, tokenType, definition) {
    const timestamp = `${Date.now()}${String(Math.floor(Math.random() * 100000)).padStart(5, "0")}`;
    const body = definition.body(timestamp);
    const response = await fetch(new URL(definition.createPath, ensureTrailingSlash(options.apiBaseUrl)), {
      method: "POST",
      headers: authorizedJsonHeaders(options, accessToken, tokenType),
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      throw new Error(`${definition.label} smoke create failed with HTTP ${response.status}: ${await response.text()}`);
    }

    const payload = await response.json();
    if (!payload?.id) {
      throw new Error(`${definition.label} smoke create response did not include id: ${JSON.stringify(payload)}`);
    }

    return payload;
  }

  function buildMasterDataDeleteCase(definition, record) {
    return {
      deleted: false,
      definition,
      detailPath: definition.detailPath(record),
      displayName: definition.displayName(record),
      editLabel: definition.editLabel,
      entityKey: definition.entityKey,
      label: definition.label,
      successText: definition.successText || `${definition.label}已删除`,
      record,
      recordId: record.id,
      routeId: definition.routeId(record),
      deletePath: definition.deletePath(record),
    };
  }

  async function deleteSmokeMasterDataRecord(options, accessToken, tokenType, deleteCase) {
    if (!deleteCase?.deletePath) {
      return false;
    }

    const response = await fetch(new URL(deleteCase.deletePath, ensureTrailingSlash(options.apiBaseUrl)), {
      method: "DELETE",
      headers: authorizedHeaders(options, accessToken, tokenType),
    });

    return response.ok || response.status === 404;
  }

  function buildMasterDataDeleteCheckUrl(webUrl, entityKey, routeId) {
    const url = new URL(webUrl);
    url.searchParams.set("smokeMasterDataDelete", `${entityKey}-${routeId}`);
    url.hash = `/master-data/${entityKey}/${routeId}`;
    return url.toString();
  }

  function buildMasterDataListCheckUrl(webUrl, entityKey, flagName) {
    const url = new URL(webUrl);
    url.searchParams.set(flagName, "1");
    url.hash = `/master-data/${entityKey}`;
    return url.toString();
  }

  return { run: waitForMasterDataDeleteCheck };
}
