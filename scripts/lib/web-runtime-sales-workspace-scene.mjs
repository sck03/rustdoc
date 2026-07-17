export function createSalesWorkspaceSmokeScene({ evaluate, includesText, waitFor }) {
  return { run };

  async function run(page, options, timeoutMs) {
    if (!options.salesWorkspaceCheck) return null;

    await navigate(page, buildHashUrl(options.webUrl, "/crm/dashboard"));
    await waitForText(page, ["销售概览", "从一位客户开始", "建立客户资料"], timeoutMs);
    const firstRun = await read(page, `(() => ({
      visible: Boolean(document.querySelector('.sales-first-run')),
      stepCount: document.querySelectorAll('.sales-first-run-steps button').length,
      metricCount: document.querySelectorAll('button.dashboard-metric').length
    }))()`);
    if (!firstRun.visible || firstRun.stepCount !== 3 || firstRun.metricCount !== 6) {
      throw new Error(`Sales first-run layout mismatch: ${JSON.stringify(firstRun)}`);
    }

    await clickButtonByText(page, "建立客户资料");
    await waitForLocation(page, "/crm/follow-ups", "view=profile", timeoutMs);
    await waitForText(page, ["客户与联系人"], timeoutMs);
    const profileDeepLink = await readSelectedTask(page);
    if (profileDeepLink !== "客户与联系人") {
      throw new Error(`Expected customer profile task to be selected, found: ${profileDeepLink}`);
    }

    await page.send("Emulation.setDeviceMetricsOverride", {
      width: 390,
      height: 844,
      deviceScaleFactor: 1,
      mobile: false,
    });
    await navigate(page, buildHashUrl(options.webUrl, "/crm/follow-ups?view=directory"));
    await waitForText(page, ["客户目录", "暂无销售客户"], timeoutMs);
    const narrowDirectory = await read(page, `(() => {
      const table = document.querySelector('.responsive-data-table');
      const secondary = table ? Array.from(table.querySelectorAll('[data-table-priority="secondary"]')) : [];
      const frame = table && table.closest('.table-frame');
      return {
        innerWidth: window.innerWidth,
        documentWidth: document.documentElement.scrollWidth,
        hiddenSecondaryCount: secondary.filter((item) => getComputedStyle(item).display === 'none').length,
        secondaryCount: secondary.length,
        tableScrollContained: Boolean(frame && frame.scrollWidth >= frame.clientWidth),
        selectedTask: document.querySelector('[role="tab"][aria-selected="true"]')?.textContent?.trim() || ''
      };
    })()`);
    if (narrowDirectory.innerWidth !== 390 || narrowDirectory.documentWidth > 392) {
      throw new Error(`Sales narrow viewport overflow: ${JSON.stringify(narrowDirectory)}`);
    }
    if (!narrowDirectory.secondaryCount || narrowDirectory.hiddenSecondaryCount !== narrowDirectory.secondaryCount) {
      throw new Error(`Sales secondary columns were not hidden at 390px: ${JSON.stringify(narrowDirectory)}`);
    }
    if (!narrowDirectory.tableScrollContained || narrowDirectory.selectedTask !== "客户目录") {
      throw new Error(`Sales narrow directory state mismatch: ${JSON.stringify(narrowDirectory)}`);
    }

    await navigate(page, buildHashUrl(options.webUrl, "/crm/follow-ups"));
    await waitForText(page, ["跟进记录", "记录新跟进", "先建立客户，再开始跟进"], timeoutMs);
    await clickButtonByText(page, "记录新跟进");
    await waitForLocation(page, "/crm/follow-ups", "view=followup-editor", timeoutMs);
    await waitForText(page, ["新增跟进", "先建立一位销售客户", "建立客户资料"], timeoutMs);
    const followUpEditor = await read(page, `(() => ({
      selectedTask: document.querySelector('[role="tab"][aria-selected="true"]')?.textContent?.trim() || '',
      documentWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth,
      guidanceVisible: Boolean(document.querySelector('.empty-guidance'))
    }))()`);
    if (followUpEditor.selectedTask !== "新增跟进" || !followUpEditor.guidanceVisible || followUpEditor.documentWidth > followUpEditor.innerWidth + 2) {
      throw new Error(`Sales follow-up editor task mismatch: ${JSON.stringify(followUpEditor)}`);
    }

    await navigate(page, buildHashUrl(options.webUrl, "/suppliers"));
    await waitForText(page, ["采购概览", "尚未建立供应商", "不会生成演示数据或虚构评分"], timeoutMs);
    const supplierOverview = await read(page, `(() => ({
      selectedTask: document.querySelector('[role="tab"][aria-selected="true"]')?.textContent?.trim() || '',
      apiErrorVisible: document.body.innerText.includes('API request failed'),
      documentWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth
    }))()`);
    if (supplierOverview.selectedTask !== "采购概览" || supplierOverview.apiErrorVisible || supplierOverview.documentWidth > supplierOverview.innerWidth + 2) {
      throw new Error(`Supplier overview task mismatch: ${JSON.stringify(supplierOverview)}`);
    }
    await clickButtonByText(page, "供应商目录");
    await waitForText(page, ["供应商目录", "暂无供应商", "建立第一家供应商"], timeoutMs);
    const supplierDirectory = await read(page, `(() => ({
      selectedTask: document.querySelector('[role="tab"][aria-selected="true"]')?.textContent?.trim() || '',
      disabledTasks: Array.from(document.querySelectorAll('[role="tab"]:disabled')).map((item) => item.textContent?.trim()),
      documentWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth
    }))()`);
    if (supplierDirectory.selectedTask !== "供应商目录" || supplierDirectory.documentWidth > supplierDirectory.innerWidth + 2 || supplierDirectory.disabledTasks.length !== 3) {
      throw new Error(`Supplier directory task mismatch: ${JSON.stringify(supplierDirectory)}`);
    }
    await clickButtonByText(page, "建立第一家供应商");
    await waitForText(page, ["新建供应商", "保存供应商"], timeoutMs);
    const supplierEditor = await read(page, `(() => ({
      selectedTask: document.querySelector('[role="tab"][aria-selected="true"]')?.textContent?.trim() || '',
      formCount: document.querySelectorAll('.form-grid').length,
      formColumns: getComputedStyle(document.querySelector('.form-grid')).gridTemplateColumns,
      documentWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth
    }))()`);
    if (supplierEditor.selectedTask !== "新建供应商" || supplierEditor.formCount !== 1 || supplierEditor.documentWidth > supplierEditor.innerWidth + 2) {
      throw new Error(`Supplier editor task mismatch: ${JSON.stringify(supplierEditor)}`);
    }
    if (supplierEditor.formColumns.trim().split(/\s+/).length !== 1) {
      throw new Error(`Supplier editor did not collapse to one column: ${JSON.stringify(supplierEditor)}`);
    }

    await navigate(page, buildHashUrl(options.webUrl, "/crm/email-templates"));
    await waitForText(page, ["模板目录", "暂无邮件模板", "建立第一个模板"], timeoutMs);
    await clickButtonByText(page, "建立第一个模板");
    await waitForText(page, ["新建模板", "保存模板", "设置变量"], timeoutMs);
    await clickButtonByText(page, "设置变量");
    await waitForText(page, ["变量设置", "生成预览", "不会自动发送邮件"], timeoutMs);
    const emailVariables = await read(page, `(() => ({
      selectedTask: document.querySelector('[role="tab"][aria-selected="true"]')?.textContent?.trim() || '',
      variableGridCount: document.querySelectorAll('.variable-setting-grid').length,
      crmToolbarVisible: Boolean(document.querySelector('input[placeholder="搜索 CRM 客户"]')),
      documentWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth
    }))()`);
    if (emailVariables.selectedTask !== "变量设置" || emailVariables.variableGridCount !== 1 || emailVariables.crmToolbarVisible || emailVariables.documentWidth > emailVariables.innerWidth + 2) {
      throw new Error(`Email variable task mismatch: ${JSON.stringify(emailVariables)}`);
    }
    await clickButtonByText(page, "生成预览");
    await waitForText(page, ["预览与套用", "套用到单封邮件", "查找客户"], timeoutMs);
    const emailPreview = await read(page, `(() => ({
      selectedTask: document.querySelector('[role="tab"][aria-selected="true"]')?.textContent?.trim() || '',
      variableGridVisible: Boolean(document.querySelector('.variable-setting-grid')),
      crmToolbarVisible: Boolean(document.querySelector('input[placeholder="搜索 CRM 客户"]')),
      documentWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth
    }))()`);
    if (emailPreview.selectedTask !== "预览与套用" || emailPreview.variableGridVisible || !emailPreview.crmToolbarVisible || emailPreview.documentWidth > emailPreview.innerWidth + 2) {
      throw new Error(`Email preview task mismatch: ${JSON.stringify(emailPreview)}`);
    }
    await clickButtonByText(page, "载入客户变量");
    await waitForText(page, ["请选择 CRM 客户"], timeoutMs);
    const operationFeedback = await read(page, `(() => {
      const feedback = document.querySelector('.operation-feedback');
      return {
        tone: feedback?.getAttribute('data-tone') || '',
        role: feedback?.getAttribute('role') || '',
        text: feedback?.textContent?.trim() || ''
      };
    })()`);
    if (operationFeedback.tone !== "warning" || operationFeedback.role !== "status") {
      throw new Error(`Sales operation feedback semantics mismatch: ${JSON.stringify(operationFeedback)}`);
    }

    await navigate(page, buildHashUrl(options.webUrl, "/crm/opportunities?view=editor"));
    await waitForText(page, ["商机与报价跟踪", "新建商机", "保存商机"], timeoutMs);
    const opportunityEditor = await read(page, `(() => ({
      selectedTask: document.querySelector('[role="tab"][aria-selected="true"]')?.textContent?.trim() || '',
      formColumns: getComputedStyle(document.querySelector('.form-grid')).gridTemplateColumns,
      sectionCount: document.querySelectorAll('.form-section-block').length,
      optionalDetailsOpen: Boolean(document.querySelector('.optional-form-details')?.open),
      customerGuidanceVisible: Boolean(document.querySelector('.empty-guidance')),
      documentWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth
    }))()`);
    if (opportunityEditor.selectedTask !== "新建商机" || opportunityEditor.sectionCount !== 3 || opportunityEditor.optionalDetailsOpen || !opportunityEditor.customerGuidanceVisible || opportunityEditor.documentWidth > opportunityEditor.innerWidth + 2) {
      throw new Error(`Sales opportunity mobile editor mismatch: ${JSON.stringify(opportunityEditor)}`);
    }
    if (opportunityEditor.formColumns.trim().split(/\s+/).length !== 1) {
      throw new Error(`Sales opportunity form did not collapse to one column: ${JSON.stringify(opportunityEditor)}`);
    }

    await page.send("Emulation.setDeviceMetricsOverride", {
      width: 1440,
      height: 1000,
      deviceScaleFactor: 1,
      mobile: false,
    });
    await navigate(page, buildHashUrl(options.webUrl, "/crm/dashboard"));
    await waitForText(page, ["销售概览", "销售客户"], timeoutMs);
    await clickButtonByText(page, "销售客户", "button.dashboard-metric");
    await waitForLocation(page, "/crm/follow-ups", "view=directory", timeoutMs);

    const longCustomerName = "宁波远洋国际贸易与全球供应链协同服务有限公司 NORTHSTAR INTERNATIONAL SOURCING AND SUPPLY CHAIN";
    await navigate(page, buildHashUrl(options.webUrl, "/crm/follow-ups?view=profile"));
    await waitForText(page, ["客户与联系人", "客户资料"], timeoutMs);
    const startedCustomer = await read(page, `(() => {
      const button = document.querySelector('.two-column-layout form:first-child .section-heading-row button');
      if (!button) return false;
      button.click();
      return true;
    })()`);
    if (!startedCustomer) throw new Error("Sales long-data customer editor could not be opened.");
    await waitForText(page, ["新建销售客户"], timeoutMs);
    const submittedCustomer = await read(page, `(() => {
      const form = document.querySelector('.two-column-layout form:first-child');
      if (!form) return false;
      const values = ${JSON.stringify({ name: longCustomerName, countryRegion: "中国 / 浙江 / 宁波", source: "国际展会与长期转介绍渠道" })};
      for (const [name, value] of Object.entries(values)) {
        const field = form.elements.namedItem(name);
        if (field) field.value = value;
      }
      form.requestSubmit();
      return true;
    })()`);
    if (!submittedCustomer) throw new Error("Sales long-data customer form could not be submitted.");
    await waitForText(page, ["CRM 客户已建立", longCustomerName], timeoutMs);

    await page.send("Emulation.setDeviceMetricsOverride", {
      width: 390,
      height: 844,
      deviceScaleFactor: 1,
      mobile: false,
    });
    await navigate(page, buildHashUrl(options.webUrl, "/crm/follow-ups?view=directory"));
    await waitFor(async () => await readSelectedTask(page) === "客户目录" ? true : null,
      timeoutMs, () => "Timed out waiting for the customer directory task after creating long data.");
    await waitForText(page, ["客户目录", longCustomerName], timeoutMs);
    const longDataDirectory = await read(page, `(() => {
      const text = Array.from(document.querySelectorAll('.table-primary-text'))
        .find((item) => item.getAttribute('title') === ${JSON.stringify(longCustomerName)});
      return {
        found: Boolean(text),
        title: text?.getAttribute('title') || '',
        clientWidth: text?.clientWidth || 0,
        scrollWidth: text?.scrollWidth || 0,
        documentWidth: document.documentElement.scrollWidth,
        innerWidth: window.innerWidth
      };
    })()`);
    if (!longDataDirectory.found || longDataDirectory.title !== longCustomerName || longDataDirectory.scrollWidth <= longDataDirectory.clientWidth || longDataDirectory.documentWidth > longDataDirectory.innerWidth + 2) {
      throw new Error(`Sales long-data directory truncation mismatch: ${JSON.stringify(longDataDirectory)}`);
    }

    const savedOpportunityTitle = "北欧客户 2027 春季环保面料系列长期采购商机";
    await navigate(page, buildHashUrl(options.webUrl, "/crm/opportunities"));
    await waitFor(async () => await readSelectedTask(page) === "商机目录" ? true : null,
      timeoutMs, () => "Timed out waiting for the opportunity directory task.");
    const appliedStageFilter = await read(page, `(() => {
      const select = document.querySelector('.form-section .toolbar select');
      if (!select) return false;
      select.value = '已成交';
      select.dispatchEvent(new Event('change', { bubbles: true }));
      return true;
    })()`);
    if (!appliedStageFilter) throw new Error("Sales opportunity stage filter could not be applied.");
    await clickButtonByText(page, "新建商机");
    await waitFor(async () => await readSelectedTask(page) === "新建商机" ? true : null,
      timeoutMs, () => "Timed out waiting for the filtered opportunity editor.");
    await waitFor(async () => await read(page, "document.querySelector('select[name=\"crmCustomerId\"]')?.options.length || 0") >= 2 ? true : null,
      timeoutMs, () => "Timed out waiting for CRM customer options in the opportunity editor.");
    const submittedOpportunity = await read(page, `(() => {
      const form = document.querySelector('form.form-grid');
      if (!form) return false;
      const customer = form.elements.namedItem('crmCustomerId');
      const title = form.elements.namedItem('title');
      const stage = form.elements.namedItem('stage');
      if (!customer || customer.options.length < 2 || !title || !stage) return false;
      customer.value = customer.options[1].value;
      title.value = ${JSON.stringify(savedOpportunityTitle)};
      stage.value = '线索';
      form.requestSubmit();
      return true;
    })()`);
    if (!submittedOpportunity) throw new Error("Sales filtered opportunity form could not be submitted.");
    await waitForText(page, ["商机已建立并生成版本 1", "编辑商机"], timeoutMs);
    await waitFor(async () => await read(page, "document.querySelector('input[name=\"title\"]')?.value || ''") === savedOpportunityTitle ? true : null,
      timeoutMs, () => "Timed out waiting for the saved opportunity to remain selected in the editor.");
    await navigate(page, buildHashUrl(options.webUrl, "/crm/opportunities"));
    await waitFor(async () => await readSelectedTask(page) === "商机目录" ? true : null,
      timeoutMs, () => "Timed out waiting for the opportunity directory after save.");
    await waitForText(page, [savedOpportunityTitle], timeoutMs);
    const saveLocation = await read(page, `(() => {
      const toolbar = document.querySelector('.form-section .toolbar');
      const stage = toolbar?.querySelector('select');
      const keyword = toolbar?.querySelector('input');
      const title = Array.from(document.querySelectorAll('.table-primary-text'))
        .find((item) => item.getAttribute('title') === ${JSON.stringify(savedOpportunityTitle)});
      return {
        stageFilter: stage?.value || '',
        keywordFilter: keyword?.value || '',
        savedRecordVisible: Boolean(title),
        documentWidth: document.documentElement.scrollWidth,
        innerWidth: window.innerWidth
      };
    })()`);
    if (saveLocation.stageFilter || saveLocation.keywordFilter || !saveLocation.savedRecordVisible || saveLocation.documentWidth > saveLocation.innerWidth + 2) {
      throw new Error(`Sales opportunity save location mismatch: ${JSON.stringify(saveLocation)}`);
    }

    await navigate(page, buildHashUrl(options.webUrl, "/suppliers"));
    await waitFor(async () => await readSelectedTask(page) === "采购概览" ? true : null,
      timeoutMs, () => "Timed out waiting for the supplier overview before product task verification.");
    await clickButtonByText(page, "供应商目录", "[role=\"tab\"]");
    await waitFor(async () => await readSelectedTask(page) === "供应商目录" ? true : null,
      timeoutMs, () => "Timed out waiting for the supplier directory before product task verification.");
    await clickButtonByText(page, "新建供应商", ".form-section button");
    await waitFor(async () => await readSelectedTask(page) === "新建供应商" ? true : null,
      timeoutMs, () => "Timed out waiting for the supplier editor before product task verification.");
    const submittedSupplier = await read(page, `(() => {
      const form = document.querySelector('form.form-grid');
      const name = form?.elements.namedItem('name');
      if (!form || !name) return false;
      name.value = '宁波轻工产品合作供应商';
      form.requestSubmit();
      return true;
    })()`);
    if (!submittedSupplier) throw new Error("Sales supplier form could not be submitted for product task verification.");
    await waitForText(page, ["供应商已建立", "供应商资料"], timeoutMs);
    await clickButtonByText(page, "供应产品", "[role=\"tab\"]");
    await waitFor(async () => await readSelectedTask(page) === "供应产品" ? true : null,
      timeoutMs, () => "Timed out waiting for the supplier product task.");
    await waitForText(page, ["供应产品目录", "尚未关联供应产品", "新增第一条供货关系"], timeoutMs);
    const productDirectoryTask = await read(page, `(() => ({
      formCount: document.querySelectorAll('.supplier-product-workspace > form.form-grid').length,
      tableCount: document.querySelectorAll('.supplier-product-workspace > .table-frame').length,
      documentWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth
    }))()`);
    if (productDirectoryTask.formCount !== 0 || productDirectoryTask.tableCount !== 1 || productDirectoryTask.documentWidth > productDirectoryTask.innerWidth + 2) {
      throw new Error(`Supplier product directory task mismatch: ${JSON.stringify(productDirectoryTask)}`);
    }
    await clickButtonByText(page, "新增第一条供货关系");
    await waitForText(page, ["新增供货关系", "返回供应产品目录", "先建立产品资料"], timeoutMs);
    const productEditorTask = await read(page, `(() => ({
      formCount: document.querySelectorAll('.supplier-product-workspace > form.form-grid').length,
      tableCount: document.querySelectorAll('.supplier-product-workspace > .table-frame').length,
      guidanceVisible: Boolean(document.querySelector('.supplier-product-workspace .empty-guidance')),
      documentWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth
    }))()`);
    if (productEditorTask.formCount !== 1 || productEditorTask.tableCount !== 0 || !productEditorTask.guidanceVisible || productEditorTask.documentWidth > productEditorTask.innerWidth + 2) {
      throw new Error(`Supplier product editor task mismatch: ${JSON.stringify(productEditorTask)}`);
    }
    await clickButtonByText(page, "供应商联系人", "[role=\"tab\"]");
    await waitFor(async () => await readSelectedTask(page) === "供应商联系人" ? true : null,
      timeoutMs, () => "Timed out waiting for the supplier contact task.");
    await waitForText(page, ["供应商联系人目录", "尚未建立供应商联系人", "添加第一位联系人"], timeoutMs);
    const contactDirectoryTask = await read(page, `(() => ({
      formCount: document.querySelectorAll('.supplier-contact-workspace > form.form-grid').length,
      tableCount: document.querySelectorAll('.supplier-contact-workspace > .table-frame').length,
      documentWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth
    }))()`);
    if (contactDirectoryTask.formCount !== 0 || contactDirectoryTask.tableCount !== 1 || contactDirectoryTask.documentWidth > contactDirectoryTask.innerWidth + 2) {
      throw new Error(`Supplier contact directory task mismatch: ${JSON.stringify(contactDirectoryTask)}`);
    }
    await clickButtonByText(page, "添加第一位联系人");
    await waitForText(page, ["新增供应商联系人", "新增联系人资料", "返回联系人目录"], timeoutMs);
    const contactEditorTask = await read(page, `(() => ({
      formCount: document.querySelectorAll('.supplier-contact-workspace > form.form-grid').length,
      tableCount: document.querySelectorAll('.supplier-contact-workspace > .table-frame').length,
      documentWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth
    }))()`);
    if (contactEditorTask.formCount !== 1 || contactEditorTask.tableCount !== 0 || contactEditorTask.documentWidth > contactEditorTask.innerWidth + 2) {
      throw new Error(`Supplier contact editor task mismatch: ${JSON.stringify(contactEditorTask)}`);
    }

    const longFollowUpSummary = "客户确认环保面料样品方向并要求重新核算大货阶梯价格、包装方式以及欧洲仓分批交付计划";
    await navigate(page, buildHashUrl(options.webUrl, "/crm/follow-ups?view=followup-editor"));
    await waitFor(async () => await readSelectedTask(page) === "新增跟进" ? true : null,
      timeoutMs, () => "Timed out waiting for the follow-up editor with real customer data.");
    await waitFor(async () => await read(page, "Number(document.querySelector('form.form-grid select')?.value || 0)") > 0 ? true : null,
      timeoutMs, () => "Timed out waiting for the selected CRM customer in the follow-up editor.");
    const submittedFollowUp = await read(page, `(() => {
      const form = document.querySelector('form.form-grid');
      if (!form) return false;
      const summary = form.elements.namedItem('summary');
      const nextAction = form.elements.namedItem('nextAction');
      const nextFollowUpAt = form.elements.namedItem('nextFollowUpAt');
      if (!summary || !nextAction || !nextFollowUpAt) return false;
      summary.value = ${JSON.stringify(longFollowUpSummary)};
      nextAction.value = '三天内发送新版报价并确认首批交期';
      nextFollowUpAt.value = '2027-12-31T10:30';
      form.requestSubmit();
      return true;
    })()`);
    if (!submittedFollowUp) throw new Error("Sales follow-up form could not be submitted.");
    await waitFor(async () => {
      if (await readSelectedTask(page) === "跟进记录") return true;
      const errorText = await read(page, "document.querySelector('.operation-feedback[data-tone=\"error\"]')?.textContent?.trim() || ''");
      if (errorText) throw new Error(`Follow-up save failed: ${errorText}`);
      return null;
    }, timeoutMs, () => "Timed out waiting for the follow-up directory after save.");
    await waitForText(page, ["客户跟进已保存", longFollowUpSummary, "待跟进"], timeoutMs);
    const followUpDirectory = await read(page, `(() => {
      const summary = Array.from(document.querySelectorAll('.follow-up-data-table .table-primary-text'))
        .find((item) => item.getAttribute('title') === ${JSON.stringify(longFollowUpSummary)});
      const row = summary?.closest('tr');
      return {
        summaryFound: Boolean(summary),
        summaryTruncated: Boolean(summary && summary.scrollWidth > summary.clientWidth),
        status: row?.querySelector('.business-status-badge')?.textContent?.trim() || '',
        hiddenSecondaryCount: Array.from(document.querySelectorAll('.follow-up-data-table [data-table-priority="secondary"]'))
          .filter((item) => getComputedStyle(item).display === 'none').length,
        documentWidth: document.documentElement.scrollWidth,
        innerWidth: window.innerWidth
      };
    })()`);
    if (!followUpDirectory.summaryFound || !followUpDirectory.summaryTruncated || followUpDirectory.status !== "待跟进" || !followUpDirectory.hiddenSecondaryCount || followUpDirectory.documentWidth > followUpDirectory.innerWidth + 2) {
      throw new Error(`Sales follow-up directory density mismatch: ${JSON.stringify(followUpDirectory)}`);
    }
    const completedFollowUp = await read(page, `(() => {
      const summary = Array.from(document.querySelectorAll('.follow-up-data-table .table-primary-text'))
        .find((item) => item.getAttribute('title') === ${JSON.stringify(longFollowUpSummary)});
      const button = Array.from(summary?.closest('tr')?.querySelectorAll('button') || [])
        .find((item) => item.textContent?.trim() === '完成');
      if (!button) return false;
      button.click();
      return true;
    })()`);
    if (!completedFollowUp) throw new Error("Sales follow-up completion action could not be triggered.");
    await waitForText(page, ["跟进记录已标记完成"], timeoutMs);
    const showedCompleted = await read(page, `(() => {
      const checkbox = document.querySelector('.section-heading-row .checkbox-field input[type="checkbox"]');
      if (!checkbox) return false;
      checkbox.click();
      return true;
    })()`);
    if (!showedCompleted) throw new Error("Sales completed follow-up filter could not be enabled.");
    await waitForText(page, [longFollowUpSummary, "已完成"], timeoutMs);
    const followUpCompletion = await read(page, `(() => {
      const summary = Array.from(document.querySelectorAll('.follow-up-data-table .table-primary-text'))
        .find((item) => item.getAttribute('title') === ${JSON.stringify(longFollowUpSummary)});
      const row = summary?.closest('tr');
      return {
        status: row?.querySelector('.business-status-badge')?.textContent?.trim() || '',
        tone: row?.querySelector('.business-status-badge')?.getAttribute('data-tone') || '',
        feedbackTone: document.querySelector('.operation-feedback')?.getAttribute('data-tone') || ''
      };
    })()`);
    if (followUpCompletion.status !== "已完成" || followUpCompletion.tone !== "positive" || followUpCompletion.feedbackTone !== "success") {
      throw new Error(`Sales follow-up completion feedback mismatch: ${JSON.stringify(followUpCompletion)}`);
    }
    const restoredFollowUp = await read(page, `(() => {
      const summary = Array.from(document.querySelectorAll('.follow-up-data-table .table-primary-text'))
        .find((item) => item.getAttribute('title') === ${JSON.stringify(longFollowUpSummary)});
      const button = Array.from(summary?.closest('tr')?.querySelectorAll('button') || [])
        .find((item) => item.textContent?.trim() === '恢复');
      if (!button) return false;
      button.click();
      return true;
    })()`);
    if (!restoredFollowUp) throw new Error("Sales completed follow-up could not be restored.");
    await waitForText(page, [longFollowUpSummary, "待跟进", "跟进记录已恢复为待跟进"], timeoutMs);
    const cancelledFollowUpDelete = await read(page, `(() => {
      window.confirm = () => false;
      const summary = Array.from(document.querySelectorAll('.follow-up-data-table .table-primary-text'))
        .find((item) => item.getAttribute('title') === ${JSON.stringify(longFollowUpSummary)});
      const button = Array.from(summary?.closest('tr')?.querySelectorAll('button') || [])
        .find((item) => item.textContent?.trim() === '删除');
      if (!button) return false;
      button.click();
      return true;
    })()`);
    if (!cancelledFollowUpDelete) throw new Error("Sales follow-up delete confirmation could not be cancelled.");
    const followUpStillVisible = await read(page, `Array.from(document.querySelectorAll('.follow-up-data-table .table-primary-text')).some((item) => item.getAttribute('title') === ${JSON.stringify(longFollowUpSummary)})`);
    if (!followUpStillVisible) throw new Error("Sales follow-up was removed after cancelling delete confirmation.");
    await read(page, "window.confirm = () => true; true");
    const confirmedFollowUpDelete = await read(page, `(() => {
      const summary = Array.from(document.querySelectorAll('.follow-up-data-table .table-primary-text'))
        .find((item) => item.getAttribute('title') === ${JSON.stringify(longFollowUpSummary)});
      const button = Array.from(summary?.closest('tr')?.querySelectorAll('button') || [])
        .find((item) => item.textContent?.trim() === '删除');
      if (!button) return false;
      button.click();
      return true;
    })()`);
    if (!confirmedFollowUpDelete) throw new Error("Sales follow-up delete could not be confirmed.");
    await waitForText(page, ["跟进记录已删除"], timeoutMs);
    await waitFor(async () => await read(page, `!Array.from(document.querySelectorAll('.follow-up-data-table .table-primary-text')).some((item) => item.getAttribute('title') === ${JSON.stringify(longFollowUpSummary)})`) ? true : null,
      timeoutMs, () => "Timed out waiting for the deleted follow-up to disappear.");
    const followUpLifecycle = await read(page, `(() => ({
      deleted: !Array.from(document.querySelectorAll('.follow-up-data-table .table-primary-text')).some((item) => item.getAttribute('title') === ${JSON.stringify(longFollowUpSummary)}),
      feedbackTone: document.querySelector('.operation-feedback')?.getAttribute('data-tone') || '',
      feedbackText: document.querySelector('.operation-feedback')?.textContent?.trim() || '',
      documentWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth
    }))()`);
    if (!followUpLifecycle.deleted || followUpLifecycle.feedbackTone !== "success" || !followUpLifecycle.feedbackText.includes("跟进记录已删除") || followUpLifecycle.documentWidth > followUpLifecycle.innerWidth + 2) {
      throw new Error(`Sales follow-up lifecycle mismatch: ${JSON.stringify(followUpLifecycle)}`);
    }

    const inactiveTemplateName = "北欧客户环保面料报价跟进模板";
    await navigate(page, buildHashUrl(options.webUrl, "/crm/email-templates"));
    await waitFor(async () => await readSelectedTask(page) === "模板目录" ? true : null,
      timeoutMs, () => "Timed out waiting for the email template directory before filtered save verification.");
    const filteredTemplates = await read(page, `(() => {
      const form = document.querySelector('.form-section form.toolbar');
      const input = form?.querySelector('input[placeholder*="搜索模板"]');
      if (!form || !input) return false;
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, '不会匹配任何模板的关键字');
      input.dispatchEvent(new Event('input', { bubbles: true }));
      form.requestSubmit();
      return true;
    })()`);
    if (!filteredTemplates) throw new Error("Sales email template keyword filter could not be applied.");
    await waitForText(page, ["暂无邮件模板"], timeoutMs);
    await clickButtonByText(page, "新建模板", ".section-header-actions button");
    await waitFor(async () => await readSelectedTask(page) === "新建模板" ? true : null,
      timeoutMs, () => "Timed out waiting for the filtered email template editor.");
    const submittedTemplate = await read(page, `(() => {
      const form = document.querySelector('form.form-grid');
      if (!form) return false;
      const name = form.querySelector('input[required]');
      const subject = Array.from(form.querySelectorAll('input')).find((item) => item.closest('label')?.textContent?.includes('邮件主题'));
      const checkboxes = form.querySelectorAll('input[type="checkbox"]');
      const active = checkboxes[0];
      const shared = checkboxes[1];
      if (!name || !subject || !active || !shared) return false;
      const setValue = (input, value) => {
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, value);
        input.dispatchEvent(new Event('input', { bubbles: true }));
      };
      setValue(name, ${JSON.stringify(inactiveTemplateName)});
      setValue(subject, '环保面料系列报价及样品跟进');
      if (active.checked) active.click();
      if (!shared.checked) shared.click();
      form.requestSubmit();
      return true;
    })()`);
    if (!submittedTemplate) throw new Error("Sales inactive email template form could not be submitted.");
    await waitForText(page, ["邮件模板已建立", "编辑模板"], timeoutMs);
    await waitFor(async () => await read(page, "document.querySelector('form.form-grid input[required]')?.value || ''") === inactiveTemplateName ? true : null,
      timeoutMs, () => "Timed out waiting for the saved inactive template to remain selected.");
    await clickButtonByText(page, "模板目录", "[role=\"tab\"]");
    await waitFor(async () => await readSelectedTask(page) === "模板目录" ? true : null,
      timeoutMs, () => "Timed out waiting for the template directory after inactive save.");
    await waitForText(page, [inactiveTemplateName], timeoutMs);
    await waitFor(async () => await read(page, `Array.from(document.querySelectorAll('.business-status-badge')).some((item) => item.textContent?.trim() === '团队共享')`) ? true : null,
      timeoutMs, () => "Timed out waiting for the shared template status badge.");
    const templateSaveLocation = await read(page, `(() => {
      const toolbar = document.querySelector('.form-section form.toolbar');
      const keyword = toolbar?.querySelector('input[placeholder*="搜索模板"]');
      const includeInactive = toolbar?.querySelector('input[type="checkbox"]');
      const template = Array.from(document.querySelectorAll('.table-primary-text'))
        .find((item) => item.getAttribute('title') === ${JSON.stringify(inactiveTemplateName)});
      const row = template?.closest('tr');
      return {
        keyword: keyword?.value || '',
        includeInactive: Boolean(includeInactive?.checked),
        templateVisible: Boolean(template),
        statuses: Array.from(row?.querySelectorAll('.business-status-badge') || []).map((item) => item.textContent?.trim()),
        documentWidth: document.documentElement.scrollWidth,
        innerWidth: window.innerWidth
      };
    })()`);
    if (templateSaveLocation.keyword || !templateSaveLocation.includeInactive || !templateSaveLocation.templateVisible || !templateSaveLocation.statuses.includes("停用") || !templateSaveLocation.statuses.includes("团队共享") || templateSaveLocation.documentWidth > templateSaveLocation.innerWidth + 2) {
      throw new Error(`Sales inactive template save location mismatch: ${JSON.stringify(templateSaveLocation)}`);
    }
    const openedTemplate = await read(page, `(() => {
      const name = Array.from(document.querySelectorAll('.table-primary-text'))
        .find((item) => item.getAttribute('title') === ${JSON.stringify(inactiveTemplateName)});
      const button = name?.closest('tr')?.querySelector('button');
      if (!button) return false;
      button.click();
      return true;
    })()`);
    if (!openedTemplate) throw new Error("Sales inactive template could not be opened for copy verification.");
    await waitFor(async () => await readSelectedTask(page) === "编辑模板" ? true : null,
      timeoutMs, () => "Timed out waiting for the email template editor before copying.");
    await clickButtonByText(page, "复制为新模板");
    const copiedTemplateName = `${inactiveTemplateName} 副本`;
    await waitForText(page, ["已复制为新模板草稿"], timeoutMs);
    await waitFor(async () => await read(page, "document.querySelector('form.form-grid input[required]')?.value || ''") === copiedTemplateName ? true : null,
      timeoutMs, () => "Timed out waiting for the copied email template draft name.");
    await clickButtonByText(page, "保存模板", "form.form-grid button");
    await waitForText(page, ["邮件模板已建立", "编辑模板"], timeoutMs);
    await clickButtonByText(page, "模板目录", "[role=\"tab\"]");
    await waitFor(async () => await readSelectedTask(page) === "模板目录" ? true : null,
      timeoutMs, () => "Timed out waiting for the template directory after saving a copy.");
    await waitForText(page, [inactiveTemplateName, copiedTemplateName], timeoutMs);
    const templateCopy = await read(page, `(() => {
      const titles = Array.from(document.querySelectorAll('.table-primary-text[title]')).map((item) => item.getAttribute('title'));
      return {
        originalVisible: titles.includes(${JSON.stringify(inactiveTemplateName)}),
        copyVisible: titles.includes(${JSON.stringify(copiedTemplateName)}),
        templateRows: document.querySelectorAll('.form-section .data-table tbody tr').length
      };
    })()`);
    if (!templateCopy.originalVisible || !templateCopy.copyVisible || templateCopy.templateRows !== 2) {
      throw new Error(`Sales email template copy mismatch: ${JSON.stringify(templateCopy)}`);
    }
    const templateScopeFilter = await read(page, `(() => {
      const scope = document.querySelector('select[aria-label="模板范围"]');
      if (!scope) return false;
      Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value').set.call(scope, 'shared');
      scope.dispatchEvent(new Event('change', { bubbles: true }));
      return true;
    })()`);
    if (!templateScopeFilter) throw new Error("Sales email template scope filter could not be changed.");
    await waitFor(async () => await read(page, `(() => {
      const titles = Array.from(document.querySelectorAll('.table-primary-text[title]')).map((item) => item.getAttribute('title'));
      return titles.includes(${JSON.stringify(inactiveTemplateName)}) && !titles.includes(${JSON.stringify(copiedTemplateName)});
    })()`) ? true : null, timeoutMs, () => "Timed out waiting for the shared email template scope.");
    const templateScope = await read(page, `(() => ({
      scope: document.querySelector('select[aria-label="模板范围"]')?.value || '',
      templateRows: document.querySelectorAll('.form-section .data-table tbody tr').length,
      documentWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth
    }))()`);
    if (templateScope.scope !== "shared" || templateScope.templateRows !== 1 || templateScope.documentWidth > templateScope.innerWidth + 2) {
      throw new Error(`Sales email template scope mismatch: ${JSON.stringify(templateScope)}`);
    }
    await read(page, `(() => {
      const scope = document.querySelector('select[aria-label="模板范围"]');
      Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value').set.call(scope, 'all');
      scope.dispatchEvent(new Event('change', { bubbles: true }));
      return true;
    })()`);
    await waitForText(page, [inactiveTemplateName, copiedTemplateName], timeoutMs);
    const openedCopy = await read(page, `(() => {
      const name = Array.from(document.querySelectorAll('.table-primary-text'))
        .find((item) => item.getAttribute('title') === ${JSON.stringify(copiedTemplateName)});
      const button = name?.closest('tr')?.querySelector('button');
      if (!button) return false;
      button.click();
      return true;
    })()`);
    if (!openedCopy) throw new Error("Sales copied template could not be opened for delete verification.");
    await waitFor(async () => await readSelectedTask(page) === "编辑模板" ? true : null,
      timeoutMs, () => "Timed out waiting for the copied template editor before deletion.");
    await read(page, "window.confirm = () => true; true");
    await clickButtonByText(page, "删除", "form.form-grid button");
    await waitFor(async () => await readSelectedTask(page) === "模板目录" ? true : null,
      timeoutMs, () => "Timed out waiting for the template directory after deletion.");
    await waitForText(page, ["邮件模板已删除", inactiveTemplateName], timeoutMs);
    const templateDelete = await read(page, `(() => {
      const titles = Array.from(document.querySelectorAll('.table-primary-text[title]')).map((item) => item.getAttribute('title'));
      return {
        originalVisible: titles.includes(${JSON.stringify(inactiveTemplateName)}),
        copyVisible: titles.includes(${JSON.stringify(copiedTemplateName)}),
        templateRows: document.querySelectorAll('.form-section .data-table tbody tr').length,
        selectedTask: document.querySelector('[role="tab"][aria-selected="true"]')?.textContent?.trim() || ''
      };
    })()`);
    if (!templateDelete.originalVisible || templateDelete.copyVisible || templateDelete.templateRows !== 1 || templateDelete.selectedTask !== "模板目录") {
      throw new Error(`Sales email template delete mismatch: ${JSON.stringify(templateDelete)}`);
    }

    return {
      firstRun,
      profileDeepLink,
      narrowDirectory,
      followUpEditor,
      supplierDirectory,
      supplierEditor,
      emailVariables,
      emailPreview,
      operationFeedback,
      opportunityEditor,
      metricTarget: "/crm/follow-ups?view=directory",
      longDataDirectory,
      saveLocation,
      productDirectoryTask,
      productEditorTask,
      contactDirectoryTask,
      contactEditorTask,
      followUpDirectory,
      followUpCompletion,
      followUpLifecycle,
      templateSaveLocation,
      templateCopy,
      templateScope,
      templateDelete,
    };
  }

  async function waitForText(page, expected, timeoutMs) {
    let latest = "";
    return waitFor(async () => {
      latest = await read(page, "document.body ? document.body.innerText : ''").catch(() => "");
      return expected.every((value) => includesText(latest, value)) ? latest : null;
    }, timeoutMs, () => `Timed out waiting for sales workspace text ${expected.join(", ")}. Latest: ${latest.slice(0, 1200)}`);
  }

  async function waitForLocation(page, hashPath, searchText, timeoutMs) {
    let latest = "";
    return waitFor(async () => {
      latest = await read(page, "window.location.href").catch(() => "");
      return latest.includes(`#${hashPath}`) && latest.includes(searchText) ? latest : null;
    }, timeoutMs, () => `Timed out waiting for sales workspace location ${hashPath} ${searchText}. Latest: ${latest}`);
  }

  async function clickButtonByText(page, text, selector = "button") {
    const result = await read(page, `(() => {
      const button = Array.from(document.querySelectorAll(${JSON.stringify(selector)}))
        .find((item) => (item.textContent || '').includes(${JSON.stringify(text)}));
      if (!button) return false;
      button.click();
      return true;
    })()`);
    if (!result) throw new Error(`Sales workspace button was not found: ${text}`);
  }

  async function readSelectedTask(page) {
    return read(page, "document.querySelector('[role=\"tab\"][aria-selected=\"true\"]')?.textContent?.trim() || ''");
  }

  async function read(page, expression) {
    const result = await evaluate(page, expression, true);
    return result.value;
  }
}

function buildHashUrl(webUrl, route) {
  const url = new URL(webUrl);
  url.hash = route;
  return url.toString();
}

async function navigate(page, url) {
  await page.send("Page.navigate", { url });
}
