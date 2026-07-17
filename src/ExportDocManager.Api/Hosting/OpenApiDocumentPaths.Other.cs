namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateOtherPaths() =>
            new Dictionary<string, object>
            {
                    ["/api/dashboard"] = new
                    {
                        get = new
                        {
                            summary = "Get dashboard",
                            operationId = "getDashboard",
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Dashboard snapshot for the current user's visible invoice and Single Window data.",
                                    content = JsonContent("ApiDashboardResponse")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        }
                    },
            };
    }
}