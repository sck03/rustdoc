//! Application service construction and readiness; no transport-specific code.
use super::*;

impl NativeService {
    pub fn open(paths: RuntimePaths) -> Result<Arc<Self>> {
        Self::open_with_retention(paths, Default::default())
    }
    pub fn open_with_retention(
        paths: RuntimePaths,
        retention: tasks::retention::Retention,
    ) -> Result<Arc<Self>> {
        export_doc_report::configure(&paths.font_path);
        let store = Arc::new(Store::open(&paths)?);
        auth::seed(&store)?;
        packing::seed(&store)?;
        #[cfg(feature = "mail")]
        email::recover(&store)?;
        let jobs = tasks::Jobs::with_retention(store.clone(), retention)?;
        Ok(Arc::new(Self {
            maintenance: RwLock::new(()),
            protector: crate::secrets::Protector::new(&paths.data_root),
            paths,
            store,
            sessions: auth::Sessions::default(),
            jobs,
            bootstrap_token: String::new(),
            clock: crate::clock::BusinessClock::default(),
            license_gate: Default::default(),
            packing_gate: Default::default(),
            #[cfg(feature = "ocr")]
            ocr_gate: Default::default(),
            #[cfg(feature = "exchange-rates")]
            exchange: Default::default(),
        }))
    }
    #[cfg(feature = "postgres")]
    pub fn open_postgres(
        paths: RuntimePaths,
        connection_string: &str,
        bootstrap_token: String,
        clock: crate::clock::BusinessClock,
    ) -> Result<Arc<Self>> {
        Self::open_postgres_with_retention(
            paths,
            connection_string,
            bootstrap_token,
            clock,
            Default::default(),
        )
    }
    #[cfg(feature = "postgres")]
    pub fn open_postgres_with_retention(
        paths: RuntimePaths,
        connection_string: &str,
        bootstrap_token: String,
        clock: crate::clock::BusinessClock,
        retention: tasks::retention::Retention,
    ) -> Result<Arc<Self>> {
        export_doc_report::configure(&paths.font_path);
        super::team_backup::postgres::ensure_no_pending(&paths)?;
        let store = Arc::new(Store::open_postgres(&paths, connection_string)?);
        packing::seed(&store)?;
        #[cfg(feature = "mail")]
        email::recover(&store)?;
        if store.all("users")?.is_empty() && bootstrap_token.len() < 32 {
            return Err(invalid("空库首次启动需要至少 32 字符的一次性部署令牌。"));
        }
        let jobs = tasks::Jobs::with_retention(store.clone(), retention)?;
        Ok(Arc::new(Self {
            maintenance: RwLock::new(()),
            protector: crate::secrets::Protector::new(&paths.data_root),
            paths,
            store,
            sessions: auth::Sessions::default(),
            jobs,
            bootstrap_token,
            clock,
            license_gate: Default::default(),
            packing_gate: Default::default(),
            #[cfg(feature = "ocr")]
            ocr_gate: Default::default(),
            #[cfg(feature = "exchange-rates")]
            exchange: Default::default(),
        }))
    }
    pub fn health(&self) -> Result<()> {
        self.jobs.health()?;
        self.store.connection()?.health().map_err(Into::into)
    }
}
