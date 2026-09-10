import { useState } from "react";
import { CalendarDays, MapPin, Plus, RefreshCw, UsersRound } from "lucide-react";
import type { ApiUserDto, ExportDocManagerApiClient, MeetingRoomRecord } from "../../api/index.ts";
import { PageState } from "../../ui/PageState.tsx";
import { officeAccess } from "./officeModel.ts";
import { MeetingBookingDialog, MeetingRoomEditor } from "./MeetingRoomDialogs.tsx";
import { OfficeRequestsPanel } from "./OfficeRequestsPanel.tsx";
import { OfficePager, OfficeQueryState, OfficeTabs } from "./OfficeUi.tsx";
import { useOfficeDirectory, useOfficeOperation, useOfficeView } from "./useOfficeData.ts";
import { RecordDeleteDialog } from "./RecordDeleteDialog.tsx";
import "../../styles/routes/office.css";

export function MeetingRoomsPage({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const { records, setRecords } = useOfficeView();
  if (!user.companyScope) return <PageState tone="permission" title="请先设置所属公司" description="请管理员在账号与权限中为当前账号选择所属公司。" />;
  return <section className="work-surface office-workspace" aria-label="会议室预约工作区">
    <OfficeTabs records={records} onChange={setRecords} resourcesLabel={user.capabilities.usesOfficeRegister ? "会议室与登记" : undefined}
      recordsLabel={user.capabilities.usesOfficeRegister ? "预约与钥匙交接记录" : "预约记录与审批"} />
    {records ? <OfficeRequestsPanel client={client} user={user} kind="rooms" /> : <MeetingRoomsDirectory client={client} user={user} />}
  </section>;
}

function MeetingRoomsDirectory({ client, user }: { client: ExportDocManagerApiClient; user: ApiUserDto }) {
  const access = officeAccess(user, "rooms");
  const model = useOfficeDirectory<MeetingRoomRecord>(user, "rooms", (input, signal) => client.listMeetingRooms(input, { signal }));
  const { paging, query, keyword, search, includeInactive } = model;
  const [editing, setEditing] = useState<MeetingRoomRecord | "new" | null>(null);
  const [booking, setBooking] = useState<MeetingRoomRecord | null>(null);
  const [deleting, setDeleting] = useState<MeetingRoomRecord | null>(null);
  const deletion = useOfficeOperation();
  return <>
    <div className="office-toolbar"><form className="office-search" onSubmit={(event) => { event.preventDefault(); model.commitSearch(); }}>
      <input aria-label="搜索会议室" placeholder="会议室名称或位置" maxLength={120} value={keyword} onChange={(event) => model.changeKeyword(event.target.value)} />
      <button className="command-button secondary" type="submit">搜索</button></form>
      {access.allows("manage") && <label className="checkbox-field"><input type="checkbox" checked={includeInactive} onChange={(event) => model.changeInactive(event.target.checked)} />含停用</label>}
      <button className="icon-button" type="button" aria-label="刷新会议室" disabled={query.isFetching} onClick={model.refresh}><RefreshCw size={17} aria-hidden="true" /></button>
      {access.allows("manage") && <button className="command-button" type="button" onClick={() => setEditing("new")}><Plus size={17} aria-hidden="true" />添加会议室</button>}
    </div>
    <OfficeQueryState query={query} emptyTitle={search ? "没有找到会议室" : "尚未添加会议室，请联系行政管理员"} />
    {!query.isError && <div className="office-resource-grid">{query.data?.items.map((room) => <article key={room.id} className="office-resource-card">
      <div className="office-card-heading"><CalendarDays size={21} aria-hidden="true" /><h2>{room.name}</h2><span className="office-badge" data-state={room.isActive ? (room.inUse ? "InUse" : "Available") : "Cancelled"}>{room.isActive ? (room.inUse ? "使用中" : "可预约") : "已停用"}</span></div>
      <p className="office-card-detail"><MapPin size={16} aria-hidden="true" />{room.location || "位置未填写"}</p>
      <p className="office-card-detail"><UsersRound size={16} aria-hidden="true" />{room.capacity} 人 · {room.requiresKey ? "需领还钥匙" : "无需钥匙"}</p>
      <p className="office-muted office-card-description">{room.equipment || "暂无设备说明"}</p>
      <footer className="office-card-actions"><button className="command-button" type="button" onClick={() => setBooking(room)}>查看日程{access.allows("create") && room.isActive ? "与预约" : ""}</button>
        {access.allows("manage") && <><button className="command-button secondary" type="button" onClick={() => setEditing(room)}>编辑</button>
          <button className="command-button secondary" type="button" onClick={() => setDeleting(room)}>删除</button></>}</footer>
    </article>)}</div>}
    <OfficePager page={query.data} paging={paging} busy={query.isFetching} />
    {editing && <MeetingRoomEditor client={client} room={editing === "new" ? undefined : editing} onClose={() => setEditing(null)} />}
    {booking && <MeetingBookingDialog client={client} user={user} room={booking} onClose={() => setBooking(null)} />}
    {deleting && <RecordDeleteDialog name={deleting.name} version={deleting.versionNumber} operation={deletion}
      description="仅可删除没有预约记录的会议室。已有历史的会议室可在编辑窗口中停用。"
      onDelete={(body, signal) => client.deleteMeetingRoom({ id: deleting.id, body }, { signal })}
      onClose={() => setDeleting(null)} onDeleted={() => setDeleting(null)} />}
  </>;
}
