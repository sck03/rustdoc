import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useId, useMemo, useState } from "react";
import { useDebouncedValue } from "./useDebouncedValue.ts";

export function RemoteSelectField<T>({
  label,
  value,
  selectedOption,
  selectedLabel,
  disabled,
  className,
  description,
  emptyLabel = "未选择",
  searchPlaceholder = "输入关键字检索",
  queryKey,
  loadOptions,
  getValue,
  getLabel,
  onChange,
  dataQueryFilter,
}: {
  label: string;
  value: string;
  selectedOption?: T | null;
  selectedLabel?: string;
  disabled?: boolean;
  className?: string;
  description?: string;
  emptyLabel?: string;
  searchPlaceholder?: string;
  queryKey: readonly unknown[];
  loadOptions: (keyword: string, signal: AbortSignal) => Promise<readonly T[]>;
  getValue: (option: T) => string;
  getLabel: (option: T) => string;
  onChange: (option: T | null) => void;
  dataQueryFilter?: string;
}) {
  const controlId = `remote-select-${useId().replace(/:/g, "-")}`;
  const labelId = `${controlId}-label`;
  const descriptionId = description ? `${controlId}-description` : undefined;
  const [keyword, setKeyword] = useState("");
  const debouncedKeyword = useDebouncedValue(keyword.trim(), 300);
  const optionsQuery = useQuery({
    queryKey: [...queryKey, { keyword: debouncedKeyword }],
    queryFn: ({ signal }) => loadOptions(debouncedKeyword, signal),
    enabled: !disabled,
    staleTime: 2 * 60 * 1000,
    placeholderData: keepPreviousData,
  });
  const options = useMemo(() => {
    const byValue = new Map<string, T>();
    if (selectedOption) {
      byValue.set(getValue(selectedOption), selectedOption);
    }
    for (const option of optionsQuery.data ?? []) {
      byValue.set(getValue(option), option);
    }
    return [...byValue.values()];
  }, [getValue, optionsQuery.data, selectedOption]);
  const hasCurrentOption = options.some((option) => getValue(option) === value);
  const classes = ["remote-select-field", disabled ? "form-field-disabled" : "", className ?? ""]
    .filter(Boolean)
    .join(" ");

  return (
    <div className={classes}>
      <span id={labelId} className="form-field-label"><span>{label}</span></span>
      <div className="remote-select-controls">
        <input
          type="search"
          value={keyword}
          disabled={disabled}
          placeholder={searchPlaceholder}
          aria-label={`${label}检索`}
          onChange={(event) => setKeyword(event.target.value)}
        />
        <select
          id={controlId}
          value={value}
          disabled={disabled}
          aria-labelledby={labelId}
          aria-describedby={descriptionId}
          aria-busy={optionsQuery.isFetching}
          data-query-filter={dataQueryFilter}
          onChange={(event) => {
            const nextValue = event.target.value;
            if (!nextValue) {
              onChange(null);
              return;
            }
            const nextOption = options.find((option) => getValue(option) === nextValue);
            if (nextOption) {
              onChange(nextOption);
            }
          }}
        >
          <option value="">{emptyLabel}</option>
          {value && !hasCurrentOption ? <option value={value}>{selectedLabel || `当前选择 ${value}`}</option> : null}
          {options.map((option) => {
            const optionValue = getValue(option);
            return <option key={optionValue} value={optionValue}>{getLabel(option) || "-"}</option>;
          })}
        </select>
      </div>
      {optionsQuery.isError ? <small className="remote-select-status error" role="alert">资料检索失败，请稍后重试。</small> : null}
      {!optionsQuery.isError && optionsQuery.isFetching ? <small className="remote-select-status">正在检索…</small> : null}
      {description ? <small id={descriptionId} className="form-field-description">{description}</small> : null}
    </div>
  );
}
