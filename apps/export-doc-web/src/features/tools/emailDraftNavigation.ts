export type EmailDraftNavigationState = {
  emailDraft?: { toAddress?: string; subject: string; body: string };
};

export function readEmailDraftNavigationState(value: unknown) {
  if (!value || typeof value !== "object") return null;
  const draft = (value as EmailDraftNavigationState).emailDraft;
  if (!draft || typeof draft.subject !== "string" || typeof draft.body !== "string") return null;
  return { toAddress: typeof draft.toAddress === "string" ? draft.toAddress : "", subject: draft.subject, body: draft.body };
}
