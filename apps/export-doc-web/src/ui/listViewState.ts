import { readStoredJsonObject, writeStoredJson } from "./browserStorage.ts";

export const listPageSizeOptions = [20, 50, 100, 200] as const;
export const defaultListPageSize = 50;

export type StoredListViewState = {
  keyword: string;
  pageSize: number;
};

export function loadListViewState(storageKey: string): StoredListViewState {
  const parsed = readStoredJsonObject(storageKey) as Partial<Record<keyof StoredListViewState, unknown>>;
  return {
    keyword: typeof parsed.keyword === "string" ? parsed.keyword.trim() : "",
    pageSize: normalizeListPageSize(parsed.pageSize),
  };
}

export function saveListViewState(storageKey: string, state: StoredListViewState) {
  writeStoredJson(storageKey, {
    keyword: state.keyword.trim(),
    pageSize: normalizeListPageSize(state.pageSize),
  });
}

export function normalizeListPageSize(value: unknown) {
  const numericValue = typeof value === "number" ? value : Number(value);
  return listPageSizeOptions.includes(numericValue as (typeof listPageSizeOptions)[number])
    ? numericValue
    : defaultListPageSize;
}

function createDefaultListViewState(): StoredListViewState {
  return {
    keyword: "",
    pageSize: defaultListPageSize,
  };
}
