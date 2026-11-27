import type { ApiBackupS3Settings } from "../../api/index.ts";
import { TextField, FieldShell } from "../../ui/FormFields.tsx";
import { S3AddressingField } from "./S3AddressingField.tsx";
export const emptyRecoveryS3: ApiBackupS3Settings = { endpoint: "", bucket: "", region: "us-east-1", prefix: "", accessKeyId: "", addressing: "path" };
export function RecoveryConnectionFields({ value, onChange, secret, onSecret, token, onToken }: {
  value: ApiBackupS3Settings; onChange: (value: ApiBackupS3Settings) => void;
  secret: string; onSecret: (value: string) => void; token: string; onToken: (value: string) => void;
}) {
  return <div className="field-grid">
    {([["endpoint", "服务地址"], ["bucket", "存储桶"], ["region", "区域"], ["prefix", "对象前缀"], ["accessKeyId", "Access Key ID"]] as const).map(([key, label]) => <TextField key={key} label={label} value={value[key]} onChange={text => onChange({ ...value, [key]: text })} />)}
    <S3AddressingField value={value.addressing} onChange={addressing => onChange({ ...value, addressing })} />
    <FieldShell label="Secret Access Key">{id => <input type="password" aria-label="Secret Access Key" aria-describedby={id} autoComplete="new-password" value={secret} onChange={event => onSecret(event.target.value)} />}</FieldShell>
    <FieldShell label="会话令牌（可选）">{id => <input type="password" aria-label="会话令牌（可选）" aria-describedby={id} autoComplete="new-password" value={token} onChange={event => onToken(event.target.value)} />}</FieldShell>
  </div>;
}
