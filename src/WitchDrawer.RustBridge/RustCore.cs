using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using WitchDrawer.Core.Models;

namespace WitchDrawer.Core.Services;

/// <summary>
/// Low-level P/Invoke declarations for the Rust native library (witchdrawer_core.dll).
/// All methods use Cdecl calling convention and UTF-8 string marshalling.
/// </summary>
public static class RustCore
{
    internal const string DllName = "witchdrawer_core.dll";

    // ── Native declarations ──────────────────────────────────────────────

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_init(byte[] dataDir);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wd_dispose(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_get_boxes(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_create_box(IntPtr ctx, byte[] name, int boxType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_update_box_name(IntPtr ctx, byte[] boxId, byte[] newName);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_reorder_boxes(IntPtr ctx, byte[] jsonArrayOfIds);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_delete_box(IntPtr ctx, byte[] boxId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_get_items(IntPtr ctx, IntPtr boxIdOrNull);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_search_items(IntPtr ctx, byte[] query);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_import_path(IntPtr ctx, byte[] boxId, byte[] sourcePath, int gridCol, int gridRow);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_move_item_to_box(IntPtr ctx, byte[] itemId, byte[] targetBoxId, int gridCol, int gridRow);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_delete_item(IntPtr ctx, byte[] itemId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_export_item(IntPtr ctx, byte[] itemId, byte[] targetDir);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_update_grid_pos(IntPtr ctx, byte[] itemId, int gridCol, int gridRow);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_get_todos(IntPtr ctx, byte[] boxId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_add_todo(IntPtr ctx, byte[] boxId, byte[] title);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_set_todo_completed(IntPtr ctx, byte[] todoId, int isCompleted);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_delete_todo(IntPtr ctx, byte[] todoId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_archive_completed(IntPtr ctx, byte[] boxId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_restore_archived(IntPtr ctx, byte[] todoId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_check_update(IntPtr ctx, byte[] currentVersion);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wd_free_string(IntPtr ptr);

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>Convert a managed string to a UTF-8 byte array for P/Invoke.</summary>
    internal static byte[] ToUtf8(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    /// <summary>Read a returned C string and free it, then deserialize the JSON FfiResponse.</summary>
    internal static T Call<T>(Func<IntPtr> nativeCall)
    {
        var ptr = nativeCall();
        var json = ReadAndFree(ptr);
        var response = JsonSerializer.Deserialize<FfiResponse<T>>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize Rust response: {json}");

        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Error ?? "Unknown Rust error");
        }

        return response.Data ?? throw new InvalidOperationException("Rust returned ok but data was null");
    }

    /// <summary>Read a returned C string and free it, then deserialize a void FfiResponse (returns null on success).</summary>
    internal static void CallVoid(Func<IntPtr> nativeCall)
    {
        var ptr = nativeCall();
        var json = ReadAndFree(ptr);
        var response = JsonSerializer.Deserialize<FfiResponse<JsonElement>>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize Rust response: {json}");

        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Error ?? "Unknown Rust error");
        }
    }

    internal static string ReadAndFree(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            throw new InvalidOperationException("Rust returned a null pointer");
        }

        var result = Marshal.PtrToStringUTF8(ptr) ?? throw new InvalidOperationException("Rust returned null string");
        wd_free_string(ptr);
        return result;
    }

    // ── FFI response model ───────────────────────────────────────────────

    /// <summary>Matches the Rust FfiResponse&lt;T&gt; JSON envelope.</summary>
    private sealed class FfiResponse<T>
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("data")]
        public T? Data { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    // ── FFI JSON models (snake_case → PascalCase mapping) ─────────────────

    /// <summary>Matches Rust FfiBox.</summary>
    public sealed class FfiBoxDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public int Type { get; set; }

        [JsonPropertyName("storage_path")]
        public string? StoragePath { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = string.Empty;

        public Box ToModel() => new(
            Guid.Parse(Id),
            Name,
            (BoxType)Type,
            StoragePath,
            SortOrder,
            DateTimeOffset.Parse(CreatedAt),
            DateTimeOffset.Parse(UpdatedAt));
    }

    /// <summary>Matches Rust FfiDrawerItem.</summary>
    public sealed class FfiDrawerItemDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("box_id")]
        public string BoxId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("item_kind")]
        public int ItemKind { get; set; }

        [JsonPropertyName("source_path")]
        public string? SourcePath { get; set; }

        [JsonPropertyName("stored_path")]
        public string? StoredPath { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = string.Empty;

        [JsonPropertyName("grid_column")]
        public int? GridColumn { get; set; }

        [JsonPropertyName("grid_row")]
        public int? GridRow { get; set; }

        public DrawerItem ToModel() => new(
            Guid.Parse(Id),
            Guid.Parse(BoxId),
            DisplayName,
            (ItemKind)ItemKind,
            SourcePath,
            StoredPath,
            SortOrder,
            DateTimeOffset.Parse(CreatedAt),
            DateTimeOffset.Parse(UpdatedAt),
            GridColumn,
            GridRow);
    }

    /// <summary>Matches Rust FfiTodoItem.</summary>
    public sealed class FfiTodoItemDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("box_id")]
        public string BoxId { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("is_completed")]
        public bool IsCompleted { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = string.Empty;

        [JsonPropertyName("completed_at")]
        public string? CompletedAt { get; set; }

        [JsonPropertyName("is_archived")]
        public bool IsArchived { get; set; }

        [JsonPropertyName("archived_at")]
        public string? ArchivedAt { get; set; }

        public TodoItem ToModel() => new(
            Guid.Parse(Id),
            Guid.Parse(BoxId),
            Title,
            IsCompleted,
            SortOrder,
            DateTimeOffset.Parse(CreatedAt),
            DateTimeOffset.Parse(UpdatedAt),
            CompletedAt is not null ? DateTimeOffset.Parse(CompletedAt) : null,
            IsArchived,
            ArchivedAt is not null ? DateTimeOffset.Parse(ArchivedAt) : null);
    }

    /// <summary>Matches Rust ItemDeleteResult.</summary>
    public sealed class FfiItemDeleteResultDto
    {
        [JsonPropertyName("item_id")]
        public string ItemId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("was_stored_item")]
        public bool WasStoredItem { get; set; }

        [JsonPropertyName("restored_path")]
        public string? RestoredPath { get; set; }

        [JsonPropertyName("restored_to_original")]
        public bool RestoredToOriginal { get; set; }

        [JsonPropertyName("restored_to_desktop")]
        public bool RestoredToDesktop { get; set; }

        public ItemDeleteResult ToModel() => new(
            Guid.Parse(ItemId),
            DisplayName,
            WasStoredItem,
            RestoredPath,
            RestoredToOriginal,
            RestoredToDesktop);
    }

    /// <summary>Matches Rust BoxDeleteResult.</summary>
    public sealed class FfiBoxDeleteResultDto
    {
        [JsonPropertyName("box_id")]
        public string BoxId { get; set; } = string.Empty;

        [JsonPropertyName("box_name")]
        public string BoxName { get; set; } = string.Empty;

        [JsonPropertyName("box_type")]
        public int BoxType { get; set; }

        [JsonPropertyName("box_removed")]
        public bool BoxRemoved { get; set; }

        [JsonPropertyName("restored_count")]
        public int RestoredCount { get; set; }

        [JsonPropertyName("failed_count")]
        public int FailedCount { get; set; }

        [JsonPropertyName("failures")]
        public List<string> Failures { get; set; } = new();

        public BoxDeleteResult ToModel() => new(
            Guid.Parse(BoxId),
            BoxName,
            (BoxType)BoxType,
            BoxRemoved,
            RestoredCount,
            FailedCount,
            Failures);
    }

    /// <summary>Matches Rust UpdateCheckResult.</summary>
    public sealed class FfiUpdateCheckResultDto
    {
        [JsonPropertyName("has_update")]
        public bool HasUpdate { get; set; }

        [JsonPropertyName("latest_version")]
        public string LatestVersion { get; set; } = "0.0.0";

        [JsonPropertyName("release_notes")]
        public string ReleaseNotes { get; set; } = string.Empty;

        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("expected_sha256")]
        public string? ExpectedSha256 { get; set; }

        public UpdateCheckResult ToModel() => new()
        {
            HasUpdate = HasUpdate,
            LatestVersion = Version.TryParse(LatestVersion, out var v) ? v : new Version(0, 0, 0),
            ReleaseNotes = ReleaseNotes,
            DownloadUrl = DownloadUrl,
            ExpectedSha256 = ExpectedSha256
        };
    }
}

// ============================================================================
// RustDrawerService – drop-in alternative for DrawerService
// ============================================================================

/// <summary>
/// Wraps the Rust native DrawerService via P/Invoke.
/// Mirrors the public API of <see cref="DrawerService"/> so the WPF app can swap between them.
/// </summary>
public sealed class RustDrawerService : IDisposable
{
    private readonly IntPtr _ctx;
    private bool _disposed;

    /// <summary>
    /// Create the native context. Must call <see cref="Dispose"/> when done.
    /// </summary>
    public RustDrawerService(string dataDirectory)
    {
        var dirBytes = System.Text.Encoding.UTF8.GetBytes(dataDirectory);
        _ctx = RustCore.wd_init(dirBytes);
        if (_ctx == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to initialize Rust core");
        }
    }

    // ── Box operations ───────────────────────────────────────────────────

    public IReadOnlyList<Box> GetBoxes()
    {
        var list = RustCore.Call<List<RustCore.FfiBoxDto>>(() => RustCore.wd_get_boxes(_ctx));
        return list.Select(dto => dto.ToModel()).ToList();
    }

    public Box CreateBox(string name, BoxType type)
    {
        var dto = RustCore.Call<RustCore.FfiBoxDto>(() =>
            RustCore.wd_create_box(_ctx, System.Text.Encoding.UTF8.GetBytes(name), (int)type));
        return dto.ToModel();
    }

    public void RenameBox(Guid boxId, string newName)
    {
        RustCore.CallVoid(() =>
            RustCore.wd_update_box_name(_ctx,
                System.Text.Encoding.UTF8.GetBytes(boxId.ToString()),
                System.Text.Encoding.UTF8.GetBytes(newName)));
    }

    public void ReorderBoxes(IReadOnlyList<Guid> orderedBoxIds)
    {
        var json = JsonSerializer.Serialize(orderedBoxIds.Select(id => id.ToString()));
        RustCore.CallVoid(() =>
            RustCore.wd_reorder_boxes(_ctx, System.Text.Encoding.UTF8.GetBytes(json)));
    }

    public BoxDeleteResult DeleteBox(Guid boxId)
    {
        var dto = RustCore.Call<RustCore.FfiBoxDeleteResultDto>(() =>
            RustCore.wd_delete_box(_ctx, System.Text.Encoding.UTF8.GetBytes(boxId.ToString())));
        return dto.ToModel();
    }

    // ── Item operations ──────────────────────────────────────────────────

    public IReadOnlyList<DrawerItem> GetItems(Guid boxId)
    {
        var ptr = Marshal.StringToCoTaskMemUTF8(boxId.ToString());
        try
        {
            var list = RustCore.Call<List<RustCore.FfiDrawerItemDto>>(() => RustCore.wd_get_items(_ctx, ptr));
            return list.Select(dto => dto.ToModel()).ToList();
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUTF8(ptr);
        }
    }

    public IReadOnlyList<DrawerItem> GetAllItems()
    {
        var list = RustCore.Call<List<RustCore.FfiDrawerItemDto>>(() => RustCore.wd_get_items(_ctx, IntPtr.Zero));
        return list.Select(dto => dto.ToModel()).ToList();
    }

    public IReadOnlyList<DrawerItem> SearchItems(string query, int limit = 200)
    {
        var list = RustCore.Call<List<RustCore.FfiDrawerItemDto>>(() =>
            RustCore.wd_search_items(_ctx, System.Text.Encoding.UTF8.GetBytes(query)));
        // Limit is applied on the C# side; the Rust side returns all matches.
        return list.Take(limit).Select(dto => dto.ToModel()).ToList();
    }

    public DrawerItem ImportPath(Guid boxId, string sourcePath, int? gridColumn = null, int? gridRow = null)
    {
        var dto = RustCore.Call<RustCore.FfiDrawerItemDto>(() =>
            RustCore.wd_import_path(_ctx,
                System.Text.Encoding.UTF8.GetBytes(boxId.ToString()),
                System.Text.Encoding.UTF8.GetBytes(sourcePath),
                gridColumn ?? -1,
                gridRow ?? -1));
        return dto.ToModel();
    }

    public void MoveItemToBox(Guid itemId, Guid targetBoxId, int? gridColumn = null, int? gridRow = null)
    {
        RustCore.CallVoid(() =>
            RustCore.wd_move_item_to_box(_ctx,
                System.Text.Encoding.UTF8.GetBytes(itemId.ToString()),
                System.Text.Encoding.UTF8.GetBytes(targetBoxId.ToString()),
                gridColumn ?? -1,
                gridRow ?? -1));
    }

    public ItemDeleteResult DeleteItem(Guid itemId)
    {
        var dto = RustCore.Call<RustCore.FfiItemDeleteResultDto>(() =>
            RustCore.wd_delete_item(_ctx, System.Text.Encoding.UTF8.GetBytes(itemId.ToString())));
        return dto.ToModel();
    }

    public string ExportItemToDirectory(Guid itemId, string targetDirectory)
    {
        return RustCore.Call<string>(() =>
            RustCore.wd_export_item(_ctx,
                System.Text.Encoding.UTF8.GetBytes(itemId.ToString()),
                System.Text.Encoding.UTF8.GetBytes(targetDirectory)));
    }

    public void UpdateItemGridPosition(Guid itemId, int? gridColumn, int? gridRow)
    {
        RustCore.CallVoid(() =>
            RustCore.wd_update_grid_pos(_ctx,
                System.Text.Encoding.UTF8.GetBytes(itemId.ToString()),
                gridColumn ?? -1,
                gridRow ?? -1));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            RustCore.wd_dispose(_ctx);
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~RustDrawerService() => Dispose();
}

// ============================================================================
// RustTodoService
// ============================================================================

/// <summary>
/// Wraps the Rust native TodoService via P/Invoke.
/// Mirrors the public API of <see cref="TodoService"/>.
/// </summary>
public sealed class RustTodoService
{
    private readonly IntPtr _ctx;

    /// <summary>
    /// The Rust context must outlive this service. Pass the same context used by <see cref="RustDrawerService"/>.
    /// </summary>
    public RustTodoService(IntPtr ctx)
    {
        _ctx = ctx;
    }

    public IReadOnlyList<TodoItem> GetTodos(Guid boxId)
    {
        var list = RustCore.Call<List<RustCore.FfiTodoItemDto>>(() =>
            RustCore.wd_get_todos(_ctx, System.Text.Encoding.UTF8.GetBytes(boxId.ToString())));
        return list.Select(dto => dto.ToModel()).ToList();
    }

    public TodoItem AddTodo(Guid boxId, string title)
    {
        var dto = RustCore.Call<RustCore.FfiTodoItemDto>(() =>
            RustCore.wd_add_todo(_ctx,
                System.Text.Encoding.UTF8.GetBytes(boxId.ToString()),
                System.Text.Encoding.UTF8.GetBytes(title)));
        return dto.ToModel();
    }

    public TodoItem SetCompleted(Guid todoId, bool isCompleted)
    {
        var dto = RustCore.Call<RustCore.FfiTodoItemDto>(() =>
            RustCore.wd_set_todo_completed(_ctx,
                System.Text.Encoding.UTF8.GetBytes(todoId.ToString()),
                isCompleted ? 1 : 0));
        return dto.ToModel();
    }

    public void DeleteTodo(Guid todoId)
    {
        RustCore.CallVoid(() =>
            RustCore.wd_delete_todo(_ctx, System.Text.Encoding.UTF8.GetBytes(todoId.ToString())));
    }

    public int ArchiveCompleted(Guid boxId)
    {
        return RustCore.Call<int>(() =>
            RustCore.wd_archive_completed(_ctx, System.Text.Encoding.UTF8.GetBytes(boxId.ToString())));
    }

    public TodoItem RestoreArchived(Guid todoId)
    {
        var dto = RustCore.Call<RustCore.FfiTodoItemDto>(() =>
            RustCore.wd_restore_archived(_ctx, System.Text.Encoding.UTF8.GetBytes(todoId.ToString())));
        return dto.ToModel();
    }
}

// ============================================================================
// RustUpdateService
// ============================================================================

/// <summary>
/// Wraps the Rust native UpdateService via P/Invoke.
/// Mirrors the public API of <see cref="UpdateService"/>.
/// </summary>
public sealed class RustUpdateService
{
    private readonly IntPtr _ctx;

    /// <summary>
    /// The Rust context must outlive this service. Pass the same context used by <see cref="RustDrawerService"/>.
    /// </summary>
    public RustUpdateService(IntPtr ctx)
    {
        _ctx = ctx;
    }

    public UpdateCheckResult CheckForUpdate(Version currentVersion)
    {
        var dto = RustCore.Call<RustCore.FfiUpdateCheckResultDto>(() =>
            RustCore.wd_check_update(_ctx,
                System.Text.Encoding.UTF8.GetBytes(currentVersion.ToString())));
        return dto.ToModel();
    }
}
