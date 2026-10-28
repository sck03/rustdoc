import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { OfficeField } from "../office/OfficeUi.tsx";
import { oaStatus } from "./oaModel.ts";

export function OaApprovalVerification({ client, user, onOpen }: { client: ExportDocManagerApiClient; user: ApiUserDto; onOpen: (id: number) => void }) {
  const [id, setId] = useState("");
  const [number, setNumber] = useState("");
  const [reference, setReference] = useState<{ id: number; submissionNumber: number } | null>(null);
  const query = useQuery({ queryKey: ["office", "oa", "expense", user.id, user.companyScope, "verification", reference],
    enabled: reference !== null, retry: false, staleTime: 0, gcTime: 0,
    queryFn: ({ signal }) => client.verifyExpenseApproval(reference!, { signal }),
    refetchInterval: reference ? 30000 : false, refetchIntervalInBackground: false });
  const result = reference ? query.data : undefined;
  return <section className="office-resource-card" aria-label="核对纸质审批单">
    <h3>核对纸质审批单</h3>
    <p className="office-muted">输入纸单上的申请编号和提交版本，核对申请人、金额和批准状态，再打开原审批单及附件比对。已接收的单据会显示交接状态。</p>
    <form className="office-toolbar" onSubmit={event => { event.preventDefault(); const next = { id: Number(id), submissionNumber: Number(number) }; if (reference?.id === next.id && reference.submissionNumber === next.submissionNumber) void query.refetch(); else setReference(next); }}>
      <OfficeField label="纸单申请编号"><input required type="number" min="1" max={Number.MAX_SAFE_INTEGER} step="1" value={id} onChange={event => { setId(event.target.value); setReference(null); }} /></OfficeField>
      <OfficeField label="纸单提交版本"><input required type="number" min="1" max={Number.MAX_SAFE_INTEGER} step="1" value={number} onChange={event => { setNumber(event.target.value); setReference(null); }} /></OfficeField>
      <button className="command-button" type="submit" disabled={query.isFetching}>{query.isFetching ? "正在核对…" : "核对审批单"}</button>
    </form>
    {reference && query.isError && <InlineNotice tone="error">核对未完成：{readApiError(query.error)}。请核对编号、版本及账号权限，勿据此办理接收。</InlineNotice>}
    {result && !query.isError && !query.isFetching && <>
      <InlineNotice tone={result.isValid ? "success" : "warning"} title={result.isValid ? (result.requestStatus === "HandedOff" ? "有效审批记录 · 已登记财务接收" : "有效审批记录 · 待财务接收") : "此纸单版本不能作为有效批准凭据"}>
        申请 #{result.requestId} · 提交版本 {result.submissionNumber} · 当前最新版本 {result.currentSubmissionNumber} · 当前状态 {oaStatus[result.requestStatus as keyof typeof oaStatus] ?? result.requestStatus}
        {!result.isCurrent && "。此版本已被后续提交替代。"}
        {result.documentStatus !== "Ready" && "。此版本没有可下载的有效正式 PDF。"}
      </InlineNotice>
      {result.approvedAt && <dl className="oa-facts"><div><dt>申请人</dt><dd>{result.employeeName} · {result.employeeNumber}</dd></div><div><dt>公司／部门</dt><dd>{result.companyName} · {result.departmentName}</dd></div><div><dt>事项</dt><dd>{result.title}</dd></div><div><dt>批准金额</dt><dd>{result.currency} {result.totalAmount}</dd></div><div><dt>批准时间</dt><dd>{formatBusinessDateTime(result.approvedAt, user.businessTimeZone)}</dd></div></dl>}
      <p className="office-muted">核对时间：{formatBusinessDateTime(result.checkedAt, user.businessTimeZone)}。纸单内容仍需与系统正式 PDF、原始发票比对；财务接收不代表付款。</p>
      {result.canOpenRequest && <button type="button" className="command-button secondary" onClick={() => onOpen(result.requestId)}>打开申请、审批单及原始凭证</button>}
    </>}
  </section>;
}
