//! Independent recovery use cases. No NativeService, business login, or working
//! schema is needed to unlock a repository and prepare a recovery plan.
use super::*;
use crate::paths;
use repository::{Repository, Visitor};
use std::{
    fs,
    sync::{
        Arc, Mutex,
        atomic::{AtomicBool, Ordering},
    },
    time::Instant,
};
struct Plan {
    id: String,
    request: zeroize::Zeroizing<Vec<u8>>,
    expires: Instant,
    target: String,
}
struct Selection {
    repository: Repository,
    plan: Option<Plan>,
}
struct BusyReset<'a>(&'a AtomicBool);
impl Drop for BusyReset<'_> {
    fn drop(&mut self) {
        self.0.store(false, Ordering::Release);
    }
}
pub struct RecoveryMaintenance {
    paths: RuntimePaths,
    #[cfg(feature = "postgres")]
    maintenance: zeroize::Zeroizing<String>,
    #[cfg(feature = "postgres")]
    application: zeroize::Zeroizing<String>,
    #[cfg(feature = "postgres")]
    owner: String,
    selection: Mutex<Option<Selection>>,
    state: Mutex<Value>,
    busy: AtomicBool,
    scope: Mutex<Option<crate::operation::OperationScope>>,
    worker: Mutex<Option<std::thread::JoinHandle<()>>>,
    _host: fs::File,
    #[cfg(feature = "postgres")]
    lease: Mutex<export_doc_storage::PostgresMaintenanceLease>,
}
impl RecoveryMaintenance {
    pub fn open(
        paths: RuntimePaths,
        maintenance: String,
        application: String,
        owner: String,
    ) -> Result<Arc<Self>> {
        #[cfg(not(feature = "postgres"))]
        {
            let _ = (paths, maintenance, application, owner);
            Err(unsupported("此构建不含 PostgreSQL 独立维护能力。"))
        }
        #[cfg(feature = "postgres")]
        {
            use fs2::FileExt;
            let directory = paths.data_root.join("Recovery");
            package::private_directory(&directory)?;
            let host_path = directory.join("maintenance-host.lock");
            paths::ensure_safe_absolute(&host_path).map_err(invalid)?;
            let host = fs::OpenOptions::new()
                .create(true)
                .truncate(false)
                .read(true)
                .write(true)
                .open(host_path)?;
            host.try_lock_exclusive()
                .map_err(|_| error(429, "此数据目录已有独立维护服务。"))?;
            #[cfg(feature = "postgres")]
            let lease =
                export_doc_storage::PostgresMaintenanceLease::acquire(&maintenance, &owner)?;
            let state_path = directory.join("maintenance-state.json");
            let previous = state_path
                .try_exists()?
                .then(|| super::scratch::read_json(&state_path))
                .transpose()?;
            let pending =
                migration::marker_path(&paths)?.try_exists()? || postgres::has_pending(&paths)?;
            let mut state = super::maintenance_state::reopen(previous.as_ref(), pending);
            state["artifacts"] = super::maintenance_artifacts::list(&paths)?;
            paths::atomic_write(
                &paths
                    .data_root
                    .join(export_doc_storage::maintenance::RECOVERY_MAINTENANCE_PENDING_FILE),
                b"{\"state\":\"Maintenance\"}",
            )
            .map_err(unavailable)?;
            paths::atomic_write(&state_path, &serde_json::to_vec(&state)?).map_err(unavailable)?;
            Ok(Arc::new(Self {
                paths,
                maintenance: zeroize::Zeroizing::new(maintenance),
                application: zeroize::Zeroizing::new(application),
                owner,
                selection: Mutex::new(None),
                state: Mutex::new(state),
                busy: AtomicBool::new(false),
                scope: Mutex::new(None),
                worker: Mutex::new(None),
                _host: host,
                #[cfg(feature = "postgres")]
                lease: Mutex::new(lease),
            }))
        }
    }
    pub fn status(&self) -> Result<Value> {
        let mut state = self
            .state
            .lock()
            .map_err(|_| unavailable("维护状态锁不可用。"))?
            .clone();
        state["busy"] = json!(self.busy.load(Ordering::Acquire));
        state["locations"] = json!(
            self.paths
                .file_storage_roots
                .keys()
                .map(|id| json!({"id":id,"name":id}))
                .collect::<Vec<_>>()
        );
        Ok(state)
    }
    fn state(&self, name: &str, message: &str, plan: &str) -> Result<()> {
        let mut state = self
            .state
            .lock()
            .map_err(|_| unavailable("维护状态锁不可用。"))?;
        state["state"] = json!(name);
        state["message"] = json!(message);
        state["planId"] = json!(plan);
        state.as_object_mut().unwrap().remove("failure");
        let snapshot = serde_json::to_vec(&*state)?;
        drop(state);
        paths::atomic_write(
            &self.paths.data_root.join("Recovery/maintenance-state.json"),
            &snapshot,
        )
        .map_err(unavailable)
    }
    pub fn start(self: &Arc<Self>, operation: &str, body: Value) -> Result<Value> {
        if !matches!(
            operation,
            "DiscardRecoveryArtifacts"
                | "UnlockRecoveryRepository"
                | "PreflightRecoveryPoint"
                | "ApplyRecoveryPoint"
                | "ListMaintenancePoints"
        ) {
            return Err(invalid("维护操作无效。"));
        }
        if self.busy.swap(true, Ordering::AcqRel) {
            return Err(error(429, "已有维护任务正在运行，请等待或取消。"));
        }
        let reset = BusyReset(&self.busy);
        let terminal = {
            let state = self
                .state
                .lock()
                .map_err(|_| unavailable("维护状态锁不可用。"))?;
            super::maintenance_state::finished(&state)
                .then(|| state["state"].as_str().unwrap().to_owned())
        };
        if operation != "DiscardRecoveryArtifacts" && terminal.is_some() {
            return Err(error(
                409,
                "本次维护已结束。如需再次恢复，请重新启动维护服务。",
            ));
        }
        let mut worker_slot = self
            .worker
            .lock()
            .map_err(|_| unavailable("维护线程状态不可用。"))?;
        let previous = worker_slot.take();
        if let Some(previous) = previous {
            let _ = previous.join();
        }
        let scope = crate::operation::OperationScope::detached(Duration::from_secs(6 * 3600));
        *self
            .scope
            .lock()
            .map_err(|_| unavailable("维护取消状态不可用。"))? = Some(scope.clone());
        if operation != "DiscardRecoveryArtifacts"
            && let Err(cause) = self.state("Preparing", "正在核验维护请求。", "")
        {
            return Err(cause);
        }
        let service = self.clone();
        let operation = operation.to_owned();
        let spawned = std::thread::Builder::new()
            .name("exportdoc-recovery-maintenance".into())
            .spawn(move || {
                let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                    scope.run(|| match operation.as_str() {
                        "DiscardRecoveryArtifacts" => service.discard(body),
                        "UnlockRecoveryRepository" => service.unlock(body),
                        "PreflightRecoveryPoint" => service.preflight(body),
                        "ListMaintenancePoints" => service.list(body),
                        _ => service.apply(body),
                    })
                }))
                .unwrap_or_else(|_| {
                    Err(unavailable(
                        "维护执行异常中断，请保留恢复现场并查询追踪编号。",
                    ))
                });
                if let Err(cause) = result {
                    let public =
                        cause.public_body(&paths::nonce().unwrap_or_else(|_| "maintenance".into()));
                    let _ = service.state(
                        terminal.as_deref().unwrap_or("Failed"),
                        public["message"]
                            .as_str()
                            .unwrap_or("维护失败，请查询追踪编号。"),
                        "",
                    );
                    if let Ok(mut state) = service.state.lock() {
                        state["failure"] = public;
                        let snapshot = serde_json::to_vec(&*state);
                        drop(state);
                        if let Ok(snapshot) = snapshot {
                            let _ = paths::atomic_write(
                                &service
                                    .paths
                                    .data_root
                                    .join("Recovery/maintenance-state.json"),
                                &snapshot,
                            );
                        }
                    }
                }
                let artifacts = crate::operation::OperationScope::detached(Duration::from_secs(30))
                    .run(|| super::maintenance_artifacts::list(&service.paths));
                if let Ok(mut state) = service.state.lock() {
                    match artifacts {
                        Ok(artifacts) => {
                            state["artifacts"] = artifacts;
                            state.as_object_mut().unwrap().remove("artifactFailure");
                        }
                        Err(cause) => {
                            state["artifactFailure"] = cause.public_body(
                                &paths::nonce().unwrap_or_else(|_| "maintenance".into()),
                            );
                        }
                    }
                }
                if let Ok(mut slot) = service.scope.lock() {
                    *slot = None;
                }
                service.busy.store(false, Ordering::Release);
            });
        match spawned {
            Ok(worker) => {
                *worker_slot = Some(worker);
                // The worker now owns the busy lifecycle.
                std::mem::forget(reset);
            }
            Err(_) => {
                return Err(unavailable("无法启动维护任务线程。"));
            }
        }
        self.status()
    }
    fn unlock(&self, mut body: Value) -> Result<()> {
        if body["oldInstanceIsolated"] != true {
            return Err(invalid(
                "请先停止并隔离旧 A，确认它不会继续写入原文件库或备份仓库。",
            ));
        }
        let password =
            zeroize::Zeroizing::new(body["password"].take().as_str().unwrap_or("").to_owned());
        let id = body["repositoryId"].as_str().unwrap_or("");
        let location = body["locationId"].as_str().unwrap_or("");
        // Releasing the old selection permits reopening the same directory lock.
        *self
            .selection
            .lock()
            .map_err(|_| unavailable("恢复仓库状态不可用。"))? = None;
        let portable = body["portableFile"].as_str().unwrap_or("");
        let repo = if !portable.is_empty() {
            if !paths::valid_file_name(portable) || !portable.ends_with(".edmpoint") {
                return Err(invalid("请选择维护收件目录中的 .edmpoint 文件。"));
            }
            let input = self.paths.data_root.join("Recovery/Inbox").join(portable);
            paths::ensure_safe_absolute(&input).map_err(invalid)?;
            let (repo, _) = Repository::from_portable(fs::File::open(input)?, &password)?;
            if !id.is_empty() && repo.id != id {
                return Err(invalid("便携文件仓库编号与所选仓库不一致。"));
            }
            repo
        } else {
            let io: Box<dyn repository::io::ObjectStore> = match body["sourceKind"]
                .as_str()
                .unwrap_or("Directory")
            {
                "Directory" => Box::new(repository::directory(
                    &self.paths,
                    location,
                    id,
                    None,
                    false,
                )?),
                "WebDav" => Box::new(repository::remote::Reader::open(
                    repository::remote::Target::WebDav(cloud::RecoveryTarget::from_credentials(
                        body["webDav"]["url"].as_str().unwrap_or(""),
                        body["webDav"]["userName"].as_str().unwrap_or(""),
                        body["webDavPassword"].as_str().unwrap_or(""),
                        id,
                    )?),
                )?),
                "S3" => {
                    #[cfg(feature = "s3-backup")]
                    {
                        let settings = serde_json::from_value(body["s3"].clone())
                            .map_err(|_| invalid("S3 恢复源配置无效。"))?;
                        Box::new(repository::remote::Reader::open(
                            repository::remote::Target::S3(s3::Target::new(
                                settings,
                                id,
                                body["secretAccessKey"].as_str().unwrap_or(""),
                                body["sessionToken"].as_str().unwrap_or(""),
                            )?),
                        )?)
                    }
                    #[cfg(not(feature = "s3-backup"))]
                    {
                        return Err(unsupported("此构建不含 S3 恢复源能力。"));
                    }
                }
                _ => return Err(invalid("恢复源类型无效。")),
            };
            Repository::open(io, id, &password, false)?
        };
        *self
            .selection
            .lock()
            .map_err(|_| unavailable("恢复仓库状态不可用。"))? = Some(Selection {
            repository: repo,
            plan: None,
        });
        self.list(json!({"after":""}))
    }
    fn list(&self, body: Value) -> Result<()> {
        let mut selected = self
            .selection
            .lock()
            .map_err(|_| unavailable("恢复仓库状态不可用。"))?;
        let selected = selected
            .as_mut()
            .ok_or_else(|| invalid("请先解锁恢复仓库。"))?;
        selected.plan = None;
        let page = selected
            .repository
            .page(body["after"].as_str().unwrap_or(""), 100)?;
        {
            let mut state = self
                .state
                .lock()
                .map_err(|_| unavailable("维护状态锁不可用。"))?;
            state["nextCursor"] = json!(page.next_cursor);
            state["points"]=json!(page.items.iter().map(|p|json!({"id":p.id,"createdAt":p.created_at,"provider":p.provider,"sizeBytes":p.size_bytes})).collect::<Vec<_>>());
        }
        self.state("Unlocked", "仓库已解锁，请选择恢复点与目标。", "")
    }
    fn preflight(&self, body: Value) -> Result<()> {
        let mut selected = self
            .selection
            .lock()
            .map_err(|_| unavailable("恢复仓库状态不可用。"))?;
        let selected = selected
            .as_mut()
            .ok_or_else(|| invalid("请先解锁恢复仓库。"))?;
        let id = body["pointId"].as_str().unwrap_or("");
        self.state("Preflight", "正在核验恢复点的全部文件与目标空间。", "")?;
        let mut sizes = Sizes {
            working: 0,
            attachments: 0,
        };
        let point = selected.repository.verify(id, &mut sizes)?;
        if point.provider != "PostgreSQL" {
            return Err(invalid(
                "服务器维护仅恢复 PostgreSQL 恢复点；SQLite 请使用桌面恢复。",
            ));
        }
        let mode = body["fileMode"].as_str().unwrap_or("");
        let location = body["targetLocation"].as_str().unwrap_or("");
        let target = match mode {
            "Local" => {
                sizes.working = sizes.working.saturating_add(sizes.attachments);
                None
            }
            "Directory" => Some(
                self.paths
                    .file_storage_roots
                    .get(location)
                    .ok_or_else(|| invalid("恢复目标未登记。"))?,
            ),
            "Reconnect" => {
                if point.source_storage["backend"]["kind"] == "local"
                    && !point.source_storage["backend"]["directory"]
                        .as_str()
                        .unwrap_or("")
                        .starts_with('@')
                {
                    return Err(invalid("恢复点原文件库在旧 A 本机，请选择重建。"));
                }
                None
            }
            "S3" => {
                #[cfg(feature = "s3-storage")]
                {
                    let settings = serde_json::from_value(body["s3"].clone())
                        .map_err(|_| invalid("请填写 S3 重建目标。"))?;
                    let target = crate::engine::s3_transport::Target::new(
                        settings,
                        &paths::nonce().map_err(unavailable)?,
                        body["secretAccessKey"].as_str().unwrap_or(""),
                        body["sessionToken"].as_str().unwrap_or(""),
                    )?;
                    if !target.create_small("probe", b"preflight")? {
                        return Err(invalid("S3 目标不支持条件创建。"));
                    }
                    let verified = target.read_small("probe", 64)?;
                    target.delete("probe")?;
                    if verified.as_deref() != Some(b"preflight") {
                        return Err(unavailable("S3 目标回读核验失败。"));
                    }
                    None
                }
                #[cfg(not(feature = "s3-storage"))]
                {
                    return Err(unsupported("此构建不含 S3 重建能力。"));
                }
            }
            _ => return Err(invalid("附件恢复方式无效。")),
        };
        // Database restore also requires PostgreSQL volume capacity, validated by
        // pg_restore. This estimate concerns only this application's working disk.
        space(&self.paths.data_root, sizes.working)?;
        if let Some(target) = target {
            space(target, sizes.attachments)?;
        }
        let plan = paths::nonce().map_err(unavailable)?;
        let target = self.target_fingerprint(&body)?;
        paths::atomic_write(&self.paths.data_root.join("Recovery/maintenance-plan.json"),&serde_json::to_vec(&json!({"planId":plan,"pointId":id,"fileMode":mode,"targetLocation":location,"targetFingerprint":target,"manifestDigest":point.manifest_digest,"state":"Ready"}))?).map_err(unavailable)?;
        selected.plan = Some(Plan {
            id: plan.clone(),
            request: zeroize::Zeroizing::new(serde_json::to_vec(&body)?),
            expires: Instant::now() + Duration::from_secs(900),
            target,
        });
        self.state("Ready","恢复点已通过完整性预检。确认目标后执行恢复；PostgreSQL 卷仍须容纳目标库和恢复前安全副本。",&plan)
    }
    fn apply(&self, body: Value) -> Result<()> {
        let mut selected = self
            .selection
            .lock()
            .map_err(|_| unavailable("恢复仓库状态不可用。"))?;
        let selected = selected
            .as_mut()
            .ok_or_else(|| invalid("请先解锁并预检恢复点。"))?;
        let plan_id = body["planId"].as_str().unwrap_or("").to_owned();
        let plan = selected
            .plan
            .as_ref()
            .ok_or_else(|| invalid("没有有效恢复计划，请先执行预检。"))?;
        let body: Value = serde_json::from_slice(&plan.request)?;
        if plan_id != plan.id
            || Instant::now() > plan.expires
            || self.target_fingerprint(&body)? != plan.target
        {
            return Err(invalid(
                "恢复计划已过期或目标介质/数据库已变更，请重新预检。",
            ));
        }
        let marker = migration::marker_path(&self.paths)?;
        if marker.try_exists()? {
            match package::snapshot(&marker)? {
                Some(package::SnapshotRestore::Repository { point_id, .. })
                    if body["pointId"] == point_id => {}
                _ => {
                    return Err(invalid(
                        "已有不同恢复任务等待切换，请先处理原维护记录，不能用新计划接管。",
                    ));
                }
            }
        }
        if !marker.try_exists()? {
            self.state("ApplyingFiles", "正在准备并核验目标文件库。", &plan_id)?;
            let gate = package::lock(&self.paths)?;
            let prepared = repository::restore::prepare(
                &self.paths,
                &selected.repository,
                body["pointId"].as_str().unwrap_or(""),
                &marker,
                &body,
            )?;
            prepared.verify_postgres(&self.paths)?;
            paths::atomic_write(&marker, &prepared.marker_content(None)?).map_err(unavailable)?;
            drop(gate);
        }
        self.state(
            "Committing",
            "正在恢复数据库并完成配置切换，请等待收尾。",
            &plan_id,
        )?;
        #[cfg(feature = "postgres")]
        crate::operation::OperationScope::detached(Duration::from_secs(2 * 3600)).run(|| {
            let mut lease = self
                .lease
                .lock()
                .map_err(|_| unavailable("维护数据库锁不可用。"))?;
            postgres::apply_pending_with_lease(
                &self.paths,
                &self.maintenance,
                &self.owner,
                &self.application,
                &mut lease,
            )
        })?;
        fs::remove_file(
            self.paths
                .data_root
                .join(export_doc_storage::maintenance::RECOVERY_MAINTENANCE_PENDING_FILE),
        )?;
        self.state(
            "Completed",
            "恢复完成。请停止维护服务，启动业务服务并使用恢复后的账号登录；安全副本继续保留。",
            &plan_id,
        )
    }
    pub fn cancel(&self) -> Result<Value> {
        let state = self.status()?;
        if state["state"] == "Committing" {
            return Err(error(409, "正在完成数据库与配置切换，请等待有界收尾。"));
        }
        if let Some(scope) = self
            .scope
            .lock()
            .map_err(|_| unavailable("维护取消状态不可用。"))?
            .as_ref()
        {
            scope.cancel();
        }
        self.status()
    }
    fn discard(&self, body: Value) -> Result<()> {
        if migration::marker_path(&self.paths)?.try_exists()? || postgres::has_pending(&self.paths)?
        {
            return Err(error(
                409,
                "存在等待切换的恢复，必须先继续原任务，不能清理。",
            ));
        }
        super::maintenance_artifacts::discard(&self.paths, &body)?;
        let state = self.status()?;
        let name = if super::maintenance_state::finished(&state) {
            state["state"].as_str().unwrap()
        } else {
            "Idle"
        };
        self.state(name, "未发布的恢复工件已清理。", "")
    }
    pub fn abandon(&self) -> Result<Value> {
        if self.busy.swap(true, Ordering::AcqRel) {
            return Err(error(409, "已有维护任务正在运行，请等待结束后再放弃维护。"));
        }
        let _reset = BusyReset(&self.busy);
        if migration::marker_path(&self.paths)?.try_exists()? || postgres::has_pending(&self.paths)?
        {
            return Err(error(
                409,
                "已有执行中或等待切换的恢复，不能解除维护保护。请完成或继续原恢复任务。",
            ));
        }
        let fence = self
            .paths
            .data_root
            .join(export_doc_storage::maintenance::RECOVERY_MAINTENANCE_PENDING_FILE);
        if fence.try_exists()? {
            fs::remove_file(fence)?;
        }
        *self
            .selection
            .lock()
            .map_err(|_| unavailable("恢复状态不可用。"))? = None;
        self.state("Abandoned","未切换业务数据。请停止维护服务后重新启动业务服务；未发布的外部恢复目录保留在维护记录中。","")?;
        self.status()
    }
    fn target_fingerprint(&self, body: &Value) -> Result<String> {
        let mut fingerprint = paths::volume_identity(&self.paths.data_root).map_err(unavailable)?;
        if body["fileMode"] == "Directory" {
            let target = self
                .paths
                .file_storage_roots
                .get(body["targetLocation"].as_str().unwrap_or(""))
                .ok_or_else(|| invalid("恢复目标未登记。"))?;
            if !target.is_dir() {
                return Err(invalid("目标介质未连接。"));
            }
            fingerprint.push_str(&paths::volume_identity(target).map_err(unavailable)?);
        }
        #[cfg(feature = "postgres")]
        fingerprint.push_str(
            &self
                .lease
                .lock()
                .map_err(|_| unavailable("维护数据库锁不可用。"))?
                .fingerprint()?,
        );
        Ok(crate::engine::media::digest(fingerprint.as_bytes()))
    }
    pub fn close(&self) -> Result<()> {
        if self.status()?["state"] != "Committing" {
            let _ = self.cancel();
        }
        if let Some(worker) = self
            .worker
            .lock()
            .map_err(|_| unavailable("维护线程状态不可用。"))?
            .take()
        {
            worker
                .join()
                .map_err(|_| unavailable("维护线程异常退出。"))?;
        }
        Ok(())
    }
}
struct Sizes {
    working: u64,
    attachments: u64,
}
impl Visitor for Sizes {
    fn begin(&mut self, role: &str, _: &str, size: u64, _: &str) -> Result<()> {
        let target = if role == "Attachment" {
            &mut self.attachments
        } else {
            &mut self.working
        };
        *target = target
            .checked_add(size)
            .ok_or_else(|| invalid("恢复容量计数溢出。"))?;
        Ok(())
    }
    fn data(&mut self, _: &[u8]) -> Result<()> {
        Ok(())
    }
    fn end(&mut self) -> Result<()> {
        Ok(())
    }
}
fn space(root: &Path, bytes: u64) -> Result<()> {
    paths::ensure_safe_absolute(root).map_err(invalid)?;
    let available = fs2::available_space(root)?;
    let required = bytes.saturating_add(256 * 1024 * 1024);
    if available < required {
        return Err(invalid(format!(
            "恢复目标可用空间不足：需要至少 {required} 字节，可用 {available} 字节。请扩容或选择其它登记位置。"
        )));
    }
    Ok(())
}
