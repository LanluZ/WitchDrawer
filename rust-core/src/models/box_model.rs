use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use super::BoxType;

/// 对应 C# Box record
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Box {
    pub id: Uuid,
    pub name: String,
    pub box_type: BoxType,
    pub storage_path: Option<String>,
    pub sort_order: i32,
    pub created_at: DateTime<Utc>,
    pub updated_at: DateTime<Utc>,
}

/// 用于 FFI 的 JSON 友好结构
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FfiBox {
    pub id: String,
    pub name: String,
    #[serde(rename = "type")]
    pub box_type: i32,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub storage_path: Option<String>,
    pub sort_order: i32,
    pub created_at: String,
    pub updated_at: String,
}

impl From<&Box> for FfiBox {
    fn from(b: &Box) -> Self {
        Self {
            id: b.id.to_string(),
            name: b.name.clone(),
            box_type: b.box_type as i32,
            storage_path: b.storage_path.clone(),
            sort_order: b.sort_order,
            created_at: b.created_at.to_rfc3339(),
            updated_at: b.updated_at.to_rfc3339(),
        }
    }
}
