//! Decode and hash immutable input before acquiring the business write transaction.
use super::*;
use export_doc_storage::BlobWrite;

pub(in crate::engine) struct ValidatedImage<'a> {
    bytes: &'a [u8],
    media_type: &'static str,
    hash: String,
    id: String,
}

impl<'a> ValidatedImage<'a> {
    pub fn new(bytes: &'a [u8], limit: usize) -> Result<Self> {
        crate::operation::check()?;
        let media_type = media::image_type(bytes, limit)?;
        let hash = media::digest(bytes);
        let extension = if media_type == "image/png" {
            "png"
        } else {
            "jpg"
        };
        let id = format!("img-{hash}.{extension}");
        crate::operation::check()?;
        Ok(Self {
            bytes,
            media_type,
            hash,
            id,
        })
    }

    pub fn media_type(&self) -> &str {
        self.media_type
    }

    pub fn register(&self, tx: &Connection, actor: &Actor, file_name: &str) -> Result<Value> {
        let Self {
            bytes,
            media_type,
            hash,
            id,
        } = self;
        if let Some(mut previous) = tx.find_identity(KIND, id)? {
            let record_id = previous["id"]
                .as_i64()
                .ok_or_else(|| unavailable("图片资源记录损坏。"))?;
            let info = tx
                .blob_info(record_id, KIND)?
                .ok_or_else(|| unavailable("图片资源内容缺失。"))?;
            if info.size != bytes.len() as u64
                || info.digest != *hash
                || info.media_type != *media_type
                || previous["byteLength"] != bytes.len() as u64
                || previous["sha256"] != *hash
                || previous["mediaType"] != *media_type
            {
                return Err(unavailable("图片资源元数据或容量无效。"));
            }
            let blob = tx
                .blob(record_id, KIND)?
                .ok_or_else(|| unavailable("图片资源内容缺失。"))?;
            // Equality with validated bytes proves integrity without decoding or hashing again.
            if blob.content != *bytes || blob.digest != *hash || blob.media_type != *media_type {
                return Err(unavailable("图片资源内容校验失败。"));
            }
            if owns(actor, &previous) {
                return Ok(previous);
            }
            previous["uploaders"]
                .as_array_mut()
                .ok_or_else(|| unavailable("图片归属记录损坏。"))?
                .push(json!(actor.id));
            return store::save(
                tx,
                KIND,
                record_id,
                previous,
                Some(id.clone()),
                actor,
                "image-own",
            );
        }
        if tx
            .query_generic(&export_doc_storage::GenericQuery {
                kind: KIND,
                scope: export_doc_storage::DataScope {
                    all: true,
                    ..Default::default()
                },
                fields: &[("id", "id")],
                limit: 1,
                ..Default::default()
            })?
            .0
            >= 1000
        {
            return Err(conflict("图片资源已达到 1000 个，请回收未使用图片后重试。"));
        }
        let record = store::save(
            tx,
            KIND,
            0,
            json!({"resourceId":id,"mediaType":media_type,"sha256":hash,"byteLength":bytes.len(),"altText":file_name,"uploaders":[actor.id]}),
            Some(id.clone()),
            actor,
            "image-upload",
        )?;
        tx.insert_blob(&BlobWrite {
            kind: KIND,
            record_id: record["id"].as_i64().unwrap(),
            file_name: id,
            media_type,
            digest: hash,
            content: bytes,
            created_at: &store::timestamp(),
        })?;
        Ok(record)
    }
}
