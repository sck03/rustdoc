namespace ExportDocManager.Api.Hosting;

internal static class ApiOpenApiLandingPage
{
    public const string Html = """
        <!doctype html>
        <html lang="zh-CN">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>ExportDocManager API</title>
          <style>
            body { font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; margin: 32px; line-height: 1.55; }
            code { background: #f2f4f7; padding: 2px 6px; border-radius: 4px; }
            a { color: #075985; }
          </style>
        </head>
        <body>
          <h1>ExportDocManager API</h1>
          <p>Sidecar is running. OpenAPI JSON is available at <a href="/openapi/v1.json"><code>/openapi/v1.json</code></a>.</p>
          <p>Process liveness is available at <a href="/livez"><code>/livez</code></a>.</p>
          <p>Dependency-aware readiness is available at <a href="/readyz"><code>/readyz</code></a>.</p>
          <p>Health check is available at <a href="/healthz"><code>/healthz</code></a>.</p>
        </body>
        </html>
        """;
}
