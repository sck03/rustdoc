export const queryKeys = {
  dashboard: () => ["dashboard"] as const,
  crmDashboard: () => ["crm", "dashboard"] as const,
  backups: () => ["backups"] as const,
  auditLogs: (
    pageNumber: number,
    pageSize: number,
    filters: {
      invoiceKeyword: string;
      entityName: string;
      action: string;
      userId: string;
      keyword: string;
      startTime: string;
      endTime: string;
    },
  ) => ["audit-logs", { pageNumber, pageSize, filters }] as const,
  auditLogsRoot: () => ["audit-logs"] as const,
  invoice: (invoiceId: number) => ["invoice", invoiceId] as const,
  invoiceParties: () => ["master-data", "invoice-parties"] as const,
  invoices: (pageNumber: number, pageSize: number, keyword: string) => ["invoices", { pageNumber, pageSize, keyword }] as const,
  invoicesRoot: () => ["invoices"] as const,
  queryInvoices: (
    pageNumber: number,
    pageSize: number,
    filters: {
      startDate: string;
      endDate: string;
      customerId: string;
      exporterId: string;
      keyword: string;
      invoiceType: string;
      transportMode: string;
    },
  ) => ["query", "invoices", { pageNumber, pageSize, filters }] as const,
  queryInvoicesRoot: () => ["query", "invoices"] as const,
  jobs: (pageNumber: number, pageSize: number, keyword: string, status: string) =>
    ["jobs", { pageNumber, pageSize, keyword, status }] as const,
  jobsRoot: () => ["jobs"] as const,
  containerPackingProjects: () => ["tools", "container-packing", "projects"] as const,
  containerPackingContainerTypes: () => ["tools", "container-packing", "container-types"] as const,
  customOptions: (optionType: string) => ["custom-options", optionType] as const,
  customOptionsGroup: (group: string) => ["custom-options", "group", group] as const,
  customOptionsRoot: () => ["custom-options"] as const,
  health: () => ["system", "health"] as const,
  licenseStatus: () => ["system", "license"] as const,
  emailStatus: () => ["tools", "email", "status"] as const,
  excelToolBookingInvoices: (pageSize: number) => ["tools", "excel", "booking-invoices", { pageSize }] as const,
  cloudBackupStatus: () => ["backups", "cloud", "status"] as const,
  cloudBackupBackups: () => ["backups", "cloud", "backups"] as const,
  postgreSqlMaintenanceBackups: () => ["postgresql-maintenance", "backups"] as const,
  sharedDatabaseOwnership: () => ["shared-database", "ownership"] as const,
  masterDataList: (entityKey: string, pageNumber: number, pageSize: number, keyword: string) =>
    ["master-data", entityKey, "list", { pageNumber, pageSize, keyword }] as const,
  masterDataRecord: (entityKey: string, recordKey: string) => ["master-data", entityKey, "record", recordKey] as const,
  masterDataRoot: (entityKey: string) => ["master-data", entityKey] as const,
  payment: (paymentId: number) => ["payment", paymentId] as const,
  payments: (pageNumber: number, pageSize: number, keyword: string) => ["payments", { pageNumber, pageSize, keyword }] as const,
  paymentsRoot: () => ["payments"] as const,
  reportTemplates: (reportType: string) => ["reports", "templates", reportType] as const,
  userReportTemplates: (reportType: string) => ["reports", "user-templates", reportType] as const,
  userReportTemplateVersions: (id: number) => ["reports", "user-templates", id, "versions"] as const,
  reportTemplateContent: (reportType: string, templatePath: string) =>
    ["reports", "templates", reportType, "content", templatePath] as const,
  reportTemplateFields: (reportType: string) => ["reports", "templates", reportType, "fields"] as const,
  reportTemplatePreviewInvoices: (pageSize: number) => ["reports", "templates", "preview-sources", "invoices", { pageSize }] as const,
  reportTemplatePreviewPayments: (pageSize: number) => ["reports", "templates", "preview-sources", "payments", { pageSize }] as const,
  settings: () => ["settings"] as const,
  users: () => ["users"] as const,
  permissionTemplates: () => ["permission-templates"] as const,
  singleWindowCollaboration: (
    pageNumber: number,
    pageSize: number,
    keyword: string,
    businessType: string,
    status: string,
    includeDisabledWorkstations: boolean,
  ) =>
    [
      "single-window",
      "collaboration",
      { pageNumber, pageSize, keyword, businessType, status, includeDisabledWorkstations },
    ] as const,
  singleWindowAgentConsignmentDocument: (invoiceId: number) => ["single-window", "acd", invoiceId, "document"] as const,
  singleWindowAgentConsignmentExportReview: (invoiceId: number) =>
    ["single-window", "acd", invoiceId, "export-review"] as const,
  singleWindowClientProfile: () => ["single-window", "client-profile", "default"] as const,
  singleWindowCustomsCooDocument: (invoiceId: number) => ["single-window", "coo", invoiceId, "document"] as const,
  singleWindowCustomsCooEditorOptions: () => ["single-window", "coo", "editor-options"] as const,
  singleWindowCustomsCooExportReview: (invoiceId: number) => ["single-window", "coo", invoiceId, "export-review"] as const,
  singleWindowCustomsCooIssuingAuthorities: () => ["single-window", "coo", "issuing-authorities"] as const,
  singleWindowCustomsCooProducerProfiles: (keyword: string) =>
    ["single-window", "coo", "producer-profiles", { keyword }] as const,
  singleWindowCustomsCooProducerProfilesRoot: () => ["single-window", "coo", "producer-profiles"] as const,
  singleWindowOperationCenter: (
    pageNumber: number,
    pageSize: number,
    keyword: string,
    businessType: string,
    status: string,
  ) => ["single-window", "operation-center", { pageNumber, pageSize, keyword, businessType, status }] as const,
  singleWindowOperationCenterDetail: (batchId: number) => ["single-window", "operation-center", batchId] as const,
  singleWindowReferenceCatalog: () => ["single-window", "reference-catalog"] as const,
  singleWindowOperationCenterRoot: () => ["single-window", "operation-center"] as const,
  singleWindowWorkstations: () => ["single-window", "workstations"] as const,
};
