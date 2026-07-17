import { ExportDocManagerApiClient } from "../../api/index.ts";

export type CustomOptionMap = Record<string, string[]>;

export const invoiceCustomOptionTypes = [
  "Currency",
  "SupervisionMode",
  "PaymentTerms",
  "PortOfLoading",
  "PortOfDestination",
  "TransportMode",
] as const;

export const paymentCustomOptionTypes = ["PaymentMethod"] as const;

export const masterDataCustomOptionTypes = ["PayeeCategory"] as const;

export async function loadCustomOptionMap(
  client: ExportDocManagerApiClient,
  optionTypes: readonly string[],
): Promise<CustomOptionMap> {
  const entries = await Promise.all(
    optionTypes.map(async (optionType) => {
      const response = await client.listCustomOptions({ optionType });
      return [response.optionType || optionType, response.options ?? []] as const;
    }),
  );

  return Object.fromEntries(entries);
}

export function getCustomOptions(options: CustomOptionMap | undefined, optionType: string) {
  return options?.[optionType] ?? [];
}

export function hasCustomOptionValue(options: CustomOptionMap | undefined, optionType: string, value: string) {
  const normalizedValue = value.trim();
  if (!normalizedValue) {
    return true;
  }

  return getCustomOptions(options, optionType).some(
    (option) => option.trim().toLowerCase() === normalizedValue.toLowerCase(),
  );
}
