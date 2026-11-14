import type { ApiBackupS3Settings } from "../../api/index.ts";
import { SelectField } from "../../ui/FormFields.tsx";

export function S3AddressingField({ value, onChange }: {
  value: ApiBackupS3Settings["addressing"];
  onChange: (value: ApiBackupS3Settings["addressing"]) => void;
}) {
  return <SelectField label="寻址方式" value={value} includeEmptyOption={false} options={[
    { value: "virtualHost", label: "虚拟主机（AWS / OSS / COS）" },
    { value: "path", label: "路径式（MinIO / Ceph / 兼容服务）" },
    { value: "bucketHost", label: "已绑定桶的域名（CNAME）" },
  ]} onChange={value => onChange(value as ApiBackupS3Settings["addressing"])} />;
}
