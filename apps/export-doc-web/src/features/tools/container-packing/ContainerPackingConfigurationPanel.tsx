import { Save, Trash2 } from "lucide-react";
import type { ApiContainerTypeDto } from "../../../api/index.ts";
import type { ContainerPackingFormState, ContainerPackingRulesFormState } from "./containerPackingModel.ts";

type Props = {
  canDeleteContainerType: boolean;
  canSaveContainerType: boolean;
  container: ContainerPackingFormState;
  containerTypes: ApiContainerTypeDto[];
  isDeletingContainerType: boolean;
  isSavingContainerType: boolean;
  rules: ContainerPackingRulesFormState;
  onApplyContainerType(value: string): void;
  onContainerFieldChange(field: keyof ContainerPackingFormState, value: string): void;
  onDeleteContainerType(): void;
  onRulesFieldChange<K extends keyof ContainerPackingRulesFormState>(
    field: K,
    value: ContainerPackingRulesFormState[K],
  ): void;
  onSaveContainerType(): void;
};

export function ContainerPackingConfigurationPanel({
  canDeleteContainerType,
  canSaveContainerType,
  container,
  containerTypes,
  isDeletingContainerType,
  isSavingContainerType,
  rules,
  onApplyContainerType,
  onContainerFieldChange,
  onDeleteContainerType,
  onRulesFieldChange,
  onSaveContainerType,
}: Props) {
  return (
    <div className="job-tool-grid container-packing-grid">
      <div className="job-tool-stack">
        <div className="container-packing-field-grid">
          <label>
            <span>柜型</span>
            <input
              list="container-packing-type-options"
              value={container.containerType}
              onChange={(event) => onApplyContainerType(event.target.value)}
            />
            <datalist id="container-packing-type-options">
              {containerTypes.map((type) => <option key={type.id} value={type.name} />)}
            </datalist>
          </label>
          <ContainerField label="柜长 cm" value={container.length} onChange={(value) => onContainerFieldChange("length", value)} />
          <ContainerField label="柜宽 cm" value={container.width} onChange={(value) => onContainerFieldChange("width", value)} />
          <ContainerField label="柜高 cm" value={container.height} onChange={(value) => onContainerFieldChange("height", value)} />
          <ContainerField label="体积 CBM" inputMode="decimal" value={container.volume} onChange={(value) => onContainerFieldChange("volume", value)} />
          <ContainerField label="限重 kg" inputMode="decimal" value={container.maxWeight} onChange={(value) => onContainerFieldChange("maxWeight", value)} />
        </div>
        <div className="container-packing-type-actions">
          <button
            className="command-button secondary"
            type="button"
            disabled={!canSaveContainerType || isSavingContainerType}
            onClick={onSaveContainerType}
          >
            <Save size={16} aria-hidden="true" />
            <span>保存柜型</span>
          </button>
          <button
            className="command-button secondary danger"
            type="button"
            disabled={!canDeleteContainerType || isDeletingContainerType}
            onClick={onDeleteContainerType}
          >
            <Trash2 size={16} aria-hidden="true" />
            <span>删除柜型</span>
          </button>
        </div>
      </div>

      <div className="job-tool-stack">
        <div className="container-packing-rules">
          <RuleToggle label="允许旋转" checked={rules.allowRotation} onChange={(value) => onRulesFieldChange("allowRotation", value)} />
          <RuleToggle label="托盘约束" checked={rules.usePalletConstraints} onChange={(value) => onRulesFieldChange("usePalletConstraints", value)} />
          <RuleToggle label="重心约束" checked={rules.enforceCenterOfGravity} onChange={(value) => onRulesFieldChange("enforceCenterOfGravity", value)} />
          <RuleToggle label="同底堆叠" checked={rules.requireSameFootprintStacking} onChange={(value) => onRulesFieldChange("requireSameFootprintStacking", value)} />
        </div>
        <div className="container-packing-field-grid container-packing-rules-grid">
          <ContainerField label="托盘长" value={rules.defaultPalletLength} disabled={!rules.usePalletConstraints} onChange={(value) => onRulesFieldChange("defaultPalletLength", value)} />
          <ContainerField label="托盘宽" value={rules.defaultPalletWidth} disabled={!rules.usePalletConstraints} onChange={(value) => onRulesFieldChange("defaultPalletWidth", value)} />
          <ContainerField label="托盘高" value={rules.defaultPalletHeight} disabled={!rules.usePalletConstraints} onChange={(value) => onRulesFieldChange("defaultPalletHeight", value)} />
          <ContainerField label="托盘重" inputMode="decimal" value={rules.defaultPalletWeight} disabled={!rules.usePalletConstraints} onChange={(value) => onRulesFieldChange("defaultPalletWeight", value)} />
          <ContainerField label="重心偏差 %" inputMode="decimal" value={rules.centerOfGravityTolerancePercent} disabled={!rules.enforceCenterOfGravity} onChange={(value) => onRulesFieldChange("centerOfGravityTolerancePercent", value)} />
          <ContainerField label="支撑 %" inputMode="decimal" value={rules.minimumSupportAreaPercent} onChange={(value) => onRulesFieldChange("minimumSupportAreaPercent", value)} />
        </div>
      </div>
    </div>
  );
}

function ContainerField({
  disabled = false,
  inputMode = "numeric",
  label,
  value,
  onChange,
}: {
  disabled?: boolean;
  inputMode?: "decimal" | "numeric";
  label: string;
  value: string;
  onChange(value: string): void;
}) {
  return (
    <label>
      <span>{label}</span>
      <input
        inputMode={inputMode}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
      />
    </label>
  );
}

function RuleToggle({ label, checked, onChange }: { label: string; checked: boolean; onChange(value: boolean): void }) {
  return (
    <label className="toggle-field">
      <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} />
      <span>{label}</span>
    </label>
  );
}
