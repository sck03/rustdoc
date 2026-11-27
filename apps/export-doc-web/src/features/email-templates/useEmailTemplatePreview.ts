import { useEffect, useMemo, useState } from "react";
import type { ApiCrmCustomerDto, ApiEmailTemplatePreviewDto, ApiEmailTemplateVariableDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { errorFeedback, successFeedback, warningFeedback, type OperationFeedbackState } from "../../ui/OperationFeedback.tsx";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import type { EmailTemplateDraft } from "./emailTemplateModel.ts";

/** Reads belong to the input snapshot, including edits made while a request is pending. */
export function useEmailTemplatePreview(client: ExportDocManagerApiClient, templateId: number | undefined, draft: EmailTemplateDraft,
  view: string, canView: boolean, canReadCrm: boolean, feedback: (value: OperationFeedbackState | null) => void) {
  const [variables, setVariables] = useState<ApiEmailTemplateVariableDto[]>([]);
  const [manualValues, setManualValues] = useState<Record<string, string>>({});
  const [customerValues, setCustomerValues] = useState<Record<string, string>>({});
  const sampleValues = useMemo(() => ({ ...Object.fromEntries(variables.map(item => [item.key, item.sampleValue])), ...customerValues, ...manualValues }), [variables, customerValues, manualValues]);
  const [crmCustomerKeyword, setCrmCustomerKeyword] = useState("");
  const [crmCustomers, setCrmCustomers] = useState<ApiCrmCustomerDto[]>([]);
  const [crmCustomerId, selectCustomer] = useState(0);
  const [recipientAddress, setRecipientAddress] = useState("");
  const input = useMemo(() => ({ client, templateId, draft, sampleValues, crmCustomerId, recipientAddress, canView, canReadCrm }),
    [client, templateId, draft, sampleValues, crmCustomerId, recipientAddress, canView, canReadCrm]);
  const scope = useMemo(() => ({ input, view }), [input, view]);
  const searchScope = useMemo(() => ({ client, crmCustomerKeyword, crmCustomerId, canReadCrm }), [client, crmCustomerKeyword, crmCustomerId, canReadCrm]);
  const catalogScope = useMemo(() => ({ client, canView }), [client, canView]);
  const readPreview = useAbortableOperation(scope, { cancelPrevious: true });
  const readCustomer = useAbortableOperation(scope, { cancelPrevious: true });
  const search = useAbortableOperation(searchScope, { cancelPrevious: true });
  const catalog = useAbortableOperation(catalogScope, { cancelPrevious: true });
  const [rendered, setRendered] = useState<{ input: typeof input; value: ApiEmailTemplatePreviewDto } | null>(null);
  const preview = rendered?.input === input ? rendered.value : null;
  function report(error: unknown) { if (!isAbortError(error)) feedback(errorFeedback(readApiError(error))); }
  function setSampleValue(key: string, value: string) { setManualValues(current => ({ ...current, [key]: value })); }
  function setCrmCustomerId(id: number) {
    if (id === crmCustomerId) return;
    selectCustomer(id); setRecipientAddress(""); setCustomerValues({}); setRendered(null);
  }
  useEffect(() => {
    setVariables([]); setManualValues({}); setCustomerValues({}); setCrmCustomers([]); selectCustomer(0); setRecipientAddress(""); setRendered(null);
    if (canView) void catalog(signal => client.listEmailTemplateVariables({ signal })).then(rows => {
      setVariables(rows);
    }).catch(report);
  }, [catalogScope]);
  useEffect(() => { if (canReadCrm) void searchCrmCustomers(""); }, [client, canReadCrm]);

  async function searchCrmCustomers(keyword = crmCustomerKeyword) {
    if (!canReadCrm) return;
    try {
      const page = await search(signal => client.queryCrmCustomers({ keyword: keyword.trim(), status: "", pageNumber: 1, pageSize: 50 }, { signal }));
      setCrmCustomers(page.items);
      setCrmCustomerId(page.items.some(item => item.id === crmCustomerId) ? crmCustomerId : page.items[0]?.id ?? 0);
    } catch (error) { report(error); }
  }
  async function loadCrmDraft() {
    if (!canReadCrm || !canView) return;
    if (!crmCustomerId) { feedback(warningFeedback("请选择 CRM 客户。")); return; }
    try {
      const customer = await readCustomer(signal => client.getCrmEmailVariableDraft({ customerId: crmCustomerId }, { signal }));
      const values = Object.fromEntries(Object.entries(customer.variables).map(([key, value]) => [key, typeof value === "string" ? value : ""]));
      setCustomerValues(values);
      setManualValues(current => Object.fromEntries(Object.entries(current).filter(([key]) => !(key in values))));
      setRecipientAddress(customer.toAddress);
      feedback(customer.toAddress ? successFeedback("已载入客户、主要联系人和建议收件人。") : warningFeedback("已载入客户变量；主要联系人尚未填写邮箱。"));
    } catch (error) { report(error); }
  }
  async function renderPreview() {
    if (!canView) return null;
    try {
      const value = await readPreview(signal => client.previewEmailTemplate({ body: { subject: draft.subject, bodyHtml: draft.bodyHtml, variables: sampleValues } }, { signal }));
      setRendered({ input, value });
      feedback(value.unresolvedTokens.length ? warningFeedback(`仍有未识别变量：${value.unresolvedTokens.join("、")}`) : null);
      return value;
    } catch (error) { report(error); return null; }
  }
  return { variables, sampleValues, setSampleValue, preview, clearPreview: () => setRendered(null), crmCustomerKeyword, setCrmCustomerKeyword,
    crmCustomers, crmCustomerId, setCrmCustomerId, recipientAddress, searchCrmCustomers, loadCrmDraft, renderPreview };
}
