import { useEffect, useState } from "react";
import type { ExportDocManagerApiClient, PersonnelImageKind } from "../../api/index.ts";
import { readApiError } from "../../ui/formUtils.ts";

export function usePersonnelBlobUrl(blob: Blob | null) {
  const [value, setValue] = useState<{ blob: Blob; url: string } | null>(null);
  useEffect(() => {
    if (!blob) { setValue(null); return; }
    const url = URL.createObjectURL(blob);
    setValue({ blob, url });
    return () => URL.revokeObjectURL(url);
  }, [blob]);
  return value?.blob === blob ? value.url : "";
}

export function usePersonnelImage(client: ExportDocManagerApiClient, id: number, kind: PersonnelImageKind, hash?: string | null) {
  const key = `${id}/${kind}/${hash ?? ""}`;
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<{ key: string; blob: Blob | null; error: string; pending: boolean } | null>(null);
  useEffect(() => {
    if (!hash) { setResult(null); return; }
    const controller = new AbortController();
    setResult({ key, blob: null, error: "", pending: true });
    const request = kind === "Avatar" ? client.getPersonnelAvatar({ id }, { signal: controller.signal })
      : client.getPersonnelImage({ id, kind }, { signal: controller.signal });
    void request.then((blob) => {
      if (!controller.signal.aborted) setResult({ key, blob, error: "", pending: false });
    }, (error: unknown) => {
      if (!controller.signal.aborted) setResult({ key, blob: null, error: readApiError(error), pending: false });
    });
    return () => controller.abort();
  }, [client, id, kind, hash, key, attempt]);
  const current = hash && result?.key === key ? result : null;
  const url = usePersonnelBlobUrl(current?.blob ?? null);
  return { url, pending: current?.pending ?? Boolean(hash), error: current?.error ?? "", retry: () => setAttempt((value) => value + 1) };
}
