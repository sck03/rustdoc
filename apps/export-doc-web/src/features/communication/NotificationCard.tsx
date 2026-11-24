import { Link } from "react-router-dom";
import type { SiteNotification } from "../../api/index.ts";
import { formatBusinessDateTime } from "../../ui/businessTime.ts";
import { StatusBadge } from "../../ui/primitives/Card.tsx";
import { notificationActionLabel, notificationFinanceReceiptStatus, notificationLink } from "./communicationModel.ts";
import "./notification-card.css";

export function NotificationCard({ row, timeZone, busy, onRead }: {
  row: SiteNotification; timeZone: string; busy: boolean; onRead: () => void;
}) {
  const link = notificationLink(row);
  const receipt = notificationFinanceReceiptStatus(row);
  return <article className="office-resource-card">
    <div className="notification-heading"><h3>{notificationActionLabel(row)} · {row.title}</h3>
      {receipt && <StatusBadge tone={receipt.tone} className="notification-receipt-status" aria-label={`财务接收状态：${receipt.label}`}>{receipt.label}</StatusBadge>}
    </div>
    <p>{formatBusinessDateTime(row.createdAt, timeZone)} · {row.status === "Read" ? "已读" : "未读"}</p>
    <div className="office-card-actions">
      {link && <Link className="command-button secondary" to={link}>查看关联申请</Link>}
      {row.status === "Unread" && <button type="button" className="command-button" disabled={busy} onClick={onRead}>标记已读</button>}
    </div>
  </article>;
}
