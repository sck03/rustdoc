namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateSalesPaths() =>
            new Dictionary<string, object>
            {
                    ["/api/crm/dashboard"] = new
                    {
                        get = new
                        {
                            summary = "Get CRM dashboard", operationId = "getCrmDashboard", security = BearerSecurity(),
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "CRM dashboard.", content = JsonContent("ApiCrmDashboardDto") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        }
                    },
                    ["/api/crm/opportunities"] = new
                    {
                        get = new
                        {
                            summary = "Search sales opportunities", operationId = "querySalesOpportunities", security = BearerSecurity(),
                            parameters = new[]
                            {
                                QueryParameter("keyword", "string", null, "Opportunity, customer, product or quotation keyword."),
                                QueryParameter("stage", "string", null, "Opportunity stage."),
                                QueryParameter("pageNumber", "integer", "int32", "One-based page number."),
                                QueryParameter("pageSize", "integer", "int32", "Page size from 10 to 100.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Paged sales opportunities.", content = JsonContent("ApiPagedResponseOfApiSalesOpportunityDto") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Sales workspace is not available." }
                            }
                        },
                        post = new
                        {
                            summary = "Create sales opportunity", operationId = "createSalesOpportunity", security = BearerSecurity(),
                            requestBody = new { required = true, content = JsonContent("ApiSalesOpportunitySaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["201"] = new { description = "Sales opportunity created.", content = JsonContent("ApiSalesOpportunityDto") },
                                ["400"] = new { description = "Invalid or duplicate opportunity." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Sales workspace is not available." }, ["404"] = new { description = "Customer or product not found." }
                            }
                        }
                    },
                    ["/api/crm/opportunities/{id}"] = new
                    {
                        put = new
                        {
                            summary = "Update sales opportunity", operationId = "updateSalesOpportunity", security = BearerSecurity(),
                            parameters = new[] { PathParameter("id", "integer", "int32", "Opportunity id.") },
                            requestBody = new { required = true, content = JsonContent("ApiSalesOpportunitySaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Sales opportunity updated.", content = JsonContent("ApiSalesOpportunityDto") },
                                ["400"] = new { description = "Invalid or duplicate opportunity." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Forbidden." }, ["404"] = new { description = "Opportunity, customer or product not found." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete sales opportunity", operationId = "deleteSalesOpportunity", security = BearerSecurity(),
                            parameters = new[] { PathParameter("id", "integer", "int32", "Opportunity id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Sales opportunity deleted.", content = JsonContent("ApiCommandResponse") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Forbidden." },
                                ["404"] = new { description = "Opportunity not found." }
                            }
                        }
                    },
                    ["/api/crm/opportunities/{id}/history"] = new
                    {
                        get = new
                        {
                            summary = "List sales opportunity history", operationId = "listSalesOpportunityHistory", security = BearerSecurity(),
                            parameters = new[] { PathParameter("id", "integer", "int32", "Opportunity id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Opportunity history snapshots.", content = JsonArrayContent("ApiSalesOpportunityHistoryDto") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Forbidden." },
                                ["404"] = new { description = "Opportunity not found." }
                            }
                        }
                    },
                    ["/api/crm/customers"] = new
                    {
                        get = new
                        {
                            summary = "List CRM customers", operationId = "listCrmCustomers", security = BearerSecurity(),
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "CRM customers.", content = JsonArrayContent("ApiCrmCustomerDto") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        },
                        post = new
                        {
                            summary = "Create CRM customer", operationId = "createCrmCustomer", security = BearerSecurity(),
                            requestBody = new { required = true, content = JsonContent("ApiCrmCustomerSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["201"] = new { description = "CRM customer created.", content = JsonContent("ApiCrmCustomerDto") },
                                ["400"] = new { description = "Invalid CRM customer." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        }
                    },
                    ["/api/crm/customers/page"] = new
                    {
                        get = new
                        {
                            summary = "Search CRM customers", operationId = "queryCrmCustomers", security = BearerSecurity(),
                            parameters = new[]
                            {
                                QueryParameter("keyword", "string", null, "Customer keyword."),
                                QueryParameter("status", "string", null, "Customer status."),
                                QueryParameter("pageNumber", "integer", "int32", "One-based page number."),
                                QueryParameter("pageSize", "integer", "int32", "Page size from 10 to 100.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Paged CRM customers.", content = JsonContent("ApiPagedResponseOfApiCrmCustomerDto") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        }
                    },
                    ["/api/crm/customers/batch-status"] = new
                    {
                        post = new
                        {
                            summary = "Update CRM customer statuses", operationId = "updateCrmCustomerBatchStatus", security = BearerSecurity(),
                            requestBody = new { required = true, content = JsonContent("ApiCrmCustomerBatchStatusRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "CRM customer statuses updated.", content = JsonContent("ApiCrmCustomerBatchStatusResult") },
                                ["400"] = new { description = "Invalid customer selection or status." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        }
                    },
                    ["/api/crm/customers/export"] = new
                    {
                        get = new
                        {
                            summary = "Export CRM customers", operationId = "exportCrmCustomers", security = BearerSecurity(),
                            parameters = new[] { QueryParameter("keyword", "string", null, "Customer keyword."), QueryParameter("status", "string", null, "Customer status.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "CRM customer Excel workbook.",
                                    content = new Dictionary<string, object> { ["application/octet-stream"] = new { schema = new { type = "string", format = "binary" } } }
                                },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Sales workspace is not available." }
                            }
                        }
                    },
                    ["/api/crm/customers/{id}"] = new
                    {
                        put = new
                        {
                            summary = "Update CRM customer", operationId = "updateCrmCustomer", security = BearerSecurity(),
                            parameters = new[] { PathParameter("id", "integer", "int32", "CRM customer id.") },
                            requestBody = new { required = true, content = JsonContent("ApiCrmCustomerSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "CRM customer updated.", content = JsonContent("ApiCrmCustomerDto") },
                                ["400"] = new { description = "Invalid CRM customer." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." },
                                ["404"] = new { description = "CRM customer not found." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete CRM customer", operationId = "deleteCrmCustomer", security = BearerSecurity(),
                            parameters = new[] { PathParameter("id", "integer", "int32", "CRM customer id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "CRM customer deleted.", content = JsonContent("ApiCommandResponse") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." },
                                ["404"] = new { description = "CRM customer not found." },
                                ["409"] = new { description = "CRM customer has follow-up history." }
                            }
                        }
                    },
                    ["/api/crm/import/preview"] = new
                    {
                        post = new
                        {
                            summary = "Preview CRM customer import", operationId = "previewCrmCustomerImport", security = BearerSecurity(),
                            parameters = new[] { QueryParameter("fileName", "string", null, "CSV or Excel file name.", true) },
                            requestBody = new
                            {
                                required = true,
                                content = new Dictionary<string, object>
                                {
                                    ["application/octet-stream"] = new { schema = new { type = "string", format = "binary" } }
                                }
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "CRM import preview.", content = JsonContent("ApiCrmCustomerImportPreviewDto") },
                                ["400"] = new { description = "Invalid import file." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        }
                    },
                    ["/api/crm/import"] = new
                    {
                        post = new
                        {
                            summary = "Import CRM customers", operationId = "importCrmCustomers", security = BearerSecurity(),
                            requestBody = new { required = true, content = JsonContent("ApiCrmCustomerImportRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "CRM customers imported.", content = JsonContent("ApiCrmCustomerImportResultDto") },
                                ["400"] = new { description = "Invalid import rows." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        }
                    },
                    ["/api/crm/customers/{customerId}/email-variable-draft"] = new
                    {
                        get = new
                        {
                            summary = "Build email variable draft from CRM customer", operationId = "getCrmEmailVariableDraft", security = BearerSecurity(),
                            parameters = new[] { PathParameter("customerId", "integer", "int32", "CRM customer id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "CRM email variable draft.", content = JsonContent("ApiCrmEmailVariableDraftDto") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Sales workspace is not available." },
                                ["404"] = new { description = "CRM customer not found." }
                            }
                        }
                    },
                    ["/api/crm/customers/{customerId}/contacts"] = new
                    {
                        get = new
                        {
                            summary = "List CRM contacts", operationId = "listCrmContacts", security = BearerSecurity(),
                            parameters = new[] { PathParameter("customerId", "integer", "int32", "CRM customer id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "CRM contacts.", content = JsonArrayContent("ApiCrmContactDto") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        },
                        post = new
                        {
                            summary = "Create CRM contact", operationId = "createCrmContact", security = BearerSecurity(),
                            parameters = new[] { PathParameter("customerId", "integer", "int32", "CRM customer id.") },
                            requestBody = new { required = true, content = JsonContent("ApiCrmContactSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["201"] = new { description = "CRM contact created.", content = JsonContent("ApiCrmContactDto") },
                                ["400"] = new { description = "Invalid CRM contact." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        }
                    },
                    ["/api/crm/customers/{customerId}/contacts/{id}"] = new
                    {
                        put = new
                        {
                            summary = "Update CRM contact", operationId = "updateCrmContact", security = BearerSecurity(),
                            parameters = new[]
                            {
                                PathParameter("customerId", "integer", "int32", "CRM customer id."),
                                PathParameter("id", "integer", "int32", "CRM contact id.")
                            },
                            requestBody = new { required = true, content = JsonContent("ApiCrmContactSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "CRM contact updated.", content = JsonContent("ApiCrmContactDto") },
                                ["400"] = new { description = "Invalid CRM contact." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." },
                                ["404"] = new { description = "CRM contact not found." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete CRM contact", operationId = "deleteCrmContact", security = BearerSecurity(),
                            parameters = new[]
                            {
                                PathParameter("customerId", "integer", "int32", "CRM customer id."),
                                PathParameter("id", "integer", "int32", "CRM contact id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "CRM contact deleted.", content = JsonContent("ApiCommandResponse") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." },
                                ["404"] = new { description = "CRM contact not found." }
                            }
                        }
                    },
                    ["/api/crm/follow-ups"] = new
                    {
                        get = new
                        {
                            summary = "List customer follow-ups",
                            operationId = "listCrmFollowUps",
                            security = BearerSecurity(),
                            parameters = new[]
                            {
                                QueryParameter("crmCustomerId", "integer", "int32", "Optional CRM customer id."),
                                QueryParameter("includeCompleted", "boolean", null, "Include completed follow-ups."),
                                QueryParameter("limit", "integer", "int32", "Maximum rows, from 1 to 200.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "CRM follow-ups.", content = JsonArrayContent("ApiCrmFollowUpDto") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        },
                        post = new
                        {
                            summary = "Create customer follow-up",
                            operationId = "createCrmFollowUp",
                            security = BearerSecurity(),
                            requestBody = new { required = true, content = JsonContent("ApiCrmFollowUpSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "CRM follow-up created.", content = JsonContent("ApiCrmFollowUpDto") },
                                ["400"] = new { description = "Invalid follow-up." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        }
                    },
                    ["/api/crm/follow-ups/{id}"] = new
                    {
                        put = new
                        {
                            summary = "Update customer follow-up",
                            operationId = "updateCrmFollowUp",
                            security = BearerSecurity(),
                            parameters = new[] { PathParameter("id", "integer", "int32", "Follow-up id.") },
                            requestBody = new { required = true, content = JsonContent("ApiCrmFollowUpSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "CRM follow-up updated.", content = JsonContent("ApiCrmFollowUpDto") },
                                ["400"] = new { description = "Invalid follow-up." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." },
                                ["404"] = new { description = "Follow-up not found." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete customer follow-up",
                            operationId = "deleteCrmFollowUp",
                            security = BearerSecurity(),
                            parameters = new[] { PathParameter("id", "integer", "int32", "Follow-up id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Customer follow-up deleted.", content = JsonContent("ApiCommandResponse") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." },
                                ["404"] = new { description = "Follow-up not found." }
                            }
                        }
                    },
                    ["/api/email-templates"] = new
                    {
                        get = new
                        {
                            summary = "List email templates", operationId = "listEmailTemplates", security = BearerSecurity(),
                            parameters = new[]
                            {
                                QueryParameter("keyword", "string", null, "Template name, subject or body."),
                                QueryParameter("category", "string", null, "Template category."),
                                QueryParameter("includeInactive", "boolean", null, "Include inactive templates.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Email templates.", content = JsonArrayContent("ApiEmailTemplateDto") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Sales workspace is not available." }
                            }
                        },
                        post = new
                        {
                            summary = "Create email template", operationId = "createEmailTemplate", security = BearerSecurity(),
                            requestBody = new { required = true, content = JsonContent("ApiEmailTemplateSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["201"] = new { description = "Email template created.", content = JsonContent("ApiEmailTemplateDto") },
                                ["400"] = new { description = "Invalid or duplicate email template." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        }
                    },
                    ["/api/email-templates/variables"] = new
                    {
                        get = new
                        {
                            summary = "List email template variables", operationId = "listEmailTemplateVariables", security = BearerSecurity(),
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Template variables.", content = JsonArrayContent("ApiEmailTemplateVariableDto") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Sales workspace is not available." }
                            }
                        }
                    },
                    ["/api/email-templates/preview"] = new
                    {
                        post = new
                        {
                            summary = "Preview email template", operationId = "previewEmailTemplate", security = BearerSecurity(),
                            requestBody = new { required = true, content = JsonContent("ApiEmailTemplatePreviewRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Rendered email template.", content = JsonContent("ApiEmailTemplatePreviewDto") },
                                ["400"] = new { description = "Invalid preview request." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        }
                    },
                    ["/api/email-templates/{id}"] = new
                    {
                        put = new
                        {
                            summary = "Update email template", operationId = "updateEmailTemplate", security = BearerSecurity(),
                            parameters = new[] { PathParameter("id", "integer", "int32", "Email template id.") },
                            requestBody = new { required = true, content = JsonContent("ApiEmailTemplateSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Email template updated.", content = JsonContent("ApiEmailTemplateDto") },
                                ["400"] = new { description = "Invalid or duplicate email template." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Forbidden." }, ["404"] = new { description = "Email template not found." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete email template", operationId = "deleteEmailTemplate", security = BearerSecurity(),
                            parameters = new[] { PathParameter("id", "integer", "int32", "Email template id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Email template deleted.", content = JsonContent("ApiCommandResponse") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Forbidden." },
                                ["404"] = new { description = "Email template not found." }
                            }
                        }
                    },
                    ["/api/email-templates/{id}/versions"] = new
                    {
                        get = new
                        {
                            summary = "List email template versions", operationId = "listEmailTemplateVersions", security = BearerSecurity(),
                            parameters = new[] { PathParameter("id", "integer", "int32", "Email template id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Email template versions.", content = JsonArrayContent("ApiEmailTemplateVersionDto") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Sales workspace is not available." },
                                ["404"] = new { description = "Email template not found." }
                            }
                        }
                    },
                    ["/api/email-templates/{id}/versions/{versionNumber}/restore"] = new
                    {
                        post = new
                        {
                            summary = "Restore email template version", operationId = "restoreEmailTemplateVersion", security = BearerSecurity(),
                            parameters = new[]
                            {
                                PathParameter("id", "integer", "int32", "Email template id."),
                                PathParameter("versionNumber", "integer", "int32", "Version number to restore.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Restored email template.", content = JsonContent("ApiEmailTemplateDto") },
                                ["400"] = new { description = "Invalid or duplicate restored template." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Forbidden." }, ["404"] = new { description = "Email template or version not found." }
                            }
                        }
                    },
                    ["/api/suppliers"] = new
                    {
                        get = new
                        {
                            summary = "List suppliers", operationId = "listSuppliers", security = BearerSecurity(),
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Suppliers.", content = JsonArrayContent("ApiSupplierDto") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        },
                        post = new
                        {
                            summary = "Create supplier", operationId = "createSupplier", security = BearerSecurity(),
                            requestBody = new { required = true, content = JsonContent("ApiSupplierSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["201"] = new { description = "Supplier created.", content = JsonContent("ApiSupplierDto") },
                                ["400"] = new { description = "Invalid supplier." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        }
                    },
                    ["/api/suppliers/page"] = new
                    {
                        get = new
                        {
                            summary = "Search suppliers", operationId = "querySuppliers", security = BearerSecurity(),
                            parameters = new[]
                            {
                                QueryParameter("keyword", "string", null, "Supplier keyword."),
                                QueryParameter("status", "string", null, "Supplier status."),
                                QueryParameter("pageNumber", "integer", "int32", "One-based page number."),
                                QueryParameter("pageSize", "integer", "int32", "Page size from 10 to 100.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Paged suppliers.", content = JsonContent("ApiPagedResponseOfApiSupplierDto") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Sales workspace is not available." }
                            }
                        }
                    },
                    ["/api/suppliers/batch-status"] = new
                    {
                        post = new
                        {
                            summary = "Update supplier statuses", operationId = "updateSupplierBatchStatus", security = BearerSecurity(),
                            requestBody = new { required = true, content = JsonContent("ApiSupplierBatchStatusRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Supplier statuses updated.", content = JsonContent("ApiSupplierBatchStatusResult") },
                                ["400"] = new { description = "Invalid supplier selection or status." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Forbidden." }
                            }
                        }
                    },
                    ["/api/suppliers/import/preview"] = new
                    {
                        post = new
                        {
                            summary = "Preview supplier import", operationId = "previewSupplierImport", security = BearerSecurity(),
                            parameters = new[] { QueryParameter("fileName", "string", null, "CSV or Excel file name.", true) },
                            requestBody = new { required = true, content = new Dictionary<string, object> { ["application/octet-stream"] = new { schema = new { type = "string", format = "binary" } } } },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Supplier import preview.", content = JsonContent("ApiSupplierImportPreviewDto") },
                                ["400"] = new { description = "Invalid import file." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Forbidden." }
                            }
                        }
                    },
                    ["/api/suppliers/import"] = new
                    {
                        post = new
                        {
                            summary = "Import suppliers", operationId = "importSuppliers", security = BearerSecurity(),
                            requestBody = new { required = true, content = JsonContent("ApiSupplierImportRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Suppliers imported.", content = JsonContent("ApiSupplierImportResultDto") },
                                ["400"] = new { description = "Invalid import rows." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Forbidden." }
                            }
                        }
                    },
                    ["/api/suppliers/export"] = new
                    {
                        get = new
                        {
                            summary = "Export suppliers", operationId = "exportSuppliers", security = BearerSecurity(),
                            parameters = new[] { QueryParameter("keyword", "string", null, "Supplier keyword."), QueryParameter("status", "string", null, "Supplier status.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Supplier Excel workbook.",
                                    content = new Dictionary<string, object> { ["application/octet-stream"] = new { schema = new { type = "string", format = "binary" } } }
                                },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Forbidden." }
                            }
                        }
                    },
                    ["/api/suppliers/product-options"] = new
                    {
                        get = new
                        {
                            summary = "Search product options for supplier links", operationId = "searchSupplierProductOptions", security = BearerSecurity(),
                            parameters = new[] { QueryParameter("keyword", "string", null, "Product code or name.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Product options.", content = JsonArrayContent("ApiSupplierProductOptionDto") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Forbidden." }
                            }
                        }
                    },
                    ["/api/suppliers/assessment-overview"] = new
                    {
                        get = new
                        {
                            summary = "Get supplier assessment overview", operationId = "getSupplierAssessmentOverview", security = BearerSecurity(),
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Supplier assessment overview.", content = JsonContent("ApiSupplierAssessmentOverviewDto") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Forbidden." }
                            }
                        }
                    },
                    ["/api/suppliers/{id}"] = new
                    {
                        put = new
                        {
                            summary = "Update supplier", operationId = "updateSupplier", security = BearerSecurity(),
                            parameters = new[] { PathParameter("id", "integer", "int32", "Supplier id.") },
                            requestBody = new { required = true, content = JsonContent("ApiSupplierSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Supplier updated.", content = JsonContent("ApiSupplierDto") },
                                ["400"] = new { description = "Invalid supplier." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Forbidden." }, ["404"] = new { description = "Supplier not found." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete supplier", operationId = "deleteSupplier", security = BearerSecurity(),
                            parameters = new[] { PathParameter("id", "integer", "int32", "Supplier id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Supplier deleted.", content = JsonContent("ApiCommandResponse") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Forbidden." },
                                ["404"] = new { description = "Supplier not found." }
                            }
                        }
                    },
                    ["/api/suppliers/{supplierId}/contacts"] = new
                    {
                        get = new
                        {
                            summary = "List supplier contacts", operationId = "listSupplierContacts", security = BearerSecurity(),
                            parameters = new[] { PathParameter("supplierId", "integer", "int32", "Supplier id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Supplier contacts.", content = JsonArrayContent("ApiSupplierContactDto") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Forbidden." }
                            }
                        },
                        post = new
                        {
                            summary = "Create supplier contact", operationId = "createSupplierContact", security = BearerSecurity(),
                            parameters = new[] { PathParameter("supplierId", "integer", "int32", "Supplier id.") },
                            requestBody = new { required = true, content = JsonContent("ApiSupplierContactSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["201"] = new { description = "Supplier contact created.", content = JsonContent("ApiSupplierContactDto") },
                                ["400"] = new { description = "Invalid contact." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Forbidden." }, ["404"] = new { description = "Supplier not found." }
                            }
                        }
                    },
                    ["/api/suppliers/{supplierId}/assessments"] = new
                    {
                        get = new
                        {
                            summary = "List supplier assessments", operationId = "listSupplierAssessments", security = BearerSecurity(),
                            parameters = new[] { PathParameter("supplierId", "integer", "int32", "Supplier id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Supplier assessments.", content = JsonArrayContent("ApiSupplierAssessmentDto") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Forbidden." }
                            }
                        },
                        post = new
                        {
                            summary = "Create supplier assessment", operationId = "createSupplierAssessment", security = BearerSecurity(),
                            parameters = new[] { PathParameter("supplierId", "integer", "int32", "Supplier id.") },
                            requestBody = new { required = true, content = JsonContent("ApiSupplierAssessmentSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["201"] = new { description = "Supplier assessment created.", content = JsonContent("ApiSupplierAssessmentDto") },
                                ["400"] = new { description = "Invalid assessment." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Forbidden." }, ["404"] = new { description = "Supplier not found." }
                            }
                        }
                    },
                    ["/api/suppliers/{supplierId}/assessments/{id}"] = new
                    {
                        put = new
                        {
                            summary = "Update supplier assessment", operationId = "updateSupplierAssessment", security = BearerSecurity(),
                            parameters = new[] { PathParameter("supplierId", "integer", "int32", "Supplier id."), PathParameter("id", "integer", "int32", "Assessment id.") },
                            requestBody = new { required = true, content = JsonContent("ApiSupplierAssessmentSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Supplier assessment updated.", content = JsonContent("ApiSupplierAssessmentDto") },
                                ["400"] = new { description = "Invalid assessment." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Forbidden." }, ["404"] = new { description = "Assessment not found." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete supplier assessment", operationId = "deleteSupplierAssessment", security = BearerSecurity(),
                            parameters = new[] { PathParameter("supplierId", "integer", "int32", "Supplier id."), PathParameter("id", "integer", "int32", "Assessment id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Supplier assessment deleted.", content = JsonContent("ApiCommandResponse") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Forbidden." },
                                ["404"] = new { description = "Assessment not found." }
                            }
                        }
                    },
                    ["/api/suppliers/{supplierId}/contacts/{id}"] = new
                    {
                        put = new
                        {
                            summary = "Update supplier contact", operationId = "updateSupplierContact", security = BearerSecurity(),
                            parameters = new[] { PathParameter("supplierId", "integer", "int32", "Supplier id."), PathParameter("id", "integer", "int32", "Contact id.") },
                            requestBody = new { required = true, content = JsonContent("ApiSupplierContactSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Supplier contact updated.", content = JsonContent("ApiSupplierContactDto") },
                                ["400"] = new { description = "Invalid contact." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Forbidden." }, ["404"] = new { description = "Contact not found." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete supplier contact", operationId = "deleteSupplierContact", security = BearerSecurity(),
                            parameters = new[] { PathParameter("supplierId", "integer", "int32", "Supplier id."), PathParameter("id", "integer", "int32", "Contact id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Supplier contact deleted.", content = JsonContent("ApiCommandResponse") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Forbidden." },
                                ["404"] = new { description = "Contact not found." }
                            }
                        }
                    },
                    ["/api/suppliers/{supplierId}/products"] = new
                    {
                        get = new
                        {
                            summary = "List supplier product links", operationId = "listSupplierProductLinks", security = BearerSecurity(),
                            parameters = new[] { PathParameter("supplierId", "integer", "int32", "Supplier id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Supplier product links.", content = JsonArrayContent("ApiSupplierProductLinkDto") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Forbidden." }
                            }
                        },
                        post = new
                        {
                            summary = "Create supplier product link", operationId = "createSupplierProductLink", security = BearerSecurity(),
                            parameters = new[] { PathParameter("supplierId", "integer", "int32", "Supplier id.") },
                            requestBody = new { required = true, content = JsonContent("ApiSupplierProductLinkSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["201"] = new { description = "Supplier product link created.", content = JsonContent("ApiSupplierProductLinkDto") },
                                ["400"] = new { description = "Invalid or duplicate product link." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Forbidden." }, ["404"] = new { description = "Supplier or product not found." }
                            }
                        }
                    },
                    ["/api/suppliers/{supplierId}/products/{id}"] = new
                    {
                        put = new
                        {
                            summary = "Update supplier product link", operationId = "updateSupplierProductLink", security = BearerSecurity(),
                            parameters = new[] { PathParameter("supplierId", "integer", "int32", "Supplier id."), PathParameter("id", "integer", "int32", "Link id.") },
                            requestBody = new { required = true, content = JsonContent("ApiSupplierProductLinkSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Supplier product link updated.", content = JsonContent("ApiSupplierProductLinkDto") },
                                ["400"] = new { description = "Invalid or duplicate product link." }, ["401"] = new { description = "Unauthorized." },
                                ["403"] = new { description = "Forbidden." }, ["404"] = new { description = "Link or product not found." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete supplier product link", operationId = "deleteSupplierProductLink", security = BearerSecurity(),
                            parameters = new[] { PathParameter("supplierId", "integer", "int32", "Supplier id."), PathParameter("id", "integer", "int32", "Link id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Supplier product link deleted.", content = JsonContent("ApiCommandResponse") },
                                ["401"] = new { description = "Unauthorized." }, ["403"] = new { description = "Forbidden." },
                                ["404"] = new { description = "Link not found." }
                            }
                        }
                    },
            };
    }
}