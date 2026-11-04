import ReactDOM from "react-dom/client";
import { Button, IconButton } from "../../src/ui/Button.tsx";
import { Field } from "../../src/ui/primitives/Field.tsx";
import { applyInterfaceDensity } from "../../src/app/interfaceDensity.ts";
import "../../src/styles/index.css";
import "../../src/styles/jobs-excel.css";
import "../../src/styles/workspaces.css";
import "../../src/styles/responsive.css";

const requestedDensity = new URLSearchParams(location.search).get("density");
applyInterfaceDensity(requestedDensity === "compact" ? "compact" : "comfortable");

function GlobalStyleContract() {
  return (
    <main className="app-shell" style={{ display: "block", minHeight: "100vh", padding: 16 }}>
      <h1 className="visually-hidden" data-style-contract="visually-hidden">全局样式运行合同</h1>

      <section className="form-section">
        <div className="section-header" data-style-contract="section-header-text">
          <h2>基础信息</h2>
          <Button variant="primary" data-style-contract="section-header-text-action">保存</Button>
        </div>
      </section>

      <section className="form-section">
        <div className="section-header" data-style-contract="section-header-icon">
          <h2>客户与出口商</h2>
          <IconButton label="刷新" data-style-contract="section-header-icon-action">↻</IconButton>
        </div>
      </section>

      <section className="form-section">
        <div className="field-grid" data-style-contract="field-grid">
          <Field label="发票号" required><input defaultValue="YH2026-001" required /></Field>
          <Field label="合同号"><input disabled /></Field>
          <Field label="发票日期"><input type="date" readOnly /></Field>
          <Field label="币种" error="请选择币种"><select defaultValue=""><option value="">请选择</option><option>USD</option></select></Field>
        </div>
      </section>

      <section className="form-section">
        <div className="filter-bar" data-style-contract="filter-bar">
          <div data-style-contract="field"><Field label="状态"><select defaultValue="all"><option value="all">全部</option></select></Field></div>
          <label className="inline-check" data-style-contract="inline-check"><input type="checkbox" defaultChecked /><span>仅显示异常</span></label>
        </div>
      </section>

      <section className="detail-grid" data-style-contract="detail-grid">
        {["运行模式", "数据目录", "数据库", "浏览器"].map((label) => (
          <div className="detail-item" key={label}><span>{label}</span><strong>可用</strong></div>
        ))}
      </section>

      <div className="row-actions-cell" data-style-contract="row-actions-cell"><button type="button" className="icon-button" aria-label="查看">查</button></div>
      <div className="job-title-cell" data-style-contract="job-title-cell"><strong>批量生成报表</strong><span>JOB-20260810-001</span></div>
      <span className="review-severity review-severity-warning" data-style-contract="review-severity">警告</span>
    </main>
  );
}

ReactDOM.createRoot(document.getElementById("root")!).render(<GlobalStyleContract />);
requestAnimationFrame(() => requestAnimationFrame(() => {
  document.documentElement.dataset.visualBaselineReady = "true";
}));
