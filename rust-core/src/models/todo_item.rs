use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

/// 对应 C# TodoItem record
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TodoItem {
    pub id: Uuid,
    pub box_id: Uuid,
    pub title: String,
    pub is_completed: bool,
    pub sort_order: i32,
    pub created_at: DateTime<Utc>,
    pub updated_at: DateTime<Utc>,
    pub completed_at: Option<DateTime<Utc>>,
    pub is_archived: bool,
    pub archived_at: Option<DateTime<Utc>>,
}

/// 用于 FFI 的 JSON 友好结构
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FfiTodoItem {
    pub id: String,
    pub box_id: String,
    pub title: String,
    pub is_completed: bool,
    pub sort_order: i32,
    pub created_at: String,
    pub updated_at: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub completed_at: Option<String>,
    pub is_archived: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub archived_at: Option<String>,
}

impl From<&TodoItem> for FfiTodoItem {
    fn from(t: &TodoItem) -> Self {
        Self {
            id: t.id.to_string(),
            box_id: t.box_id.to_string(),
            title: t.title.clone(),
            is_completed: t.is_completed,
            sort_order: t.sort_order,
            created_at: t.created_at.to_rfc3339(),
            updated_at: t.updated_at.to_rfc3339(),
            completed_at: t.completed_at.map(|dt| dt.to_rfc3339()),
            is_archived: t.is_archived,
            archived_at: t.archived_at.map(|dt| dt.to_rfc3339()),
        }
    }
}
