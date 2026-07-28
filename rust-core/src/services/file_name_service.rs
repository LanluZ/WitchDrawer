//! Generate unique destination paths by appending `(1)`, `(2)`, … suffixes.

use std::ffi::OsStr;
use std::fs;
use std::path::{Path, PathBuf};

use crate::models::{AppError, AppResult};

/// Return a path inside `directory` that does not yet exist, based on
/// `original_name`. If the name is taken, appends `(1)`, `(2)`, etc.
///
/// For directories the full name is used; for files the extension is kept
/// outside the suffix.
pub fn get_unique_destination_path(
    directory: &Path,
    original_name: &str,
    is_dir: bool,
) -> AppResult<PathBuf> {
    fs::create_dir_all(directory)?;

    let candidate = directory.join(original_name);
    if !exists(&candidate, is_dir) {
        return Ok(candidate);
    }

    let (name_part, ext_part) = if is_dir {
        (original_name.to_string(), String::new())
    } else {
        let stem = Path::new(original_name)
            .file_stem()
            .unwrap_or_default()
            .to_string_lossy()
            .to_string();
        let ext = Path::new(original_name)
            .extension()
            .map(|e| format!(".{}", e.to_string_lossy()))
            .unwrap_or_default();
        (stem, ext)
    };

    for index in 1..10_000 {
        let candidate = directory.join(format!("{} ({}){}", name_part, index, ext_part));
        if !exists(&candidate, is_dir) {
            return Ok(candidate);
        }
    }

    Err(AppError::io_error(format!(
        "Could not find a free file name for {}.",
        original_name
    )))
}

fn exists(path: &Path, is_dir: bool) -> bool {
    if is_dir {
        path.is_dir()
    } else {
        path.is_file()
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------
#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::TempDir;

    #[test]
    fn unique_when_no_conflict() {
        let tmp = TempDir::new().unwrap();
        let result =
            get_unique_destination_path(tmp.path(), "hello.txt", false).unwrap();
        assert_eq!(result, tmp.path().join("hello.txt"));
    }

    #[test]
    fn file_suffix_on_conflict() {
        let tmp = TempDir::new().unwrap();
        fs::write(tmp.path().join("hello.txt"), "").unwrap();
        let result =
            get_unique_destination_path(tmp.path(), "hello.txt", false).unwrap();
        assert_eq!(result, tmp.path().join("hello (1).txt"));
    }

    #[test]
    fn dir_suffix_on_conflict() {
        let tmp = TempDir::new().unwrap();
        fs::create_dir(tmp.path().join("mydir")).unwrap();
        let result =
            get_unique_destination_path(tmp.path(), "mydir", true).unwrap();
        assert_eq!(result, tmp.path().join("mydir (1)"));
    }

    #[test]
    fn multiple_conflicts_increment() {
        let tmp = TempDir::new().unwrap();
        fs::write(tmp.path().join("f.txt"), "").unwrap();
        fs::write(tmp.path().join("f (1).txt"), "").unwrap();
        fs::write(tmp.path().join("f (2).txt"), "").unwrap();
        let result =
            get_unique_destination_path(tmp.path(), "f.txt", false).unwrap();
        assert_eq!(result, tmp.path().join("f (3).txt"));
    }

    #[test]
    fn file_without_extension() {
        let tmp = TempDir::new().unwrap();
        fs::write(tmp.path().join("Makefile"), "").unwrap();
        let result =
            get_unique_destination_path(tmp.path(), "Makefile", false).unwrap();
        assert_eq!(result, tmp.path().join("Makefile (1)"));
    }
}
