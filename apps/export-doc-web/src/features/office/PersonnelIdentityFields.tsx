import { useState } from "react";
import type { PersonnelProfile } from "../../api/index.ts";
import { OfficeField } from "./OfficeUi.tsx";

export function PersonnelIdentityFields({ profile }: { profile?: PersonnelProfile }) {
  const [longTerm, setLongTerm] = useState(profile?.identityLongTerm ?? false);
  const [validFrom, setValidFrom] = useState(profile?.identityValidFrom ?? "");
  return <>
    <OfficeField label="居民身份证号码（选填）" wide><input name="identityNumber" maxLength={18} autoComplete="off"
      placeholder="18 位号码，末位可以是 X" defaultValue={profile?.identityNumber ?? ""} /></OfficeField>
    <OfficeField label="签发机关"><input name="identityAuthority" maxLength={120} defaultValue={profile?.identityAuthority ?? ""} /></OfficeField>
    <OfficeField label="有效起始日"><input name="identityValidFrom" type="date" min="1900-01-01" value={validFrom} onChange={(event) => setValidFrom(event.target.value)} /></OfficeField>
    <OfficeField label="有效截止日"><input name="identityValidUntil" type="date" min={validFrom || "1900-01-01"}
      disabled={longTerm} defaultValue={profile?.identityValidUntil ?? ""} /></OfficeField>
    <label className="checkbox-field"><input type="checkbox" name="identityLongTerm" checked={longTerm} onChange={(event) => setLongTerm(event.target.checked)} />身份证长期有效</label>
    <OfficeField label="身份证住址" wide><textarea name="registeredAddress" rows={2} maxLength={300} defaultValue={profile?.registeredAddress ?? ""} /></OfficeField>
  </>;
}
