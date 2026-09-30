import { ListPaginationControls } from "../../ui/ListPaginationControls.tsx";
export function CommunicationPager({ page, total, busy, change }: { page: number; total: number; busy: boolean; change: (page: number) => void }) {
  return <ListPaginationControls pageNumber={page} totalPages={Math.ceil(total / 20)} totalCount={total} pageSize={20} pageSizeOptions={[20]} isBusy={busy} onPageChange={change} onPageSizeChange={() => {}} />;
}
