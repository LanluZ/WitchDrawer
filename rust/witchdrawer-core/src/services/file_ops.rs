//! Safe file move operations with cross-volume fallback (copy then delete).

use std::fs;
use std::path::Path;

use crate::models::{AppError, AppResult};

/// Move a file or directory from `source` to `dest`.
///
/// Tries `fs::rename` first (fast, works same-volume). Falls back to
/// copy-then-delete when rename fails (cross-volume, locked paths, etc.).
pub fn move_file(source: &Path, dest: &Path, is_dir: bool) -> AppResult<()> {
    // Resolve source to an absolute canonical path (must exist).
    let source = source
        .canonicalize()
        .map_err(|e| AppError::io_error(format!("Source path does not exist: {}", e)))?;
    // Canonicalize dest too if it exists (avoids Windows extended-length path mismatch).
    let dest = if dest.exists() {
        dest.canonicalize().unwrap_or_else(|_| dest.to_path_buf())
    } else {
        dest.to_path_buf()
    };

    if source == dest {
        return Ok(());
    }

    // Validate source existence.
    if is_dir {
        if !source.is_dir() {
            return Err(AppError::io_error(format!(
                "Source directory does not exist: {}",
                source.display()
            )));
        }
    } else if !source.is_file() {
        return Err(AppError::io_error(format!(
            "Source file does not exist: {}",
            source.display()
        )));
    }

    // Validate destination does not already exist.
    if dest.exists() {
        return Err(AppError::io_error(format!(
            "Destination already exists: {}",
            dest.display()
        )));
    }

    // Ensure the parent directory of the destination exists.
    if let Some(parent) = dest.parent() {
        fs::create_dir_all(parent)?;
    }

    // Try rename first (fast path for same-volume).
    if are_same_volume(&source, &dest) {
        match fs::rename(&source, &dest) {
            Ok(()) => return Ok(()),
            Err(_) => {
                // Fall through to copy + delete.
            }
        }
    }

    copy_then_delete(&source, &dest, is_dir)
}

/// Returns `true` if both paths share the same volume / mount point.
pub fn are_same_volume(a: &Path, b: &Path) -> bool {
    let root_a = path_root_str(a);
    let root_b = path_root_str(b);
    match (root_a, root_b) {
        (Some(ref ra), Some(ref rb)) => ra.eq_ignore_ascii_case(rb),
        _ => false,
    }
}

/// Extract the root component of a path as a string.
/// - Windows: `"C:\\"` from `"C:\\Users\\…"`
/// - Unix: `"/"` from `"/home/…"`
fn path_root_str(path: &Path) -> Option<String> {
    use std::path::Component;
    path.components().next().map(|c| match c {
        Component::Prefix(p) => p.as_os_str().to_string_lossy().to_string(),
        Component::RootDir => "/".to_string(),
        _ => String::new(),
    })
}

/// Copy the source to `dest`, then delete the source.
/// On failure during deletion, the copy is rolled back (best-effort).
fn copy_then_delete(source: &Path, dest: &Path, is_dir: bool) -> AppResult<()> {
    if is_dir {
        if let Err(error) = copy_directory(source, dest) {
            // A recursive copy may already have created part of the tree.
            let _ = fs::remove_dir_all(dest);
            return Err(error);
        }
        if let Err(e) = fs::remove_dir_all(source) {
            // Best-effort rollback.
            let _ = fs::remove_dir_all(dest);
            return Err(e.into());
        }
    } else {
        if let Err(error) = fs::copy(source, dest) {
            // Some filesystems can leave a partial destination after failure.
            let _ = fs::remove_file(dest);
            return Err(error.into());
        }
        if let Err(e) = fs::remove_file(source) {
            let _ = fs::remove_file(dest);
            return Err(e.into());
        }
    }
    Ok(())
}

/// Recursively copy a directory tree.
fn copy_directory(source: &Path, dest: &Path) -> AppResult<()> {
    fs::create_dir_all(dest)?;
    for entry in fs::read_dir(source)? {
        let entry = entry?;
        let meta = entry.metadata()?;
        let target = dest.join(entry.file_name());
        if meta.is_dir() {
            copy_directory(&entry.path(), &target)?;
        } else {
            fs::copy(entry.path(), &target)?;
        }
    }
    Ok(())
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------
#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use tempfile::TempDir;

    #[test]
    fn same_path_is_noop() {
        let tmp = TempDir::new().unwrap();
        let file = tmp.path().join("a.txt");
        fs::write(&file, "hello").unwrap();
        move_file(&file, &file, false).unwrap();
        assert!(file.exists());
        assert_eq!(fs::read_to_string(&file).unwrap(), "hello");
    }

    #[test]
    fn source_not_exist_fails() {
        let tmp = TempDir::new().unwrap();
        let r = move_file(&tmp.path().join("nope"), &tmp.path().join("dst"), false);
        assert!(r.is_err());
    }

    #[test]
    fn dest_already_exists_fails() {
        let tmp = TempDir::new().unwrap();
        let src = tmp.path().join("src.txt");
        let dst = tmp.path().join("dst.txt");
        fs::write(&src, "a").unwrap();
        fs::write(&dst, "b").unwrap();
        assert!(move_file(&src, &dst, false).is_err());
    }

    #[test]
    fn move_file_success() {
        let tmp = TempDir::new().unwrap();
        let src = tmp.path().join("src.txt");
        let dst = tmp.path().join("dst.txt");
        fs::write(&src, "data").unwrap();
        move_file(&src, &dst, false).unwrap();
        assert!(!src.exists());
        assert_eq!(fs::read_to_string(&dst).unwrap(), "data");
    }

    #[test]
    fn move_directory_success() {
        let tmp = TempDir::new().unwrap();
        let src = tmp.path().join("dir");
        fs::create_dir(&src).unwrap();
        fs::write(src.join("f.txt"), "content").unwrap();
        let dst = tmp.path().join("dir2");
        move_file(&src, &dst, true).unwrap();
        assert!(!src.exists());
        assert_eq!(fs::read_to_string(dst.join("f.txt")).unwrap(), "content");
    }

    #[test]
    fn move_file_creates_parent() {
        let tmp = TempDir::new().unwrap();
        let src = tmp.path().join("a.txt");
        let dst = tmp.path().join("x").join("y").join("a.txt");
        fs::write(&src, "z").unwrap();
        move_file(&src, &dst, false).unwrap();
        assert_eq!(fs::read_to_string(&dst).unwrap(), "z");
    }

    #[test]
    fn same_volume_linux() {
        let a = Path::new("/home/user/f.txt");
        let b = Path::new("/tmp/f.txt");
        assert!(are_same_volume(a, b));
    }

    #[test]
    fn copy_then_delete_fallback_moves_file_and_removes_source() {
        let tmp = TempDir::new().unwrap();
        let src = tmp.path().join("source.txt");
        let dst = tmp.path().join("destination.txt");
        fs::write(&src, "cross-volume-data").unwrap();

        copy_then_delete(&src, &dst, false).unwrap();

        assert!(!src.exists());
        assert_eq!(fs::read_to_string(&dst).unwrap(), "cross-volume-data");
    }

    #[test]
    fn failed_directory_copy_removes_partial_destination() {
        let tmp = TempDir::new().unwrap();
        let src = tmp.path().join("source");
        let dst = tmp.path().join("destination");
        fs::create_dir(&src).unwrap();
        fs::write(src.join("entry"), "content").unwrap();

        // Force fs::copy(file, target) to fail by pre-creating that target as a directory.
        fs::create_dir(&dst).unwrap();
        fs::create_dir(dst.join("entry")).unwrap();

        assert!(copy_then_delete(&src, &dst, true).is_err());
        assert!(src.exists());
        assert!(!dst.exists());
    }

    #[cfg(windows)]
    #[test]
    fn windows_volume_comparison_is_case_insensitive() {
        assert!(are_same_volume(
            Path::new(r"C:\Users\source.txt"),
            Path::new(r"c:\Temp\destination.txt")
        ));
        assert!(!are_same_volume(
            Path::new(r"C:\Users\source.txt"),
            Path::new(r"D:\Temp\destination.txt")
        ));
    }
}
