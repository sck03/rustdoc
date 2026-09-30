//! Application service construction and readiness; no transport-specific code.
use super::*;

impl NativeService {
    pub fn runtime_metrics(&self) -> Result<Value> {
        Ok(
            json!({"checkedAt":store::timestamp(),"storage":self.store.metrics(),"jobs":self.jobs.metrics()?,"http":null}),
        )
    }
    pub fn open(paths: RuntimePaths) -> Result<Arc<Self>> {
        Self::open_with_retention(paths, Default::default())
    }
    pub fn open_with_retention(
        paths: RuntimePaths,
        retention: tasks::retention::Retention,
    ) -> Result<Arc<Self>> {
        Self::open_desktop(paths, retention, "Full")
    }
    pub fn open_desktop(
        paths: RuntimePaths,
        retention: tasks::retention::Retention,
        edition: &str,
    ) -> Result<Arc<Self>> {
        let edition =
            export_doc_domain::permissions::ProductEdition::parse(edition).map_err(invalid)?;
        let mut store = Store::open(&paths)?;
        store.edition = edition;
        let store = Arc::new(store);
        auth::seed(&store)?;
        packing::seed(&store)?;
        #[cfg(feature = "mail")]
        email::recover(&store)?;
        let jobs = tasks::Jobs::with_retention(store.clone(), retention)?;
        Ok(Arc::new(Self {
            maintenance: RwLock::new(()),
            protector: crate::secrets::Protector::new(&paths.data_root),
            report_fonts: Arc::new(export_doc_report::Fonts::new(&paths.font_path)),
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
        Self::open_postgres_configured(
            paths,
            connection_string,
            bootstrap_token,
            clock,
            retention,
            Default::default(),
        )
    }
    #[cfg(feature = "postgres")]
    pub fn open_postgres_configured(
        paths: RuntimePaths,
        connection_string: &str,
        bootstrap_token: String,
        clock: crate::clock::BusinessClock,
        retention: tasks::retention::Retention,
        pool: export_doc_storage::pool::PoolOptions,
    ) -> Result<Arc<Self>> {
        super::team_backup::postgres::ensure_no_pending(&paths)?;
        let store = Arc::new(Store::open_postgres_with_pool(
            &paths,
            connection_string,
            pool,
        )?);
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
            report_fonts: Arc::new(export_doc_report::Fonts::new(&paths.font_path)),
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
        self.store.health()
    }
}
