const PRINT_PREVIEW_OVERRIDES = `
<style id="edm-print-preview-overrides">
@page {
  @top-left-corner { content: ""; }
  @top-left { content: ""; }
  @top-center { content: ""; }
  @top-right { content: ""; }
  @top-right-corner { content: ""; }
  @bottom-left-corner { content: ""; }
  @bottom-left { content: ""; }
  @bottom-center { content: ""; }
  @bottom-right { content: ""; }
  @bottom-right-corner { content: ""; }
}
@media print {
  html,
  body {
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }
}
</style>`;

export function buildPrintSourceHtml(html: string) {
  if (/<\/head>/i.test(html)) {
    return html.replace(/<\/head>/i, `${PRINT_PREVIEW_OVERRIDES}</head>`);
  }

  if (/<html[\s>]/i.test(html)) {
    return html.replace(/<html([^>]*)>/i, `<html$1><head>${PRINT_PREVIEW_OVERRIDES}</head>`);
  }

  return `<!doctype html><html><head>${PRINT_PREVIEW_OVERRIDES}</head><body>${html}</body></html>`;
}
