namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateRuntimeAccessSystemPaths() =>
            new Dictionary<string, object>
            {
                    ["/livez"] = new
                    {
                        get = new
                        {
                            summary = "Process liveness check",
                            operationId = "getLiveness",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "The API process is alive."
                                }
                            }
                        }
                    },
                    ["/readyz"] = new
                    {
                        get = new
                        {
                            summary = "Dependency-aware API readiness check",
                            operationId = "getReadiness",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "The API process is ready to accept requests."
                                },
                                ["503"] = new
                                {
                                    description = "A required database, runtime directory, or configured browser dependency is unavailable."
                                }
                            }
                        }
                    },
                    ["/healthz"] = new
                    {
                        get = new
                        {
                            summary = "Public health check or authorized runtime diagnostics",
                            operationId = "getHealth",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Anonymous requests receive a lightweight status/version response. Administrators and trusted desktop clients also receive runtime path and dependency diagnostics.",
                                    content = JsonContent("ApiHealthResponse")
                                }
                            }
                        }
                    },
                    ["/openapi/v1.json"] = new
                    {
                        get = new
                        {
                            summary = "OpenAPI document",
                            operationId = "getOpenApiDocument",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "OpenAPI document for the sidecar API.",
                                    content = JsonContent("OpenApiDocument")
                                }
                            }
                        }
                    },
                    ["/api/system/shutdown-maintenance"] = new
                    {
                        post = new
                        {
                            summary = "Run desktop shutdown maintenance",
                            operationId = "runShutdownMaintenance",
                            security = new[]
                            {
                                new Dictionary<string, string[]>
                                {
                                    ["DesktopAccess"] = Array.Empty<string>()
                                }
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Shutdown maintenance finished or returned a non-blocking maintenance failure result.",
                                    content = JsonContent("ApiShutdownMaintenanceResponse")
                                },
                                ["403"] = new
                                {
                                    description = "Missing or invalid desktop access token.",
                                    content = JsonContent("ApiErrorResponse")
                                }
                            }
                        }
                    },
                    ["/api/system/logs/cleanup"] = new
                    {
                        post = new
                        {
                            summary = "Clean system logs using saved retention settings",
                            operationId = "cleanupSystemLogs",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "System audit and text logs were cleaned according to saved settings.",
                                    content = JsonContent("ApiSystemLogCleanupResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new
                                {
                                    description = "Only administrators can clean system logs.",
                                    content = JsonContent("ApiErrorResponse")
                                },
                                ["409"] = new
                                {
                                    description = "Log cleanup failed.",
                                    content = JsonContent("ApiErrorResponse")
                                }
                            }
                        }
                    },
                    ["/api/system/license"] = new
                    {
                        get = new
                        {
                            summary = "Get license status",
                            operationId = "getLicenseStatus",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Current Tauri/Web/API runtime license status and machine id.",
                                    content = JsonContent("ApiLicenseStatusResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/system/license/register"] = new
                    {
                        post = new
                        {
                            summary = "Register license",
                            operationId = "registerLicense",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiLicenseRegisterRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "License registration succeeded.",
                                    content = JsonContent("ApiLicenseRegisterResponse")
                                },
                                ["400"] = new
                                {
                                    description = "License key is missing, invalid, or for another machine.",
                                    content = JsonContent("ApiLicenseRegisterResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/auth/login"] = new
                    {
                        post = new
                        {
                            summary = "Login",
                            operationId = "login",
                            parameters = new object[]
                            {
                                new
                                {
                                    name = ApiRuntimeOptions.BootstrapTokenHeaderName,
                                    @in = "header",
                                    required = false,
                                    description = "Deployment bootstrap token required only when a network-mode PostgreSQL database has no application users yet.",
                                    schema = new { type = "string", minLength = 24 }
                                }
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiLoginRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Authenticated local sidecar session.",
                                    content = JsonContent("ApiLoginResponse")
                                },
                                ["400"] = new { description = "Missing username." },
                                ["401"] = new { description = "Invalid username or password." },
                                ["429"] = new { description = "Login attempts are temporarily rate limited." },
                                ["503"] = new { description = "Database initialization failed." }
                            }
                        }
                    },
                    ["/api/auth/me"] = new
                    {
                        get = new
                        {
                            summary = "Current user",
                            operationId = "getCurrentUser",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Current authenticated user.",
                                    content = JsonContent("ApiUserDto")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
                    ["/api/auth/logout"] = new
                    {
                        post = new
                        {
                            summary = "Logout",
                            operationId = "logout",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Token revocation result.",
                                    content = JsonContent("ApiLogoutResponse")
                                }
                            }
                        }
                    },
                    ["/api/auth/renew"] = new
                    {
                        post = new
                        {
                            summary = "Renew the current authenticated session",
                            operationId = "renewSession",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "A replacement bearer token was issued and the previous token was revoked.",
                                    content = JsonContent("ApiLoginResponse")
                                },
                                ["401"] = new { description = "Missing, invalid, or already expired bearer token." }
                            }
                        }
                    },
                    ["/api/users"] = new
                    {
                        get = new
                        {
                            summary = "List users",
                            operationId = "listUsers",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "User accounts and role catalog for administrators.",
                                    content = JsonContent("ApiUserListResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage user accounts." }
                            }
                        },
                        post = new
                        {
                            summary = "Create user",
                            operationId = "createUserAccount",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiUserSaveRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "User account was created.",
                                    content = JsonContent("ApiUserSaveResponse")
                                },
                                ["400"] = new { description = "Invalid user payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage user accounts." },
                                ["409"] = new { description = "User account could not be saved." }
                            }
                        }
                    },
                    ["/api/users/{id}"] = new
                    {
                        put = new
                        {
                            summary = "Update user",
                            operationId = "updateUserAccount",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "User id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiUserSaveRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "User account was saved.",
                                    content = JsonContent("ApiUserSaveResponse")
                                },
                                ["400"] = new { description = "Invalid user payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage user accounts." },
                                ["409"] = new { description = "User account could not be saved." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete user",
                            operationId = "deleteUserAccount",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "User id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "User account was deleted.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "Invalid user id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage user accounts." },
                                ["404"] = new { description = "User account was not found." },
                                ["409"] = new { description = "User account could not be deleted." }
                            }
                        }
                    },
                    ["/api/permission-templates"] = new
                    {
                        get = new
                        {
                            summary = "List permission templates and module catalog",
                            operationId = "listPermissionTemplates",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Permission template catalog.", content = JsonContent("ApiPermissionTemplateCatalogResponse") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Only Full edition administrators can manage permission templates." }
                            }
                        },
                        post = new
                        {
                            summary = "Create permission template",
                            operationId = "createPermissionTemplate",
                            requestBody = new { required = true, content = JsonContent("ApiPermissionTemplateSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Permission template was created.", content = JsonContent("ApiPermissionTemplateDto") },
                                ["400"] = new { description = "Invalid permission template payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Only Full edition administrators can manage permission templates." },
                                ["409"] = new { description = "Permission template could not be saved." }
                            }
                        }
                    },
                    ["/api/permission-templates/{id}"] = new
                    {
                        put = new
                        {
                            summary = "Update permission template",
                            operationId = "updatePermissionTemplate",
                            parameters = new object[] { PathParameter("id", "integer", "int32", "Permission template id.") },
                            requestBody = new { required = true, content = JsonContent("ApiPermissionTemplateSaveRequest") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Permission template was updated.", content = JsonContent("ApiPermissionTemplateDto") },
                                ["400"] = new { description = "Invalid permission template payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Only Full edition administrators can manage permission templates." },
                                ["404"] = new { description = "Permission template was not found." },
                                ["409"] = new { description = "Permission template could not be saved." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete permission template",
                            operationId = "deletePermissionTemplate",
                            parameters = new object[] { PathParameter("id", "integer", "int32", "Permission template id.") },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new { description = "Permission template was deleted.", content = JsonContent("ApiCommandResponse") },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "Only Full edition administrators can manage permission templates." },
                                ["404"] = new { description = "Permission template was not found." },
                                ["409"] = new { description = "System or assigned templates cannot be deleted." }
                            }
                        }
                    },
                    ["/api/settings"] = new
                    {
                        get = new
                        {
                            summary = "Get settings",
                            operationId = "getSettings",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Current local settings with secret values redacted.",
                                    content = JsonContent("ApiSettingsResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        },
                        put = new
                        {
                            summary = "Update settings",
                            operationId = "updateSettings",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiSettingsSaveRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Settings were saved to the runtime data Config/appsettings.json.",
                                    content = JsonContent("ApiSettingsSaveResponse")
                                },
                                ["400"] = new { description = "Invalid settings payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage program settings." },
                                ["409"] = new { description = "Settings could not be saved." }
                            }
                        }
                    },
                    ["/api/settings/validate"] = new
                    {
                        post = new
                        {
                            summary = "Validate settings draft",
                            operationId = "validateSettings",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiSettingsValidationRequest")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Settings draft validation result. The sidecar does not save appsettings.json.",
                                    content = JsonContent("ApiSettingsValidationResponse")
                                },
                                ["400"] = new { description = "Invalid settings validation payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["403"] = new { description = "The current user cannot manage program settings." }
                            }
                        }
                    },
            };
    }
}
