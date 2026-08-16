/// <reference types="vite/client" />

import "react";

declare module "react" {
  interface HTMLAttributes<T> {
    /** Native inert attribute is supported by current evergreen browsers. */
    inert?: boolean;
  }
}

interface ImportMetaEnv {
  readonly VITE_EXPORTDOC_API_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
