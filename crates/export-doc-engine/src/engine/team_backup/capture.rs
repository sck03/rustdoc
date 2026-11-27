//! Small durable inventory captured from the same database snapshot as the dump.
use super::*;
use export_doc_storage::FileObject;
use std::{
    fs,
    io::{BufRead, BufReader, BufWriter, Write},
};

const MAGIC: &str = "EDM-SNAPSHOT-INVENTORY-1";
pub(super) fn path(database: &Path) -> PathBuf {
    database.with_extension("inventory.jsonl")
}

pub(super) fn write(
    database: &Path,
    mut page: impl FnMut(&str) -> Result<Vec<FileObject>>,
) -> Result<()> {
    let mut file = fs::OpenOptions::new()
        .create_new(true)
        .write(true)
        .open(path(database))?;
    let mut output = BufWriter::new(&mut file);
    writeln!(output, "{MAGIC}")?;
    let mut cursor = String::new();
    loop {
        let rows = page(&cursor)?;
        if rows.is_empty() {
            break;
        }
        for row in rows {
            crate::operation::check()?;
            if row.digest <= cursor {
                return Err(invalid("快照文件清单顺序无效。"));
            }
            writeln!(output, "{}", serde_json::to_string(&row)?)?;
            cursor = row.digest;
        }
    }
    writeln!(output, "END")?;
    output.flush()?;
    drop(output);
    file.sync_all()?;
    Ok(())
}

pub(super) struct Inventory {
    reader: BufReader<fs::File>,
    previous: String,
    ended: bool,
}
impl Inventory {
    pub(super) fn open(database: &Path) -> Result<Self> {
        let mut result = Self {
            reader: BufReader::new(fs::File::open(path(database))?),
            previous: String::new(),
            ended: false,
        };
        if result.line()? != MAGIC {
            return Err(invalid("快照文件清单格式无效。"));
        }
        Ok(result)
    }
    fn line(&mut self) -> Result<String> {
        use std::io::Read;
        let mut value = String::new();
        self.reader.by_ref().take(1025).read_line(&mut value)?;
        if value.len() > 1024 || !value.ends_with('\n') {
            return Err(invalid("快照文件清单已截断或行过长。"));
        }
        Ok(value.trim_end().to_owned())
    }
    pub(super) fn page(&mut self) -> Result<Vec<FileObject>> {
        if self.ended {
            return Ok(vec![]);
        }
        let mut page = vec![];
        for _ in 0..100 {
            crate::operation::check()?;
            let line = self.line()?;
            if line == "END" {
                if !self.reader.fill_buf()?.is_empty() {
                    return Err(invalid("快照文件清单结束后含多余内容。"));
                }
                self.ended = true;
                break;
            }
            let row: FileObject = serde_json::from_str(&line)?;
            if row.digest <= self.previous {
                return Err(invalid("快照文件清单重复或顺序无效。"));
            }
            self.previous = row.digest.clone();
            page.push(row);
        }
        Ok(page)
    }
}
