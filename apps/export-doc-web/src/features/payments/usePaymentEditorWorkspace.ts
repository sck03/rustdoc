import { type FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useLocation, useNavigate } from "react-router-dom";
import type { ApiPaymentDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import { useModulePermission } from "../../app/PermissionAccessContext.tsx";
import { isComposingKey } from "../../ui/keyboardEvent.ts";
import { isConcurrencyConflict, normalizeText, readApiError, readRouteSuccessMessage } from "../../ui/formUtils.ts";
import { useUnsavedChangesGuard } from "../../ui/unsavedChangesGuard.tsx";
import { useServerDraftSync } from "../../ui/serverDraftSync.tsx";
import { useConfirmation } from "../../ui/confirmationContext.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { useScopedConfirmation } from "../../ui/useScopedConfirmation.ts";
import { isAbortError, useAbortableOperation } from "../../ui/useAbortableOperation.ts";
import { hasCustomOptionValue, loadCustomOptionMap, paymentCustomOptionTypes } from "../custom-options/customOptionModel.ts";
import { calculatePaymentExpenseTotal, createEmptyPayment, normalizePaymentForSave, paymentExpenseFields, validatePaymentDraft } from "./paymentModel.ts";

type PaymentCommand = { kind: "save" | "delete"; payment: ApiPaymentDto } | { kind: "reload" };
export type PaymentEditorProps = { businessDate: string; client: ExportDocManagerApiClient; mode: "new" | "edit" };

export function usePaymentEditorWorkspace({ businessDate, client, mode }: PaymentEditorProps, paymentId?: string) {
  const masterDataPermission = useModulePermission("document.master-data");
  const requestConfirmation = useConfirmation();
  const navigate = useNavigate();
  const location = useLocation();
  const routeSuccessMessage = readRouteSuccessMessage(location.state);
  const parsedPaymentId = Number(paymentId);
  const isNew = mode === "new";
  const isPaymentIdValid = Number.isInteger(parsedPaymentId) && parsedPaymentId > 0;
  const [payment, setPayment] = useState<ApiPaymentDto | null>(() =>
    isNew ? createEmptyPayment(businessDate) : null,
  );
  const paymentPermission = useModulePermission("document.payments", mode === "new" ? undefined : payment);
  const [message, setMessage] = useState<string | null>(!isNew && !isPaymentIdValid ? "付款 ID 无效。" : null);
  const [successMessage, setSuccessMessage] = useState<string | null>(routeSuccessMessage);
  const [concurrencyMessage, setConcurrencyMessage] = useState<string | null>(null);
  const [persistedPaymentSnapshot, setPersistedPaymentSnapshot] = useState<string | null>(() =>
    payment ? buildPaymentSnapshot(payment, 0) : null,
  );

  const scope = JSON.stringify([isNew, parsedPaymentId, paymentPermission.canOperate, paymentPermission.canManage]);
  const confirmationScope = useMemo(() => ({ scope, client }), [scope, client]);
  const runCustomOption = useAbortableOperation(confirmationScope);
  const queryClient = useQueryClient();
  const formRef = useRef<HTMLFormElement>(null);
  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: ({ signal }) => client.getSettings({ signal }),
  });

  const paymentQuery = useQuery({
    queryKey: queryKeys.payment(parsedPaymentId),
    queryFn: ({ signal }) => client.getPayment({ id: parsedPaymentId }, { signal }),
    enabled: !isNew && isPaymentIdValid,
  });

  const customOptionsQuery = useQuery({
    queryKey: queryKeys.customOptionsGroup("payment-editor"),
    queryFn: ({ signal }) => loadCustomOptionMap(client, paymentCustomOptionTypes, signal),
    staleTime: 5 * 60 * 1000,
  });

  const selectedPayeeId = payment?.payeeId ?? 0;
  const selectedPayeeQuery = useQuery({
    queryKey: queryKeys.masterDataRecord("payees", String(selectedPayeeId)),
    queryFn: ({ signal }) => client.getPayee({ id: selectedPayeeId }, { signal }),
    enabled: selectedPayeeId > 0,
    staleTime: 5 * 60 * 1000,
  });

  useEffect(() => {
    if (!isNew && paymentQuery.isError) {
      setMessage(readApiError(paymentQuery.error));
      setSuccessMessage(null);
    }
  }, [paymentQuery.error, paymentQuery.isError, isNew]);

  const commands = useScopedMutation<boolean, PaymentCommand>(scope, client, {
    mutationFn: async (command, signal, { checkCurrent }) => {
      if (command.kind === "delete") {
        if (!paymentPermission.canManage) return false;
        const title = command.payment.voucherNo?.trim() || command.payment.invoiceNo?.trim() || command.payment.payeeName?.trim() || `#${command.payment.id}`;
        const confirmed = await requestConfirmation({ title: "删除付款/报销记录", description: `确定删除当前记录“${title}”吗？`, details: ["删除后无法在列表中继续查看。"], confirmLabel: "确认删除", tone: "danger" });
        checkCurrent();
        if (!confirmed) return false;
        const response = await client.deletePayment({ id: command.payment.id, rowVersion: command.payment.rowVersion ?? "" }, { signal });
        checkCurrent();
        queryClient.removeQueries({ queryKey: queryKeys.payment(command.payment.id) });
        navigate("/payments", { replace: true, state: { successMessage: response.message || "付款报销已删除。" } });
        return true;
      }
      if (command.kind === "reload") {
        const confirmed = await requestConfirmation({
          title: "加载最新付款版本", description: "服务器上的付款记录已被其他用户修改。",
          details: ["当前页面尚未保存的修改将被替换。", "加载后请重新检查并继续编辑。"], confirmLabel: "加载最新版本",
        });
        checkCurrent();
        if (!confirmed) return false;
        const latest = await client.getPayment({ id: parsedPaymentId }, { signal });
        checkCurrent();
        queryClient.setQueryData(queryKeys.payment(parsedPaymentId), latest);
        if (serverDraftSync.acceptServerVersion(latest)) {
          setConcurrencyMessage(null);
          setMessage(null);
          setSuccessMessage("已加载服务器上的最新付款记录，请检查后继续编辑。");
        }
        return true;
      }
      if (!paymentPermission.canOperate) return false;
      const body = command.payment;
      const response = await (isNew
        ? client.createPayment({ body }, { signal })
        : client.updatePayment({ id: body.id, body }, { signal }));
      checkCurrent();
      setPayment(response.payment);
      setPersistedPaymentSnapshot(buildPaymentSnapshot(response.payment, response.id));
      setMessage(null);
      setConcurrencyMessage(null);
      setSuccessMessage(isNew ? "付款报销已创建。" : "付款报销已保存。");
      queryClient.setQueryData(queryKeys.payment(response.id), response.payment);
      if (isNew) navigate(`/payments/${response.id}${location.search}`, {
        replace: true, state: { successMessage: "付款报销已创建。" },
      });
      return true;
    },
    refreshKeys: (changed, command) => !changed || command.kind === "reload" ? []
      : command.kind === "delete" ? [queryKeys.paymentsRoot(), queryKeys.jobsRoot()] : [queryKeys.paymentsRoot()],
    onRefreshError: error => setMessage(`操作已完成，但列表刷新失败：${readApiError(error)}`),
    onError: error => {
      const nextMessage = readApiError(error);
      setMessage(isConcurrencyConflict(error) ? null : nextMessage);
      if (isConcurrencyConflict(error)) setConcurrencyMessage(nextMessage);
      setSuccessMessage(null);
    },
  });

  const isBusy = paymentQuery.isFetching || commands.isPending;
  const paymentCustomOptions = customOptionsQuery.data ?? {};
  const payerNameOptions = useMemo(
    () => paymentCustomOptions.PaymentPayerName ?? [],
    [paymentCustomOptions.PaymentPayerName],
  );
  const referenceDataMessage = selectedPayeeQuery.isError
    ? readApiError(selectedPayeeQuery.error)
    : customOptionsQuery.isError
      ? readApiError(customOptionsQuery.error)
      : null;
  const isReferenceDataBusy = selectedPayeeQuery.isFetching || customOptionsQuery.isFetching;
  const currentPaymentDraft = useMemo(
    () => (payment ? normalizePaymentForSave(payment, isNew || !isPaymentIdValid ? 0 : parsedPaymentId) : undefined),
    [isNew, isPaymentIdValid, parsedPaymentId, payment],
  );
  const currentPaymentSnapshot = useMemo(
    () => (currentPaymentDraft ? JSON.stringify(currentPaymentDraft) : null),
    [currentPaymentDraft],
  );
  const hasUnsavedPaymentChanges = Boolean(
    payment &&
      persistedPaymentSnapshot &&
      currentPaymentSnapshot &&
      currentPaymentSnapshot !== persistedPaymentSnapshot,
  );
  const serverDraftSync = useServerDraftSync({
    resourceKey: isNew ? "new" : parsedPaymentId,
    incomingValue: isNew ? null : paymentQuery.data,
    isDirty: hasUnsavedPaymentChanges,
    fingerprint: (serverPayment) => buildPaymentSnapshot(serverPayment, serverPayment.id),
    applyIncoming: (serverPayment) => {
      setPayment(serverPayment);
      setPersistedPaymentSnapshot(buildPaymentSnapshot(serverPayment, parsedPaymentId));
      setMessage(null);
      if (routeSuccessMessage && !successMessage) {
        setSuccessMessage(routeSuccessMessage);
      }
    },
  });
  const { confirmDiscardChanges: confirmDiscard } = useUnsavedChangesGuard({
    isDirty: hasUnsavedPaymentChanges,
    message: "当前付款/报销记录有未保存的修改。",
  });
  const confirmDiscardChanges = useScopedConfirmation(confirmationScope, confirmDiscard);

  function patchPayment(next: Partial<ApiPaymentDto>) {
    setPayment((current) => (current ? { ...current, ...next } : current));
    setSuccessMessage(null);
  }

  function patchPaymentAmounts(next: Partial<ApiPaymentDto>) {
    const updatesExpense = paymentExpenseFields.some(({ field }) => Object.prototype.hasOwnProperty.call(next, field));

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

    void runCustomOption(async signal => {
      await client.saveCustomOption({ optionType, body: { value: normalizedValue } }, { signal });
      signal.throwIfAborted();
      await queryClient.invalidateQueries({ queryKey: queryKeys.customOptionsRoot() });
    }).catch(error => { if (!isAbortError(error)) setMessage(readApiError(error)); });
  }

  function saveCurrentPaymentDraft() {
    if (!paymentPermission.canOperate || !payment || isBusy || (!isNew && (!isPaymentIdValid || payment.id !== parsedPaymentId))) {
      return;
    }

    setMessage(null);
    setSuccessMessage(null);
    const validationMessage = validatePaymentDraft(payment);
    if (validationMessage) {
      setMessage(validationMessage);
      return;
    }
    commands.mutate({ kind: "save", payment: normalizePaymentForSave(payment, isNew ? 0 : payment.id) });
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    saveCurrentPaymentDraft();
  }

  useEffect(() => {
    function handleDocumentKeyDown(event: KeyboardEvent) {
      if (event.defaultPrevented || isComposingKey(event)) return;
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "s") {
        event.preventDefault();
        formRef.current?.requestSubmit();
      }
    }

    window.addEventListener("keydown", handleDocumentKeyDown);
    return () => window.removeEventListener("keydown", handleDocumentKeyDown);
  }, []);

  function handleDeletePayment() {
    if (!paymentPermission.canManage || isNew || !isPaymentIdValid || !payment || isBusy || payment.id !== parsedPaymentId) return;
    setMessage(null);
    setSuccessMessage(null);
    commands.mutate({ kind: "delete", payment });
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

  function handleReloadLatestPayment() {
    if (payment && !isNew && !isBusy) commands.mutate({ kind: "reload" });
  }

  function refreshReferenceData() {
    void queryClient.invalidateQueries({ queryKey: queryKeys.masterDataRoot("payees") });
    void customOptionsQuery.refetch();
  }
  return { isNew, parsedPaymentId, isPaymentIdValid, payment, paymentPermission, masterDataPermission,
    message, successMessage, concurrencyMessage, settingsQuery, paymentQuery, serverDraftSync,
    formRef, isBusy, commands, hasUnsavedPaymentChanges, currentPaymentDraft,
    isReferenceDataBusy, selectedPayeeQuery, payerNameOptions, referenceDataMessage, paymentCustomOptions,
    handleBackToPaymentList, handleDeletePayment, handleReloadLatestPayment, handleSubmit,
    patchPayment, patchPaymentAmounts, commitPaymentCustomOption, handleOpenPayeeManagement, refreshReferenceData };
}

function buildPaymentSnapshot(payment: ApiPaymentDto, id: number): string {
  return JSON.stringify(normalizePaymentForSave(payment, id));
}
