using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SftpEsitle;

/// <summary>Aynı sunucudaki bir site: ad + yerel klasör + sunucu klasörü.</summary>
public sealed class SiteProfile
{
    public string Name { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public string RemotePath { get; set; } = "";
    public string Excludes { get; set; } = "";
}

public sealed class AppSettings
{
    public List<SiteProfile> Sites { get; set; } = new();
    public string? LastSite { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string User { get; set; } = "root";
    public string? EncPassword { get; set; }
    public bool RememberPassword { get; set; }
    public string LocalPath { get; set; } = "";
    public string RemotePath { get; set; } = "/var/www";
    public string Excludes { get; set; } = ".git; node_modules; *.log; *.bak; .DS_Store; Thumbs.db";
    public bool MakeBackup { get; set; }
    public bool InheritOwner { get; set; } = true;
    public bool HideSame { get; set; } = true;
    public bool SkipDotFiles { get; set; } = true;
    /// <summary>"system" (Windows'u izle), "light" ya da "dark". Uygulama açılışında okunur.</summary>
    public string ThemeMode { get; set; } = "system";
    public Dictionary<string, string> KnownHosts { get; set; } = new();
    /// <summary>"sunucu|sunucu klasörü|yerel klasör" → sürekli atlanacak dosyaların göreli yolları</summary>
    public Dictionary<string, List<string>> AlwaysSkip { get; set; } = new();

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SftpEsitle");
    static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    // Şifre Windows DPAPI ile (yalnızca bu Windows kullanıcısı çözebilir) saklanır.
    public static string Protect(string s) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}
