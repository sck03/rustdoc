//! Self-service account credentials; independent of business roles and unit grants.
use super::*;

pub(super) fn matches_password(
    password: &str,
    salt: &[u8],
    expected: &[u8],
    rounds: u32,
) -> Result<bool> {
    if !(ITERATIONS..=2_000_000).contains(&rounds) || salt.len() != 32 || expected.len() != 32 {
        return Err(unavailable("账号凭证参数损坏。"));
    }
    let actual = pbkdf2::pbkdf2_hmac_array::<Sha256, 32>(password.as_bytes(), salt, rounds);
    Ok(actual.as_slice().ct_eq(expected).unwrap_u8() == 1)
}

impl Sessions {
    pub fn change_password(&self, store: &Store, token: &str, body: &Value) -> Result<Value> {
        export_doc_contracts::validation::provided_fields(
            contracts::request("ChangeOwnPassword"),
            body,
        )
        .map_err(invalid)?;
        let current = body["currentPassword"]
            .as_str()
            .ok_or_else(|| invalid("请填写原密码。"))?;
        let new = body["newPassword"]
            .as_str()
            .ok_or_else(|| invalid("请填写新密码。"))?;
        if current.chars().count() > 128 {
            return Err(invalid("原密码最多 128 个字符。"));
        }
        validate_new_password(new)?;
        if current == new {
            return Err(invalid("新密码不能与原密码相同。"));
        }
        let session = self.validated_session(store, token)?;
        let (user, credential) = store.read(|tx| {
            Ok((
                store::get(tx, "users", session.user_id)?,
                tx.credential(session.user_id)?
                    .ok_or_else(|| unavailable("账号凭证缺失。"))?,
            ))
        })?;
        let reservation = self.attempts.reserve(
            &username_key(&text(&user, "username"))?,
            crate::operation::source(),
        )?;
        let verified = matches_password(
            current,
            &credential.salt,
            &credential.hash,
            credential.iterations,
        )?;
        reservation.finish(verified)?;
        if !verified {
            return Err(invalid("原密码不正确。"));
        }
        let (salt, hash) = password_hash(new)?;
        store.transaction(|tx| {
            // A concurrent reset, revocation or logout cannot authorize this write.
            self.identity(token)?;
            let current = store::get(tx, "users", session.user_id)?;
            if current["isActive"] != true || session_version_in(tx, &current)? != session.version {
                return Err(error(409, "账号已变化，请重新登录后修改密码。"));
            }
            let actor = current_actor_in(tx, session.user_id, store.edition)?;
            let identity = text(&current, "username");
            store::save(
                tx,
                "users",
                session.user_id,
                current,
                Some(identity),
                &actor,
                "change-password",
            )?;
            tx.set_credential(session.user_id, &salt, &hash, ITERATIONS)?;
            Ok(())
        })?;
        self.values
            .lock()
            .map_err(|_| unavailable("会话状态异常。"))?
            .retain(|_, value| value.user_id != session.user_id);
        Ok(json!({"success":true}))
    }
}
