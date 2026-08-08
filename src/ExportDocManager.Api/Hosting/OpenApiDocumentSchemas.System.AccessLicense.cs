namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateAccessLicenseSystemSchemas() =>
            new Dictionary<string, object>
            {
                        ["ApiLoginRequest"] = new
                        {
                            type = "object",
                            required = new[] { "username" },
                            properties = new Dictionary<string, object>
                            {
                                ["username"] = StringProperty("Login username."),
                                ["password"] = StringProperty("Login password or SQLite database password.")
                            }
                        },
                        ["ApiLoginResponse"] = new
                        {
                            type = "object",
                            required = new[] { "tokenType", "accessToken", "expiresAt", "user" },
                            properties = new Dictionary<string, object>
                            {
                                ["tokenType"] = StringProperty("Bearer token type."),
                                ["accessToken"] = StringProperty("Short-lived local sidecar token."),
                                ["expiresAt"] = new { type = "string", format = "date-time" },
                                ["user"] = RefSchema("ApiUserDto")
                            }
                        },
                        ["ApiUserDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "username", "role", "isActive", "capabilities" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["username"] = StringProperty("Username."),
                                ["fullName"] = StringProperty("Display name."),
                                ["role"] = StringProperty("User role."),
                                ["departmentId"] = StringProperty("Department scope."),
                                ["companyScope"] = StringProperty("Company scope."),
                                ["isActive"] = new { type = "boolean" },
                                ["capabilities"] = RefSchema("ApiUserCapabilitiesDto")
                            }
                        },
                        ["ApiUserCapabilitiesDto"] = new
                        {
                            type = "object",
                            required = new[] { "canManageSettings", "canManageUsers", "canViewAllBusinessData", "canUseDocumentWorkspace", "canUseSalesWorkspace", "productEdition", "enabledModules", "moduleAccess" },
                            properties = new Dictionary<string, object>
                            {
                                ["canManageSettings"] = new { type = "boolean" },
                                ["canManageUsers"] = new { type = "boolean" },
                                ["canViewAllBusinessData"] = new { type = "boolean" },
                                ["canUseDocumentWorkspace"] = new { type = "boolean" },
                                ["canUseSalesWorkspace"] = new { type = "boolean" },
                                ["productEdition"] = StringProperty("Document, Sales, or Full product edition."),
                                ["enabledModules"] = StringArrayProperty("Effective module keys captured when the user logged in."),
                                ["moduleAccess"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiModuleAccessDto")
                                }
                            }
                        },
                        ["ApiModuleAccessDto"] = new
                        {
                            type = "object",
                            required = new[] { "moduleKey", "accessLevel" },
                            properties = new Dictionary<string, object>
                            {
                                ["moduleKey"] = StringProperty("Effective module key."),
                                ["accessLevel"] = StringProperty("view, operate, or manage.")
                            }
                        },
                        ["ApiUserAccountDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "username", "role", "permissionTemplateCode", "permissionTemplateName", "isActive" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["username"] = StringProperty("Username."),
                                ["fullName"] = StringProperty("Display name."),
                                ["role"] = StringProperty("User role."),
                                ["permissionTemplateId"] = new { type = "integer", format = "int32", nullable = true },
                                ["permissionTemplateCode"] = StringProperty("Assigned permission template code."),
                                ["permissionTemplateName"] = StringProperty("Assigned permission template name."),
                                ["departmentId"] = StringProperty("Department scope."),
                                ["companyScope"] = StringProperty("Company scope."),
                                ["isActive"] = new { type = "boolean" }
                            }
                        },
                        ["ApiUserListResponse"] = new
                        {
                            type = "object",
                            required = new[] { "users", "roles", "permissionTemplates" },
                            properties = new Dictionary<string, object>
                            {
                                ["users"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiUserAccountDto")
                                },
                                ["roles"] = StringArrayProperty("Available user roles."),
                                ["permissionTemplates"] = new
                                {
                                    type = "array",
                                    items = RefSchema("ApiPermissionTemplateOptionDto")
                                }
                            }
                        },
                        ["ApiUserSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "username", "role", "isActive" },
                            properties = new Dictionary<string, object>
                            {
                                ["username"] = StringProperty("Username."),
                                ["fullName"] = StringProperty("Display name."),
                                ["role"] = StringProperty("User role."),
                                ["permissionTemplateId"] = new { type = "integer", format = "int32", nullable = true },
                                ["departmentId"] = StringProperty("Department scope."),
                                ["companyScope"] = StringProperty("Company scope."),
                                ["isActive"] = new { type = "boolean" },
                                ["resetPassword"] = StringProperty("Initial or reset password. Required when creating a user.")
                            }
                        },
                        ["ApiUserSaveResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "message", "user" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Save result message."),
                                ["user"] = RefSchema("ApiUserAccountDto")
                            }
                        },
                        ["ApiPermissionTemplateOptionDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "code", "name", "isSystem", "isActive" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["code"] = StringProperty("Stable permission template code."),
                                ["name"] = StringProperty("Permission template display name."),
                                ["isSystem"] = new { type = "boolean" },
                                ["isActive"] = new { type = "boolean" }
                            }
                        },
                        ["ApiPermissionModuleDefinitionDto"] = new
                        {
                            type = "object",
                            required = new[] { "key", "name", "group", "workspace", "sortOrder", "isTechnical" },
                            properties = new Dictionary<string, object>
                            {
                                ["key"] = StringProperty("Stable module key."),
                                ["name"] = StringProperty("Module display name."),
                                ["group"] = StringProperty("Module group."),
                                ["workspace"] = StringProperty("document, sales, or common."),
                                ["sortOrder"] = new { type = "integer", format = "int32" },
                                ["isTechnical"] = new { type = "boolean" }
                            }
                        },
                        ["ApiPermissionTemplateModuleDto"] = new
                        {
                            type = "object",
                            required = new[] { "moduleKey", "accessLevel" },
                            properties = new Dictionary<string, object>
                            {
                                ["moduleKey"] = StringProperty("Stable module key."),
                                ["accessLevel"] = StringProperty("view, operate, or manage.")
                            }
                        },
                        ["ApiPermissionTemplateDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "code", "name", "description", "isSystem", "isActive", "updatedAt", "modules" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["code"] = StringProperty("Stable permission template code."),
                                ["name"] = StringProperty("Permission template display name."),
                                ["description"] = StringProperty("Permission template description."),
                                ["isSystem"] = new { type = "boolean" },
                                ["isActive"] = new { type = "boolean" },
                                ["updatedAt"] = new { type = "string", format = "date-time" },
                                ["modules"] = new { type = "array", items = RefSchema("ApiPermissionTemplateModuleDto") }
                            }
                        },
                        ["ApiPermissionTemplateCatalogResponse"] = new
                        {
                            type = "object",
                            required = new[] { "modules", "templates", "accessLevels", "applyPolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["modules"] = new { type = "array", items = RefSchema("ApiPermissionModuleDefinitionDto") },
                                ["templates"] = new { type = "array", items = RefSchema("ApiPermissionTemplateDto") },
                                ["accessLevels"] = StringArrayProperty("Supported access levels."),
                                ["applyPolicy"] = StringProperty("Permission snapshot apply policy.")
                            }
                        },
                        ["ApiPermissionTemplateSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "code", "name", "isActive", "modules" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["code"] = StringProperty("Unique permission template code."),
                                ["name"] = StringProperty("Permission template display name."),
                                ["description"] = StringProperty("Permission template description."),
                                ["isActive"] = new { type = "boolean" },
                                ["modules"] = new { type = "array", items = RefSchema("ApiPermissionTemplateModuleDto") }
                            }
                        },
                        ["ApiSystemLogCleanupResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "success",
                                "message",
                                "deletedAuditLogs",
                                "deletedTextLogs",
                                "deletedTextLogsByAge",
                                "deletedTextLogsByCount",
                                "logRoot",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Manual system log cleanup result message."),
                                ["deletedAuditLogs"] = new { type = "integer", format = "int32" },
                                ["deletedTextLogs"] = new { type = "integer", format = "int32" },
                                ["deletedTextLogsByAge"] = new { type = "integer", format = "int32" },
                                ["deletedTextLogsByCount"] = new { type = "integer", format = "int32" },
                                ["logRoot"] = StringProperty("Runtime log directory for trusted desktop clients; empty for browser clients."),
                                ["storagePolicy"] = StringProperty("Manual log cleanup storage and data-domain policy.")
                            }
                        },
                        ["ApiLicenseStatusResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "isRegistered",
                                "isTrialExpired",
                                "trialDays",
                                "daysRemaining",
                                "machineId",
                                "message",
                                "expireDate",
                                "licenseStoragePath",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["isRegistered"] = new { type = "boolean" },
                                ["isTrialExpired"] = new { type = "boolean" },
                                ["trialDays"] = new { type = "integer", format = "int32" },
                                ["daysRemaining"] = new { type = "integer", format = "int32" },
                                ["machineId"] = StringProperty("Runtime license machine id shown to the user for key generation."),
                                ["message"] = StringProperty("Human-readable license status."),
                                ["expireDate"] = new { type = "string", format = "date-time" },
                                ["licenseStoragePath"] = StringProperty("Runtime data root Security/license.dat file path."),
                                ["storagePolicy"] = StringProperty("Path and storage policy for Tauri/Web/API license state.")
                            }
                        },
                        ["ApiLicenseRegisterRequest"] = new
                        {
                            type = "object",
                            required = new[] { "licenseKey" },
                            properties = new Dictionary<string, object>
                            {
                                ["licenseKey"] = StringProperty("License key generated for the current machine id.")
                            }
                        },
                        ["ApiLicenseRegisterResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "message", "status" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Registration result message."),
                                ["status"] = RefSchema("ApiLicenseStatusResponse")
                            }
                        },
            };
    }
}
