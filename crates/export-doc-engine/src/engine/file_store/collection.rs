//! Durable deletion progress shared by collection and archive journal operations.
#[derive(Default)]
pub(super) struct CollectionProgress {
    pub objects: usize,
    pub bytes: u64,
    pub stage: &'static str,
}
impl CollectionProgress {
    pub fn removed(&mut self, size: u64) {
        self.objects += 1;
        self.bytes += size;
    }
    pub fn failure(&self, mut cause: crate::api::ApiError) -> crate::api::ApiError {
        if self.objects > 0 || cause.outcome_unknown || cause.partially_committed {
            let reason = cause.public_body("")["message"]
                .as_str()
                .unwrap_or("请查询任务回执。")
                .to_owned();
            cause.partially_committed |= self.objects > 0;
            cause.code = Some(if cause.partially_committed {
                crate::api::ErrorCode::PartialCommit
            } else {
                crate::api::ErrorCode::FileUnavailable
            });
            cause.message = format!(
                "回收已确认删除 {} 个对象、{} 字节；{}未完成：{reason} 请先查询任务回执并重新预览回收计划，剩余清理记录已保留。",
                self.objects, self.bytes, self.stage
            );
        }
        cause
    }
}
