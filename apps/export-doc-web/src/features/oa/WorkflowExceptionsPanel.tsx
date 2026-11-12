import { officePolling } from "../../api/queryPollingPolicy.ts";
import { Button } from "../../ui/Button.tsx";
import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import type { ExportDocManagerApiClient, OaWorkflowException } from "../../api/index.ts";
import { useOfficeOperation } from "../office/useOfficeData.ts";
import { InlineNotice } from "../../ui/PageState.tsx";
import { readApiError } from "../../ui/formUtils.ts";
import { OfficeDialog, OfficeField, OfficeSubmit } from "../office/OfficeUi.tsx";
import { createRequestKey } from "../../ui/createRequestKey.ts";
import { notificationLink } from "../communication/communicationModel.ts";
import { Pagination } from "../../ui/primitives/Pagination.tsx";
import { useServerPageNumber } from "../../ui/useServerPageNumber.ts";
function requestRoute(item: OaWorkflowException): string {
  return notificationLink(item) ?? "/office/approvals";
}
export function WorkflowExceptionsPanel({ client }: { client: ExportDocManagerApiClient }) {
  const [page, setPage] = useState(1),
    [selected, setSelected] = useState<OaWorkflowException | null>(null);
  const query = useQuery({
    queryKey: ["office", "approval-settings", "exceptions", page],
    queryFn: ({ signal }) => client.listOaWorkflowExceptions({ pageNumber: page, pageSize: 20 }, { signal }),
    ...officePolling,
  });
  useServerPageNumber(query, page, setPage, 20);
  return (
    <section className="office-resource-card" aria-label="流程异常队列">
      <h3>流程异常队列</h3>
      <p className="office-muted">
        此处仅显示运行元数据。人员或授权失效先移交任务；系统动作可重新排队，执行时仍核对原业务授权。
      </p>
      {query.isError && (
        <InlineNotice tone="error" action={<Button onClick={() => void query.refetch()}>重试读取</Button>}>
          {readApiError(query.error)}
        </InlineNotice>
      )}
      <ul>
        {query.data?.items.map((item) => (
          <li key={`${item.requestId}:${item.nodeKey}`}>
            <strong>
              申请 #{item.requestId} · {item.nodeName}
            </strong>{" "}
            · {item.errorCode}
            <Link to={requestRoute(item)}>查看申请</Link>
            {item.nodeKind === "System" && (
              <Button type="button" disabled={query.isFetching} onClick={() => setSelected(item)}>
                重试系统动作
              </Button>
            )}
            {item.nodeKind === "Approval" && <p>在下方未完成任务中选择合格接任身份。</p>}
          </li>
        ))}
      </ul>
      {query.data && !query.data.items.length && <p>当前没有失败节点。</p>}
      <Pagination
        pageNumber={page}
        pageSize={20}
        totalCount={query.data?.totalCount ?? 0}
        isBusy={query.isFetching}
        onPageChange={setPage}
      />
      {selected && <RetryDialog client={client} item={selected} onClose={() => setSelected(null)} />}
    </section>
  );
}
function RetryDialog({
  client,
  item,
  onClose,
}: {
  client: ExportDocManagerApiClient;
  item: OaWorkflowException;
  onClose: () => void;
}) {
  const operation = useOfficeOperation([["office", "approval-settings"], ["unit-workflow-tasks"]], false);
  const [note, setNote] = useState(""),
    [commandKey] = useState(createRequestKey);
  return (
    <OfficeDialog title="重新排队系统动作" onClose={onClose} {...operation} protectChanges hasChanges={Boolean(note)}>
      <p>
        申请 #{item.requestId} · {item.nodeName}。重试不会改变已保存的审批决定。
      </p>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          void operation.run(
            (signal) =>
              client.retryOaSystemNode(
                {
                  body: {
                    requestId: item.requestId,
                    requestKind: item.requestKind,
                    nodeKey: item.nodeKey,
                    expectedVersion: item.versionNumber,
                    commandKey,
                    note,
                  },
                },
                { signal },
              ),
            onClose,
          );
        }}
      >
        <OfficeField label="重试原因">
          <textarea
            required
            maxLength={500}
            value={note}
            disabled={operation.busy}
            onChange={(e) => setNote(e.target.value)}
          />
        </OfficeField>
        <OfficeSubmit busy={operation.busy} label="重新排队" />
      </form>
    </OfficeDialog>
  );
}
