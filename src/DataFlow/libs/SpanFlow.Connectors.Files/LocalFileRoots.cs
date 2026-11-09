using System.Text.Json.Nodes;

namespace SpanFlow.Connectors.Files;

/// <summary>Where <c>local</c> file connections may point (E50-T6). Section <c>Files:Local</c>.</summary>
public sealed class LocalFileOptions
{
    public const string SectionName = "Files:Local";

    /// <summary>
    /// Whether <c>local</c> file connections exist on this server at all. Default true (self-hosted); a
    /// hosted, multi-tenant operator turns it off, which removes <c>local</c> from the catalog and refuses
    /// saving or opening one. The platform's own stores (app assets) are not connections and don't care.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Folders a connection's <c>rootPath</c> may sit in, besides its own project's folder. Empty (the
    /// default) means a connection reaches only <c>{DataRoot}/projects/{projectId}/files</c>.
    /// </summary>
    public List<string> AllowedRoots { get; set; } = [];

    /// <summary>Holds each project's own folder, under <c>projects/{projectId}/files</c>. Set at startup when empty.</summary>
    public string DataRoot { get; set; } = "";

    /// <summary>No checks at all — test fixtures that point a store at a temp folder.</summary>
    internal bool Unrestricted { get; init; }
}

/// <summary>A local file root this server will not use; a 400 on save, a failed node on open.</summary>
public sealed class LocalFileRootRefusedException(string message) : InvalidOperationException(message);

/// <summary>
/// The local-file root rule (E50-T6). Before this, a connection's <c>rootPath</c> was whatever its author
/// typed, so <c>/</c> or <c>C:\</c> handed a pipeline the whole disk; the store only stopped paths escaping
/// that root.
/// <list type="bullet">
/// <item><b>On save</b> (<see cref="NormalizeForSave"/>), the only way a config gets in (create, edit, import,
/// copy, demo seed): an empty or relative root becomes a folder under the project's own folder; a root in
/// another project's folder is moved under this one (copy-to-project keeps working and never shares a
/// folder); anything else must sit in <see cref="LocalFileOptions.AllowedRoots"/>. The stored root is
/// absolute, so the store needs no project to find it.</item>
/// <item><b>On open</b> (<see cref="CheckOnOpen"/>), the root is checked again, symlinks followed, against the
/// allowed roots and the projects folder. That catches a config saved before this rule existed, and a
/// folder that became a link to somewhere else after it was saved.</item>
/// </list>
/// </summary>
public sealed class LocalFileRoots(LocalFileOptions options)
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public LocalFileOptions Options => options;

    /// <summary>The folder every project's own folder lives in.</summary>
    public string ProjectsRoot => Path.Combine(Path.GetFullPath(options.DataRoot), "projects");

    /// <summary>A project's own folder: the default root of its local file connections.</summary>
    public string ProjectRoot(Guid projectId) => Path.Combine(ProjectsRoot, projectId.ToString("D"), "files");

    /// <summary>True for a file connection config that uses the local store (the default store).</summary>
    public static bool IsLocal(string type, JsonNode? config) =>
        type.Equals("file", StringComparison.OrdinalIgnoreCase)
        && config?["store"]?.GetValue<string>() is null or "" or "local";

    /// <summary>
    /// The absolute <c>rootPath</c> to store for a connection in <paramref name="projectId"/>, creating the
    /// folder when it is the project's own. Throws <see cref="ArgumentException"/> (a validation error) when
    /// the root is not allowed.
    /// </summary>
    public string NormalizeForSave(string? rootPath, Guid? projectId)
    {
        if (!options.Enabled) throw new ArgumentException(DisabledMessage);
        if (options.Unrestricted) return rootPath ?? "";

        var typed = (rootPath ?? "").Trim();
        string candidate;
        if (typed.Length == 0 || !Path.IsPathFullyQualified(typed))
        {
            if (projectId is not Guid project || project == Guid.Empty)
            {
                throw new ArgumentException("A local file connection outside a project needs an absolute rootPath inside Files:Local:AllowedRoots.");
            }
            candidate = Path.GetFullPath(Path.Combine(ProjectRoot(project), typed));
            EnsureInside(candidate, ProjectRoot(project), typed);
        }
        else
        {
            candidate = Path.GetFullPath(typed);
            if (projectId is Guid project && project != Guid.Empty && Rebase(candidate, project) is { } rebased)
            {
                candidate = rebased;
            }
        }

        var real = RealPath(candidate);
        var own = projectId is Guid p && p != Guid.Empty ? RealPath(ProjectRoot(p)) : null;
        if (own is not null && IsInside(real, own))
        {
            Directory.CreateDirectory(candidate);
            return candidate;
        }
        if (AllowedRoots().Any(root => IsInside(real, root))) return candidate;

        throw new ArgumentException(OutsideMessage(typed.Length == 0 ? candidate : typed));
    }

    /// <summary>The root a store may open, symlinks resolved; throws <see cref="LocalFileRootRefusedException"/> otherwise.</summary>
    public string CheckOnOpen(string? rootPath)
    {
        if (!options.Enabled) throw new LocalFileRootRefusedException(DisabledMessage);
        if (string.IsNullOrWhiteSpace(rootPath)) throw new InvalidOperationException("local file store config requires a 'rootPath'.");
        if (options.Unrestricted) return Path.GetFullPath(rootPath);

        if (!Path.IsPathFullyQualified(rootPath))
        {
            throw new LocalFileRootRefusedException(
                $"rootPath '{rootPath}' is relative. Open the connection and save it once: a relative root is placed in its project's own folder.");
        }
        var real = RealPath(rootPath);
        if (IsInside(real, RealPath(ProjectsRoot)) || AllowedRoots().Any(root => IsInside(real, root))) return real;
        throw new LocalFileRootRefusedException(OutsideMessage(rootPath));
    }

    /// <summary>Null when the root may be used, otherwise why not — for the startup warning.</summary>
    public string? Problem(string? rootPath)
    {
        try
        {
            CheckOnOpen(rootPath);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    private const string DisabledMessage =
        "Local file connections are disabled on this server (Files:Local:Enabled). Use sftp, ftp, s3 or azureBlob.";

    private static string OutsideMessage(string root) =>
        $"rootPath '{root}' is outside the folders this server allows for local files: this project's own folder, " +
        "or one an operator listed in Files:Local:AllowedRoots.";

    private IEnumerable<string> AllowedRoots() =>
        options.AllowedRoots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => RealPath(r.Trim()));

    /// <summary><c>{ProjectsRoot}/{other}/files/rest</c> → this project's folder + <c>rest</c>; null when not of that shape.</summary>
    private string? Rebase(string fullPath, Guid projectId)
    {
        var prefix = ProjectsRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, PathComparison)) return null;
        var parts = fullPath[prefix.Length..].Split(Path.DirectorySeparatorChar, 3);
        if (parts.Length < 2 || !Guid.TryParse(parts[0], out _) || !parts[1].Equals("files", PathComparison)) return null;
        return parts.Length == 3 ? Path.Combine(ProjectRoot(projectId), parts[2]) : ProjectRoot(projectId);
    }

    private static void EnsureInside(string candidate, string root, string typed)
    {
        if (!IsInside(candidate, Path.GetFullPath(root))) throw new ArgumentException($"rootPath '{typed}' escapes this project's folder.");
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or below it.</summary>
    public static bool IsInside(string path, string root)
    {
        path = Path.TrimEndingDirectorySeparator(path);
        root = Path.TrimEndingDirectorySeparator(root);
        return path.Equals(root, PathComparison)
            || path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison)
            || (root.Length > 0 && root[^1] == Path.DirectorySeparatorChar && path.StartsWith(root, PathComparison)); // a drive root, "C:\" or "/"
    }

    /// <summary>
    /// <paramref name="path"/> with every symbolic link (or junction) along it resolved, so "inside" is judged
    /// on where the bytes really are. Components that don't exist yet (a file about to be written) are kept as
    /// written below the last one that does.
    /// </summary>
    public static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        for (var hops = 0; hops < 40; hops++)
        {
            var resolved = ResolveOnce(full);
            if (resolved is null) return Path.TrimEndingDirectorySeparator(full);
            full = resolved;
        }
        throw new IOException($"Too many symbolic links resolving '{path}'.");
    }

    /// <summary>The path with its first link component replaced by the link's target; null when it has none.</summary>
    private static string? ResolveOnce(string full)
    {
        var root = Path.GetPathRoot(full) ?? "";
        var current = root;
        var parts = full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (!info.Exists) return null;
            if (info.LinkTarget is { } target)
            {
                var targetFull = Path.GetFullPath(target, Path.GetDirectoryName(current) ?? root);
                return Path.Combine([targetFull, .. parts[(i + 1)..]]);
            }
        }
        return null;
    }
}
