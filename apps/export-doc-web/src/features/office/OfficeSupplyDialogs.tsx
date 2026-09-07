import { useState, type FormEvent } from "react";
import type { ApiUserDto, ExportDocManagerApiClient, OfficeSupplyRecord, PersonnelDirectoryRecord } from "../../api/index.ts";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { readOfficeSupplyForm } from "./officeModel.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "./OfficeUi.tsx";
import { useOfficeOperation } from "./useOfficeData.ts";
import { OfficeEmployeePicker } from "./OfficeEmployeePicker.tsx";

export function OfficeSupplyEditor({ client, supply, onClose }: { client: ExportDocManagerApiClient; supply?: OfficeSupplyRecord; onClose: () => void }) {
  const operation = useOfficeOperation();
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const body = readOfficeSupplyForm(new FormData(event.currentTarget), supply?.versionNumber ?? 0);
    void operation.run((signal) => supply ? client.updateOfficeSupply({ id: supply.id, body }, { signal }) : client.createOfficeSupply({ body }, { signal }), onClose);
  }
  return <OfficeDialog title={supply ? "编辑物品" : "添加物品"} onClose={onClose} {...operation} protectChanges>
    <form onSubmit={submit}><fieldset disabled={operation.busy} className="office-form-grid">
      <OfficeField label="物品名称" wide><input name="name" required maxLength={120} defaultValue={supply?.name} /></OfficeField>
      <OfficeField label="计量单位"><input name="unit" required maxLength={20} defaultValue={supply?.unit ?? "件"} placeholder="支、本、盒、件" /></OfficeField>
      <OfficeField label="最低可用库存"><input name="minimumStock" type="number" required min={0} max={1000000} defaultValue={supply?.minimumStock ?? 0} /></OfficeField>
      <OfficeField label="存放／领取位置" wide><input name="location" maxLength={200} defaultValue={supply?.location} /></OfficeField>
      <OfficeField label="说明" wide><textarea name="description" rows={3} maxLength={500} defaultValue={supply?.description} /></OfficeField>
      <label className="checkbox-field"><input type="checkbox" name="isReturnable" defaultChecked={supply?.isReturnable ?? false} />借用后需要归还</label>
      <label className="checkbox-field"><input type="checkbox" name="isActive" defaultChecked={supply?.isActive ?? true} />启用物品</label>
      <p className="office-muted office-field-wide">保存后使用“补充库存”登记入库。产生申请后，计量单位与归还类型保持固定。</p>
    </fieldset><OfficeSubmit busy={operation.busy} /></form>
  </OfficeDialog>;
}

export function OfficeSupplyApplication({ client, supply, user, onClose }: {
  client: ExportDocManagerApiClient; supply: OfficeSupplyRecord; user: ApiUserDto; onClose: () => void;
}) {
  const [requestKey] = useState(createRequestKey);
  const [employee, setEmployee] = useState<PersonnelDirectoryRecord | null>(null);
  const register = user.capabilities.usesOfficeRegister;
  const operation = useOfficeOperation();
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    void operation.run((signal) => client.createOfficeSupplyRequest({ body: {
      requestKey, officeSupplyId: supply.id, quantity: Number(form.get("quantity")), purpose: String(form.get("purpose") ?? ""),
      returnDueDate: supply.isReturnable ? String(form.get("returnDueDate") ?? "") : null,
      employeeId: employee?.id ?? null,
    } }, { signal }), onClose);
  }
  return <OfficeDialog title={`${register ? "登记" : "申请"}${supply.isReturnable ? "借用" : "领用"} · ${supply.name}`} onClose={onClose} {...operation} protectChanges>
    <p>当前可用 {supply.availableQuantity} {supply.unit} · {supply.location || "请向行政管理员确认领取位置"}</p>
    <p className="office-muted">{register ? "登记后预留库存，实际交接时再确认发放；库存不足请先补充。" : "申请由管理员审批后发放。库存不足时，可先提交申请，待补充后审批。"}</p>
    <form onSubmit={submit}><fieldset disabled={operation.busy} className="office-form-grid">
      {register && <OfficeEmployeePicker client={client} user={user} value={employee} onChange={setEmployee} disabled={operation.busy} />}
      <OfficeField label={`申请数量（${supply.unit}）`}><input name="quantity" type="number" required min={1} max={1000000} defaultValue={1} /></OfficeField>
      {supply.isReturnable && <OfficeField label="预计归还日期"><input name="returnDueDate" type="date" required min={user.businessDate} defaultValue={user.businessDate} /></OfficeField>}
      <OfficeField label="领用用途" wide><textarea name="purpose" rows={3} required maxLength={500} placeholder="请说明使用用途" /></OfficeField>
    </fieldset><OfficeSubmit busy={operation.busy} disabled={register && (!employee || supply.availableQuantity < 1)} label={register ? "登记领用" : "提交申请"} /></form>
  </OfficeDialog>;
}

export function OfficeStockDialog({ client, supply, stocktake, onClose }: {
  client: ExportDocManagerApiClient; supply: OfficeSupplyRecord; stocktake: boolean; onClose: () => void;
}) {
  const [operationId] = useState(createRequestKey);
  const operation = useOfficeOperation();
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const input = { id: supply.id, body: { operationId, expectedVersion: supply.versionNumber, quantity: Number(form.get("quantity")), note: String(form.get("note") ?? "") } };
    void operation.run((signal) => stocktake ? client.stocktakeOfficeSupply(input, { signal }) : client.restockOfficeSupply(input, { signal }), onClose);
  }
  return <OfficeDialog title={`${stocktake ? "库存盘点" : "补充库存"} · ${supply.name}`} onClose={onClose} {...operation} protectChanges>
    <p>当前在库 {supply.stockQuantity} {supply.unit} · 已预留 {supply.reservedQuantity} {supply.unit}</p>
    {stocktake && <p className="office-muted">填写实际在库数量，系统记录差额。盘减不能占用已经审批预留的库存。</p>}
    <form onSubmit={submit}><fieldset disabled={operation.busy} className="office-form-grid">
      <OfficeField label={`${stocktake ? "盘点后在库数量" : "本次补充数量"}（${supply.unit}）`} wide><input name="quantity" type="number" required min={stocktake ? 0 : 1} max={1000000} defaultValue={stocktake ? supply.stockQuantity : 1} /></OfficeField>
      <OfficeField label={stocktake ? "盘点调整原因" : "入库说明／采购单号"} wide><textarea name="note" rows={3} required maxLength={500} /></OfficeField>
    </fieldset><OfficeSubmit busy={operation.busy} label={stocktake ? "确认盘点" : "确认入库"} /></form>
  </OfficeDialog>;
}
