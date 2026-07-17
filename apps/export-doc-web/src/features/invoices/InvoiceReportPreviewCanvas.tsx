import type { ApiInvoiceDocumentPackagePreviewResponse, ApiReportHtmlPreviewResponse } from "../../api/index.ts";
import { fileNameFromPath } from "./invoiceReportPreviewModel.ts";
type Props={isBusy:boolean;packagePreview:ApiInvoiceDocumentPackagePreviewResponse|null;preview:ApiReportHtmlPreviewResponse|null};
export function InvoiceReportPreviewCanvas({isBusy,packagePreview,preview}:Props){return (
      <div className="report-preview-frame-wrap">
        {packagePreview?.items.length ? (
          <div className="document-package-preview-list" aria-label="单据包 HTML 预览">
            {packagePreview.items.map((item, index) => (
              <section className="document-package-preview-item" key={`${item.templatePath}-${index}`}>
                <div className="document-package-preview-heading">
                  <h3>{item.name || fileNameFromPath(item.templatePath)}</h3>
                  <span>{item.withSeal ? "带章" : "无章"}</span>
                </div>
                <iframe
                  className="report-preview-frame document-package-preview-frame"
                  title={`单据包 HTML 预览 ${index + 1}`}
                  sandbox=""
                  srcDoc={item.html}
                />
              </section>
            ))}
          </div>
        ) : preview ? (
          <iframe className="report-preview-frame" title="报表 HTML 预览" sandbox="" srcDoc={preview.html} />
        ) : (
          <div className="report-preview-empty">{isBusy ? "加载中" : "暂无预览"}</div>
        )}
      </div>

);}

