export function HsKnowledgeWorkflow() {
  return <details className="knowledge-help">
    <summary>使用说明：税则、申报实例与智能查询</summary>
    <ol>
      <li>导入有来源和年度的税则，在税则目录查询当前有效编码。</li>
      <li>联网资料和历史单据提供候选经验，人工审核后加入申报实例库。</li>
      <li>智能查询同时匹配有效税则与已审核实例，可在发票中使用。</li>
    </ol>
    <p>可按需要直接进入各项功能，无需依次完成。知识库导入导出仅包含归类资料。</p>
  </details>;
}
