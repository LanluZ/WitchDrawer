use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

use super::ItemKind;

/// 对应 C# DrawerItem record
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DrawerItem {
    pub id: Uuid,
    pub box_id: Uuid,
    pub display_name: String,
    pub item_kind: ItemKind,
    pub source_path: Option<String>,
    pub stored_path: Option<String>,
    pub sort_order: i32,
    pub created_at: DateTime<Utc>,
    pub updated_at: DateTime<Utc>,
    pub grid_column: Option<i32>,
    pub grid_row: Option<i32>,
}

impl DrawerItem {
    pub fn effective_path(&self) -> Option<&str> {
        self.stored_path.as_deref().or(self.source_path.as_deref())
    }
}

/// 用于 FFI 的 JSON 友好结构
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FfiDrawerItem {
    pub id: String,
    pub box_id: String,
    pub display_name: String,
    pub item_kind: i32,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub source_path: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub stored_path: Option<String>,
    pub sort_order: i32,
    pub created_at: String,
    pub updated_at: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub grid_column: Option<i32>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub grid_row: Option<i32>,
}

impl From<&DrawerItem> for FfiDrawerItem {
    fn from(item: &DrawerItem) -> Self {
        Self {
            id: item.id.to_string(),
            box_id: item.box_id.to_string(),
            display_name: item.display_name.clone(),
            item_kind: item.item_kind as i32,
            source_path: item.source_path.clone(),
            stored_path: item.stored_path.clone(),
            sort_order: item.sort_order,
            created_at: item.created_at.to_rfc3339(),
            updated_at: item.updated_at.to_rfc3339(),
            grid_column: item.grid_column,
            grid_row: item.grid_row,
        }
    }
}
