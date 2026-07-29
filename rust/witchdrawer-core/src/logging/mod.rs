use chrono::Local;
use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::PathBuf;
use std::sync::Mutex;

/// 对应 C# FileAppLogger
pub struct FileLogger {
    log_dir: PathBuf,
    retention_days: u32,
    file: Mutex<Option<std::fs::File>>,
}

impl FileLogger {
    pub fn new(log_dir: impl Into<PathBuf>, retention_days: u32) -> Self {
        let log_dir = log_dir.into();
        let _ = fs::create_dir_all(&log_dir);
        let logger = Self {
            log_dir,
            retention_days,
            file: Mutex::new(None),
        };
        logger.trim_old_logs();
        logger
    }

    pub fn info(&self, message: &str) {
        self.write("INFO", message);
    }

    pub fn error(&self, message: &str) {
        self.write("ERROR", message);
    }

    fn write(&self, level: &str, message: &str) {
        let now = Local::now();
        let line = format!(
            "{} [{}] {}\n",
            now.format("%Y-%m-%dT%H:%M:%S%.3f%:z"),
            level,
            message
        );
        let filename = format!("{}.log", now.format("%Y-%m-%d"));
        let path = self.log_dir.join(filename);

        let mut guard = self.file.lock().unwrap();
        if let Ok(mut f) = OpenOptions::new().create(true).append(true).open(&path) {
            let _ = f.write_all(line.as_bytes());
        }
        // Keep the file handle for potential reuse (but not critical)
        *guard = None;
    }

    fn trim_old_logs(&self) {
        let cutoff = Local::now() - chrono::Duration::days(self.retention_days as i64);
        if let Ok(entries) = fs::read_dir(&self.log_dir) {
            for entry in entries.flatten() {
                if let Ok(metadata) = entry.metadata() {
                    if let Ok(modified) = metadata.modified() {
                        let modified_dt: chrono::DateTime<Local> = modified.into();
                        if modified_dt < cutoff {
                            let _ = fs::remove_file(entry.path());
                        }
                    }
                }
            }
        }
    }
}
