import type { ApiHsCodeVersionReference } from "../../api/index.ts";
import type { MasterDataRecord } from "./masterDataTypes.ts";
import { readString } from "./masterDataModel.ts";

export function hsCodeVersionReference(record: MasterDataRecord): ApiHsCodeVersionReference {
  const rowVersion = readString(record, "rowVersion");
  if (!Number.isSafeInteger(record.id) || record.id <= 0 || !rowVersion) {
    throw new Error("编码编号或版本缺失，请重新读取后选择。");
  }
  return { id: record.id, rowVersion };
}
