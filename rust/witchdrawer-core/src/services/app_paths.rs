use std::env;
use std::fs;
use std::path::PathBuf;

use crate::models::{AppError, AppResult};

/// Environment variable to override the default data root directory.
pub const DATA_DIR_ENV_VAR: &str = "WITCHDRAWER_DATA_DIR";

/// Default directory name under LocalApplicationData.
pub const DEFAULT_ROOT_DIR_NAME: &str = "WitchDrawer";

/// Database file name.
pub const DATABASE_FILE_NAME: &str = "witchdrawer.db";

/// Boxes sub-directory name.
pub const BOXES_DIR_NAME: &str = "Boxes";

/// Logs sub-directory name.
pub const LOGS_DIR_NAME: &str = "logs";

/// Writability probe file name (created then deleted).
const WRITABILITY_PROBE: &str = ".witchdrawer_write_probe";

/// Application local data paths.
/// Mirrors the C# `AppPaths` record.
#[derive(Debug, Clone)]
pub struct AppPaths {
    root: PathBuf,
}

impl AppPaths {
    /// Create a new `AppPaths` pointing at the given root directory.
    pub fn new(root: PathBuf) -> Self {
        Self { root }
    }

    /// Root data directory.
    pub fn root(&self) -> &PathBuf {
        &self.root
    }

    /// `Boxes/` sub-directory.
    pub fn boxes_directory(&self) -> PathBuf {
        self.root.join(BOXES_DIR_NAME)
    }

    /// Path to `witchdrawer.db`.
    pub fn database_path(&self) -> PathBuf {
        self.root.join(DATABASE_FILE_NAME)
    }

    /// `logs/` sub-directory.
    pub fn logs_directory(&self) -> PathBuf {
        self.root.join(LOGS_DIR_NAME)
    }

    /// Resolve the data path for the current user:
    /// 1. `WITCHDRAWER_DATA_DIR` env var (if set and valid)
    /// 2. `%LOCALAPPDATA%\WitchDrawer` (Windows) or `$XDG_DATA_HOME/WitchDrawer`
    pub fn for_current_user() -> AppResult<Self> {
        if let Ok(configured) = env::var(DATA_DIR_ENV_VAR) {
            let trimmed = configured.trim().to_string();
            if !trimmed.is_empty() {
                let paths = Self::new(PathBuf::from(&trimmed));
                paths.ensure_created_and_writable()?;
                return Ok(paths);
            }
        }

        // Try LOCALAPPDATA first (Windows), then XDG_DATA_HOME (Linux/macOS).
        let base = env::var("LOCALAPPDATA")
            .or_else(|_| env::var("XDG_DATA_HOME"))
            .or_else(|_| {
                // Fallback: ~/.local/share
                dirs_data_home()
            })
            .map_err(|_| {
                AppError::io_error(
                    "Cannot resolve data directory. Set \
                     WITCHDRAWER_DATA_DIR to a writable directory.",
                )
            })?;

        let default_paths = Self::new(PathBuf::from(base).join(DEFAULT_ROOT_DIR_NAME));
        default_paths.ensure_created_and_writable()?;
        Ok(default_paths)
    }

    /// Create the necessary directory structure (no writability check).
    pub fn ensure_created(&self) -> AppResult<()> {
        fs::create_dir_all(&self.root)?;
        fs::create_dir_all(self.boxes_directory())?;
        fs::create_dir_all(self.logs_directory())?;
        Ok(())
    }

    /// Create directories and verify the root is writable.
    pub fn ensure_created_and_writable(&self) -> AppResult<()> {
        self.ensure_created()?;
        self.ensure_root_directory_writable()
    }

    /// Probe the root directory by creating and immediately deleting a file.
    fn ensure_root_directory_writable(&self) -> AppResult<()> {
        let probe = self.root.join(WRITABILITY_PROBE);
        match fs::write(&probe, &[1u8]) {
            Ok(()) => {
                let _ = fs::remove_file(&probe);
                Ok(())
            }
            Err(e) => {
                let _ = fs::remove_file(&probe);
                Err(AppError::io_error(format!(
                    "WitchDrawer data directory is not writable: {}. \
                     Path: {}. Set {} to a writable directory.",
                    e,
                    self.root.display(),
                    DATA_DIR_ENV_VAR,
                )))
            }
        }
    }
}

/// Best-effort fallback for `~/.local/share`.
fn dirs_data_home() -> Result<String, env::VarError> {
    if let Ok(home) = env::var("HOME") {
        Ok(format!("{}/.local/share", home))
    } else {
        Err(env::VarError::NotPresent)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::TempDir;

    #[test]
    fn paths_construction() {
        let tmp = TempDir::new().unwrap();
        let paths = AppPaths::new(tmp.path().to_path_buf());
        assert_eq!(paths.root(), tmp.path());
        assert_eq!(paths.boxes_directory(), tmp.path().join("Boxes"));
        assert_eq!(paths.database_path(), tmp.path().join("witchdrawer.db"));
        assert_eq!(paths.logs_directory(), tmp.path().join("logs"));
    }

    #[test]
    fn ensure_created_makes_dirs() {
        let tmp = TempDir::new().unwrap();
        let root = tmp.path().join("data");
        let paths = AppPaths::new(root.clone());
        paths.ensure_created().unwrap();
        assert!(root.exists());
        assert!(root.join("Boxes").exists());
        assert!(root.join("logs").exists());
    }

    #[test]
    fn ensure_writable_succeeds() {
        let tmp = TempDir::new().unwrap();
        let paths = AppPaths::new(tmp.path().to_path_buf());
        paths.ensure_created_and_writable().unwrap();
    }
}
