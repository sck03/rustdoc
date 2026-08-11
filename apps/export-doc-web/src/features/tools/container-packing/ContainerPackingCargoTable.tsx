import { Trash2 } from "lucide-react";
import { ResponsiveTableFrame } from "../../../ui/ResponsiveTable.tsx";
import {
  containerPackingZoneOptions,
  type ContainerPackingCargoRow,
  type ContainerPackingZoneValue,
} from "./containerPackingModel.ts";

type Props = {
  cargoRows: ContainerPackingCargoRow[];
  onRemoveCargo(id: string): void;
  onUpdateCargo(id: string, changes: Partial<ContainerPackingCargoRow>): void;
};

export function ContainerPackingCargoTable({ cargoRows, onRemoveCargo, onUpdateCargo }: Props) {
  return (
    <ResponsiveTableFrame className="container-packing-cargo-frame" label="装柜货物清单">
      <table className="container-packing-cargo-table">
        <thead>
          <tr>
            <th>色</th>
            <th>名称</th>
            <th>长</th>
            <th>宽</th>
            <th>高</th>
            <th>重</th>
            <th>数量</th>
            <th>区域</th>
            <th>托盘</th>
            <th>每托</th>
            <th>顶载</th>
            <th>顺序</th>
            <th>组</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          {cargoRows.length === 0 ? (
            <tr>
              <td colSpan={14} className="empty-cell small-empty">货物列表已清空</td>
            </tr>
          ) : cargoRows.map((row) => (
            <tr key={row.id}>
              <td>
                <input
                  className="container-packing-color-input"
                  type="color"
                  aria-label={`${row.name || "货物"}颜色`}
                  value={row.colorHex}
                  onChange={(event) => onUpdateCargo(row.id, { colorHex: event.target.value })}
                />
              </td>
              <CargoTextCell value={row.name} onChange={(value) => onUpdateCargo(row.id, { name: value })} />
              <CargoNumberCell value={row.length} onChange={(value) => onUpdateCargo(row.id, { length: value })} />
              <CargoNumberCell value={row.width} onChange={(value) => onUpdateCargo(row.id, { width: value })} />
              <CargoNumberCell value={row.height} onChange={(value) => onUpdateCargo(row.id, { height: value })} />
              <CargoNumberCell value={row.weight} onChange={(value) => onUpdateCargo(row.id, { weight: value })} />
              <CargoNumberCell inputMode="numeric" value={row.quantity} onChange={(value) => onUpdateCargo(row.id, { quantity: value })} />
              <td>
                <select
                  className="item-cell-input"
                  value={row.preferredZone}
                  onChange={(event) => onUpdateCargo(row.id, { preferredZone: event.target.value as ContainerPackingZoneValue })}
                >
                  {containerPackingZoneOptions.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
              </td>
              <td className="container-packing-check-cell">
                <input
                  type="checkbox"
                  aria-label={`${row.name || "货物"}使用托盘`}
                  checked={row.usePallet}
                  onChange={(event) => onUpdateCargo(row.id, { usePallet: event.target.checked })}
                />
              </td>
              <CargoNumberCell
                inputMode="numeric"
                value={row.unitsPerPallet}
                disabled={!row.usePallet}
                onChange={(value) => onUpdateCargo(row.id, { unitsPerPallet: value })}
              />
              <CargoNumberCell value={row.maxTopLoadWeight} onChange={(value) => onUpdateCargo(row.id, { maxTopLoadWeight: value })} />
              <CargoNumberCell inputMode="numeric" value={row.loadSequence} onChange={(value) => onUpdateCargo(row.id, { loadSequence: value })} />
              <CargoTextCell value={row.priorityGroup} onChange={(value) => onUpdateCargo(row.id, { priorityGroup: value })} />
              <td>
                <button
                  className="icon-button compact-icon-button"
                  type="button"
                  title="删除货物"
                  aria-label="删除货物"
                  onClick={() => onRemoveCargo(row.id)}
                >
                  <Trash2 size={15} aria-hidden="true" />
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </ResponsiveTableFrame>
  );
}

function CargoTextCell({ value, onChange }: { value: string; onChange(value: string): void }) {
  return (
    <td>
      <input className="item-cell-input" value={value} onChange={(event) => onChange(event.target.value)} />
    </td>
  );
}

function CargoNumberCell({
  disabled = false,
  inputMode = "decimal",
  value,
  onChange,
}: {
  disabled?: boolean;
  inputMode?: "decimal" | "numeric";
  value: string;
  onChange(value: string): void;
}) {
  return (
    <td>
      <input
        className="item-cell-input item-number-input"
        inputMode={inputMode}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
      />
    </td>
  );
}
