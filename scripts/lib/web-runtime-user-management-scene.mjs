export function createUserManagementSmokeScene(runtime) {
  const {
    authorizedHeaders,
    ensureTrailingSlash,
    evaluate,
    includesText,
    waitFor,
    waitForPageExpression,
  } = runtime;

  async function run(page, options, accessToken, tokenType, timeoutMs) {
    const userManagementCrudCheck = await waitForUserManagementCrudCheck(
      page,
      options,
      accessToken,
      tokenType,
      timeoutMs,
    );
    const userRows = await waitForUserRows(page, options.expectedUserRows, timeoutMs);
    return { userManagementCrudCheck, userRows };
  }

  async function waitForUserManagementCrudCheck(page, options, accessToken, tokenType, timeoutMs) {
    if (!options.userManagementCrudCheck) {
      return null;
    }

    const suffix = `${Date.now()}-${Math.floor(Math.random() * 100000)}`;
    const username = `smoke-ui-${suffix}`;
    const password = `Smoke-${suffix}!`;
    const fullName = `Smoke UI User ${suffix}`;
    const updatedFullName = `Smoke UI Updated ${suffix}`;
    const departmentId = `UI-${suffix.slice(-6)}`;
    const companyScope = "SMOKE-HQ";

    const checkUrl = new URL(options.webUrl);
    checkUrl.searchParams.set("smokeUserManagement", "1");
    checkUrl.hash = "/system/access-control";
    await page.send("Page.navigate", { url: checkUrl.toString() });

    await waitForPageExpression(
      page,
      `Boolean(document.querySelector('[aria-label="用户与权限"] .user-management-table') &&
        Array.from(document.querySelectorAll('[aria-label="用户与权限"] button')).some((button) => (button.title || '').includes('新建用户')))`,
      timeoutMs,
      "Timed out waiting for the user management panel.",
    );

    const createAction = await runUserManagementUiAction(page, "create", {
      username,
      password,
      fullName,
      role: "Finance",
      departmentId,
      companyScope,
    });

    const createdRow = await waitForUserManagementRow(
      page,
      username,
      (row) => includesText(row.text, username) &&
        includesText(row.text, fullName) &&
        includesText(row.text, "财务人员") &&
        includesText(row.text, "启用"),
      timeoutMs,
      `Timed out waiting for created user row: ${username}`,
    );
    const createdApiUser = await waitForApiUserManagementUser(
      options,
      accessToken,
      tokenType,
      username,
      (user) =>
        user &&
        user.fullName === fullName &&
        user.role === "Finance" &&
        user.departmentId === departmentId &&
        user.companyScope === companyScope &&
        user.isActive === true &&
        !hasLeakedPasswordHash(user),
      timeoutMs,
      `Timed out waiting for API-created user: ${username}`,
    );

    const updateAction = await runUserManagementUiAction(page, "update", {
      username,
      fullName: updatedFullName,
      role: "User",
      departmentId,
      companyScope,
    });

    const updatedRow = await waitForUserManagementRow(
      page,
      username,
      (row) => includesText(row.text, username) &&
        includesText(row.text, updatedFullName) &&
        includesText(row.text, "单证人员") &&
        !includesText(row.text, "财务人员"),
      timeoutMs,
      `Timed out waiting for updated user row: ${username}`,
    );
    const updatedApiUser = await waitForApiUserManagementUser(
      options,
      accessToken,
      tokenType,
      username,
      (user) =>
        user &&
        user.fullName === updatedFullName &&
        user.role === "User" &&
        user.departmentId === departmentId &&
        user.companyScope === companyScope &&
        user.isActive === true &&
        !hasLeakedPasswordHash(user),
      timeoutMs,
      `Timed out waiting for API-updated user: ${username}`,
    );

    const updatedForm = await waitForPageExpression(
      page,
      `(() => {
        const section = document.querySelector('[aria-label="用户与权限"]');
        if (!section) {
          return false;
        }
        const readField = (labelText) => {
          const labels = Array.from(section.querySelectorAll('label'));
          const label = labels.find((item) => Array.from(item.querySelectorAll('span')).some((span) => (span.textContent || '').trim() === labelText));
          const control = label && label.querySelector('input, select, textarea');
          return control ? control.value || '' : '';
        };
        return readField('账号') === ${JSON.stringify(username)} &&
          readField('姓名') === ${JSON.stringify(updatedFullName)} &&
          readField('角色') === 'User' &&
          readField('部门') === ${JSON.stringify(departmentId)} &&
          readField('公司范围') === ${JSON.stringify(companyScope)};
      })()`,
      timeoutMs,
      `Timed out waiting for updated user form fields: ${username}`,
    );

    const deleteAction = await runUserManagementUiAction(page, "delete", { username });
    await waitForUserManagementRow(
      page,
      username,
      (row) => !row.exists,
      timeoutMs,
      `Timed out waiting for deleted user row to disappear: ${username}`,
    );
    const deletedApiUser = await waitForApiUserManagementUser(
      options,
      accessToken,
      tokenType,
      username,
      (user) => !user,
      timeoutMs,
      `Timed out waiting for API-deleted user to disappear: ${username}`,
    );

    return {
      username,
      createdRow: createdRow.text,
      updatedRow: updatedRow.text,
      created: createAction,
      createdApiUser,
      updated: updateAction,
      updatedApiUser,
      updatedForm,
      deleted: deleteAction,
      deletedApiUser,
      apiDtoPasswordHashHidden: Boolean(createdApiUser?.passwordHashHidden && updatedApiUser?.passwordHashHidden),
    };
  }

  async function waitForApiUserManagementUser(options, accessToken, tokenType, username, predicate, timeoutMs, description) {
    let latestUsers = [];
    return waitFor(async () => {
      const response = await fetch(new URL("/api/users", ensureTrailingSlash(options.apiBaseUrl)), {
        headers: authorizedHeaders(options, accessToken, tokenType),
      });
      if (!response.ok) {
        throw new Error(`GET /api/users failed with HTTP ${response.status}: ${await response.text()}`);
      }

      const payload = await response.json();
      latestUsers = Array.isArray(payload?.users) ? payload.users : [];
      const user = latestUsers.find((item) => item?.username === username) ?? null;
      if (!predicate(user)) {
        return null;
      }

      return user
        ? {
            exists: true,
            id: user.id,
            username: user.username,
            fullName: user.fullName,
            role: user.role,
            departmentId: user.departmentId,
            companyScope: user.companyScope,
            isActive: user.isActive,
            passwordHashHidden: !hasLeakedPasswordHash(user),
          }
        : {
            exists: false,
            username,
            passwordHashHidden: true,
          };
    }, timeoutMs, () => `${description}. Latest users: ${JSON.stringify(latestUsers.map((user) => ({
      username: user?.username,
      fullName: user?.fullName,
      role: user?.role,
      departmentId: user?.departmentId,
      companyScope: user?.companyScope,
      isActive: user?.isActive,
      hasPasswordHash: hasLeakedPasswordHash(user),
    })))}`);
  }

  function hasLeakedPasswordHash(user) {
    if (!user || typeof user !== "object") {
      return false;
    }

    return Object.keys(user).some((key) => key.toLowerCase() === "passwordhash");
  }

  async function runUserManagementUiAction(page, action, payload) {
    const result = await evaluate(
      page,
      `(async (payload) => {
        const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
        const section = document.querySelector('[aria-label="用户与权限"]');
        if (!section) {
          throw new Error("用户与权限区域未找到。");
        }

        const notifyReactChange = (control) => {
          const reactPropsKey = Object.keys(control).find((key) => key.startsWith("__reactProps$"));
          const reactProps = reactPropsKey ? control[reactPropsKey] : null;
          if (reactProps && typeof reactProps.onChange === "function") {
            reactProps.onChange({ target: control, currentTarget: control });
          }
        };

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
          notifyReactChange(control);
        };

        const setNativeChecked = (control, checked) => {
          const prototype = Object.getPrototypeOf(control);
          const descriptor = Object.getOwnPropertyDescriptor(prototype, "checked");
          if (descriptor && typeof descriptor.set === "function") {
            descriptor.set.call(control, checked);
          } else {
            control.checked = checked;
          }
          control.dispatchEvent(new Event("input", { bubbles: true }));
          control.dispatchEvent(new Event("change", { bubbles: true }));
          notifyReactChange(control);
        };

        const fieldByLabel = (labelText) => {
          const labels = Array.from(section.querySelectorAll("label"));
          const label = labels.find((item) =>
            Array.from(item.querySelectorAll("span")).some((span) => (span.textContent || "").trim() === labelText));
          if (!label) {
            throw new Error("字段未找到: " + labelText);
          }
          const control = label.querySelector("input, select, textarea");
          if (!control) {
            throw new Error("字段没有可编辑控件: " + labelText);
          }
          return control;
        };

        const setField = (labelText, value) => {
          setNativeValue(fieldByLabel(labelText), value);
        };

        const setCheckbox = (labelText, checked) => {
          const labels = Array.from(section.querySelectorAll("label"));
          const label = labels.find((item) =>
            Array.from(item.querySelectorAll("span")).some((span) => (span.textContent || "").trim() === labelText));
          if (!label) {
            throw new Error("复选框未找到: " + labelText);
          }
          const control = label.querySelector("input[type='checkbox']");
          if (!control) {
            throw new Error("复选框没有 input: " + labelText);
          }
          setNativeChecked(control, checked);
        };

        const readFieldValue = (labelText) => {
          const control = fieldByLabel(labelText);
          return control.type === "checkbox" ? Boolean(control.checked) : control.value || "";
        };

        const findButton = (predicate, description) => {
          const button = Array.from(section.querySelectorAll("button")).find(predicate);
          if (!button) {
            throw new Error("按钮未找到: " + description);
          }
          if (button.disabled) {
            throw new Error("按钮不可用: " + description);
          }
          return button;
        };

        const waitForButton = async (predicate, description) => {
          const deadline = Date.now() + 8000;
          let latestReason = "";
          while (Date.now() < deadline) {
            const button = Array.from(section.querySelectorAll("button")).find(predicate);
            if (button && !button.disabled) {
              return button;
            }

            latestReason = button ? "按钮仍不可用: " + description : "按钮未找到: " + description;
            await delay(100);
          }

          throw new Error(latestReason || "等待按钮超时: " + description);
        };

        const waitForIdle = async () => {
          await waitForButton((button) => (button.title || "").includes("新建用户"), "用户管理空闲");
        };

        const clickButtonByTitle = async (titleText) => {
          const button = await waitForButton((item) => (item.title || "").includes(titleText), titleText);
          button.click();
        };

        const clickSave = async () => {
          const button = await waitForButton((item) => (item.textContent || "").includes("保存"), "保存");
          button.click();
        };

        const readDeleteButtonDisabled = () => {
          const button = Array.from(section.querySelectorAll("button")).find((item) => (item.title || "").includes("删除用户"));
          return button ? Boolean(button.disabled) : null;
        };

        const waitForFormState = async (predicate, description) => {
          const deadline = Date.now() + 8000;
          let latest = {};
          while (Date.now() < deadline) {
            latest = {
              username: readFieldValue("账号"),
              fullName: readFieldValue("姓名"),
              role: readFieldValue("角色"),
              departmentId: readFieldValue("部门"),
              companyScope: readFieldValue("公司范围"),
              resetPasswordLength: String(readFieldValue("初始/重置密码") || "").length,
              deleteDisabled: readDeleteButtonDisabled()
            };

            if (predicate(latest)) {
              return latest;
            }

            await delay(100);
          }

          throw new Error("等待表单状态超时: " + description + " latest=" + JSON.stringify(latest));
        };

        const findRow = (username) => {
          return Array.from(section.querySelectorAll(".user-management-table tbody tr"))
            .find((row) => (row.cells && row.cells[0] ? row.cells[0].innerText.trim() : "") === username);
        };

        if (payload.action === "create") {
          await waitForIdle();
          await clickButtonByTitle("新建用户");
          await waitForFormState((state) => state.username === "" && state.deleteDisabled === true, "新建用户草稿");
          setField("账号", payload.username);
          setField("姓名", payload.fullName);
          setField("角色", payload.role);
          setField("部门", payload.departmentId);
          setField("公司范围", payload.companyScope);
          setField("初始/重置密码", payload.password);
          setCheckbox("启用账号", true);
          await waitForFormState(
            (state) => state.username === payload.username &&
              state.fullName === payload.fullName &&
              state.role === payload.role &&
              state.resetPasswordLength > 0,
            "新增用户表单值");
          await clickSave();
          return { action: payload.action, submitted: true, username: payload.username };
        }

        if (payload.action === "update") {
          await waitForIdle();
          const row = findRow(payload.username);
          if (!row) {
            throw new Error("要更新的用户行未找到: " + payload.username);
          }
          row.click();
          await waitForFormState((state) => state.username === payload.username && state.deleteDisabled === false, "选中待更新用户");
          setField("姓名", payload.fullName);
          setField("角色", payload.role);
          setField("部门", payload.departmentId);
          setField("公司范围", payload.companyScope);
          setField("初始/重置密码", "");
          await waitForFormState(
            (state) => state.username === payload.username &&
              state.fullName === payload.fullName &&
              state.role === payload.role &&
              state.departmentId === payload.departmentId &&
              state.companyScope === payload.companyScope,
            "更新用户表单值");
          await clickSave();
          return { action: payload.action, submitted: true, username: payload.username };
        }

        if (payload.action === "delete") {
          await waitForIdle();
          const row = findRow(payload.username);
          if (!row) {
            throw new Error("要删除的用户行未找到: " + payload.username);
          }
          row.click();
          await waitForFormState((state) => state.username === payload.username && state.deleteDisabled === false, "选中待删除用户");
          await clickButtonByTitle("删除用户");
          const confirmButton = await waitForButton(
            (item) => item.classList.contains("confirmation-dialog-confirm") &&
              (item.textContent || "").includes("删除账号"),
            "确认删除账号",
          );
          confirmButton.click();
          return { action: payload.action, submitted: true, username: payload.username };
        }

        throw new Error("未知用户管理动作: " + payload.action);
      })(${JSON.stringify({ ...payload, action })})`,
      true,
    );

    return result.value ?? { action, submitted: true, username: payload.username };
  }

  async function waitForUserManagementRow(page, username, predicate, timeoutMs, description) {
    let latestRows = [];
    let latestDiagnostics = {};

    return waitFor(async () => {
      const result = await evaluate(
        page,
        `(() => {
          const section = document.querySelector('[aria-label="用户与权限"]');
          const rows = Array.from(section?.querySelectorAll('.user-management-table tbody tr') || []).map((row) => ({
            text: row.innerText || "",
            username: row.cells && row.cells[0] ? row.cells[0].innerText.trim() : ""
          }));
          const readField = (labelText) => {
            const labels = Array.from(section?.querySelectorAll('label') || []);
            const label = labels.find((item) =>
              Array.from(item.querySelectorAll('span')).some((span) => (span.textContent || '').trim() === labelText));
            const control = label && label.querySelector('input, select, textarea');
            if (!control) {
              return null;
            }

            return control.type === 'checkbox' ? Boolean(control.checked) : control.value || '';
          };
          return {
            rows,
            alerts: Array.from(section?.querySelectorAll('.alert, .success-alert') || []).map((item) => item.innerText || ''),
            fields: {
              username: readField('账号'),
              fullName: readField('姓名'),
              role: readField('角色'),
              departmentId: readField('部门'),
              companyScope: readField('公司范围'),
              resetPasswordLength: String(readField('初始/重置密码') || '').length,
              isActive: readField('启用账号')
            },
            buttons: Array.from(section?.querySelectorAll('button') || []).map((button) => ({
              title: button.title || '',
              text: button.innerText || button.textContent || '',
              disabled: Boolean(button.disabled)
            }))
          };
        })()`,
        true,
      ).catch(() => ({ value: [] }));

      latestDiagnostics = result.value && !Array.isArray(result.value) ? result.value : {};
      latestRows = Array.isArray(latestDiagnostics.rows) ? latestDiagnostics.rows : [];
      const row = latestRows.find((item) => item.username === username);
      const snapshot = row
        ? { exists: true, text: row.text, rows: latestRows.map((item) => item.text) }
        : { exists: false, text: "", rows: latestRows.map((item) => item.text) };
      return predicate(snapshot) ? snapshot : null;
    }, timeoutMs, () => [
      description,
      `Rows: ${JSON.stringify(latestRows.map((item) => item.text))}`,
      `Diagnostics: ${JSON.stringify(latestDiagnostics)}`,
    ].join("\n"));
  }

  async function waitForUserRows(page, expectedUserRows, timeoutMs) {
    if (expectedUserRows.length === 0) {
      return [];
    }

    return waitFor(async () => {
      const result = await evaluate(
        page,
        'Array.from(document.querySelectorAll(".user-management-table tbody tr")).map((row) => row.innerText)',
        true,
      ).catch(() => ({ value: [] }));
      const rows = Array.isArray(result.value) ? result.value : [];
      return expectedUserRows.every((expected) =>
        rows.some((row) => includesText(row, expected.username) && includesText(row, expected.role)),
      )
        ? rows
        : null;
    }, timeoutMs, `Timed out waiting for user management rows: ${expectedUserRows
      .map((row) => `${row.username}/${row.role}`)
      .join(", ")}`);
  }

  return { run };
}
