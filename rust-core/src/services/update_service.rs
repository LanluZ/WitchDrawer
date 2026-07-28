//! Update service — checks GitHub releases for new versions and applies them.

use std::fs;
use std::io::Read as _;
use std::path::{Path, PathBuf};

use reqwest::Client;
use serde::Deserialize;
use sha2::{Digest, Sha256};

use crate::models::{AppError, AppResult, UpdateCheckResult};

const GITHUB_OWNER: &str = "witchscottishfoldcat";
const GITHUB_REPO: &str = "WitchDrawer";
const GITHUB_API_URL: &str =
    "https://api.github.com/repos/witchscottishfoldcat/WitchDrawer/releases/latest";
const GITHUB_RELEASE_PAGE: &str =
    "https://github.com/witchscottishfoldcat/WitchDrawer/releases/latest";
const VERSION_TAG_PREFIX: &str = "v";

// ---------------------------------------------------------------------------
// GitHub API response types
// ---------------------------------------------------------------------------

#[derive(Debug, Deserialize)]
struct GitHubReleaseResponse {
    #[serde(default)]
    tag_name: String,
    #[serde(default)]
    body: String,
    #[serde(default)]
    html_url: String,
    #[serde(default)]
    assets: Vec<GitHubAsset>,
}

#[derive(Debug, Deserialize)]
struct GitHubAsset {
    #[serde(default)]
    name: String,
    #[serde(default)]
    browser_download_url: String,
}

// ---------------------------------------------------------------------------
// Simple SemVer-like version for comparison
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, PartialEq, Eq)]
struct Version {
    major: u32,
    minor: u32,
    patch: u32,
    build: u32,
}

impl Version {
    fn parse(s: &str) -> Option<Self> {
        let parts: Vec<&str> = s.split('.').collect();
        if parts.is_empty() {
            return None;
        }
        let major = parts[0].parse::<u32>().ok()?;
        let minor = parts.get(1).and_then(|p| p.parse().ok()).unwrap_or(0);
        let patch = parts.get(2).and_then(|p| p.parse().ok()).unwrap_or(0);
        let build = parts.get(3).and_then(|p| p.parse().ok()).unwrap_or(0);
        Some(Self { major, minor, patch, build })
    }
}

impl PartialOrd for Version {
    fn partial_cmp(&self, other: &Self) -> Option<std::cmp::Ordering> {
        Some(self.cmp(other))
    }
}

impl Ord for Version {
    fn cmp(&self, other: &Self) -> std::cmp::Ordering {
        self.major
            .cmp(&other.major)
            .then_with(|| self.minor.cmp(&other.minor))
            .then_with(|| self.patch.cmp(&other.patch))
            .then_with(|| self.build.cmp(&other.build))
    }
}

// ---------------------------------------------------------------------------
// UpdateService
// ---------------------------------------------------------------------------

pub struct UpdateService {
    client: Client,
}

impl UpdateService {
    pub fn new() -> Self {
        let client = Client::builder()
            .user_agent("WitchDrawer")
            .build()
            .expect("Failed to create HTTP client");
        Self { client }
    }

    // -- Check for update ---------------------------------------------------

    pub async fn check_for_update(
        &self,
        current_version: &str,
    ) -> AppResult<UpdateCheckResult> {
        let current = Version::parse(current_version)
            .unwrap_or(Version { major: 0, minor: 0, patch: 0, build: 0 });

        let response: GitHubReleaseResponse = match self
            .client
            .get(GITHUB_API_URL)
            .send()
            .await
        {
            Ok(resp) => match resp.json::<GitHubReleaseResponse>().await {
                Ok(r) => r,
                Err(_) => return Ok(UpdateCheckResult::default()),
            },
            Err(_) => return Ok(UpdateCheckResult::default()),
        };

        if response.tag_name.is_empty() {
            return Ok(UpdateCheckResult::default());
        }

        let tag_text = response
            .tag_name
            .strip_prefix(VERSION_TAG_PREFIX)
            .unwrap_or(&response.tag_name);

        let remote = match Version::parse(tag_text) {
            Some(v) => v,
            None => {
                tracing::info!(
                    "Failed to parse remote version tag: {}",
                    response.tag_name
                );
                return Ok(UpdateCheckResult::default());
            }
        };

        let has_update = remote > current;
        let (download_url, expected_sha256) =
            self.resolve_asset(&response.assets).await;

        let url = if download_url.is_empty() {
            if response.html_url.is_empty() {
                GITHUB_RELEASE_PAGE.to_string()
            } else {
                response.html_url
            }
        } else {
            download_url
        };

        Ok(UpdateCheckResult {
            has_update,
            latest_version: format!(
                "{}.{}.{}.{}",
                remote.major, remote.minor, remote.patch, remote.build
            ),
            release_notes: Self::truncate_release_notes(&response.body, 500),
            download_url: url,
            expected_sha256,
        })
    }

    // -- Download & apply ---------------------------------------------------

    pub async fn download_and_apply_update(
        &self,
        download_url: &str,
        expected_sha256: Option<&str>,
    ) -> AppResult<bool> {
        if !Self::is_allowed_download_url(download_url) {
            tracing::info!("Rejected update download URL: {}", download_url);
            return Ok(false);
        }

        let temp_dir = std::env::temp_dir().join("WitchDrawerUpdate");
        if temp_dir.exists() {
            let _ = fs::remove_dir_all(&temp_dir);
        }
        fs::create_dir_all(&temp_dir)?;

        let zip_path = temp_dir.join("update.zip");

        // Download the zip.
        let response = self
            .client
            .get(download_url)
            .send()
            .await
            .map_err(|e| AppError::io_error(format!("Download failed: {}", e)))?;

        if !response.status().is_success() {
            return Err(AppError::io_error(format!(
                "Download returned status {}",
                response.status()
            )));
        }

        // Read the full response into bytes then write to disk.
        let bytes = response
            .bytes()
            .await
            .map_err(|e| AppError::io_error(format!("Read error: {}", e)))?;
        fs::write(&zip_path, &bytes)?;

        // SHA-256 verification.
        if let Some(expected) = expected_sha256 {
            let actual = Self::compute_sha256_hex(&zip_path)?;
            if !actual.eq_ignore_ascii_case(expected) {
                tracing::info!(
                    "Update hash mismatch. expected={} actual={}",
                    expected,
                    actual
                );
                let _ = fs::remove_dir_all(&temp_dir);
                return Ok(false);
            }
        } else {
            tracing::info!(
                "Update asset has no published SHA-256; continuing with URL allowlist only."
            );
        }

        // Extract zip.
        let zip_file = fs::File::open(&zip_path)?;
        let mut archive = zip::ZipArchive::new(zip_file)
            .map_err(|e| AppError::io_error(format!("Failed to open zip: {}", e)))?;
        archive
            .extract(&temp_dir)
            .map_err(|e| AppError::io_error(format!("Failed to extract zip: {}", e)))?;

        // Create updater script.
        let app_dir = std::env::current_exe()
            .ok()
            .and_then(|p| p.parent().map(|p| p.to_path_buf()))
            .unwrap_or_else(|| PathBuf::from("."));
        let app_dir_s = app_dir.to_string_lossy();
        let temp_dir_s = temp_dir.to_string_lossy();

        let bat_content = format!(
            r#"@echo off
chcp 65001 >nul
echo Updating WitchDrawer...
timeout /t 2 /nobreak >nul

taskkill /im "WitchDrawer.App.exe" /f >nul 2>&1
timeout /t 1 /nobreak >nul

xcopy "{td}\*" "{ad}" /e /y /i >nul 2>&1

start "" "{ad}\WitchDrawer.App.exe"

cd /d "%temp%"
rmdir /s /q "{td}" >nul 2>&1
del "%~f0" >nul 2>&1
"#,
            td = temp_dir_s,
            ad = app_dir_s,
        );

        let updater_path = temp_dir.join("updater.bat");
        fs::write(&updater_path, bat_content)?;

        // Launch the updater script (Windows only).
        #[cfg(target_os = "windows")]
        {
            use std::os::windows::process::CommandExt;
            const CREATE_NO_WINDOW: u32 = 0x08000000;
            std::process::Command::new("cmd")
                .args(["/C", "start", "", updater_path.to_string_lossy().as_ref()])
                .creation_flags(CREATE_NO_WINDOW)
                .spawn()?;
        }

        Ok(true)
    }

    // =======================================================================
    // Internal helpers
    // =======================================================================

    /// Check whether a download URL points to an allowed host.
    pub fn is_allowed_download_url(url: &str) -> bool {
        let parsed = match reqwest::Url::parse(url) {
            Ok(u) => u,
            Err(_) => return false,
        };

        if parsed.scheme() != "https" {
            return false;
        }

        let host = match parsed.host_str() {
            Some(h) => h.to_lowercase(),
            None => return false,
        };

        if host == "github.com" {
            let path = parsed.path().to_lowercase();
            return path
                .contains(&format!("/{}/{}/", GITHUB_OWNER.to_lowercase(), GITHUB_REPO.to_lowercase()));
        }

        if host == "objects.githubusercontent.com"
            || host == "release-assets.githubusercontent.com"
            || host == "uploads.github.com"
            || host.ends_with(".githubusercontent.com")
        {
            return true;
        }

        false
    }

    /// Find the best asset in the release assets list.
    async fn resolve_asset(
        &self,
        assets: &[GitHubAsset],
    ) -> (String, Option<String>) {
        if assets.is_empty() {
            return (String::new(), None);
        }

        let arch_keyword = if cfg!(target_arch = "aarch64") {
            "arm64"
        } else {
            "x64"
        };

        // Prefer .zip files matching the architecture.
        let zip_assets: Vec<&GitHubAsset> = assets
            .iter()
            .filter(|a| a.name.to_lowercase().ends_with(".zip"))
            .collect();

        let best = zip_assets
            .iter()
            .find(|a| a.name.to_lowercase().contains(arch_keyword))
            .copied()
            .or_else(|| zip_assets.first().copied())
            .or_else(|| {
                assets
                    .iter()
                    .find(|a| a.name.to_lowercase().contains(arch_keyword))
            })
            .or_else(|| assets.first());

        let match_asset = match best {
            Some(a) => a,
            None => return (String::new(), None),
        };

        if match_asset.browser_download_url.is_empty() {
            return (String::new(), None);
        }

        if !Self::is_allowed_download_url(&match_asset.browser_download_url)
        {
            tracing::info!(
                "Rejected release asset URL: {}",
                match_asset.browser_download_url
            );
            return (String::new(), None);
        }

        let sha256 = self.try_resolve_sha256(assets, match_asset).await;
        (
            match_asset.browser_download_url.clone(),
            sha256,
        )
    }

    /// Try to find a SHA-256 checksum for the package asset.
    async fn try_resolve_sha256(
        &self,
        assets: &[GitHubAsset],
        package: &GitHubAsset,
    ) -> Option<String> {
        let pkg_lower = package.name.to_lowercase();

        // Companion .sha256 / .sha256.txt
        let companion = assets.iter().find(|a| {
            let n = a.name.to_lowercase();
            n == format!("{}.sha256", pkg_lower)
                || n == format!("{}.sha256.txt", pkg_lower)
        });

        if let Some(c) = companion {
            if Self::is_allowed_download_url(&c.browser_download_url) {
                return self
                    .read_sha256_from_asset(
                        &c.browser_download_url,
                        &package.name,
                    )
                    .await;
            }
        }

        // SHA256SUMS / checksums.txt
        let checksums = assets.iter().find(|a| {
            let n = a.name.to_lowercase();
            n == "sha256sums"
                || n == "checksums.txt"
                || n.ends_with(".sha256sums")
        });

        if let Some(c) = checksums {
            if Self::is_allowed_download_url(&c.browser_download_url) {
                return self
                    .read_sha256_from_asset(
                        &c.browser_download_url,
                        &package.name,
                    )
                    .await;
            }
        }

        None
    }

    /// Parse a checksum text file and extract the hash for `package_name`.
    async fn read_sha256_from_asset(
        &self,
        url: &str,
        package_name: &str,
    ) -> Option<String> {
        let text = self
            .client
            .get(url)
            .send()
            .await
            .ok()?
            .text()
            .await
            .ok()?;

        for raw_line in text.lines() {
            let line = raw_line.trim();
            if line.is_empty() || line.starts_with('#') {
                continue;
            }

            let parts: Vec<&str> = line.split_whitespace().collect();
            if parts.is_empty() {
                continue;
            }

            let candidate_hash = parts[0].trim_start_matches('*');
            if !is_valid_sha256_hex(candidate_hash) {
                continue;
            }

            if parts.len() == 1 {
                return Some(candidate_hash.to_lowercase());
            }

            let file_name =
                parts.last().unwrap().trim_start_matches('*');
            if file_name.eq_ignore_ascii_case(package_name)
                || Path::new(file_name)
                    .file_name()
                    .and_then(|f| f.to_str())
                    .map(|f| f.eq_ignore_ascii_case(package_name))
                    .unwrap_or(false)
            {
                return Some(candidate_hash.to_lowercase());
            }
        }

        None
    }

    /// Compute the lowercase hex SHA-256 of a file.
    fn compute_sha256_hex(path: &Path) -> AppResult<String> {
        let mut file = fs::File::open(path)?;
        let mut hasher = Sha256::new();
        let mut buffer = vec![0u8; 81920];
        loop {
            let n = file.read(&mut buffer)?;
            if n == 0 {
                break;
            }
            hasher.update(&buffer[..n]);
        }
        Ok(hex::encode(hasher.finalize()))
    }

    /// Truncate release notes to `max_len` characters.
    fn truncate_release_notes(body: &str, max_len: usize) -> String {
        let clean = body.replace("\r\n", "\n").trim().to_string();
        if clean.len() <= max_len {
            clean
        } else {
            format!("{}...", &clean[..max_len])
        }
    }
}

/// Check if a string is a valid 64-character hex SHA-256 hash.
fn is_valid_sha256_hex(s: &str) -> bool {
    s.len() == 64 && s.chars().all(|c| c.is_ascii_hexdigit())
}

impl Default for UpdateCheckResult {
    fn default() -> Self {
        Self {
            has_update: false,
            latest_version: "0.0.0.0".to_string(),
            release_notes: String::new(),
            download_url: String::new(),
            expected_sha256: None,
        }
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------
#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn version_parse_basic() {
        let v = Version::parse("1.2.3").unwrap();
        assert_eq!(v, Version { major: 1, minor: 2, patch: 3, build: 0 });
    }

    #[test]
    fn version_parse_four_part() {
        let v = Version::parse("1.2.3.4").unwrap();
        assert_eq!(v, Version { major: 1, minor: 2, patch: 3, build: 4 });
    }

    #[test]
    fn version_compare() {
        let a = Version::parse("1.2.3").unwrap();
        let b = Version::parse("1.2.4").unwrap();
        assert!(a < b);
        assert!(b > a);
    }

    #[test]
    fn version_compare_equal() {
        let a = Version::parse("2.0.0").unwrap();
        let b = Version::parse("2.0.0").unwrap();
        assert_eq!(a, b);
    }

    #[test]
    fn version_compare_major() {
        let a = Version::parse("1.9.9").unwrap();
        let b = Version::parse("2.0.0").unwrap();
        assert!(a < b);
    }

    #[test]
    fn allowed_url_github() {
        assert!(UpdateService::is_allowed_download_url(
            "https://github.com/witchscottishfoldcat/WitchDrawer/releases/download/v1.0/app.zip"
        ));
    }

    #[test]
    fn allowed_url_objects_githubusercontent() {
        assert!(UpdateService::is_allowed_download_url(
            "https://objects.githubusercontent.com/github-production-release-asset-2e65be/12345/abc.zip"
        ));
    }

    #[test]
    fn allowed_url_release_assets_githubusercontent() {
        assert!(UpdateService::is_allowed_download_url(
            "https://release-assets.githubusercontent.com/12345/abc.zip"
        ));
    }

    #[test]
    fn allowed_url_subdomain_githubusercontent() {
        assert!(UpdateService::is_allowed_download_url(
            "https://uploads.github.com/witchscottishfoldcat/WitchDrawer/releases/1/assets/abc.zip"
        ));
    }

    #[test]
    fn rejected_url_http() {
        assert!(!UpdateService::is_allowed_download_url(
            "http://github.com/witchscottishfoldcat/WitchDrawer/releases/download/v1.0/app.zip"
        ));
    }

    #[test]
    fn rejected_url_unknown_host() {
        assert!(!UpdateService::is_allowed_download_url(
            "https://evil.com/malware.zip"
        ));
    }

    #[test]
    fn rejected_url_malformed() {
        assert!(!UpdateService::is_allowed_download_url("not a url"));
    }

    #[test]
    fn sha256_hex_validation() {
        assert!(is_valid_sha256_hex(&"a".repeat(64)));
        assert!(!is_valid_sha256_hex("not_a_hash"));
        assert!(!is_valid_sha256_hex("abc"));
        assert!(!is_valid_sha256_hex(&"a".repeat(63)));
        assert!(!is_valid_sha256_hex(&"a".repeat(65)));
    }

    #[test]
    fn truncate_notes() {
        let long = "a".repeat(600);
        let result = UpdateService::truncate_release_notes(&long, 500);
        assert_eq!(result.len(), 503); // 500 + "..."
        assert!(result.ends_with("..."));

        let short = "short";
        assert_eq!(
            UpdateService::truncate_release_notes(short, 500),
            "short"
        );
    }

    #[test]
    fn truncate_notes_crlf() {
        let input = "line1\r\nline2\r\nline3";
        let result = UpdateService::truncate_release_notes(input, 500);
        assert_eq!(result, "line1\nline2\nline3");
    }
}
