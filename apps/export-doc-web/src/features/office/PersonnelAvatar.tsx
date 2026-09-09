import { useState } from "react";
import type { ExportDocManagerApiClient, PersonnelDirectoryRecord } from "../../api/index.ts";
import { usePersonnelImage } from "./usePersonnelImage.ts";

export function PersonnelAvatar({ client, employee }: { client: ExportDocManagerApiClient; employee: PersonnelDirectoryRecord }) {
  const image = usePersonnelImage(client, employee.id, "Avatar", employee.avatarHash);
  const [failedUrl, setFailedUrl] = useState("");
  const initial = Array.from(employee.fullName)[0];
  if (image.error || image.url && failedUrl === image.url) return <button type="button" className="personnel-initial personnel-avatar-retry"
    aria-label={`${employee.fullName}的头像加载失败，点击重试`} title="头像加载失败，点击重试" onClick={image.retry}>{initial}!</button>;
  return image.url ? <img className="personnel-avatar" src={image.url} alt={`${employee.fullName}的头像`} onError={() => setFailedUrl(image.url)} />
    : <span className="personnel-initial" aria-hidden="true">{initial}</span>;
}
