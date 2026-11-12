// Editors own their drafts. Release large server snapshots once no view observes
// them; the zero-delay GC still allows React's synchronous remount cycle.
export const largeDocumentQueryOptions = { gcTime: 0 } as const;
