export const documentSpareKeys = ["spare1", "spare2", "spare3", "spare4", "spare5", "spare6", "spare7", "spare8", "spare9", "spare10"] as const;
export type DocumentSpareKey = typeof documentSpareKeys[number];
export type DocumentSpareFields = Record<DocumentSpareKey, string>;

export function mapDocumentSpareFields(source: Partial<DocumentSpareFields>, transform = (value: string) => value.trim()): DocumentSpareFields {
  return Object.fromEntries(documentSpareKeys.map((key) => [key, transform(source[key] ?? "")])) as DocumentSpareFields;
}
