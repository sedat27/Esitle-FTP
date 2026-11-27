using System.Diagnostics;
using Microsoft.Win32;

namespace SftpEsitle;

/// <summary>Tek exe'yi kullanıcı profiline kurar: Başlat menüsü, masaüstü kısayolu ve "Uygulamalar" listesinden kaldırma.</summary>
static class Installer
{
    const string AppName = "SFTP Eşitle";
    static readonly string Version = AppInfo.Version;
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SftpEsitle";

    public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SFTP Esitle");
    public static string InstalledExe => Path.Combine(InstallDir, "SftpEsitle.exe");
    static string StartMenuLnk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
    static string DesktopLnk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

    public static bool IsInstalled => File.Exists(InstalledExe);
    public static bool IsRunningInstalled =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    /// <returns>true: kurulu kopya başlatıldı, bu pencere kapanmalı.</returns>
    public static bool RunInstallWizard(IWin32Window owner)
    {
        // Tek dosya (PublishSingleFile) değilse exe yanındaki .dll'ler olmadan çalışmaz; kopyalamak kurulu sürümü bozar.
        if (!string.IsNullOrEmpty(typeof(Installer).Assembly.Location))
        {
            MessageBox.Show(owner,
                "Bu kopya tek dosyalık bir derleme değil (yanında .dll dosyaları var), bu yüzden kurulamaz.\n\n" +
                "Kurulum için 'dotnet publish ... -p:PublishSingleFile=true' ile üretilen exe'yi kullanın.",
                "Kurulum", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var ans = MessageBox.Show(owner,
            $"{AppName} bilgisayarına kurulacak:\n\n{InstallDir}\n\n" +
            "• Başlat menüsüne kısayol eklenir\n• Ayarlar > Uygulamalar listesinden kaldırılabilir\n• Yönetici yetkisi gerekmez\n\n" +
            "Masaüstüne de kısayol eklensin mi?",
            "Kurulum", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (ans == DialogResult.Cancel) return false;

        try
        {
            Install(ans == DialogResult.Yes);
        }
        catch (IOException)
        {
            MessageBox.Show(owner, "Kurulu sürüm şu an açık görünüyor. Onu kapatıp tekrar deneyin.", "Kurulum",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Kurulum yapılamadı:\n" + ex.Message, "Kurulum", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        if (MessageBox.Show(owner, "Kurulum tamamlandı ✓\n\nKurulu sürüm şimdi açılsın mı?", "Kurulum",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
        {
            Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });
            return true;
        }
        return false;
    }

    static void Install(bool desktop)
    {
        Directory.CreateDirectory(InstallDir);
        File.Copy(Environment.ProcessPath!, InstalledExe, true);
        CreateShortcut(StartMenuLnk, InstalledExe);
        if (desktop) CreateShortcut(DesktopLnk, InstalledExe);

        using var k = Registry.CurrentUser.CreateSubKey(UninstallKey);
        k.SetValue("DisplayName", AppName);
        k.SetValue("DisplayVersion", Version);
        k.SetValue("Publisher", AppName);
        k.SetValue("DisplayIcon", InstalledExe + ",0");
        k.SetValue("InstallLocation", InstallDir);
        k.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
    }

    static void CreateShortcut(string lnk, string target)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell bulunamadı");
        dynamic shell = Activator.CreateInstance(t)!;
        dynamic sc = shell.CreateShortcut(lnk);
        sc.TargetPath = target;
        sc.WorkingDirectory = Path.GetDirectoryName(target);
        sc.IconLocation = target + ",0";
        sc.Description = "Yerel ve SFTP sunucu klasörlerini eşitle";
        sc.Save();
    }

    public static void Uninstall()
    {
        if (MessageBox.Show($"{AppName} bilgisayardan kaldırılsın mı?\n\nKayıtlı ayarlar (sunucu bilgileri, şifre) da silinecek.",
                "Kaldır", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        try { File.Delete(StartMenuLnk); } catch { }
        try { File.Delete(DesktopLnk); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        try { Directory.Delete(AppSettings.Dir, true); } catch { }

        // Çalışan exe kendini silemez; kısa bir gecikmeyle klasörü sil.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{InstallDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
            WindowStyle = ProcessWindowStyle.Hidden,
        });
        MessageBox.Show($"{AppName} kaldırıldı.", "Kaldır", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
