import { useEffect, useState } from "react";
import { readRouteId } from "./routeQueryState.ts";
import { useRouteQuery } from "./useRouteQuery.ts";

export function useDirectoryLocation(prefix = "", defaultPageSize = 20) {
  const { params, update } = useRouteQuery();
  const key = (name: string) => prefix ? `${prefix}${name[0].toUpperCase()}${name.slice(1)}` : name;
  const keyword = (params.get(key("keyword")) ?? "").slice(0, 100);
  const status = params.get(key("status")) ?? "";
  const pageNumber = readRouteId(params.get(key("page"))) ?? 1;
  const requestedSize = readRouteId(params.get(key("pageSize")));
  const pageSize = requestedSize && [12, 20, 24, 30, 48, 50, 96, 100].includes(requestedSize) ? requestedSize : defaultPageSize;
  const [keywordInput, setKeywordInput] = useState(keyword);
  useEffect(() => setKeywordInput(keyword), [keyword]);
  return { keyword, keywordInput, setKeywordInput, status, pageNumber, pageSize,
    setKeyword: (value: string) => update({ [key("keyword")]: value.trim(), [key("page")]: null }),
    setStatus: (value: string) => update({ [key("status")]: value, [key("page")]: null }),
    setPageNumber: (value: number) => update({ [key("page")]: value === 1 ? null : value }),
    setPageSize: (value: number) => update({ [key("pageSize")]: value, [key("page")]: null }),
  };
}
