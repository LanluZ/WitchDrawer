# WitchDrawer Architecture

## Runtime
- Target runtime: .NET 10 LTS, locked by `global.json`.
- UI: WPF on `net10.0-windows`.
- Persistence: SQLite at `%LocalAppData%\WitchDrawer\witchdrawer.db`.
- User file storage for normal boxes: `%LocalAppData%\WitchDrawer\Boxes\{BoxId}`.

## Layers
- `WitchDrawer.App`: WPF shell, main drawer, quick panel, drag/drop, command binding, and hotkey message handling.
- `WitchDrawer.Core`: shared models, async service interfaces, logging, application paths, UTF-8 P/Invoke, native-context lifetime, mutation serialization, and production service composition.
- `rust/witchdrawer-core`: Core's native implementation of SQLite persistence, import/delete/update orchestration, search, path validation, and file-name conflict handling.
- `WitchDrawer.Native`: Shell open and `RegisterHotKey`/`UnregisterHotKey` wrappers.

App references only Core and Native. Core owns the data/file/update contracts and their Rust-backed implementations; Native implements Windows Shell and hotkey integration.

The former C# SQLite/file implementation lives only in `benchmarks/WitchDrawer.LegacyCore` so the migration remains measurable and the original regression suite can run. Production App does not reference or ship that assembly.

## Data Flow
- Startup creates a Rust context, initializes the SQLite schema, and creates the default normal and mapping boxes if the database is empty.
- Dragging into a normal box moves the file or folder into that box's storage directory, then persists a `DrawerItem`.
- Dragging into a mapping box stores the original absolute path only. The source file remains untouched.
- Quick panel reloads indexed items from SQLite and filters in memory for fast interactive search.

## File Safety
- Destination paths are normalized and verified to stay inside the target box storage root.
- Normal-box name conflicts use `name (1).ext`, `name (2).ext`, and so on.
- Delete restores stored items to their original `SourcePath`; if that directory is missing, files fall back to the desktop. Name conflicts use `name (1).ext`, `name (2).ext`, and so on.
- File moves use same-volume rename when possible and fall back to copy-then-delete across volumes.
- Deleting a storage box restores items one-by-one and only removes the box when every restore succeeds.
- Mapping items are removed from SQLite only; their source files are not changed.

## Performance Budget
- UI thread must not perform file IO, SQLite writes, or thumbnail/icon extraction.
- Core executes blocking native calls on worker threads and serializes mutations through one gate.
- List controls must keep virtualization enabled.
- Quick panel should open from hotkey in under 200 ms for normal MVP-sized indexes.
- Idle CPU should stay near 0%, and idle memory should be kept under 150 MB where practical.

