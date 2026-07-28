//! Path validation utilities: resolve full existing paths and enforce child containment.

use std::ffi::OsString;
use std::path::{Component, Path, PathBuf};

use crate::models::{AppError, AppResult};

/// Resolve `path` to a full, canonical path and verify it exists on disk.
pub fn get_full_existing_path(path: &str) -> AppResult<PathBuf> {
    let trimmed = path.trim();
    if trimmed.is_empty() {
        return Err(AppError::invalid_arg("Path cannot be empty."));
    }

    let p = Path::new(trimmed);
    // canonicalize resolves relative paths AND checks existence.
    let full = p.canonicalize().map_err(|_| {
        AppError::not_found(format!("Dropped path does not exist: {}", p.display()))
    })?;

    Ok(full)
}

/// Ensure that `candidate` is a descendant of `root`.
///
/// Existing ancestors are canonicalized before comparison so the function is
/// safe for both existing files and destinations that are about to be created.
/// Symlinked ancestors are resolved and cannot be used to escape the root.
pub fn ensure_child_path(root: &Path, candidate: &Path) -> AppResult<()> {
    let root_full = root
        .canonicalize()
        .map_err(|e| AppError::io_error(format!("Cannot resolve root path: {}", e)))?;
    let candidate_full = canonicalize_allow_missing(candidate)?;

    if candidate_full == root_full || !candidate_full.starts_with(&root_full) {
        return Err(AppError::io_error(format!(
            "Target path is outside the allowed storage root: {}",
            candidate_full.display()
        )));
    }

    Ok(())
}

/// Canonicalize the nearest existing ancestor and append the missing suffix.
/// This preserves the security properties of `canonicalize` without requiring
/// the final destination to exist already.
fn canonicalize_allow_missing(path: &Path) -> AppResult<PathBuf> {
    let absolute = if path.is_absolute() {
        path.to_path_buf()
    } else {
        std::env::current_dir()?.join(path)
    };

    let mut ancestor = absolute.as_path();
    let mut missing_parts: Vec<OsString> = Vec::new();

    while !ancestor.exists() {
        let name = ancestor.file_name().ok_or_else(|| {
            AppError::io_error(format!(
                "Cannot resolve candidate path: {}",
                absolute.display()
            ))
        })?;
        missing_parts.push(name.to_os_string());
        ancestor = ancestor.parent().ok_or_else(|| {
            AppError::io_error(format!(
                "Cannot resolve candidate path: {}",
                absolute.display()
            ))
        })?;
    }

    let mut resolved = ancestor
        .canonicalize()
        .map_err(|e| AppError::io_error(format!("Cannot resolve candidate path: {}", e)))?;
    for part in missing_parts.iter().rev() {
        let component = Path::new(part).components().next();
        if !matches!(component, Some(Component::Normal(_))) {
            return Err(AppError::io_error(format!(
                "Candidate path contains an invalid component: {}",
                absolute.display()
            )));
        }
        resolved.push(part);
    }

    Ok(resolved)
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------
#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::TempDir;

    #[test]
    fn empty_path_fails() {
        assert!(get_full_existing_path("").is_err());
        assert!(get_full_existing_path("   ").is_err());
    }

    #[test]
    fn nonexistent_path_fails() {
        assert!(get_full_existing_path("/no/such/path/ever").is_err());
    }

    #[test]
    fn existing_file_resolves() {
        let tmp = TempDir::new().unwrap();
        let file = tmp.path().join("exists.txt");
        std::fs::write(&file, "x").unwrap();
        let resolved = get_full_existing_path(file.to_str().unwrap()).unwrap();
        assert!(resolved.exists());
    }

    #[test]
    fn existing_dir_resolves() {
        let tmp = TempDir::new().unwrap();
        let resolved = get_full_existing_path(tmp.path().to_str().unwrap()).unwrap();
        assert!(resolved.is_dir());
    }

    #[test]
    fn child_path_ok() {
        let tmp = TempDir::new().unwrap();
        let child = tmp.path().join("sub").join("file.txt");
        std::fs::create_dir_all(child.parent().unwrap()).unwrap();
        std::fs::write(&child, "x").unwrap();
        // Use canonicalize-safe paths.
        let root = tmp.path().canonicalize().unwrap();
        let child_full = child.canonicalize().unwrap();
        assert!(ensure_child_path(&root, &child_full).is_ok());
    }

    #[test]
    fn missing_child_path_ok() {
        let tmp = TempDir::new().unwrap();
        let child = tmp.path().join("missing").join("file.txt");

        assert!(ensure_child_path(tmp.path(), &child).is_ok());
    }

    #[test]
    fn root_itself_is_not_a_child() {
        let tmp = TempDir::new().unwrap();

        assert!(ensure_child_path(tmp.path(), tmp.path()).is_err());
    }

    #[test]
    fn child_path_outside_fails() {
        let tmp = TempDir::new().unwrap();
        let root = tmp.path().join("allowed");
        std::fs::create_dir(&root).unwrap();
        let outside = tmp.path().join("forbidden");
        std::fs::create_dir(&outside).unwrap();
        let root_c = root.canonicalize().unwrap();
        let outside_c = outside.canonicalize().unwrap();
        assert!(ensure_child_path(&root_c, &outside_c).is_err());
    }
}
