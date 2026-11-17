import { useMemo, useState, type SetStateAction } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type { ApiContainerTypeListResponse, ExportDocManagerApiClient } from "../../../api/index.ts";
import { queryKeys } from "../../../api/queryKeys.ts";
import { readApiError } from "../../../ui/formUtils.ts";
import { useScopedMutation } from "../../../ui/useScopedMutation.ts";
import { findContainerType, readNonNegativeNumberInput, readPositiveIntegerInput, type ContainerPackingFormState } from "./containerPackingModel.ts";

/** One command boundary for the selected type; a late response cannot edit another draft. */
export function useContainerPackingTypes(
  client: ExportDocManagerApiClient,
  projectId: number,
  container: ContainerPackingFormState,
  setContainer: (value: SetStateAction<ContainerPackingFormState>) => void,
  canManage: boolean,
  setMessage: (message: string | null) => void,
) {
  const cache = useQueryClient();
  const query = useQuery({
    queryKey: queryKeys.containerPackingContainerTypes(),
    queryFn: ({ signal }) => client.listContainerPackingContainerTypes({ signal }),
  });
  const types = query.data?.containerTypes ?? [];
  const selected = findContainerType(types, container.containerType);
  const scope = `${projectId}:${container.containerType}`;
  const owner = useMemo(() => ({ client, canManage }), [client, canManage]);
  const [failed, setFailed] = useState(false);
  const command = useScopedMutation(scope, owner, {
    mutationFn: async (kind: "save" | "delete", signal, { checkCurrent, continueInScope }) => {
      if (!canManage || selected?.isSystemDefault) return false;
      setFailed(false);
      setMessage(null);
      const expectedVersion = selected?.versionNumber;
      if (kind === "delete") {
        if (!selected || !expectedVersion) return false;
        await client.deleteContainerPackingContainerType({ id: selected.id, expectedVersion }, { signal });
        checkCurrent();
        continueInScope(`${projectId}:`);
        cache.setQueryData<ApiContainerTypeListResponse>(queryKeys.containerPackingContainerTypes(), current => current && ({
          ...current, containerTypes: current.containerTypes.filter(type => type.id !== selected.id),
        }));
        setContainer(current => ({ ...current, containerType: "" }));
        setMessage("柜型已删除。");
      } else {
        const response = await client.saveContainerPackingContainerType({ body: {
          id: selected?.id ?? 0, expectedVersion,
          name: container.containerType.trim(),
          length: readPositiveIntegerInput(container.length, 0),
          width: readPositiveIntegerInput(container.width, 0),
          height: readPositiveIntegerInput(container.height, 0),
          maxVolume: readNonNegativeNumberInput(container.volume),
          maxWeight: readNonNegativeNumberInput(container.maxWeight),
        } }, { signal });
        checkCurrent();
        const nextScope = `${projectId}:${response.containerType.name}`;
        continueInScope(nextScope);
        cache.setQueryData<ApiContainerTypeListResponse>(queryKeys.containerPackingContainerTypes(), current => current && ({
          ...current, containerTypes: [...current.containerTypes.filter(type => type.id !== response.id), response.containerType],
        }));
        setContainer(current => ({ ...current, containerType: response.containerType.name }));
        setMessage(response.message || "柜型已保存。");
      }
      return true;
    },
    refreshKeys: changed => changed ? [queryKeys.containerPackingContainerTypes()] : [],
    onError: error => { setFailed(true); setMessage(readApiError(error)); },
  });
  return { query, types, selected, command, failed };
}
