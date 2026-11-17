import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import type { ApiCrmContactDto, ExportDocManagerApiClient } from "../../../api/index.ts";
import { useDirectoryLocation } from "../../../ui/useDirectoryLocation.ts";
import { useServerPageNumber } from "../../../ui/useServerPageNumber.ts";

export function useCrmContactsPage(client: ExportDocManagerApiClient, customerId: number, enabled: boolean) {
  const location = useDirectoryLocation("contacts");
  const cache = useQueryClient();
  const [preferred, setPreferred] = useState<{ record: ApiCrmContactDto; client: ExportDocManagerApiClient }>();
  const query = useQuery({
    queryKey: ["crm-contacts", customerId, location.pageNumber, location.pageSize],
    queryFn: ({ signal }) => client.queryCrmContacts({ customerId, pageNumber: location.pageNumber, pageSize: location.pageSize }, { signal }),
    enabled: enabled && customerId > 0, gcTime: 0,
  });
  useServerPageNumber(query, location.pageNumber, location.setPageNumber);
  const rows = query.data?.items ?? [];
  const pinned = preferred?.client === client && preferred.record.crmCustomerId === customerId ? preferred.record : undefined;
  const contacts = !enabled ? [] : pinned && !rows.some(row => row.id === pinned.id) ? [pinned, ...rows] : rows;
  async function reloadContacts(record?: ApiCrmContactDto, signal?: AbortSignal) {
    signal?.throwIfAborted();
    setPreferred(record ? { record, client } : undefined);
    await cache.invalidateQueries({ queryKey: ["crm-contacts", customerId] }, { throwOnError: true });
  }
  function selectContact(id: number) {
    const record = contacts.find(item => item.id === id);
    setPreferred(record ? { record, client } : undefined);
  }
  return { contacts, reloadContacts, query, location, selectContact,
    pagination: { pageNumber: location.pageNumber, pageSize: location.pageSize, totalPages: query.data?.totalPages,
      totalCount: query.data?.totalCount, isBusy: query.isFetching,
      onPageChange: (page: number) => { setPreferred(undefined); location.setPageNumber(page); } } };
}
