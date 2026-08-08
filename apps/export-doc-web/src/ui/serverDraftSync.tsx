import { useEffect, useRef, useState } from "react";
import { Button } from "./Button.tsx";
import { InlineNotice } from "./PageState.tsx";

type ServerDraftSyncOptions<T> = {
  resourceKey: string | number;
  incomingValue: T | null | undefined;
  isDirty: boolean;
  fingerprint: (value: T) => string;
  applyIncoming: (value: T) => void;
};

type AppliedServerDraft = {
  resourceKey: string;
  fingerprint: string;
};

type PendingServerDraft<T> = AppliedServerDraft & {
  value: T;
};

export function useServerDraftSync<T>({
  resourceKey,
  incomingValue,
  isDirty,
  fingerprint,
  applyIncoming,
}: ServerDraftSyncOptions<T>) {
  const normalizedResourceKey = String(resourceKey);
  const fingerprintRef = useRef(fingerprint);
  const applyIncomingRef = useRef(applyIncoming);
  const appliedRef = useRef<AppliedServerDraft | null>(null);
  const acknowledgedRef = useRef<AppliedServerDraft | null>(null);
  const [pending, setPending] = useState<PendingServerDraft<T> | null>(null);

  fingerprintRef.current = fingerprint;
  applyIncomingRef.current = applyIncoming;

  useEffect(() => {
    if (pending && pending.resourceKey !== normalizedResourceKey) {
      setPending(null);
    }
  }, [normalizedResourceKey, pending]);

  useEffect(() => {
    if (incomingValue == null) {
      return;
    }

    const incomingFingerprint = fingerprintRef.current(incomingValue);
    const applied = appliedRef.current;
    const isFirstValueForResource = !applied || applied.resourceKey !== normalizedResourceKey;
    if (isFirstValueForResource || (!isDirty && applied.fingerprint !== incomingFingerprint)) {
      applyIncomingRef.current(incomingValue);
      appliedRef.current = {
        resourceKey: normalizedResourceKey,
        fingerprint: incomingFingerprint,
      };
      acknowledgedRef.current = null;
      setPending(null);
      return;
    }

    if (applied.fingerprint === incomingFingerprint) {
      acknowledgedRef.current = null;
      setPending(null);
      return;
    }

    const acknowledged = acknowledgedRef.current;
    if (acknowledged?.resourceKey === normalizedResourceKey
      && acknowledged.fingerprint === incomingFingerprint) {
      setPending(null);
      return;
    }

    setPending((current) => current?.resourceKey === normalizedResourceKey
      && current.fingerprint === incomingFingerprint
      ? current
      : {
          resourceKey: normalizedResourceKey,
          fingerprint: incomingFingerprint,
          value: incomingValue,
        });
  }, [incomingValue, isDirty, normalizedResourceKey]);

  function loadServerVersion() {
    if (!pending) {
      return;
    }

    applyIncomingRef.current(pending.value);
    appliedRef.current = {
      resourceKey: pending.resourceKey,
      fingerprint: pending.fingerprint,
    };
    acknowledgedRef.current = null;
    setPending(null);
  }

  function keepLocalDraft() {
    if (!pending) {
      return;
    }

    // Acknowledge this server revision without pretending it was loaded. The
    // existing persisted snapshot remains unchanged, so optimistic concurrency
    // still prevents the subsequent save from overwriting a newer record. If
    // the local draft later becomes clean, the acknowledged server value is
    // applied automatically instead of leaving an older value on screen.
    acknowledgedRef.current = {
      resourceKey: pending.resourceKey,
      fingerprint: pending.fingerprint,
    };
    setPending(null);
  }

  return {
    hasPendingServerVersion: Boolean(pending),
    keepLocalDraft,
    loadServerVersion,
  };
}

export function ServerDraftUpdateNotice({
  entityLabel,
  onKeepLocal,
  onLoadServer,
}: {
  entityLabel: string;
  onKeepLocal: () => void;
  onLoadServer: () => void;
}) {
  return <InlineNotice
    tone="warning"
    title={`服务器上的${entityLabel}已更新`}
    action={<div className="toolbar-actions">
      <Button variant="secondary" onClick={onKeepLocal}>保留本地草稿</Button>
      <Button variant="primary" onClick={onLoadServer}>载入服务器版本</Button>
    </div>}
  >
    当前页面仍有未保存修改，系统没有覆盖它们。可继续保留本地内容，或放弃本地修改并载入服务器最新版本。
  </InlineNotice>;
}
