import { useMemo } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "../../api/queryKeys.ts";
import { readApiError } from "../../ui/formUtils.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import type { ExporterSealType } from "../master-data/ExporterSealField.tsx";
import { invoiceSealDataUrl } from "./invoiceSealImage.ts";
import { useInvoiceSaveMutation, type InvoicePersistenceOptions } from "./useInvoiceSaveMutation.ts";

export function useInvoicePersistenceOperations(options: InvoicePersistenceOptions) {
  const { client, invoice, invoiceId, isNew, setInvoice, setMessage, setSuccessMessage } = options;
  const cache = useQueryClient();
  const scope = useMemo(() => ({}), [client, invoiceId, isNew]);
  const failed = (error: unknown) => { setMessage(readApiError(error)); setSuccessMessage(null); };
  const saveInvoiceMutation = useInvoiceSaveMutation(options);
  const exporterSealMutation = useScopedMutation("invoice-seal", scope, {
    mutationFn: async ({ file }: { sealType: ExporterSealType; file: File }, signal) => {
      if (!invoice) throw new Error("请先打开需要编辑的发票。");
      const imageDataUrl = await invoiceSealDataUrl(file);
      signal.throwIfAborted();
      return client.saveInvoiceSealImage({ body: { imageDataUrl } }, { signal });
    },
    onSuccess: (saved, { sealType }) => {
      const field = sealType === "document" ? "docSealPath" : "customsSealPath";
      setInvoice(current => current ? { ...current, [field]: saved.imagePath } : current);
      setMessage(null); setSuccessMessage(sealType === "document" ? "单证章已上传，请保存发票。" : "报关章已上传，请保存发票。");
    }, onError: failed,
  });
  const saveCustomOptionMutation = useScopedMutation("invoice-options", scope, {
    mutationFn: ({ optionType, value }: { optionType: string; value: string }, signal) => client.saveCustomOption({ optionType, body: { value } }, { signal }),
    refreshKeys: () => [queryKeys.customOptionsRoot()], onError: failed,
  });
  const refreshParties = () => Promise.all([queryKeys.masterDataRoot("customers"), queryKeys.masterDataRoot("exporters")]
    .map(queryKey => cache.invalidateQueries({ queryKey })));
  return { exporterSealMutation, refreshParties, saveCustomOptionMutation, saveInvoiceMutation };
}
