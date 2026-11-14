//! Rotate credentials for the same namespace without reading or moving objects.
use super::*;
use crate::engine::{
    auth,
    records::text,
    store::{Actor, Store},
};
use serde_json::Value;

pub(super) fn rotate(store: &Store, actor: &Actor, body: &Value) -> Result<()> {
    let _maintenance = super::admin::maintenance_lock(&store.files)?;
    let source = store.files.configuration()?;
    if body["expectedVersion"] != source.credential_revision {
        return Err(conflict("存储凭据已变化，请刷新。"));
    }
    let mut target = source.clone();
    let Backend::S3 {
        settings,
        secret,
        token,
    } = &mut target.backend
    else {
        return Err(invalid("当前不是 S3 存储。"));
    };
    settings.access_key_id = text(body, "accessKeyId");
    *secret = store
        .files
        .protector
        .protect("file-storage-secret", &text(body, "secret"))
        .map_err(unavailable)?;
    *token = store
        .files
        .protector
        .protect("file-storage-token", &text(body, "token"))
        .map_err(unavailable)?;
    target.credential_revision += 1;
    store.files.check_identity(&target)?;
    store.with_writes_paused(|| {
        let current = store.read(|tx| auth::refresh_actor_in(tx, actor))?;
        if !current.admin {
            return Err(crate::engine::error::error(403, "管理员权限已撤销。"));
        }
        let config = store.files.configuration()?;
        if config.identity != source.identity
            || config.credential_revision != source.credential_revision
        {
            return Err(conflict("存储配置已变化，请刷新。"));
        }
        store.files.activate(target)
    })
}
