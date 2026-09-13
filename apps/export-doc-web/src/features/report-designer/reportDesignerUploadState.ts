import { createContext } from "react";

export const ReportDesignerUploadState = createContext<(uploading: boolean) => void>(() => undefined);
