import { FolderOpen } from "lucide-react";
import { isDesktopBridgeAvailable } from "../../desktop/desktopBridge.ts";
import { EditableComboField,NumberField,TextAreaField,TextField } from "../../ui/FormFields.tsx";
import { PathField } from "../../ui/PathField.tsx";
import type { CustomOptionMap } from "../custom-options/customOptionModel.ts";
import { getCustomOptions } from "../custom-options/customOptionModel.ts";
import {
isProductUnitField,
isProductUnitSourceField,
readProductInputAssistanceOptions,
readProductUnitFieldOptions,
readRecordNumber,
readString,
} from "./masterDataModel.ts";
import {
type MasterDataFieldDefinition,
type MasterDataRecord,
type ProductAssistanceField,
type ProductInputAssistance,
type ProductUnitAssistance,
type ProductUnitSourceField,
} from "./masterDataTypes.ts";

export function MasterDataField({
  field,
  isEdit,
  record,
  customOptions,
  productInputAssistance,
  productUnitAssistance,
  onChange,
  onCommitCustomOption,
  onCommitProductAssistance,
  onCommitProductUnit,
  onSelectPath,
}: {
  field: MasterDataFieldDefinition;
  isEdit: boolean;
  record: MasterDataRecord;
  customOptions: CustomOptionMap;
  productInputAssistance: ProductInputAssistance;
  productUnitAssistance: ProductUnitAssistance;
  onChange: (value: string | number) => void;
  onCommitCustomOption: (optionType: string, value: string) => void;
  onCommitProductAssistance: (field: ProductAssistanceField, value: string) => void;
  onCommitProductUnit: (sourceField: ProductUnitSourceField) => void;
  onSelectPath: () => void;
}) {
  const disabled = Boolean(field.readOnlyOnEdit && isEdit);

  if (field.type === "number") {
    return (
      <NumberField
        className={field.className}
        disabled={disabled}
        label={field.label}
        required={field.required}
        value={readRecordNumber(record, field.name)}
        onChange={onChange}
      />
    );
  }

  if (field.type === "textarea") {
    return (
      <TextAreaField
        className={field.className}
        disabled={disabled}
        label={field.label}
        required={field.required}
        value={readString(record, field.name)}
        onChange={onChange}
      />
    );
  }

  if (field.pathPicker) {
    return (
      <PathField
        disabled={disabled}
        label={field.label}
        value={readString(record, field.name)}
        onChange={onChange}
        actions={
          isDesktopBridgeAvailable() ? (
            <button className="icon-button compact-icon-button" type="button" title="选择图片" disabled={disabled} onClick={onSelectPath}>
              <FolderOpen size={15} aria-hidden="true" />
            </button>
          ) : null
        }
      />
    );
  }

  if (field.customOptionType) {
    return (
      <EditableComboField
        className={field.className}
        disabled={disabled}
        label={field.label}
        required={field.required}
        value={readString(record, field.name)}
        options={getCustomOptions(customOptions, field.customOptionType)}
        onChange={onChange}
        onCommit={(value) => onCommitCustomOption(field.customOptionType ?? "", value)}
      />
    );
  }

  if (field.productAssistanceField) {
    return (
      <EditableComboField
        className={field.className}
        disabled={disabled}
        label={field.label}
        required={field.required}
        value={readString(record, field.name)}
        options={readProductInputAssistanceOptions(field.productAssistanceField, productInputAssistance)}
        transformValue={field.productAssistanceField === "hsCode" ? (value) => value.toUpperCase() : undefined}
        onChange={onChange}
        onCommit={(value) => onCommitProductAssistance(field.productAssistanceField ?? "productCode", value)}
      />
    );
  }

  if (isProductUnitField(field.name)) {
    const productUnitSourceField = isProductUnitSourceField(field.name) ? field.name : null;
    return (
      <EditableComboField
        className={field.className}
        disabled={disabled}
        label={field.label}
        required={field.required}
        value={readString(record, field.name)}
        options={readProductUnitFieldOptions(field.name, productUnitAssistance)}
        transformValue={productUnitSourceField ? (value) => value.toUpperCase() : undefined}
        onChange={onChange}
        onCommit={productUnitSourceField ? () => onCommitProductUnit(productUnitSourceField) : undefined}
      />
    );
  }

  return (
    <TextField
      className={field.className}
      disabled={disabled}
      label={field.label}
      required={field.required}
      value={readString(record, field.name)}
      onChange={onChange}
    />
  );
}

