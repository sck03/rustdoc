import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import type { ApiUserDto, ExportDocManagerApiClient } from "../../api/index.ts";
import { communicationAccess } from "./communicationModel.ts";

function useValidPage(total: number | undefined, page: number, change: (page: number) => void) {
  useEffect(() => {
    if (total !== undefined && page > Math.max(1, Math.ceil(total / 20))) change(Math.max(1, Math.ceil(total / 20)));
  }, [total, page, change]);
}

function useFilters(defaultManage = false) {
  const [page, setPage] = useState(1);
  const [unread, setUnread] = useState(false);
  const [manage, setManage] = useState(defaultManage);
  const [params, setParams] = useSearchParams();
  const selected = Number(params.get("announcementId")) || 0;
  return { page, setPage, unread, manage, selected,
    changeUnread: (value: boolean) => { setUnread(value); setPage(1); }, changeManage: (value: boolean) => { setManage(value); setPage(1); },
    select: (id: number) => setParams(id ? { announcementId: String(id) } : {}) };
}
export function useAnnouncements(client: ExportDocManagerApiClient, user: ApiUserDto) {
  const filters = useFilters(communicationAccess(user, "announcements", "manage"));
  const { page, unread, manage, selected } = filters;
  const allowed = communicationAccess(user, "announcements");
  const key = ["office", "communication", user.id, user.companyScope, user.departmentId];
  const query = useQuery({ queryKey: [...key, "announcements", page, unread, manage], enabled: allowed,
    queryFn: ({ signal }) => manage ? client.manageAnnouncements({ pageNumber: page, pageSize: 20 }, { signal })
        : client.listAnnouncements({ pageNumber: page, pageSize: 20, unreadOnly: unread }, { signal }),
    refetchInterval: 30000, refetchIntervalInBackground: false });
  const detail = useQuery({ queryKey: [...key, "announcement", selected], enabled: allowed && selected > 0,
    queryFn: ({ signal }) => client.getAnnouncement({ id: selected }, { signal }), refetchInterval: 30000, refetchIntervalInBackground: false });
  useValidPage(query.data?.totalCount, page, filters.setPage);
  return { ...filters, query, detail };
}
export function useNotifications(client: ExportDocManagerApiClient, user: ApiUserDto) {
  const filters = useFilters();
  const query = useQuery({ queryKey: ["office", "communication", user.id, user.companyScope, user.departmentId, "inbox", filters.page, filters.unread], enabled: communicationAccess(user, "notifications"),
    queryFn: ({ signal }) => client.listNotifications({ pageNumber: filters.page, pageSize: 20, unreadOnly: filters.unread }, { signal }), refetchInterval: 30000, refetchIntervalInBackground: false });
  useValidPage(query.data?.totalCount, filters.page, filters.setPage);
  return { ...filters, query };
}
export function useNotificationCount(client: ExportDocManagerApiClient, user: ApiUserDto) {
  return useQuery({ queryKey: ["office", "communication", user.id, user.companyScope, user.departmentId, "unread-count"],
    enabled: communicationAccess(user, "notifications"), queryFn: ({ signal }) => client.getNotificationUnreadCount({ signal }),
    refetchInterval: 30000, refetchIntervalInBackground: false });
}
