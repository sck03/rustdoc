// CI and local final verification share the same suite registration.
export const frontendRegressionSuites = {
  editing: ['font-build', 'invoice-editing', 'document-editor-ui', 'input-method-ui', 'report-designer-v3', 'concurrency-feedback', 'payment-query-lifecycle-ui'],
  maintenance: ['abortable-operations-ui', 'settings-models', 'audit-log-maintenance-model', 'report-template-draft-ui', 'oa-review-lifecycle-ui'],
  business: ['email-template-workspace-ui', 'business-features-ui', 'list-pagination-ui', 'crm-export-lifecycle-ui', 'sales-opportunity-lifecycle-ui', 'workspace-refresh-ui', 'directory-import-organization-ui'],
  session: ['browser-session-contracts', 'business-date-refresh'],
};
