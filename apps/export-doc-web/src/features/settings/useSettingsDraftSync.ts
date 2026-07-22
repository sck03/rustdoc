import { useEffect, type Dispatch, type SetStateAction } from "react";
import type { ApiSettingsResponse, ApiSettingsValidationResponse } from "../../api/index.ts";
import type { SettingsRecord } from "./settingsTypes.ts";

type SingleWindowAuthorityAutoState = {
  fetchPlace: string;
  aplAdd: string;
};

type Options = {
  response: ApiSettingsResponse | undefined;
  hasUnsavedChanges: boolean;
  setSettings: Dispatch<SetStateAction<SettingsRecord | null>>;
  setMessage: Dispatch<SetStateAction<string | null>>;
  setUpdateSecrets: Dispatch<SetStateAction<boolean>>;
  setHasUnsavedChanges: Dispatch<SetStateAction<boolean>>;
  setValidationResult: Dispatch<SetStateAction<ApiSettingsValidationResponse | null>>;
  setSingleWindowAuthorityAutoState: Dispatch<SetStateAction<SingleWindowAuthorityAutoState>>;
};

/**
 * Synchronizes the server settings snapshot into the editable draft.
 *
 * React Query can refresh settings when the window regains focus. A refresh
 * must never replace a draft that the user is currently editing; the save
 * mutation explicitly clears the dirty flag before its own refreshed snapshot
 * is applied.
 */
export function useSettingsDraftSync({
  response,
  hasUnsavedChanges,
  setSettings,
  setMessage,
  setUpdateSecrets,
  setHasUnsavedChanges,
  setValidationResult,
  setSingleWindowAuthorityAutoState,
}: Options) {
  useEffect(() => {
    if (!response || hasUnsavedChanges) {
      return;
    }

    setSettings(response.settings);
    setMessage(null);
    setUpdateSecrets(false);
    setHasUnsavedChanges(false);
    setValidationResult(null);
    setSingleWindowAuthorityAutoState({ fetchPlace: "", aplAdd: "" });
  }, [
    hasUnsavedChanges,
    response,
    setHasUnsavedChanges,
    setMessage,
    setSettings,
    setSingleWindowAuthorityAutoState,
    setUpdateSecrets,
    setValidationResult,
  ]);
}
