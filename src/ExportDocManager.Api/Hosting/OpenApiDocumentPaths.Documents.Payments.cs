namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreatePaymentDocumentPaths() =>
            new Dictionary<string, object>
            {
                    ["/api/payments"] = new
                    {
                        get = new
                        {
                            summary = "List payments",
                            operationId = "listPayments",
                            parameters = new object[]
                            {
                                QueryParameter("pageNumber", "integer", "int32", "Page number starting from 1."),
                                QueryParameter("pageSize", "integer", "int32", "Page size. The repository caps this to the shared maximum."),
                                QueryParameter("keyword", "string", null, "Keyword for invoice number, payer, payee, project, bank, goods, country, or notes.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Paged payment list for the authenticated local user.",
                                    content = JsonContent("ApiPagedResponseOfApiPaymentDto")
                                },
                                ["401"] = new { description = "Missing or invalid bearer token." }
                            }
                        },
                        post = new
                        {
                            summary = "Create payment",
                            operationId = "createPayment",
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiPaymentDto")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["201"] = new
                                {
                                    description = "Created payment.",
                                    content = JsonContent("ApiPaymentSaveResponse")
                                },
                                ["400"] = new { description = "Invalid payment payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["409"] = new { description = "Payment could not be saved." }
                            }
                        }
                    },
                    ["/api/payments/{id}"] = new
                    {
                        get = new
                        {
                            summary = "Get payment detail",
                            operationId = "getPayment",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Payment id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Payment detail for the authenticated local user.",
                                    content = JsonContent("ApiPaymentDto")
                                },
                                ["400"] = new { description = "Invalid payment id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Payment not found or outside the current user's business scope." }
                            }
                        },
                        put = new
                        {
                            summary = "Update payment",
                            operationId = "updatePayment",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Payment id.")
                            },
                            requestBody = new
                            {
                                required = true,
                                content = JsonContent("ApiPaymentDto")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Updated payment.",
                                    content = JsonContent("ApiPaymentSaveResponse")
                                },
                                ["400"] = new { description = "Invalid payment id or payload." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Payment not found or outside the current user's business scope." },
                                ["409"] = new { description = "Payment could not be saved." }
                            }
                        },
                        delete = new
                        {
                            summary = "Delete payment",
                            operationId = "deletePayment",
                            parameters = new object[]
                            {
                                PathParameter("id", "integer", "int32", "Payment id.")
                            },
                            responses = new Dictionary<string, object>
                            {
                                ["200"] = new
                                {
                                    description = "Deleted payment.",
                                    content = JsonContent("ApiCommandResponse")
                                },
                                ["400"] = new { description = "Invalid payment id." },
                                ["401"] = new { description = "Missing or invalid bearer token." },
                                ["404"] = new { description = "Payment not found or outside the current user's business scope." }
                            }
                        }
                    },
            };
    }
}
