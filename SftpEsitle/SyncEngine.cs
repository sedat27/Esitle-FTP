using System.IO.Enumeration;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace SftpEsitle;

public enum SyncStatus { Same, LocalOnly, RemoteOnly, LocalNewer, RemoteNewer, SizeDiffers }
public enum SyncAction { Skip, Upload, Download, Delete, AlwaysSkip }

public sealed class SyncItem
{
    static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

    public required string RelPath { get; init; }
    public string? LocalRel { get; set; }
    public string? RemoteRel { get; set; }
    public long? LocalSize { get; set; }
    public DateTime? LocalTime { get; set; }   // UTC
    public long? RemoteSize { get; set; }
    public DateTime? RemoteTime { get; set; }  // UTC
    public SyncStatus Status { get; set; }
    public SyncAction Suggested { get; set; }
    public SyncAction Action { get; set; }
    public string? Error { get; set; }

    public bool CanUpload => LocalSize != null;
    public bool CanDownload => RemoteSize != null;
    /// <summary>Silme, dosyanın bulunduğu her taraftan yapılır (iki taraftaysa ikisinden de).</summary>
    public bool CanDelete => LocalSize != null || RemoteSize != null;
    public bool DeletesRemote => RemoteSize != null;
    public bool DeletesLocal => LocalSize != null;
    public string DeleteWhere => (DeletesRemote, DeletesLocal) switch
    {
        (true, true) => "İki taraftan",
        (true, false) => "Sunucudan",
        _ => "Yerelden",
    };
    public bool IsTransfer => Action is SyncAction.Upload or SyncAction.Download or SyncAction.Delete;
    public long TransferSize => Action switch
    {
        SyncAction.Upload => LocalSize ?? 0,
        SyncAction.Download => RemoteSize ?? 0,
        _ => 0,
    };

    public bool Allows(SyncAction a) => a switch
    {
        SyncAction.Upload => CanUpload,
        SyncAction.Download => CanDownload,
        SyncAction.Delete => CanDelete,
        _ => true,
    };

    public void Classify()
    {
        Error = null;
        // Yerelde silinmiş, sunucuda duran dosya: silmeyi öner (uygulamadan önce ayrıca uyarılır).
        if (LocalSize == null) { Status = SyncStatus.RemoteOnly; Suggested = SyncAction.Delete; }
        else if (RemoteSize == null) { Status = SyncStatus.LocalOnly; Suggested = SyncAction.Upload; }
        else
        {
            var diff = LocalTime!.Value - RemoteTime!.Value;
            bool sameTime = diff.Duration() <= Tolerance;
            if (sameTime && LocalSize == RemoteSize) { Status = SyncStatus.Same; Suggested = SyncAction.Skip; }
            else if (sameTime) { Status = SyncStatus.SizeDiffers; Suggested = SyncAction.Skip; }
            else if (diff > TimeSpan.Zero) { Status = SyncStatus.LocalNewer; Suggested = SyncAction.Upload; }
            else { Status = SyncStatus.RemoteNewer; Suggested = SyncAction.Download; }
        }
        Action = Suggested;
    }
}

public sealed class ExcludeMatcher
{
    readonly List<string> _patterns;
    readonly bool _skipDot;

    public ExcludeMatcher(string text, bool skipDot)
    {
        _skipDot = skipDot;
        // Noktalı virgül, virgül veya boşlukla ayrılabilir.
        _patterns = text.Split(new[] { ';', ',', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Replace('\\', '/'))
            .Append("*.sftptmp")
            .ToList();
    }

    public bool IsExcluded(string name, string relPath)
    {
        if (_skipDot && name.StartsWith('.')) return true;
        foreach (var p in _patterns)
        {
            if (p.Contains('/'))
            {
                if (FileSystemName.MatchesSimpleExpression(p.Trim('/'), relPath, ignoreCase: true)) return true;
            }
            else if (FileSystemName.MatchesSimpleExpression(p, name, ignoreCase: true)) return true;
        }
        return false;
    }
}

public sealed record ApplyOptions(bool Backup, bool InheritOwner);
public sealed record ApplyProgress(int Index, int Total, string File, long BytesDone, long BytesTotal);
public sealed record ApplyResult(SyncItem Item, string? Error, long NewSize, DateTime NewTime, bool Deleted = false);

public static class SyncEngine
{
    public static string RemoteJoin(string root, string rel) => root.TrimEnd('/') + "/" + rel;
    public static string LocalJoin(string root, string rel) => Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
    static string RemoteParent(string p) { var i = p.LastIndexOf('/'); return i <= 0 ? "/" : p[..i]; }

    public static string NormalizeRemote(string p)
    {
        p = p.Trim().Replace('\\', '/');
        if (!p.StartsWith('/')) p = "/" + p;
        while (p.Length > 1 && p.EndsWith('/')) p = p[..^1];
        return p;
    }

    public static List<SyncItem> Compare(SftpClient c, string localRoot, string remoteRoot, ExcludeMatcher ex,
        IProgress<string>? progress, List<string> warnings, CancellationToken ct)
    {
        var map = new Dictionary<string, SyncItem>(StringComparer.OrdinalIgnoreCase);
        SyncItem Get(string rel)
        {
            if (!map.TryGetValue(rel, out var it)) map[rel] = it = new SyncItem { RelPath = rel };
            return it;
        }

        void WalkLocal(string dir, string rel)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report("Yerel taranıyor: " + (rel.Length == 0 ? "\\" : rel));
            IEnumerable<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList(); }
            catch (Exception e) { warnings.Add($"Yerel klasör okunamadı: {dir} ({e.Message})"); return; }

            foreach (var fsi in entries)
            {
                var r = rel + fsi.Name;
                if (ex.IsExcluded(fsi.Name, r)) continue;
                if ((fsi.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (fsi is DirectoryInfo d) WalkLocal(d.FullName, r + "/");
                else if (fsi is FileInfo f)
                {
                    var it = Get(r);
                    it.LocalRel = r; it.LocalSize = f.Length; it.LocalTime = f.LastWriteTimeUtc;
                }
            }
        }

        void WalkRemote(string dir, string rel)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report("Sunucu taranıyor: /" + rel);
            List<ISftpFile> entries;
            try { entries = c.ListDirectory(dir).ToList(); }
            catch (Renci.SshNet.Common.SftpPermissionDeniedException e) { warnings.Add($"Sunucu klasörü okunamadı: {dir} ({e.Message})"); return; }

            foreach (var e in entries)
            {
                if (e.Name is "." or "..") continue;
                var r = rel + e.Name;
                if (ex.IsExcluded(e.Name, r)) continue;
                if (e.IsSymbolicLink) continue;
                if (e.IsDirectory) WalkRemote(e.FullName, r + "/");
                else if (e.IsRegularFile)
                {
                    var it = Get(r);
                    it.RemoteRel = r; it.RemoteSize = e.Length; it.RemoteTime = e.LastWriteTimeUtc;
                }
            }
        }

        WalkLocal(localRoot, "");
        WalkRemote(remoteRoot, "");
        foreach (var it in map.Values) it.Classify();
        return map.Values.OrderBy(i => i.RelPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<ApplyResult> Apply(SftpClient c, string localRoot, string remoteRoot, IList<SyncItem> todo,
        ApplyOptions opt, IProgress<ApplyProgress> p, CancellationToken ct)
    {
        var results = new List<ApplyResult>();
        long total = todo.Sum(i => i.TransferSize);
        long doneBytes = 0;
        var dirs = new HashSet<string>(StringComparer.Ordinal);

        for (int k = 0; k < todo.Count; k++)
        {
            if (ct.IsCancellationRequested) break;
            var it = todo[k];
            long baseBytes = doneBytes;
            int idx = k;
            p.Report(new(idx, todo.Count, it.RelPath, baseBytes, total));
            void Cb(ulong b) => p.Report(new(idx, todo.Count, it.RelPath, baseBytes + (long)b, total));
            try
            {
                if (it.Action == SyncAction.Delete)
                {
                    DeleteItem(c, localRoot, remoteRoot, it, opt);
                    results.Add(new(it, null, 0, default, Deleted: true));
                    continue;
                }
                var (size, time) = it.Action == SyncAction.Upload
                    ? Upload(c, localRoot, remoteRoot, it, opt, dirs, Cb)
                    : Download(c, localRoot, remoteRoot, it, opt, Cb);
                results.Add(new(it, null, size, time));
            }
            catch (Exception e)
            {
                results.Add(new(it, e.Message, 0, default));
            }
            doneBytes = baseBytes + it.TransferSize;
        }
        p.Report(new(todo.Count, todo.Count, "", total, total));
        return results;
    }

    static (long, DateTime) Upload(SftpClient c, string lroot, string rroot, SyncItem it, ApplyOptions o,
        HashSet<string> dirs, Action<ulong> cb)
    {
        var local = LocalJoin(lroot, it.LocalRel ?? it.RelPath);
        var remote = RemoteJoin(rroot, it.RemoteRel ?? it.LocalRel ?? it.RelPath);
        var parent = RemoteParent(remote);
        EnsureRemoteDir(c, parent, o.InheritOwner, dirs);

        bool exists = c.Exists(remote);
        SftpFileAttributes? prev = exists ? c.GetAttributes(remote) : null;
        if (exists && o.Backup)
        {
            var bak = remote + ".bak";
            if (c.Exists(bak)) c.DeleteFile(bak);
            c.RenameFile(remote, bak);
        }

        var mtime = File.GetLastWriteTimeUtc(local);
        using (var fs = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            c.UploadFile(fs, remote, true, cb);

        // Tarihi yerel dosyayla aynı yap; sahiplik/izinleri koru.
        var a = c.GetAttributes(remote);
        a.LastWriteTimeUtc = mtime;
        if (prev != null && o.Backup)
        {
            a.UserId = prev.UserId; a.GroupId = prev.GroupId;
            a.OwnerCanRead = prev.OwnerCanRead; a.OwnerCanWrite = prev.OwnerCanWrite; a.OwnerCanExecute = prev.OwnerCanExecute;
            a.GroupCanRead = prev.GroupCanRead; a.GroupCanWrite = prev.GroupCanWrite; a.GroupCanExecute = prev.GroupCanExecute;
            a.OthersCanRead = prev.OthersCanRead; a.OthersCanWrite = prev.OthersCanWrite; a.OthersCanExecute = prev.OthersCanExecute;
        }
        else if (prev == null && o.InheritOwner)
        {
            var pa = c.GetAttributes(parent);
            a.UserId = pa.UserId; a.GroupId = pa.GroupId;
        }
        c.SetAttributes(remote, a);
        return (a.Size, mtime);
    }

    static void DeleteItem(SftpClient c, string lroot, string rroot, SyncItem it, ApplyOptions o)
    {
        // Önce sunucu: başarısız olursa hiçbir şey değişmemiş olur.
        if (it.DeletesRemote) DeleteRemote(c, rroot, it, o);
        if (it.DeletesLocal)
        {
            try { DeleteLocal(lroot, it); }
            catch when (it.DeletesRemote)
            {
                // Sunucu kopyası gitti ama yerel silinemedi: listede doğru görünsün diye durumu güncelle.
                it.RemoteSize = null; it.RemoteTime = null; it.RemoteRel = null;
                throw;
            }
        }
    }

    static void DeleteRemote(SftpClient c, string rroot, SyncItem it, ApplyOptions o)
    {
        var remote = RemoteJoin(rroot, it.RemoteRel ?? it.RelPath);
        if (o.Backup)
        {
            var bak = remote + ".bak";
            if (c.Exists(bak)) c.DeleteFile(bak);
            c.RenameFile(remote, bak);
            return;
        }
        c.DeleteFile(remote);
        // Boş kalan klasörleri temizle (kök klasöre kadar, kök hariç).
        var root = rroot.TrimEnd('/');
        var dir = RemoteParent(remote);
        while (dir.Length > root.Length && dir.StartsWith(root + "/", StringComparison.Ordinal))
        {
            if (c.ListDirectory(dir).Any(e => e.Name is not "." and not "..")) break;
            c.DeleteDirectory(dir);
            dir = RemoteParent(dir);
        }
    }

    static void DeleteLocal(string lroot, SyncItem it)
    {
        // Yerel dosya Geri Dönüşüm Kutusu'na gönderilir.
        var local = LocalJoin(lroot, it.LocalRel ?? it.RelPath);
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(local,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(lroot));
        var dir = Path.GetDirectoryName(local);
        while (dir != null && dir.Length > root.Length && dir.StartsWith(root, StringComparison.OrdinalIgnoreCase)
               && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    static void EnsureRemoteDir(SftpClient c, string dir, bool inherit, HashSet<string> known)
    {
        if (dir == "/" || known.Contains(dir)) return;
        if (c.Exists(dir)) { known.Add(dir); return; }
        var parent = RemoteParent(dir);
        EnsureRemoteDir(c, parent, inherit, known);
        c.CreateDirectory(dir);
        if (inherit)
        {
            try
            {
                var pa = c.GetAttributes(parent);
                var a = c.GetAttributes(dir);
                a.UserId = pa.UserId; a.GroupId = pa.GroupId;
                c.SetAttributes(dir, a);
            }
            catch { }
        }
        known.Add(dir);
    }

    static (long, DateTime) Download(SftpClient c, string lroot, string rroot, SyncItem it, ApplyOptions o, Action<ulong> cb)
    {
        var local = LocalJoin(lroot, it.LocalRel ?? it.RemoteRel ?? it.RelPath);
        var remote = RemoteJoin(rroot, it.RemoteRel ?? it.RelPath);
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        var tmp = local + ".sftptmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                c.DownloadFile(remote, fs, cb);
            var rtime = c.GetAttributes(remote).LastWriteTimeUtc;
            if (File.Exists(local))
            {
                File.SetAttributes(local, FileAttributes.Normal);
                if (o.Backup) File.Copy(local, local + ".bak", true);
            }
            File.Move(tmp, local, true);
            File.SetLastWriteTimeUtc(local, rtime);
            return (new FileInfo(local).Length, rtime);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
