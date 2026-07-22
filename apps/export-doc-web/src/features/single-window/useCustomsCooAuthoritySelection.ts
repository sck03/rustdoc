import { useState } from "react";
import type { ApiCustomsCooDocumentDto, ApiSingleWindowIssuingAuthorityOptionDto } from "../../api/index.ts";
import { findIssuingAuthority, normalizeAuthorityCompareText, parseIssuingAuthorityCode } from "./customsCooModel.ts";

type AutoState = { fetchPlace: string; aplAdd: string };
export function useCustomsCooAuthoritySelection(document: ApiCustomsCooDocumentDto | null, options: ApiSingleWindowIssuingAuthorityOptionDto[], patchDocument: (next: Partial<ApiCustomsCooDocumentDto>) => void) {
  const [autoState, setAutoState] = useState<AutoState>({ fetchPlace: "", aplAdd: "" });
  function reset() { setAutoState({ fetchPlace: "", aplAdd: "" }); }
  function changeOrgCode(value: string) {
    if (!document) return;
    const orgCode = parseIssuingAuthorityCode(value, options);
    const authority = findIssuingAuthority(orgCode, options);
    const nextDocument: Partial<ApiCustomsCooDocumentDto> = { orgCode };
    const nextAutoState = { ...autoState };
    if (authority) {
      if (!document.fetchPlace.trim() || normalizeAuthorityCompareText(document.fetchPlace) === normalizeAuthorityCompareText(autoState.fetchPlace)) { nextDocument.fetchPlace = authority.code; nextAutoState.fetchPlace = authority.code; }
      if (authority.applicationAddress && (!document.aplAdd.trim() || normalizeAuthorityCompareText(document.aplAdd) === normalizeAuthorityCompareText(autoState.aplAdd))) { nextDocument.aplAdd = authority.applicationAddress; nextAutoState.aplAdd = authority.applicationAddress; }
    }
    setAutoState(nextAutoState); patchDocument(nextDocument);
  }
  function changeFetchPlace(value: string) { const fetchPlace = parseIssuingAuthorityCode(value, options); if (autoState.fetchPlace && normalizeAuthorityCompareText(fetchPlace) !== normalizeAuthorityCompareText(autoState.fetchPlace)) setAutoState((current) => ({ ...current, fetchPlace: "" })); patchDocument({ fetchPlace }); }
  function changeApplicationAddress(value: string) { if (autoState.aplAdd && normalizeAuthorityCompareText(value) !== normalizeAuthorityCompareText(autoState.aplAdd)) setAutoState((current) => ({ ...current, aplAdd: "" })); patchDocument({ aplAdd: value }); }
  return { reset, changeOrgCode, changeFetchPlace, changeApplicationAddress };
}
