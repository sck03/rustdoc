import { useQuery } from "@tanstack/react-query";
import type { ExportDocManagerApiClient } from "../../api/index.ts";
import { queryKeys } from "../../api/queryKeys.ts";
import {
  invoiceCustomOptionTypes,
  loadCustomOptionMap,
} from "../custom-options/customOptionModel.ts";

export function useInvoiceEditorReferenceData(
  client: ExportDocManagerApiClient,
  customerId: number,
  exporterId: number,
) {
  const selectedCustomerQuery = useQuery({
    queryKey: queryKeys.masterDataRecord("customers", String(customerId)),
    queryFn: ({ signal }) => client.getCustomer({ id: customerId }, { signal }),
    enabled: customerId > 0,
    staleTime: 5 * 60 * 1000,
  });

  const selectedExporterQuery = useQuery({
    queryKey: queryKeys.masterDataRecord("exporters", String(exporterId)),
    queryFn: ({ signal }) => client.getExporter({ id: exporterId }, { signal }),
    enabled: exporterId > 0,
    staleTime: 5 * 60 * 1000,
  });

  const unitsQuery = useQuery({
    queryKey: queryKeys.masterDataRoot("units"),
    queryFn: ({ signal }) => client.listUnits({}, { signal }),
    staleTime: 5 * 60 * 1000,
  });

  const settingsQuery = useQuery({
    queryKey: queryKeys.settings(),
    queryFn: ({ signal }) => client.getSettings({ signal }),
    staleTime: 5 * 60 * 1000,
  });

  const customOptionsQuery = useQuery({
    queryKey: queryKeys.customOptionsGroup("invoice-editor"),
    queryFn: ({ signal }) => loadCustomOptionMap(client, invoiceCustomOptionTypes, signal),
    staleTime: 5 * 60 * 1000,
  });

  return {
    selectedCustomerQuery,
    selectedExporterQuery,
    unitsQuery,
    settingsQuery,
    customOptionsQuery,
  };
}
