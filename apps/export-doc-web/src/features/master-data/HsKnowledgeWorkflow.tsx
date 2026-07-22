export function HsKnowledgeWorkflow({ activeSection }: { activeSection: string }) {
  const activeStep = activeSection === "online" ? 1
    : activeSection === "annual" ? 2
      : activeSection === "examples" || activeSection === "history" ? 3
        : activeSection === "search" ? 4
          : 0;
  const steps = [
    ["联网获取", "申报实例进入候选池"],
    ["匹配税则", "选择当前年度有效编码"],
    ["人工审核", "确认或忽略候选"],
    ["实例入库", "成为本公司的正式经验"],
    ["智能使用", "查询推荐并回填发票"],
  ];
  return <div className="knowledge-workflow" tabIndex={0} aria-label="HS 编码知识闭环，可横向滚动查看">
    {steps.map(([title, description], index) => <div className={index <= activeStep ? "active" : ""} key={title}>
      <span>{index + 1}</span><strong>{title}</strong><small>{description}</small>
    </div>)}
  </div>;
}
