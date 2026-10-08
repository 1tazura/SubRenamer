using System.Text.Json.Serialization;

namespace SubRenamer.Mobile.Services;

// Explicit metadata keeps persisted settings, undo hashes and archive indexes
// usable when Android Release publishing disables reflection-based JSON.
// Keep the existing property names, numeric enum values and defaults so older
// installs can update without losing their SAF bookmark or undo journal.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(StorageRootIdentity))]
internal partial class SettingsJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(ArchiveIndexCacheRecord[]))]
internal partial class ArchiveCacheJsonContext : JsonSerializerContext;
