import type { ApiSingleWindowIssuingAuthorityOptionDto } from "../../api/index.ts";

export function parseIssuingAuthorityCode(
  value: string,
  options: ApiSingleWindowIssuingAuthorityOptionDto[],
) {
  const trimmed = value.trim();
  if (!trimmed) {
    return "";
  }

  const codeMatch = trimmed.match(/(?:^|\D)(\d{4})(?:\D|$)/);
  if (codeMatch) {
    return codeMatch[1];
  }

  const normalized = normalizeAuthorityLookupText(trimmed);
  const matched = options.find((option) => {
    const normalizedCode = normalizeAuthorityLookupText(option.code);
    const normalizedLabel = normalizeAuthorityLookupText(option.label);
    return normalizedCode === normalized ||
      normalizedLabel === normalized ||
      (normalized.length >= 2 && normalizedLabel.includes(normalized));
  });

  return matched?.code || trimmed;
}

export function findIssuingAuthority(
  code: string,
  options: ApiSingleWindowIssuingAuthorityOptionDto[],
) {
  const normalizedCode = normalizeAuthorityLookupText(code);
  return options.find((option) => normalizeAuthorityLookupText(option.code) === normalizedCode) ?? null;
}

function normalizeAuthorityLookupText(value: string) {
  return value
    .trim()
    .replace(/[\s:：]/g, "")
    .toUpperCase();
}
