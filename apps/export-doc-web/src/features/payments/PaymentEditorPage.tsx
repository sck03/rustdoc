import { FormEvent, useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Edit3, Trash2 } from "lucide-react";
import { useLocation, useNavigate, useParams } from "react-router-dom";
import { ApiPaymentDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { queryKeys } from "../../api/queryKeys.ts";
import { handleEnterAsTabFormKeyDown } from "../../ui/formKeyboard.ts";
import { isConcurrencyConflict, normalizeText, readApiError, readRouteSuccessMessage } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useConfirmation } from "../../ui/ConfirmationProvider.tsx";
import { ConcurrencyConflictNotice, InlineNotice, PageState, PermissionNotice } from "../../ui/PageState.tsx";
import {
  hasCustomOptionValue,
  loadCustomOptionMap,
  paymentCustomOptionTypes,
} from "../custom-options/customOptionModel.ts";
import { PaymentAmountsPanel, PaymentBasicInfoPanel, PaymentBusinessInfoPanel } from "./PaymentFormPanels.tsx";
import { PaymentReportPreviewPanel } from "./PaymentReportPreviewPanel.tsx";
import { createEmptyPayment, normalizePaymentForSave } from "./paymentModel.ts";

export function PaymentEditorPage({
  client,
  mode,
}: {
  client: ExportDocManagerApiClient;
  mode: "new" | "edit";
}) {
  const paymentPermission = useModulePermission("document.payments");
  const masterDataPermission = useModulePermission("document.master-data");
  const requestConfirmation = useConfirmation();
  const { paymentId } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const routeSuccessMessage = readRouteSuccessMessage(location.state);
  const [payment, setPayment] = useState<ApiPaymentDto | null>(() => (mode === "new" ? createEmptyPayment() : null));
  const [message, setMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(routeSuccessMessage);
  const [concurrencyMessage, setConcurrencyMessage] = useState<string | null>(null);
  const [persistedPaymentSnapshot, setPersistedPaymentSnapshot] = useState<string | null>(null);

  const parsedPaymentId = Number(paymentId);
  const isNew = mode === "new";
  const isPaymentIdValid = Number.isInteger(parsedPaymentId) && parsedPaymentId > 0;
  const queryClient = useQueryClient();

  const paymentQuery = useQuery({
    queryKey: queryKeys.payment(parsedPaymentId),
    queryFn: () => client.getPayment({ id: parsedPaymentId }),
    enabled: !isNew && isPaymentIdValid,
  });

  const customOptionsQuery = useQuery({
    queryKey: queryKeys.customOptionsGroup("payment-editor"),
    queryFn: () => loadCustomOptionMap(client, paymentCustomOptionTypes),
    staleTime: 5 * 60 * 1000,
  });

  const payeesQuery = useQuery({
    queryKey: queryKeys.masterDataRoot("payees"),
    queryFn: () => client.listPayees({}),
    staleTime: 5 * 60 * 1000,
  });

  const exportersQuery = useQuery({
    queryKey: queryKeys.masterDataRoot("exporters"),
    queryFn: () => client.listExporters({}),
    staleTime: 5 * 60 * 1000,
  });

  useEffect(() => {
    if (isNew) {
      const nextPayment = createEmptyPayment();
      setPayment(nextPayment);
      setPersistedPaymentSnapshot(buildPaymentSnapshot(nextPayment, 0));
      setMessage(null);
      setConcurrencyMessage(null);
      setSuccessMessage(null);
      return;
    }

    if (!isPaymentIdValid) {
      setPayment(null);
      setPersistedPaymentSnapshot(null);
      setMessage("付款 ID 无效。");
      setSuccessMessage(null);
    }
  }, [isNew, isPaymentIdValid, parsedPaymentId]);

  useEffect(() => {
    if (!isNew && paymentQuery.data) {
      setPayment(paymentQuery.data);
      setPersistedPaymentSnapshot(buildPaymentSnapshot(paymentQuery.data, parsedPaymentId));
      setMessage(null);
      if (routeSuccessMessage && !successMessage) {
        setSuccessMessage(routeSuccessMessage);
      }
    }
  }, [paymentQuery.data, isNew, routeSuccessMessage, successMessage]);

  useEffect(() => {
    if (!isNew && paymentQuery.isError) {
      setMessage(readApiError(paymentQuery.error));
      setSuccessMessage(null);
    }
  }, [paymentQuery.error, paymentQuery.isError, isNew]);

  const savePaymentMutation = useMutation({
    mutationFn: (body: ApiPaymentDto) =>
      isNew
        ? client.createPayment({ body })
        : client.updatePayment({ id: parsedPaymentId, body }),
    onSuccess: async (response) => {
      const nextMessage = isNew ? "付款报销已创建。" : "付款报销已保存。";
      setPayment(response.payment);
      setPersistedPaymentSnapshot(buildPaymentSnapshot(response.payment, response.id));
      setMessage(null);
      setSuccessMessage(nextMessage);
      queryClient.setQueryData(queryKeys.payment(response.id), response.payment);
      await queryClient.invalidateQueries({ queryKey: queryKeys.paymentsRoot() });
      if (isNew) {
        navigate(`/payments/${response.id}`, {
          replace: true,
          state: { successMessage: "付款报销已创建。" },
        });
      }
    },
    onError: (error) => {
      const nextMessage = readApiError(error);
      setMessage(isConcurrencyConflict(error) ? null : nextMessage);
      setConcurrencyMessage(isConcurrencyConflict(error) ? nextMessage : null);
      setSuccessMessage(null);
    },
  });

  const deletePaymentMutation = useMutation({
    mutationFn: () => client.deletePayment({ id: parsedPaymentId }),
    onSuccess: async (response) => {
      setMessage(null);
      setSuccessMessage(null);
      queryClient.removeQueries({ queryKey: queryKeys.payment(parsedPaymentId) });
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.paymentsRoot() }),
        queryClient.invalidateQueries({ queryKey: queryKeys.jobsRoot() }),
      ]);
      navigate("/payments", {
        replace: true,
        state: { successMessage: response.message || "付款报销已删除。" },
      });
    },
    onError: (error) => {
      setMessage(readApiError(error));
      setSuccessMessage(null);
    },
  });

  const saveCustomOptionMutation = useMutation({
    mutationFn: ({ optionType, value }: { optionType: string; value: string }) =>
      client.saveCustomOption({
        optionType,
        body: { value },
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.customOptionsRoot() });
    },
  });

  const isBusy = paymentQuery.isFetching || savePaymentMutation.isPending || deletePaymentMutation.isPending;
  const paymentCustomOptions = customOptionsQuery.data ?? {};
  const payees = useMemo(
    () =>
      [...(payeesQuery.data ?? [])]
        .filter((payee) => payee.name?.trim())
        .sort((left, right) => left.name.localeCompare(right.name, "zh-CN")),
    [payeesQuery.data],
  );
  const payerNameOptions = useMemo(
    () =>
      Array.from(
        new Set(
          (exportersQuery.data ?? [])
            .map((exporter) => exporter.exporterNameCN?.trim())
            .filter((value): value is string => Boolean(value)),
        ),
      ).sort((left, right) => left.localeCompare(right, "zh-CN")),
    [exportersQuery.data],
  );
  const referenceDataMessage = payeesQuery.isError
    ? readApiError(payeesQuery.error)
    : exportersQuery.isError
      ? readApiError(exportersQuery.error)
      : null;
  const isReferenceDataBusy = payeesQuery.isFetching || exportersQuery.isFetching;
  const currentPaymentSnapshot = useMemo(
    () => (payment ? buildPaymentSnapshot(payment, isNew || !isPaymentIdValid ? 0 : parsedPaymentId) : null),
    [isNew, isPaymentIdValid, parsedPaymentId, payment],
  );
  const hasUnsavedPaymentChanges = Boolean(
    payment &&
      persistedPaymentSnapshot &&
      currentPaymentSnapshot &&
      currentPaymentSnapshot !== persistedPaymentSnapshot,
  );
  const { confirmDiscardChanges } = useUnsavedChangesGuard({
    isDirty: hasUnsavedPaymentChanges,
    message: "当前付款/报销记录有未保存的修改。",
  });

  function patchPayment(next: Partial<ApiPaymentDto>) {
    setPayment((current) => (current ? { ...current, ...next } : current));
    setSuccessMessage(null);
  }

  function patchPaymentAmounts(next: Partial<ApiPaymentDto>) {
    const updatesExpense = paymentExpenseFields.some((field) => Object.prototype.hasOwnProperty.call(next, field));

    setPayment((current) => {
      if (!current) {
        return current;
      }

      const nextPayment = { ...current, ...next };
      if (updatesExpense) {
        nextPayment.cnyAmount = calculatePaymentExpenseTotal(nextPayment);
      }

      return nextPayment;
    });

    setSuccessMessage(null);
  }

  function commitPaymentCustomOption(optionType: string, value: string) {
    if (!paymentPermission.canOperate) return;
    const normalizedValue = normalizeText(value);
    if (!normalizedValue || hasCustomOptionValue(paymentCustomOptions, optionType, normalizedValue)) {
      return;
    }

    saveCustomOptionMutation.mutate({ optionType, value: normalizedValue });
  }

  function saveCurrentPaymentDraft() {
    if (!paymentPermission.canOperate || !payment || isBusy || (!isNew && !isPaymentIdValid)) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    savePaymentMutation.mutate(normalizePaymentForSave(payment, isNew ? 0 : parsedPaymentId));
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    saveCurrentPaymentDraft();
  }

  useEffect(() => {
    function handleDocumentKeyDown(event: KeyboardEvent) {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s") {
        event.preventDefault();
        saveCurrentPaymentDraft();
      }
    }

    window.addEventListener("keydown", handleDocumentKeyDown);
    return () => window.removeEventListener("keydown", handleDocumentKeyDown);
  }, [isBusy, isNew, isPaymentIdValid, parsedPaymentId, payment]);

  async function handleDeletePayment() {
    if (isNew || !isPaymentIdValid || !payment || deletePaymentMutation.isPending) {
      return;
    }

    const title = payment.invoiceNo?.trim() || payment.payeeName?.trim() || `#${parsedPaymentId}`;
    if (!await requestConfirmation({ title: "删除付款/报销记录", description: `确定删除当前记录“${title}”吗？`, details: ["删除后无法在列表中继续查看。"], confirmLabel: "确认删除", tone: "danger" })) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    deletePaymentMutation.mutate();
  }

  async function handleBackToPaymentList() {
    if (await confirmDiscardChanges("返回付款列表")) {
      navigate("/payments");
    }
  }

  async function handleOpenPayeeManagement() {
    if (await confirmDiscardChanges("打开收款方资料库")) {
      navigate("/master-data/payees");
    }
  }

  async function handleReloadLatestPayment() {
    if (!await requestConfirmation({
      title: "加载最新付款版本",
      description: "服务器上的付款记录已被其他用户修改。",
      details: ["当前页面尚未保存的修改将被替换。", "加载后请重新检查并继续编辑。"],
      confirmLabel: "加载最新版本",
    })) return;
    const result = await paymentQuery.refetch();
    if (result.data) {
      setConcurrencyMessage(null);
      setMessage(null);
      setSuccessMessage("已加载服务器上的最新付款记录，请检查后继续编辑。");
    }
  }

  return (
    <section className="editor-surface" aria-label={isNew ? "新建付款报销" : "编辑付款报销"}>
      <div className="editor-toolbar">
        <button className="command-button secondary" type="button" onClick={handleBackToPaymentList}>
          <ArrowLeft size={17} aria-hidden="true" />
          <span>返回列表</span>
        </button>
        <div className="editor-title">
          <Edit3 size={18} aria-hidden="true" />
          <span>{isNew ? "新建付款报销" : payment?.invoiceNo || "编辑付款报销"}</span>
        </div>
        {!isNew && isPaymentIdValid && paymentPermission.canManage ? (
          <button
            className="command-button secondary danger"
            type="button"
            disabled={isBusy || !payment}
            onClick={handleDeletePayment}
          >
            <Trash2 size={17} aria-hidden="true" />
            <span>删除</span>
          </button>
        ) : null}
      </div>

      {concurrencyMessage ? <ConcurrencyConflictNotice message={concurrencyMessage} isBusy={paymentQuery.isFetching} onReload={() => void handleReloadLatestPayment()} /> : null}
      {message ? <InlineNotice tone="error" title="操作未完成">{message}</InlineNotice> : null}
      {successMessage ? <InlineNotice tone="success">{successMessage}</InlineNotice> : null}
      {!paymentPermission.canOperate ? (
        <PermissionNotice>当前模板仅允许查看付款报销，表单修改、保存和删除已禁用。</PermissionNotice>
      ) : null}

      {!payment && isBusy ? <PageState tone="loading" title="正在加载付款报销" description="正在读取付款信息、关联发票和报表配置。" /> : null}

      {payment ? (
        <form className="entity-form" onSubmit={handleSubmit} onKeyDownCapture={handleEnterAsTabFormKeyDown}>
          <fieldset className="permission-fieldset" disabled={!paymentPermission.canOperate}>
          <PaymentBasicInfoPanel
            payment={payment}
            isBusy={isBusy}
            isReferenceDataBusy={isReferenceDataBusy}
            payees={payees}
            payerNameOptions={payerNameOptions}
            referenceDataMessage={referenceDataMessage}
            customOptions={paymentCustomOptions}
            onChange={patchPayment}
            onCommitCustomOption={commitPaymentCustomOption}
            onOpenPayeeManagement={handleOpenPayeeManagement}
            canOpenPayeeManagement={masterDataPermission.canView}
            onRefreshReferenceData={() => {
              void payeesQuery.refetch();
              void exportersQuery.refetch();
            }}
          />
          {isNew ? (
            <details className="invoice-new-optional-section payment-new-optional-section">
              <summary>业务信息</summary>
              <PaymentBusinessInfoPanel payment={payment} onChange={patchPayment} />
            </details>
          ) : (
            <PaymentBusinessInfoPanel payment={payment} onChange={patchPayment} />
          )}
          <PaymentAmountsPanel
            payment={payment}
            onChange={patchPaymentAmounts}
          />
          </fieldset>

          <PaymentReportPreviewPanel
            client={client}
            paymentId={isNew || !isPaymentIdValid ? 0 : parsedPaymentId}
            paymentDraft={normalizePaymentForSave(payment, isNew || !isPaymentIdValid ? 0 : parsedPaymentId)}
            hasUnsavedDraftChanges={hasUnsavedPaymentChanges}
          />
        </form>
      ) : null}
    </section>
  );
}

const paymentExpenseFields = [
  "travelExpense",
  "businessEntertainmentExpense",
  "telephoneExpense",
  "officeExpense",
  "repairExpense",
  "freightMiscExpense",
  "inspectionExpense",
  "otherExpense",
] as const;

function calculatePaymentExpenseTotal(payment: ApiPaymentDto) {
  return paymentExpenseFields.reduce((sum, field) => sum + (Number(payment[field]) || 0), 0);
}

function buildPaymentSnapshot(payment: ApiPaymentDto, id: number) {
  return JSON.stringify(normalizePaymentForSave(payment, id));
}
