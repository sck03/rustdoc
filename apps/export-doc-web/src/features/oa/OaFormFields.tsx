import { useMemo } from "react";
import type { ExportDocManagerApiClient, OaFormDefinition, OaFormField, OaFormValues } from "../../api/index.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import { Button } from "../../ui/Button.tsx";
import { OaFormReferenceInput } from "./OaFormReferenceInput.tsx";
import { formReferenceLabels } from "./oaFormModel.ts";

type FormRow = Record<string, string>;
type ReferenceContext = {
  client?: ExportDocManagerApiClient;
  company?: string;
  referenceLabels?: ReadonlyMap<string, string>;
};

function visibleFormField(field: OaFormField, values: OaFormValues) {
  return !field.visibleWhen || values[field.visibleWhen.field] === field.visibleWhen.equals;
}

function cleanFormValues(form: OaFormDefinition, values: OaFormValues): OaFormValues {
  const visible = new Set(form.fields.filter((field) => visibleFormField(field, values)).map((field) => field.key));
  return Object.fromEntries(Object.entries(values).filter(([key]) => visible.has(key)));
}

export function OaFormFields({
  form,
  values,
  displayValues,
  referenceValues,
  onChange,
  client,
  company,
  required = false,
}: ReferenceContext & {
  form: OaFormDefinition;
  values: OaFormValues;
  displayValues?: OaFormValues;
  referenceValues?: OaFormValues;
  onChange?: (values: OaFormValues) => void;
  required?: boolean;
}) {
  const patch = (key: string, value: OaFormValues[string]) =>
    onChange?.(cleanFormValues(form, { ...values, [key]: value }));
  const referenceLabels = useMemo(
    () => formReferenceLabels(form, referenceValues ?? values, displayValues ?? {}),
    [form, referenceValues, values, displayValues],
  );
  const context = { client, company, required, referenceLabels };
  return (
    <div className="office-form-grid office-field-wide">
      {form.fields
        .filter((field) => visibleFormField(field, values))
        .map((field) => {
          const value = values[field.key];
          const display = displayValues?.[field.key];
          if (field.kind !== "Table")
            return (
              <Scalar
                key={field.key}
                field={field}
                {...context}
                value={!onChange && typeof display === "string" ? display : typeof value === "string" ? value : ""}
                onChange={onChange ? (value) => patch(field.key, value) : undefined}
              />
            );
          const rows: FormRow[] = Array.isArray(value) ? value : [];
          const displayRows: FormRow[] = Array.isArray(display) ? display : [];
          return (
            <section className="office-field-wide" key={field.key} aria-label={field.label}>
              <h3>{field.label}</h3>
              {rows.map((row, index) => (
                <fieldset key={index} className="office-resource-card">
                  <legend>第 {index + 1} 行</legend>
                  {field.columns?.map((column) => (
                    <Scalar
                      key={column.key}
                      field={column}
                      {...context}
                      value={(!onChange ? displayRows[index]?.[column.key] : undefined) ?? row[column.key] ?? ""}
                      onChange={
                        onChange
                          ? (value) =>
                              patch(
                                field.key,
                                rows.map((r, i) => (i === index ? { ...r, [column.key]: value } : r)),
                              )
                          : undefined
                      }
                    />
                  ))}
                  {onChange && (
                    <Button
                      variant="danger"
                      onClick={() =>
                        patch(
                          field.key,
                          rows.filter((_, i) => i !== index),
                        )
                      }
                    >
                      移除此行
                    </Button>
                  )}
                </fieldset>
              ))}
              {onChange && (
                <Button disabled={rows.length >= 100} onClick={() => patch(field.key, [...rows, {}])}>
                  添加明细行
                </Button>
              )}
            </section>
          );
        })}
    </div>
  );
}

function Scalar({
  field,
  value,
  onChange,
  client,
  company,
  required,
  referenceLabels,
}: ReferenceContext & {
  field: OaFormField;
  value: string;
  onChange?: (value: string) => void;
  required: boolean;
}) {
  const label = field.label + (field.kind === "Money" ? `（${field.currency}）` : "");
  if (!onChange)
    return (
      <dl className="oa-facts">
        <div>
          <dt>{label}</dt>
          <dd>{value || "—"}</dd>
        </div>
      </dl>
    );
  const mandatory = required && Boolean(field.required);
  if ((field.kind === "Person" || field.kind === "Department") && client && company)
    return (
      <OaFormReferenceInput
        client={client}
        company={company}
        kind={field.kind}
        label={label}
        value={value}
        displayValue={referenceLabels?.get(`${field.kind}:${value}`)}
        required={mandatory}
        onChange={onChange}
      />
    );
  return (
    <OfficeField label={label} wide={field.kind === "Multiline"}>
      {field.kind === "Select" ? (
        <select required={mandatory} value={value} onChange={(e) => onChange(e.target.value)}>
          <option value="">请选择</option>
          {field.options?.map((option) => (
            <option key={option}>{option}</option>
          ))}
        </select>
      ) : field.kind === "Multiline" ? (
        <textarea maxLength={2000} required={mandatory} value={value} onChange={(e) => onChange(e.target.value)} />
      ) : (
        <input
          type={field.kind === "Date" ? "date" : "text"}
          inputMode={field.kind === "Decimal" || field.kind === "Money" ? "decimal" : undefined}
          maxLength={200}
          required={mandatory}
          value={value}
          onChange={(e) => onChange(e.target.value)}
        />
      )}
    </OfficeField>
  );
}
