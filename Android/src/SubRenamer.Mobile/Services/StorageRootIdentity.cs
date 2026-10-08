namespace SubRenamer.Mobile.Services;

/// <summary>
/// Persisted location identity, not a credential or a display label. Unknown
/// backends must fail closed; a network-relative path is never a Download path.
/// SMB support must additionally pin the server/share identity before writing.
/// </summary>
public sealed record StorageRootIdentity(string Backend, string RootUri)
{
    public const string SafBackend = "saf";

    public static StorageRootIdentity ForSaf(Uri rootUri)
    {
        if (!IsSafUri(rootUri))
            throw new NotSupportedException("A network URI requires its own verified storage backend.");
        return new(SafBackend, rootUri.AbsoluteUri);
    }

    public bool MatchesSaf(Uri rootUri)
        => Backend == SafBackend && IsSafUri(rootUri) &&
           string.Equals(RootUri, rootUri.AbsoluteUri, StringComparison.Ordinal);

    private static bool IsSafUri(Uri uri)
        => uri.IsAbsoluteUri && (uri.IsFile || uri.Scheme == "content");
}
