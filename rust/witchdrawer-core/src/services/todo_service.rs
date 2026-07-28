//! Todo service — manages todo items within Todo-type boxes.

use chrono::Utc;
use uuid::Uuid;

use crate::models::{AppError, AppResult, BoxType, TodoItem};
use crate::storage::DrawerRepository;

/// Maximum allowed title length (characters).
pub const MAX_TITLE_LENGTH: usize = 200;

pub struct TodoService {
    repository: DrawerRepository,
}

impl TodoService {
    pub fn new(repository: DrawerRepository) -> Self {
        Self { repository }
    }

    // -- Queries ------------------------------------------------------------

    pub fn get_todos(&self, box_id: Uuid) -> AppResult<Vec<TodoItem>> {
        self.repository.get_todos(box_id)
    }

    pub fn get_archived_todos(&self, box_id: Option<Uuid>) -> AppResult<Vec<TodoItem>> {
        self.repository.get_archived_todos(box_id)
    }

    // -- Mutations ----------------------------------------------------------

    pub fn add_todo(&self, box_id: Uuid, title: &str) -> AppResult<TodoItem> {
        let b = self
            .repository
            .get_box(box_id)?
            .ok_or_else(|| {
                AppError::not_found(
                    "\u{5F85}\u{529E}\u{76D2}\u{4E0D}\u{5B58}\u{5728}\u{6216}\u{5DF2}\u{88AB}\u{5220}\u{9664}\u{3002}",
                )
            })?;

        if b.box_type != BoxType::Todo {
            return Err(AppError::invalid_arg(
                "\u{53EA}\u{80FD}\u{5411}\u{5F85}\u{529E}\u{76D2}\u{6DFB}\u{52A0}\u{5F85}\u{529E}\u{4E8B}\u{9879}\u{3002}",
            ));
        }

        let normalized = Self::normalize_title(title)?;
        let now = Utc::now();
        let sort_order = self.repository.get_next_todo_sort_order(box_id)?;

        let todo = TodoItem {
            id: Uuid::new_v4(),
            box_id,
            title: normalized,
            is_completed: false,
            sort_order,
            created_at: now,
            updated_at: now,
            completed_at: None,
            is_archived: false,
            archived_at: None,
        };

        self.repository.add_todo(&todo)?;
        Ok(todo)
    }

    pub fn set_completed(&self, todo_id: Uuid, is_completed: bool) -> AppResult<TodoItem> {
        let existing = self
            .repository
            .get_todo(todo_id)?
            .ok_or_else(|| {
                AppError::not_found(
                    "\u{5F85}\u{529E}\u{4E8B}\u{9879}\u{4E0D}\u{5B58}\u{5728}\u{6216}\u{5DF2}\u{88AB}\u{5220}\u{9664}\u{3002}",
                )
            })?;

        if existing.is_completed == is_completed {
            return Ok(existing);
        }

        let now = Utc::now();
        let completed_at = if is_completed { Some(now) } else { None };

        self.repository
            .update_todo_completion(todo_id, is_completed, completed_at, now)?;

        Ok(TodoItem {
            is_completed,
            completed_at,
            updated_at: now,
            ..existing
        })
    }

    pub fn delete_todo(&self, todo_id: Uuid) -> AppResult<()> {
        self.repository.remove_todo(todo_id)
    }

    pub fn archive_completed(&self, box_id: Uuid) -> AppResult<i32> {
        let b = self
            .repository
            .get_box(box_id)?
            .ok_or_else(|| {
                AppError::not_found(
                    "\u{5F85}\u{529E}\u{76D2}\u{4E0D}\u{5B58}\u{5728}\u{6216}\u{5DF2}\u{88AB}\u{5220}\u{9664}\u{3002}",
                )
            })?;

        if b.box_type != BoxType::Todo {
            return Err(AppError::invalid_arg(
                "\u{53EA}\u{80FD}\u{5F52}\u{6863}\u{5F85}\u{529E}\u{76D2}\u{4E2D}\u{7684}\u{4E8B}\u{9879}\u{3002}",
            ));
        }

        self.repository.archive_completed_todos(box_id, Utc::now())
    }

    pub fn restore_archived(&self, todo_id: Uuid) -> AppResult<TodoItem> {
        let existing = self
            .repository
            .get_todo(todo_id)?
            .ok_or_else(|| {
                AppError::not_found(
                    "\u{5F52}\u{6863}\u{4E8B}\u{9879}\u{4E0D}\u{5B58}\u{5728}\u{6216}\u{5DF2}\u{88AB}\u{5220}\u{9664}\u{3002}",
                )
            })?;

        if !existing.is_archived {
            return Ok(existing);
        }

        let now = Utc::now();
        self.repository
            .update_todo_archive_state(todo_id, false, None, now)?;

        Ok(TodoItem {
            is_archived: false,
            archived_at: None,
            updated_at: now,
            ..existing
        })
    }

    // -- Helpers ------------------------------------------------------------

    fn normalize_title(title: &str) -> AppResult<String> {
        let trimmed = title.trim().to_string();
        if trimmed.is_empty() {
            return Err(AppError::invalid_arg(
                "\u{5F85}\u{529E}\u{5185}\u{5BB9}\u{4E0D}\u{80FD}\u{4E3A}\u{7A7A}\u{3002}",
            ));
        }
        if trimmed.len() > MAX_TITLE_LENGTH {
            return Err(AppError::invalid_arg(format!(
                "\u{5F85}\u{529E}\u{5185}\u{5BB9}\u{4E0D}\u{80FD}\u{8D85}\u{8FC7} {} \u{4E2A}\u{5B57}\u{7B26}\u{3002}",
                MAX_TITLE_LENGTH
            )));
        }
        Ok(trimmed)
    }
}
