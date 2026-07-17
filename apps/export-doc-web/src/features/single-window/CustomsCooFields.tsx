import { useId } from "react";
import type { ApiCustomsCooItemDto, ApiCustomsCooOptionDto } from "../../api/index.ts";
import { FieldShell } from "../../ui/FormFields.tsx";
import { buildCooSelectOptions, normalizeCooOptions, normalizeText } from "./customsCooModel.ts";

export function CooSelectField({
  label,
  value,
  options,
  disabled,
  onChange,
}: {
  label: string;
  value?: string;
  options: ApiCustomsCooOptionDto[];
  disabled?: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <FieldShell label={label} disabled={disabled}>
      {(descriptionId) => (
      <select
        value={value ?? ""}
        disabled={disabled}
        aria-describedby={descriptionId}
        onChange={(event) => onChange(event.target.value)}
      >
        {buildCooSelectOptions(options, value).map((option) => (
          <option key={`${option.value}-${option.label}`} value={option.value}>
            {option.label || option.value || "未选择"}
          </option>
        ))}
      </select>
      )}
    </FieldShell>
  );
}

export function CooDatalistField({
  label,
  value,
  options,
  disabled,
  onChange,
}: {
  label: string;
  value?: string;
  options: ApiCustomsCooOptionDto[];
  disabled?: boolean;
  onChange: (value: string) => void;
}) {
  const listId = `coo-datalist-${useId().replace(/:/g, "-")}`;
  const normalizedOptions = normalizeCooOptions(options).filter((option) => option.value);

  return (
    <FieldShell label={label} disabled={disabled}>
      {(descriptionId) => (
      <>
      <input
        list={normalizedOptions.length > 0 ? listId : undefined}
        value={value ?? ""}
        disabled={disabled}
        aria-describedby={descriptionId}
        onChange={(event) => onChange(event.target.value)}
      />
      {normalizedOptions.length > 0 ? (
        <datalist id={listId}>
          {normalizedOptions.map((option) => (
            <option key={`${option.value}-${option.label}`} value={option.value} label={option.label} />
          ))}
        </datalist>
      ) : null}
      </>
      )}
    </FieldShell>
  );
}

export function CooItemSelectInput({
  ariaLabel,
  value,
  options,
  disabled,
  onChange,
}: {
  ariaLabel: string;
  value?: string;
  options: ApiCustomsCooOptionDto[];
  disabled?: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <select
      className="item-cell-input"
      aria-label={ariaLabel}
      value={value ?? ""}
      disabled={disabled}
      onChange={(event) => onChange(event.target.value)}
    >
      {buildCooSelectOptions(options, value).map((option) => (
        <option key={`${option.value}-${option.label}`} value={option.value}>
          {option.label || option.value || "-"}
        </option>
      ))}
    </select>
  );
}

export function CooItemDatalistInput({
  ariaLabel,
  value,
  options,
  disabled,
  onChange,
}: {
  ariaLabel: string;
  value?: string;
  options: ApiCustomsCooOptionDto[];
  disabled?: boolean;
  onChange: (value: string) => void;
}) {
  const listId = `coo-item-datalist-${useId().replace(/:/g, "-")}`;
  const normalizedOptions = normalizeCooOptions(options).filter((option) => option.value);

  return (
    <>
      <input
        className="item-cell-input"
        aria-label={ariaLabel}
        list={normalizedOptions.length > 0 ? listId : undefined}
        value={value ?? ""}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
      />
      {normalizedOptions.length > 0 ? (
        <datalist id={listId}>
          {normalizedOptions.map((option) => (
            <option key={`${option.value}-${option.label}`} value={option.value} label={option.label} />
          ))}
        </datalist>
      ) : null}
    </>
  );
}

export function buildCooItemPreviousValueOptions(
  items: ApiCustomsCooItemDto[],
  rowIndex: number,
  field: keyof ApiCustomsCooItemDto,
) {
  const options: ApiCustomsCooOptionDto[] = [];
  const seen = new Set<string>();

  for (let index = rowIndex - 1; index >= 0 && options.length < 12; index -= 1) {
    const rawValue = items[index]?.[field];
    if (typeof rawValue !== "string" && typeof rawValue !== "number") {
      continue;
    }

    const value = normalizeText(String(rawValue));
    const key = value.toUpperCase();
    if (!value || seen.has(key)) {
      continue;
    }

    options.push({ value, label: value });
    seen.add(key);
  }

  return options;
}

export function mergeCooDatalistOptions(...optionGroups: ApiCustomsCooOptionDto[][]) {
  const merged: ApiCustomsCooOptionDto[] = [];
  const seen = new Set<string>();

  for (const optionGroup of optionGroups) {
    for (const option of normalizeCooOptions(optionGroup)) {
      const value = normalizeText(option.value);
      const key = value.toUpperCase();
      if (!value || seen.has(key)) {
        continue;
      }

      merged.push({ value, label: normalizeText(option.label) || value });
      seen.add(key);
    }
  }

  return merged;
}
