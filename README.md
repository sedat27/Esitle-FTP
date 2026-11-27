# SFTP Eşitle

**TR:** Yerel bir klasörü SFTP sunucusundaki bir klasörle karşılaştırır, farkları listeler ve dosyaları seçerek yükler, indirir veya siler. Windows için yazıldı (C#, WinForms, SSH.NET).

**EN:** Compares a local folder with a folder on an SFTP server, lists the differences and lets you upload, download or delete files one by one. Built for Windows (C#, WinForms, SSH.NET).

> Hiçbir dosya siz **Uygula** demeden değişmez. / Nothing changes until you press **Apply**.

---

## Türkçe

### Özellikler

- **Karşılaştırma:** Yerel ve sunucu klasörleri taranır; her dosya için durum gösterilir: *Aynı*, *Sadece yerelde*, *Sadece sunucuda*, *Yerel daha yeni*, *Sunucu daha yeni*, *Boyut farklı* (2 sn tarih toleransı).
- **Akıllı öneri:** Her dosyaya uygun işlem önceden seçilir (Upload / Download / Sil / Atla); dilediğiniz gibi değiştirebilirsiniz.
- **Sürekli atla:** Bazı dosyaları belirli bir sunucu + klasör çifti için kalıcı olarak hariç tutabilirsiniz.
- **Hariç tutma kuralları:** `*` ve `?` joker karakterleri (`.git; node_modules; *.log` gibi). İsterseniz nokta ile başlayan dosyalar da atlanabilir.
- **Güvenli aktarım:**
  - Sunucuya yüklenen dosyanın değiştirilme tarihi yerel dosyayla aynı yapılır.
  - İndirmeler önce geçici bir dosyaya yazılır, sonra yerine taşınır.
  - İsteğe bağlı `.bak` yedeği alınır.
  - Yeni dosya ve klasörler üst klasörün sahibini (uid/gid) alabilir.
- **Silme güvenliği:** Yerel silmeler Geri Dönüşüm Kutusu'na gider. Silme içeren işlemlerden önce kırmızı uyarılı bir onay penceresi açılır.
- **Host key doğrulama:** İlk bağlantıda sunucunun SHA256 parmak izi sorulup kaydedilir. Anahtar değişirse uyarı verilir.
- **Kayıtlı siteler:** Aynı sunucudaki farklı siteler (yerel klasör + sunucu klasörü + hariç tutma kuralları) profil olarak saklanır.
- **Sunucu klasörü seçici:** Sunucudaki klasörler ağaç görünümünde gezilebilir.
- **Şifre güvenliği:** "Şifreyi hatırla" seçilirse şifre Windows DPAPI ile yalnızca sizin Windows hesabınızın çözebileceği şekilde şifrelenir.
- Klavye kısayolları (`U`, `D`, `Delete`, `S`, `A`, `Space`), sağ tık menüsü, ilerleme ve hız göstergesi, açık/koyu tema.

### Gereksinimler

- Windows 10 / 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (derlemek için)
- SFTP erişimi olan bir sunucu (varsayılan port **22**)

### Derleme ve çalıştırma

```powershell
cd SftpEsitle
dotnet run -c Release
```

Tek dosyalık `.exe` üretmek için:

```powershell
dotnet publish SftpEsitle -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o cikti
```

### Kullanım

1. Sunucu IP, port, kullanıcı ve şifreyi girip **Bağlan**'a basın.
2. Yerel klasörü ve sunucu klasörünü seçin.
3. **Karşılaştır**'a basın ve listede her dosya için işlemi kontrol edin.
4. **Uygula**'ya basın. Onay penceresinde özeti görüp onaylayın.

### Ayarlar nerede?

Ayarlar `%APPDATA%\SftpEsitle\settings.json` dosyasında tutulur, proje klasöründe değil. Şifre bu dosyada düz metin olarak yer almaz.

---

## English

### Features

- **Compare:** Scans the local and remote folders and shows a status for each file: *Same*, *Local only*, *Remote only*, *Local newer*, *Remote newer*, *Size differs* (2-second time tolerance).
- **Suggested actions:** Each file gets a suggested action (Upload / Download / Delete / Skip) that you can change.
- **Always skip:** Lets you permanently ignore files for a given server + folder pair.
- **Exclude rules:** Wildcards `*` and `?` (e.g. `.git; node_modules; *.log`). Dot-files can optionally be skipped.
- **Safe transfers:**
  - Uploaded files keep the local modification time.
  - Downloads go to a temp file first and then replace the target.
  - Optional `.bak` backups.
  - New files and folders can inherit the parent folder's owner (uid/gid).
- **Safe deletes:** Local deletes go to the Recycle Bin. Any batch that includes deletions shows a red warning dialog before it runs.
- **Host key verification:** On the first connection you confirm the server's SHA256 fingerprint and it is saved. If the key changes later, you get a warning.
- **Saved sites:** Store several sites on the same server as profiles (local folder + remote folder + excludes).
- **Remote folder picker:** Browse the server's folders in a tree view.
- **Password security:** "Remember password" encrypts the password with Windows DPAPI, so only your Windows account can decrypt it.
- Keyboard shortcuts, context menu, progress and speed display, light/dark theme.

### Requirements

- Windows 10 / 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (to build)
- A server with SFTP access (default port **22**)

### Build & run

```powershell
cd SftpEsitle
dotnet run -c Release
```

Single-file executable:

```powershell
dotnet publish SftpEsitle -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o cikti
```

### Usage

1. Enter host, port, user and password, then click **Bağlan** (Connect).
2. Pick the local folder and the remote folder.
3. Click **Karşılaştır** (Compare) and check the action for each file.
4. Click **Uygula** (Apply), review the summary and confirm.

### Where are settings stored?

Settings are stored in `%APPDATA%\SftpEsitle\settings.json`, outside the project folder. The password is never written to that file in plain text.

---

## Proje yapısı / Project layout

| Dosya / File | Açıklama / Description |
|---|---|
| `SftpEsitle/MainForm.cs` | Ana pencere / Main window |
| `SftpEsitle/SyncEngine.cs` | Karşılaştırma ve aktarım / Compare & transfer engine |
| `SftpEsitle/Settings.cs` | Ayarlar ve DPAPI şifreleme / Settings & DPAPI encryption |
| `SftpEsitle/FileBrowserForm.cs` | Sunucu dosya gezgini / Remote file browser |
| `SftpEsitle/RemoteFolderDialog.cs` | Sunucu klasörü seçici / Remote folder picker |
| `SftpEsitle/ConfirmDialog.cs` | Uygula onay penceresi / Apply confirmation dialog |
| `SftpEsitle/Theme.cs` | Tema ve özel kontroller / Theme & custom controls |
| `SftpEsitle/Installer.cs` | Bilgisayara kur / kaldır / Install & uninstall |
| `MAC_PROMPT.md` | macOS sürümü için şartname / Spec for a macOS port |

## Lisans / License

Ayrıntılar için [LICENSE](LICENSE) dosyasına bakın. / See [LICENSE](LICENSE).
