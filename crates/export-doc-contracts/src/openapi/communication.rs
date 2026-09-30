use super::office::{endpoint, object, reference};
use serde_json::{Value, json};

pub(super) fn extend(doc: &mut Value) {
    schemas(doc);
    for (path, method, id, permission, body, response, action) in [
        (
            "",
            "get",
            "ListAnnouncements",
            "view",
            None,
            "AnnouncementPage",
            "list",
        ),
        (
            "/manage",
            "get",
            "ManageAnnouncements",
            "manage",
            None,
            "AnnouncementPage",
            "manage-list",
        ),
        (
            "/departments",
            "get",
            "ListAnnouncementDepartments",
            "manage",
            None,
            "AnnouncementDepartmentList",
            "departments",
        ),
        (
            "",
            "post",
            "CreateAnnouncement",
            "manage",
            Some("AnnouncementSave"),
            "Announcement",
            "create",
        ),
        (
            "/{id}",
            "get",
            "GetAnnouncement",
            "view",
            None,
            "Announcement",
            "get",
        ),
        (
            "/{id}",
            "put",
            "UpdateAnnouncement",
            "manage",
            Some("AnnouncementSave"),
            "Announcement",
            "update",
        ),
        (
            "/{id}/publish",
            "post",
            "PublishAnnouncement",
            "manage",
            Some("OaAction"),
            "Announcement",
            "publish",
        ),
        (
            "/{id}",
            "delete",
            "DeleteAnnouncement",
            "manage",
            Some("OaAction"),
            "AnnouncementDeleteResult",
            "delete",
        ),
        (
            "/{id}/withdraw",
            "post",
            "WithdrawAnnouncement",
            "manage",
            Some("OaAction"),
            "Announcement",
            "withdraw",
        ),
        (
            "/{id}/archive",
            "post",
            "ArchiveAnnouncement",
            "manage",
            Some("OaAction"),
            "Announcement",
            "archive",
        ),
        (
            "/{id}/read",
            "post",
            "ConfirmAnnouncementRead",
            "view",
            Some("AnnouncementRead"),
            "Announcement",
            "read",
        ),
        (
            "/{id}/receipts",
            "get",
            "ListAnnouncementReceipts",
            "receipts",
            None,
            "AnnouncementReceiptPage",
            "receipts",
        ),
        (
            "/{id}/attachments",
            "post",
            "UploadAnnouncementAttachment",
            "manage",
            None,
            "Announcement",
            "upload",
        ),
        (
            "/{id}/attachments/{attachmentId}",
            "get",
            "DownloadAnnouncementAttachment",
            "view",
            None,
            "OaAttachment",
            "download",
        ),
        (
            "/{id}/attachments/{attachmentId}",
            "delete",
            "DeleteAnnouncementAttachment",
            "manage",
            Some("OaAction"),
            "Announcement",
            "delete-attachment",
        ),
    ] {
        let path = format!("/api/office/announcements{path}");
        endpoint(
            doc,
            &path,
            method,
            id,
            "office.announcements",
            permission,
            body,
            response,
            "announcement",
            action,
        );
        let op = &mut doc["paths"][&path][method];
        op["tags"] = json!(["Announcements and notifications"]);
        op["x-exportdoc-communication"] = op["x-exportdoc-office"].take();
        op.as_object_mut().unwrap().remove("x-exportdoc-office");
        if matches!(action, "list" | "manage-list" | "receipts") {
            op["parameters"] = page_parameters(path.contains("{id}"));
            if action == "list" {
                op["parameters"]
                    .as_array_mut()
                    .unwrap()
                    .push(json!({"name":"unreadOnly","in":"query","schema":{"type":"boolean"}}));
            }
        }
        if action == "upload" {
            op["requestBody"] = doc_upload();
        }
        if action == "download" {
            op["responses"]["200"]["content"] =
                json!({"application/octet-stream":{"schema":{"type":"string","format":"binary"}}});
        }
    }
    for (path, id, method, action, response) in [
        ("", "ListNotifications", "get", "inbox", "NotificationPage"),
        (
            "/unread-count",
            "GetNotificationUnreadCount",
            "get",
            "unread-count",
            "NotificationCount",
        ),
        (
            "/read-all",
            "ReadAllNotifications",
            "post",
            "read-all",
            "NotificationCount",
        ),
        (
            "/{id}/read",
            "ReadNotification",
            "post",
            "notification-read",
            "SiteNotification",
        ),
    ] {
        let path = format!("/api/office/notifications{path}");
        endpoint(
            doc,
            &path,
            method,
            id,
            "office.notifications",
            "view",
            None,
            response,
            "notification",
            action,
        );
        let op = &mut doc["paths"][path][method];
        op["x-exportdoc-communication"] = op["x-exportdoc-office"].take();
        op.as_object_mut().unwrap().remove("x-exportdoc-office");
        if action == "inbox" {
            op["parameters"] = page_parameters(false);
            op["parameters"]
                .as_array_mut()
                .unwrap()
                .push(json!({"name":"unreadOnly","in":"query","schema":{"type":"boolean"}}));
        }
    }
    for (resource, label, order, actions) in [
        (
            "office.announcements",
            "公司公告",
            410,
            vec![
                ("view", "阅读与确认", "view"),
                ("manage", "发布管理", "manage"),
                ("receipts", "回执统计", "manage"),
            ],
        ),
        (
            "office.notifications",
            "站内通知",
            420,
            vec![("view", "个人收件箱", "view")],
        ),
    ] {
        let catalog = &mut doc["x-exportdoc-permissions"];
        let actions: Vec<_> = actions.into_iter().enumerate().map(|(i,(key,name,level))| json!({"key":key,"name":name,"description":name,"sortOrder":i*10,"navigationAccessLevel":level})).collect();
        catalog["resources"].as_array_mut().unwrap().push(json!({"key":resource,"name":label,"group":"行政办公","workspace":"office","moduleKey":resource,"sortOrder":order,"isTechnical":false,"supportsDataScope":false,"actions":actions}));
        catalog["modules"].as_array_mut().unwrap().push(json!({"key":resource,"name":label,"group":"行政办公","workspace":"office","sortOrder":order,"isTechnical":false}));
        catalog["editions"]["Full"]
            .as_array_mut()
            .unwrap()
            .push(json!(resource));
        for role in catalog["roles"].as_array_mut().unwrap() {
            for action in &actions {
                if action["key"] != "view"
                    && !matches!(role["code"].as_str(), Some("Admin" | "OfficeManager"))
                {
                    continue;
                }
                role["grants"].as_array_mut().unwrap().push(
                    json!({"resourceKey":resource,"action":action["key"],"dataScope":"company"}),
                );
            }
        }
    }
}
fn page_parameters(id: bool) -> Value {
    let mut p = vec![];
    if id {
        p.push(json!({"name":"id","in":"path","required":true,"schema":{"type":"integer","format":"int64","minimum":1}}));
    }
    for name in ["pageNumber", "pageSize"] {
        p.push(json!({"name":name,"in":"query","schema":{"type":"integer","minimum":1}}));
    }
    json!(p)
}
fn doc_upload() -> Value {
    json!({"required":true,"content":{"multipart/form-data":{"schema":object(json!({"file":{"type":"string","format":"binary"},"expectedVersion":{"type":"integer","format":"int64"}}), &["file","expectedVersion"])}}})
}
fn schemas(doc: &mut Value) {
    let s = &mut doc["components"]["schemas"];
    let string = json!({"type":"string"});
    let int = json!({"type":"integer","format":"int64"});
    let time = json!({"type":"string","format":"date-time"});
    let properties = json!({"requestKey":string,"expectedVersion":int,"title":{"type":"string","maxLength":200},"body":{"type":"string","maxLength":20000},"audienceDepartment":string,"isPinned":{"type":"boolean"},"startsAt":time,"expiresAt":time});
    let required = [
        "requestKey",
        "title",
        "body",
        "audienceDepartment",
        "isPinned",
        "startsAt",
        "expiresAt",
    ];
    s["AnnouncementSave"] = object(properties.clone(), &required);
    let mut p = properties;
    for (k,v) in json!({"id":int,"versionNumber":int,"publishVersion":int,"status":{"type":"string","enum":["Draft","Published","Withdrawn","Archived"]},"authorName":string,"publishedAt":string,"readAt":string,"createdAt":time,"updatedAt":time,"attachments":{"type":"array","items":reference("OaAttachment")}}).as_object().unwrap() { p[k] = v.clone(); }
    let mut required = required.to_vec();
    required.extend([
        "id",
        "versionNumber",
        "publishVersion",
        "status",
        "authorName",
        "publishedAt",
        "readAt",
        "createdAt",
        "updatedAt",
        "attachments",
    ]);
    s["Announcement"] = object(p, &required);
    s["AnnouncementDeleteResult"] = object(json!({"success":{"type":"boolean"}}), &["success"]);
    s["AnnouncementRead"] = object(json!({"publishVersion":int}), &["publishVersion"]);
    s["AnnouncementReceipt"] = object(
        json!({"id":int,"readerName":string,"publishVersion":int,"readAt":time}),
        &["id", "readerName", "publishVersion", "readAt"],
    );
    s["AnnouncementDepartment"] = object(json!({"code":string,"name":string}), &["code", "name"]);
    s["AnnouncementDepartmentList"] =
        json!({"type":"array","items":reference("AnnouncementDepartment")});
    s["SiteNotification"] = object(
        json!({"id":int,"title":string,"action":string,"requestId":int,"requestKind":string,"createdAt":time,"readAt":string,"status":{"type":"string","enum":["Unread","Read"]}}),
        &[
            "id",
            "title",
            "action",
            "requestId",
            "requestKind",
            "createdAt",
            "readAt",
            "status",
        ],
    );
    s["NotificationCount"] = object(json!({"unreadCount":int}), &["unreadCount"]);
    for (name, item) in [
        ("AnnouncementPage", "Announcement"),
        ("AnnouncementReceiptPage", "AnnouncementReceipt"),
        ("NotificationPage", "SiteNotification"),
    ] {
        s[name] = object(
            json!({"items":{"type":"array","items":reference(item)},"totalCount":int,"pageNumber":int,"pageSize":int}),
            &["items", "totalCount", "pageNumber", "pageSize"],
        );
    }
}
