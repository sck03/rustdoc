import { ApiError, type ApiHsCodeDto, type ExportDocManagerApiClient } from "../../api/index.ts";
import { useConfirmation } from "../../ui/confirmationContext.ts";
import { useScopedMutation } from "../../ui/useScopedMutation.ts";
import { mergeHsCodeReference, normalizeHsCodeDtoForSave } from "./masterDataModel.ts";

type SavedReference = { record: ApiHsCodeDto; updated: boolean };

export function useHsReferenceSave({ client, scope, onSaved, onError, refresh }: {
  client: ExportDocManagerApiClient;
  scope: string;
  onSaved: (result: SavedReference, source: ApiHsCodeDto) => void;
  onError: (error: unknown) => void;
  refresh: () => Promise<void>;
}) {
  const requestConfirmation = useConfirmation();
  return useScopedMutation<SavedReference | null, ApiHsCodeDto>(scope, client, {
    mutationFn: async (source, signal, { checkCurrent }) => {
      const body = normalizeHsCodeDtoForSave(source);
      let existing: ApiHsCodeDto;
      try {
        existing = await client.getHsCode({ code: body.code }, { signal });
      } catch (error) {
        checkCurrent();
        if (!(error instanceof ApiError && error.status === 404)) throw error;
        return { record: await client.createHsCode({ body }, { signal }), updated: false };
      }
      checkCurrent();
      if (existing.status !== "ReferenceOnly") throw new Error("此编码已有税则记录，请在本地 HS 编码中查看或编辑。");
      const accepted = await requestConfirmation({
        title: "更新本地参考资料",
        description: `编码 ${existing.code} 已存在。更新远程提供的参考信息，并保留本地备注和年度信息。`,
        confirmLabel: "确认更新",
      });
      checkCurrent();
      if (!accepted) return null;
      return { record: await client.updateHsCode({ code: existing.code, body: mergeHsCodeReference(existing, source) }, { signal }), updated: true };
    },
    onSuccess: (result, source) => { if (result) onSaved(result, source); },
    refresh: result => result ? refresh() : undefined,
    onError,
  });
}
