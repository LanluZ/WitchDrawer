use std::time::Instant;
use witchdrawer_core::models::*;
use witchdrawer_core::services::app_paths::AppPaths;
use witchdrawer_core::services::DrawerService;
use witchdrawer_core::storage::DrawerRepository;

fn bench<F: FnMut()>(name: &str, iters: u32, mut f: F) {
    for _ in 0..3 {
        f();
    }
    let start = Instant::now();
    for _ in 0..iters {
        f();
    }
    let elapsed = start.elapsed();
    let avg_us = elapsed.as_micros() as f64 / iters as f64;
    println!("{:<45} {:>6} iters  {:>10.2} us/iter", name, iters, avg_us);
}

fn main() {
    println!("=== WitchDrawer Rust Core Benchmark ===\n");

    let tmp = tempfile::tempdir().unwrap();
    let paths = AppPaths::new(tmp.path().to_path_buf());
    paths.ensure_created().unwrap();

    let repo = DrawerRepository::new(paths.database_path().to_string_lossy().to_string());
    repo.initialize().unwrap();

    println!("-- Storage: Write --");

    bench("add_box", 1000, || {
        repo.add_box(&DrawerBox {
            id: uuid::Uuid::new_v4(),
            name: format!("Box {}", uuid::Uuid::new_v4()),
            box_type: BoxType::Normal,
            storage_path: None,
            sort_order: 0,
            created_at: chrono::Utc::now(),
            updated_at: chrono::Utc::now(),
        })
        .unwrap();
    });

    let mut box_ids = Vec::new();
    for i in 0..100 {
        let id = uuid::Uuid::new_v4();
        repo.add_box(&DrawerBox {
            id,
            name: format!("ReadBox {}", i),
            box_type: BoxType::Normal,
            storage_path: None,
            sort_order: i,
            created_at: chrono::Utc::now(),
            updated_at: chrono::Utc::now(),
        })
        .unwrap();
        box_ids.push(id);
    }

    let main_box = box_ids[0];
    for i in 0..500 {
        repo.add_item(&DrawerItem {
            id: uuid::Uuid::new_v4(),
            box_id: main_box,
            display_name: format!("Item {}.txt", i),
            item_kind: ItemKind::File,
            source_path: Some(format!("C:/test/{}.txt", i)),
            stored_path: None,
            sort_order: i,
            created_at: chrono::Utc::now(),
            updated_at: chrono::Utc::now(),
            grid_column: None,
            grid_row: None,
        })
        .unwrap();
    }

    println!("\n-- Storage: Read --");

    bench("get_boxes (100 rows)", 500, || {
        let _ = repo.get_boxes().unwrap();
    });

    bench("get_items (500 rows, one box)", 500, || {
        let _ = repo.get_items(Some(main_box)).unwrap();
    });

    bench("get_items_all (500 rows)", 500, || {
        let _ = repo.get_items(None).unwrap();
    });

    bench("search_items (LIKE, limit 200)", 500, || {
        let _ = repo.search_items("Item 4", 200).unwrap();
    });

    bench("get_box (single by id)", 5000, || {
        let _ = repo.get_box(main_box).unwrap();
    });

    let sample_item = repo.get_items(Some(main_box)).unwrap().remove(0);
    bench("get_item (single by id)", 5000, || {
        let _ = repo.get_item(sample_item.id).unwrap();
    });

    println!("\n-- Storage: Todo --");

    let todo_box = uuid::Uuid::new_v4();
    repo.add_box(&DrawerBox {
        id: todo_box,
        name: "TodoBox".into(),
        box_type: BoxType::Todo,
        storage_path: None,
        sort_order: 999,
        created_at: chrono::Utc::now(),
        updated_at: chrono::Utc::now(),
    })
    .unwrap();
    for i in 0..200 {
        repo.add_todo(&TodoItem {
            id: uuid::Uuid::new_v4(),
            box_id: todo_box,
            title: format!("Todo {}", i),
            is_completed: i % 3 == 0,
            sort_order: i,
            created_at: chrono::Utc::now(),
            updated_at: chrono::Utc::now(),
            completed_at: None,
            is_archived: false,
            archived_at: None,
        })
        .unwrap();
    }

    bench("get_todos (200 rows)", 500, || {
        let _ = repo.get_todos(todo_box).unwrap();
    });

    bench("add_todo + remove_todo", 500, || {
        let id = uuid::Uuid::new_v4();
        repo.add_todo(&TodoItem {
            id,
            box_id: todo_box,
            title: "Temp".into(),
            is_completed: false,
            sort_order: 9999,
            created_at: chrono::Utc::now(),
            updated_at: chrono::Utc::now(),
            completed_at: None,
            is_archived: false,
            archived_at: None,
        })
        .unwrap();
        repo.remove_todo(id).unwrap();
    });

    println!("\n-- Service Layer --");

    let svc_repo = DrawerRepository::new(tmp.path().join("svc.db").to_string_lossy().to_string());
    svc_repo.initialize().unwrap();
    let svc = DrawerService::new(paths.clone(), svc_repo);
    svc.initialize().unwrap();

    bench("create_box (service, full flow)", 500, || {
        svc.create_box(&format!("Box {}", uuid::Uuid::new_v4()), BoxType::Normal)
            .unwrap();
    });

    bench("get_boxes (service, all)", 500, || {
        let _ = svc.get_boxes().unwrap();
    });

    println!("\n-- FFI JSON Serialization --");

    let boxes = repo.get_boxes().unwrap();
    let ffi_boxes: Vec<FfiBox> = boxes.iter().map(FfiBox::from).collect();
    let json = serde_json::to_string(&ffi_boxes).unwrap();
    bench("serialize 100 FfiBox -> JSON", 1000, || {
        let _: Vec<FfiBox> = serde_json::from_str(&json).unwrap();
    });

    let items = repo.get_items(None).unwrap();
    let ffi_items: Vec<FfiDrawerItem> = items.iter().map(FfiDrawerItem::from).collect();
    let json = serde_json::to_string(&ffi_items).unwrap();
    bench("deserialize 500 FfiDrawerItem from JSON", 1000, || {
        let _: Vec<FfiDrawerItem> = serde_json::from_str(&json).unwrap();
    });

    println!("\n=== Done ===");
}
