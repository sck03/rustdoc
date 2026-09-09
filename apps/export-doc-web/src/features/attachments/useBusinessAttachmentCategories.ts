import { useQuery } from "@tanstack/react-query";
import type { BusinessAttachmentCategoryRecord, ExportDocManagerApiClient } from "../../api/index.ts";

type OperationRunner = <T>(operation: (signal: AbortSignal) => Promise<T>, onSuccess: (result: T) => void, write?: boolean) => Promise<boolean>;

export function useBusinessAttachmentCategories(client: ExportDocManagerApiClient, userId: number, invoiceId: number | undefined,
  run: OperationRunner, notify: (message: string) => void) {
  const categories = useQuery({
    queryKey: ["business-attachment-categories", userId, invoiceId],
    queryFn: ({ signal }) => client.listBusinessAttachmentCategories({ invoiceId }, { signal }),
  });
  function saveCategory(item: BusinessAttachmentCategoryRecord | null, name: string) {
    if (!categories.data) return Promise.resolve(false);
    const companyScope = categories.data.companyScope;
    return run((signal) => item
      ? client.updateBusinessAttachmentCategory({ id: item.id, body: { name, expectedVersion: item.versionNumber } }, { signal })
      : client.createBusinessAttachmentCategory({ body: { name, companyScope } }, { signal }),
    () => notify(item ? "分类名称已修改。" : "资料分类已新增。"), true);
  }
  function removeCategory(item: BusinessAttachmentCategoryRecord) {
    return run((signal) => client.deleteBusinessAttachmentCategory({ id: item.id, expectedVersion: item.versionNumber }, { signal }),
      () => notify("未使用的资料分类已删除。"), true);
  }
  return { categories, saveCategory, removeCategory };
}
