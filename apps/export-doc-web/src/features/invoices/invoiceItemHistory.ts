import { ApiInvoiceItemDto } from "../../api/index.ts";
import { normalizeText } from "../../ui/formUtils.ts";
import { InvoiceItemColumnDefinition } from "./invoiceItemTableModel.ts";

const maximumHistoryOptionCount = 12;

export class InvoiceItemHistoryOptionCache {
  private readonly entries = new Map<string, string[]>();

  public getOptions(items: ApiInvoiceItemDto[], rowIndex: number, column: InvoiceItemColumnDefinition) {
    const key = createHistoryCacheKey(rowIndex, column.field);
    const cached = this.entries.get(key);
    if (cached) {
      return cached;
    }

    const options = buildInvoiceItemPreviousValueOptions(items, rowIndex, column);
    this.entries.set(key, options);
    return options;
  }

  public invalidateAfter(rowIndex: number) {
    for (const key of this.entries.keys()) {
      const separatorIndex = key.indexOf(":");
      const cachedRowIndex = Number(key.slice(0, separatorIndex));
      if (cachedRowIndex > rowIndex) {
        this.entries.delete(key);
      }
    }
  }

  public clear() {
    this.entries.clear();
  }
}

export function filterInvoiceItemHistoryOptionsByPrefix(options: string[], prefix: string) {
  const prefixValue = normalizeText(prefix);
  if (!prefixValue) {
    return [];
  }

  const prefixKey = prefixValue.toLocaleLowerCase();
  return options.filter((option) => {
    const optionKey = normalizeText(option).toLocaleLowerCase();
    return optionKey.startsWith(prefixKey) && optionKey !== prefixKey;
  });
}

function buildInvoiceItemPreviousValueOptions(
  items: ApiInvoiceItemDto[],
  rowIndex: number,
  column: InvoiceItemColumnDefinition,
) {
  const options: string[] = [];
  const seen = new Set<string>();
  const startIndex = Math.min(rowIndex, items.length) - 1;

  for (let index = startIndex; index >= 0 && options.length < maximumHistoryOptionCount; index -= 1) {
    const item = items[index];
    if (!item) {
      continue;
    }

    const value = readHistoryValue(item, column);
    if (!value) {
      continue;
    }

    const key = value.toLocaleLowerCase();
    if (seen.has(key)) {
      continue;
    }

    seen.add(key);
    options.push(value);
  }

  return options;
}

function readHistoryValue(item: ApiInvoiceItemDto, column: InvoiceItemColumnDefinition) {
  const value = item[column.field];
  if (column.kind === "number") {
    return typeof value === "number" && Number.isFinite(value) && value !== 0 ? String(value) : "";
  }

  return typeof value === "string" ? normalizeText(value) : "";
}

function createHistoryCacheKey(rowIndex: number, field: string) {
  return `${rowIndex}:${field}`;
}
