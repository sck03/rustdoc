import type { ExportDocManagerApiClient, OaFormDefinition, OaFormField } from "../../api/index.ts";
import { newFormField } from "./oaFormModel.ts";
import { OaFormDepartmentScope } from "./OaFormDepartmentScope.tsx";
import { OfficeField } from "../office/OfficeUi.tsx";
import { Button } from "../../ui/Button.tsx";
import { CollapsibleSection } from "../../ui/primitives/Card.tsx";

const formKinds: Record<OaFormField["kind"], string> = {
  Text: "单行文字",
  Multiline: "多行文字",
  Decimal: "数字",
  Money: "金额",
  Date: "日期",
  Select: "单选",
  Person: "人员",
  Department: "部门",
  Table: "明细表",
};

export function OaFormDesigner({
  client,
  company,
  value,
  onChange,
}: {
  client: ExportDocManagerApiClient;
  company: string;
  value: OaFormDefinition;
  onChange: (value: OaFormDefinition) => void;
}) {
  return (
    <section aria-label="申请表单设计">
      <div className="office-form-grid">
        <OfficeField label="事项分类">
          <input
            required
            maxLength={50}
            value={value.category}
            onChange={(e) => onChange({ ...value, category: e.target.value })}
          />
        </OfficeField>
        <OaFormDepartmentScope
          client={client}
          company={company}
          value={value.departmentIds ?? []}
          onChange={(departmentIds) => onChange({ ...value, departmentIds })}
        />
        <label className="checkbox-field">
          <input
            type="checkbox"
            checked={value.attachmentRequired ?? false}
            onChange={(e) => onChange({ ...value, attachmentRequired: e.target.checked })}
          />
          提交时必须上传附件
        </label>
      </div>
      <Fields value={value.fields} onChange={(fields) => onChange({ ...value, fields })} />
    </section>
  );
}

function Fields({
  value,
  onChange,
  nested = false,
}: {
  value: OaFormField[];
  onChange: (value: OaFormField[]) => void;
  nested?: boolean;
}) {
  const patch = (index: number, change: Partial<OaFormField>) =>
    onChange(value.map((f, i) => (i === index ? { ...f, ...change } : f)));
  const move = (index: number, delta: number) => {
    const next = [...value];
    [next[index], next[index + delta]] = [next[index + delta], next[index]];
    onChange(next);
  };
  const changeKind = (index: number, kind: OaFormField["kind"]) =>
    patch(index, {
      kind,
      options: kind === "Select" ? ["选项一"] : [],
      currency: kind === "Money" ? "CNY" : "",
      columns: kind === "Table" ? [newFormField()] : [],
    });
  return (
    <>
      <div className="office-resource-grid">
        {value.map((field, index) => (
          <fieldset key={field.key} className="office-resource-card">
            <legend>{field.label || `字段 ${index + 1}`}</legend>
            <OfficeField label="字段名称">
              <input
                required
                maxLength={100}
                value={field.label}
                onChange={(e) => patch(index, { label: e.target.value })}
              />
            </OfficeField>
            <OfficeField label="字段类型">
              <select value={field.kind} onChange={(e) => changeKind(index, e.target.value as OaFormField["kind"])}>
                {Object.entries(formKinds)
                  .filter(([kind]) => !nested || kind !== "Table")
                  .map(([key, label]) => (
                    <option key={key} value={key}>
                      {label}
                    </option>
                  ))}
              </select>
            </OfficeField>
            <label className="checkbox-field">
              <input
                type="checkbox"
                checked={field.required ?? false}
                onChange={(e) => patch(index, { required: e.target.checked })}
              />
              必填
            </label>
            {field.kind === "Select" && (
              <OfficeField label="选项（每行一个）">
                <textarea
                  required
                  value={(field.options ?? []).join("\n")}
                  onChange={(e) => patch(index, { options: e.target.value.split("\n") })}
                />
              </OfficeField>
            )}
            {field.kind === "Money" && (
              <OfficeField label="币种">
                <select value={field.currency ?? "CNY"} onChange={(e) => patch(index, { currency: e.target.value })}>
                  {["CNY", "USD", "EUR", "HKD", "JPY", "GBP"].map((code) => (
                    <option key={code}>{code}</option>
                  ))}
                </select>
              </OfficeField>
            )}
            {!nested && (
              <CollapsibleSection title="显示条件">
                <OfficeField label="依赖字段">
                  <select
                    value={field.visibleWhen?.field ?? ""}
                    onChange={(e) =>
                      patch(index, {
                        visibleWhen: e.target.value ? { field: e.target.value, equals: "" } : null,
                      })
                    }
                  >
                    <option value="">始终显示</option>
                    {value
                      .slice(0, index)
                      .filter((f) => !f.visibleWhen && f.kind !== "Table")
                      .map((f) => (
                        <option key={f.key} value={f.key}>
                          {f.label}
                        </option>
                      ))}
                  </select>
                </OfficeField>
                {field.visibleWhen && (
                  <OfficeField label="等于以下值时显示">
                    <input
                      maxLength={100}
                      value={field.visibleWhen.equals}
                      onChange={(e) =>
                        patch(index, {
                          visibleWhen: {
                            ...field.visibleWhen!,
                            equals: e.target.value,
                          },
                        })
                      }
                    />
                  </OfficeField>
                )}
              </CollapsibleSection>
            )}
            {field.kind === "Table" && (
              <Fields nested value={field.columns ?? []} onChange={(columns) => patch(index, { columns })} />
            )}
            <div className="office-card-actions">
              <Button disabled={index === 0} onClick={() => move(index, -1)}>
                上移
              </Button>
              <Button disabled={index === value.length - 1} onClick={() => move(index, 1)}>
                下移
              </Button>
              <Button variant="danger" onClick={() => onChange(value.filter((f) => f.key !== field.key))}>
                移除字段
              </Button>
            </div>
          </fieldset>
        ))}
      </div>
      <Button disabled={value.length >= (nested ? 10 : 40)} onClick={() => onChange([...value, newFormField()])}>
        {nested ? "添加明细列" : "添加字段"}
      </Button>
    </>
  );
}
