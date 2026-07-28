use serde::{Deserialize, Serialize};


/// 对应 C# ItemDeleteResult
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ItemDeleteResult {
    pub item_id: String,
    pub display_name: String,
    pub was_stored_item: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub restored_path: Option<String>,
    pub restored_to_original: bool,
    pub restored_to_desktop: bool,
    pub status_message: String,
}

/// 对应 C# BoxDeleteResult
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BoxDeleteResult {
    pub box_id: String,
    pub box_name: String,
    pub box_type: i32,
    pub box_removed: bool,
    pub restored_count: i32,
    pub failed_count: i32,
    pub failures: Vec<String>,
    pub status_message: String,
}

/// 对应 C# UpdateCheckResult
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UpdateCheckResult {
    pub has_update: bool,
    pub latest_version: String,
    pub release_notes: String,
    pub download_url: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub expected_sha256: Option<String>,
}
