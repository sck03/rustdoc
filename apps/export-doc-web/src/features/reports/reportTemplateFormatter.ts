export function formatReportTemplateSource(source: string) {
  if (!source.trim()) {
    return "";
  }

  let metadataPrefix = "";
  let body = normalizeScribanBlocks(source.trim());
  if (body.startsWith("<!--")) {
    const endIndex = body.indexOf("-->");
    if (endIndex >= 0) {
      metadataPrefix = body.slice(0, endIndex + 3).trim();
      body = body.slice(endIndex + 3).trimStart();
    }
  }

  const scribanPlaceholders: string[] = [];
  body = body.replace(/\{\{[\s\S]*?\}\}/g, (match) => {
    const key = `__SCRIBAN_BLOCK_${scribanPlaceholders.length}__`;
    scribanPlaceholders.push(match);
    return key;
  });

  const lines = body
    .replace(/></g, ">\n<")
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean);

  const formattedLines: string[] = [];
  if (metadataPrefix) {
    formattedLines.push(metadataPrefix);
  }

  let indent = 0;
  for (const rawLine of lines) {
    const line = rawLine;
    if (line.startsWith("</") || line === "}") {
      indent = Math.max(0, indent - 1);
    }

    formattedLines.push(`${" ".repeat(indent * 2)}${line}`);

    if (
      (line.startsWith("<") && !line.startsWith("</") && !line.endsWith("/>") && !line.includes("</")) ||
      line.endsWith("{")
    ) {
      indent += 1;
    }
  }

  return scribanPlaceholders.reduce(
    (formatted, placeholder, index) => formatted.replace(`__SCRIBAN_BLOCK_${index}__`, placeholder),
    formattedLines.join("\n").trimEnd(),
  );
}

function normalizeScribanBlocks(source: string) {
  return source.replace(/\{\s*\{([\s\S]*?)\}\s*\}/g, (_match, inner: string) => {
    const normalized = inner.replace(/\r/g, " ").replace(/\n/g, " ").trim();
    return `{{ ${normalized} }}`;
  });
}
