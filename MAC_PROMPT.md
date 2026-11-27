# macOS için “SFTP Eşitle” uygulaması — Yapay zekaya verilecek prompt

> Aşağıdaki metnin tamamını (bu satırın altından itibaren) Mac'teki yapay zekaya (Claude Code, Cursor, Xcode asistanı vb.) yapıştır.

---

Benim için **native bir macOS uygulaması** yaz: adı **“SFTP Eşitle”**. Bu uygulamanın Windows sürümü zaten var (C# / WinForms / SSH.NET). Mac sürümü aynı özelliklere, aynı davranışa, aynı Türkçe metinlere ve aynı görsel dile sahip olmalı. Aşağıdaki şartname eksiksiz uygulanmalı; bir şeyi atlarsan veya değiştirirsen bana söyle.

## 1. Teknik çerçeve

- **Dil / arayüz:** Swift 5.10+ ve **SwiftUI** (gerekirse AppKit köprüsü). Hedef **macOS 14 Sonoma ve üzeri**, Apple Silicon + Intel (Universal).
- **Proje:** Xcode projesi (veya Swift Package + `.app` paketleme betiği). Komut satırından `xcodebuild` ile derlenebilmeli.
- **SFTP kütüphanesi:** Swift Package Manager ile **Citadel** (SwiftNIO SSH tabanlı, saf Swift). Kodlamaya başlamadan önce kütüphanenin şunları desteklediğini **doğrula**: parola ile giriş, klasör listeleme (boyut + değiştirilme tarihi + tür), dosya yükleme/indirme (ilerleme bildirimiyle), `mkdir`, `rename`, `remove`, `rmdir`, **öznitelik yazma (mtime, uid/gid, izinler)**, sunucu host key'ini doğrulama kancası. Eksik olan varsa ya kütüphanenin alt seviye SFTP API'siyle tamamla ya da libssh2 tabanlı bir alternatife geç ve neden geçtiğini bana açıkla.
- **Şifre saklama:** macOS **Keychain** (Windows'taki DPAPI'nin karşılığı). Şifre asla düz metin olarak JSON'a yazılmayacak.
- **Ayarlar dosyası:** `~/Library/Application Support/SftpEsitle/settings.json` (okunaklı, girintili JSON).
- **Uzun işlemler** (bağlanma, tarama, aktarım) arka planda `async/await` ile çalışmalı, arayüz donmamalı, iptal edilebilmeli (`Task` cancellation).
- Tek pencere uygulaması. Pencere varsayılan **1220×840**, en küçük **1020×700**, açılışta ekranın %94'ünü aşmayacak şekilde ortalanır.
- Windows'taki “Bilgisayara kur” düğmesi ve kurulum/kaldırma sihirbazı **Mac'te gerekmez**; yerine düzgün bir `.app` paketi + isteğe bağlı `.dmg` oluşturma betiği yeter.

## 2. Veri modeli

```
SyncStatus: same | localOnly | remoteOnly | localNewer | remoteNewer | sizeDiffers
SyncAction: skip | upload | download | delete | alwaysSkip

SyncItem:
  relPath        (göreli yol, "/" ayraçlı, ör. "css/site.css")
  localRel, remoteRel  (o tarafta bulunan gerçek yazımı)
  localSize?, localTime? (UTC)
  remoteSize?, remoteTime? (UTC)
  status, suggested, action, error?
```

Kurallar:
- `canUpload` = yerelde var; `canDownload` = sunucuda var; **`canDelete` = dosya yalnızca tek tarafta varsa** (silme o taraftan yapılır). `deletesOnServer` = sunucuda var, yerelde yok.
- Bir işlem, dosya için izin verilmiyorsa uygulanmaz (ör. yerelde olmayan dosyaya Upload atanamaz).
- **Sınıflandırma (classify):** tarih toleransı **2 saniye**.
  - Yerelde yok → `remoteOnly`, öneri **Sil** (sunucudan).
  - Sunucuda yok → `localOnly`, öneri **Upload**.
  - Tarih farkı ≤ 2 sn ve boyut aynı → `same`, öneri **Atla**.
  - Tarih farkı ≤ 2 sn ama boyut farklı → `sizeDiffers`, öneri **Atla**.
  - Yerel daha yeni → `localNewer`, öneri **Upload**.
  - Sunucu daha yeni → `remoteNewer`, öneri **Download**.
  - `action` başlangıçta `suggested` ile aynı.
- Yol eşleştirmesi **büyük/küçük harf duyarsız** (Windows davranışı korunacak).

Ayarlar (JSON) alanları: `sites[] {name, localPath, remotePath, excludes}`, `lastSite`, `host`, `port` (varsayılan **22**), `user` (varsayılan **root**), `rememberPassword`, `localPath`, `remotePath` (varsayılan `/var/www`), `excludes` (varsayılan `.git; node_modules; *.log; *.bak; .DS_Store; Thumbs.db`), `makeBackup` (false), `inheritOwner` (true), `hideSame` (true), `skipDotFiles` (true), `knownHosts {"host:port": "SHA256 parmak izi"}`, `alwaysSkip {"host:port|sunucuKlasörü|yerelKlasör(küçük harf)": [göreli yollar]}`.

## 3. Hariç tutma (exclude) kuralları

- Metin `;` `,` boşluk, tab veya satır sonu ile bölünür; `\` → `/` çevrilir. Listeye her zaman `*.sftptmp` eklenir.
- “Nokta ile başlayanları atla” açıksa adı `.` ile başlayan **tüm** dosya/klasörler atlanır.
- Desen `/` içeriyorsa **göreli yolun tamamına** (baştaki/sondaki `/` kırpılarak) eşleştirilir; içermiyorsa **sadece ada**, her seviyede. Joker: `*` ve `?`, büyük/küçük harf duyarsız.
- Hariç tutulan klasörün içine girilmez.

## 4. Karşılaştırma (tarama)

1. Yerel klasör geçerli değilse “Geçerli bir yerel klasör seç.” uyarısı; sunucu klasörü boşsa “Sunucu klasörünü yaz veya seç.”
2. Sunucu yolu normalize edilir: kırp, `\`→`/`, başa `/` ekle, sondaki `/`'leri kaldır.
3. Gerekirse otomatik bağlan.
4. Sunucu klasörü yoksa: “Sunucuda bu klasör yok: … Oluşturulsun mu?” → Evet ise yolu parça parça oluştur.
5. Seçili site varsa ve klasörler değiştiyse siteyi güncelle; ayarları kaydet.
6. Yereli özyinelemeli tara (sembolik linkleri atla), sonra sunucuyu tara (`.`/`..` ve sembolik linkleri atla, sadece normal dosyalar). İlerleme etiketinde “Yerel taranıyor: …” / “Sunucu taranıyor: /…” göster.
7. Okunamayan klasörler işlemi durdurmaz; günlüğe turuncu uyarı olarak yazılır.
8. Sonuçlar yola göre sıralanır, her öğe sınıflandırılır. `alwaysSkip` listesinde olan yollar `alwaysSkip` olarak işaretlenir ve günlüğe “N dosya “sürekli atla” olarak işaretli.” yazılır.
9. Günlük: “Karşılaştırma bitti (X,X sn): N dosya, F farklı, A aynı.” İlerleme etiketi: farklı yoksa “İki klasör eşit ✓”, varsa “F farklı dosya bulundu. İşlemleri kontrol et, sonra “Uygula”ya bas.”
10. “Nokta ile başlayanları atla” kutusu değişince, liste doluysa otomatik yeniden karşılaştır.

**Karşılaştır hiçbir dosyayı değiştirmez.**

## 5. Uygula (aktarım)

- Uygulanacaklar: action ∈ {upload, download, delete}. Hiç yoksa: “Uygulanacak işlem yok. Listeden dosyalar için Upload / Download / Sil seç.”
- Önce **onay penceresi** (bölüm 7) gösterilir.
- Dosyalar sırayla işlenir; toplam bayt ve anlık hız (≥0,5 sn sonra “X MB/sn”) ilerleme çubuğunda ve etikette: “3 / 20   css/site.css  ·  1,2 MB/sn”.
- **Durdur** düğmesi: “Durduruluyor… (aktarılan dosya bitince)” — o anki dosya bitince durur.
- Bir dosyadaki hata diğerlerini durdurmaz; hata öğeye yazılır.

**Upload:**
1. Sunucuda üst klasörler yoksa oluşturulur (oluşturulanlar önbelleğe alınır). “Yeni dosyalara klasörün sahibini ver” açıksa yeni klasörün uid/gid'i üst klasörden kopyalanır (hata olursa sessizce geç).
2. Hedef varsa ve **.bak** açıksa: varsa eski `dosya.bak` silinir, mevcut dosya `dosya.bak` olarak yeniden adlandırılır.
3. Dosya yüklenir (yerel dosya paylaşımlı okunur).
4. Sunucu dosyasının **mtime'ı yerel dosyanınkiyle aynı** yapılır.
5. Önceden vardı ve .bak alındıysa: eski dosyanın uid/gid ve tüm izin bitleri yeni dosyaya kopyalanır. Önceden yoktu ve “sahibini ver” açıksa: uid/gid üst klasörden alınır.

**Download:**
1. Yerel üst klasörler oluşturulur.
2. Önce `dosya.sftptmp` geçici dosyasına indirilir.
3. Yerel dosya varsa: salt-okunur bayrağı kaldırılır; .bak açıksa `dosya.bak` olarak kopyalanır.
4. Geçici dosya hedefin üzerine taşınır; yerel mtime **sunucudaki mtime** yapılır.
5. Hata olsa da geçici dosya her durumda temizlenir.

**Sil:**
- Sunucuda: .bak açıksa silmek yerine `dosya.bak` olarak yeniden adlandır. Değilse sil ve boş kalan üst klasörleri kök klasöre kadar (kök hariç) sil.
- Yerelde: dosya **Çöp Sepeti'ne** gönderilir (`FileManager.trashItem`), sonra boş kalan üst klasörler kök hariç silinir.

**Sonrası:** Başarılı silmeler listeden çıkar; başarılı aktarımlarda iki tarafın boyut/tarih bilgisi güncellenip yeniden sınıflandırılır (artık “Aynı”). Günlüğe her dosya için `↑ yol` (mavi), `↓ yol` (yeşil), `✕ yol (sunucudan silindi / sunucuda .bak yapıldı / Çöp Sepeti'ne gönderildi)` (kırmızı), `✗ yol: hata` (kırmızı). Özet: “Bitti (X sn): N başarılı, F hatalı, S durdurulduğu için yapılmadı.” Hata varsa uyarı: “F dosya aktarılamadı. Ayrıntılar günlükte ve listede (kırmızı “Hata”).”

## 6. Bağlantı

- Alanlar: Sunucu IP (placeholder “örn. 192.168.1.10”), Port, Kullanıcı, Şifre (gizli). Herhangi birinde Enter → Bağlan.
- Doğrulama: IP boşsa “Sunucu IP adresini gir.”, port 1–65535 değilse “Port geçersiz.”, kullanıcı boşsa `root` kullan, şifre boşsa “Şifreyi gir.”
- Kimlik doğrulama: **parola**, sunucu isterse **keyboard-interactive** (tüm sorulara şifreyi ver). Bağlantı zaman aşımı 15 sn, keep-alive 30 sn, işlem zaman aşımı 2 dk.
- **Host key doğrulama (önemli güvenlik özelliği):**
  - İlk bağlantı: “Bu sunucuya ilk kez bağlanılıyor. Sunucu: host:port, Anahtar parmak izi: SHA256:… Bu sunucuya güveniyor musun? (Anahtar kaydedilir, değişirse uyarılırsın.)” Evet → `knownHosts`'a kaydet.
  - Kayıtlı ama farklı: “⚠ DİKKAT: Sunucunun güvenlik anahtarı DEĞİŞMİŞ! … Sunucu yeniden kurulduysa bu normaldir. Değilse araya biri girmiş olabilir. Yeni anahtara güvenip devam edilsin mi?” — varsayılan düğme **Hayır**.
- Bağlıyken durum etiketi yeşil “● Bağlı: kullanıcı@host”, değilken gri “● Bağlı değil”. Düğme “Bağlan” (birincil) / “Bağlantıyı kes” (ikincil). Bağlıyken bağlantı alanları kilitli.
- Başarılı bağlantıda günlüğe “Bağlandı ✓ (sunucu sürümü)”, ayarlar kaydedilir.
- “Şifreyi hatırla” kutusu: işaretliyse şifre Keychain'e yazılır, açılışta oradan okunur; işaretsizse Keychain'den silinir. İpucu: “Şifre bu Mac'te Anahtar Zinciri'nde güvenli şekilde saklanır.”
- **Anlaşılır hata mesajları** (Türkçe):
  - Kimlik hatası → “Kullanıcı adı veya şifre hatalı. (Sunucuda root ile şifreli giriş kapalı olabilir: sshd_config → PermitRootLogin)”
  - Host key reddedildi → “Sunucu anahtarı onaylanmadı, bağlantı iptal edildi.”
  - Zaman aşımı → “Zaman aşımı. IP ve port doğru mu, sunucu erişilebilir mi?”
  - Bağlantı reddedildi → “Bağlantı reddedildi. Port doğru mu? SSH servisi bu portta çalışıyor mu?”
  - Host bulunamadı → “Sunucu bulunamadı. Adres doğru mu?”
  - İzin / yol yok / bağlantı koptu → “İzin reddedildi: …”, “Yol bulunamadı: …”, “Bağlantı koptu: …”

## 7. Onay penceresi (Uygula'dan önce)

- Özet metni:
  ```
  ↑  Upload   (yerel → sunucu):  N dosya  ·  X MB
  ↓  Download (sunucu → yerel):  N dosya  ·  X MB
  ✕  Sil:  sunucudan N, yerelden N dosya

  Üzerine yazılacak:  sunucuda N, yerelde N dosya   (.bak yedeği alınacak)

  Yerel:   /Users/…
  Sunucu:  /var/www/…
  ```
- Silme yoksa: küçük pencere, “Vazgeç” + “✓ Uygula”.
- **Silme varsa:** büyük, boyutlandırılabilir pencere, başlık “DİKKAT — Silinecek dosyalar var”, uyarı sesi çalar, kırmızı çerçeveli açık kırmızı kart:
  - “⚠ N DOSYA SİLİNECEK” (büyük kırmızı)
  - Açıklama: sunucudan silme varsa .bak açıkken “Sunucudaki dosyalar silinmek yerine dosya.bak olarak yeniden adlandırılacak.”, kapalıyken “Sunucudan silinen dosyalar GERİ ALINAMAZ. Listeyi dikkatlice kontrol et.” Yerelden silme varsa “Yerelden silinenler Çöp Sepeti'ne gider.”
  - Eş aralıklı yazıyla liste: `SUNUCU  ✕  yol` / `YEREL   ✕  yol`
  - Onay kutusu “Bu N dosyanın silineceğini anladım” — **işaretlenmeden “✕ Sil ve uygula” düğmesi pasif.**
- Esc = Vazgeç.

## 8. Siteler (kayıtlı profiller)

- “Site” açılır listesi: ilk öğe site yoksa “— kayıtlı site yok (Siteler ▾ → Yeni) —”, varsa “— kayıtsız —”. Site seçilince yerel klasör, sunucu klasörü ve hariç tut alanı dolar, liste temizlenir, etiket ““Ad” seçildi — “Karşılaştır”a bas.”, günlüğe site bilgisi.
- “Siteler ▾” menüsü:
  - “＋ Yeni site olarak kaydet…” — klasörler boşsa “Önce yerel klasörü ve sunucu klasörünü seç, sonra site olarak kaydet.” Ad önerisi = yerel klasörün adı. Aynı ad varsa (harf duyarsız) üzerine yazma onayı.
  - “↻ Bu siteyi şimdiki klasörlerle güncelle” (değişiklik yoksa “zaten güncel”)
  - “✎ Yeniden adlandır…” (çakışan ad engellenir)
  - ayraç
  - “✕ Siteyi listeden kaldır” (kırmızı) — onay: “(Hiçbir dosya silinmez; sadece kayıt kaldırılır.)”
  - Site seçili değilken son üçü pasif.
- Son seçilen site açılışta hatırlanır.

## 9. Sunucu klasörü seçici

- Ayrı modal pencere “Sunucu klasörü seç” (540×600, min 420×400). Üstte düzenlenebilir yol alanı, ortada **tembel yüklenen klasör ağacı** (kökten `/` başlar, açılınca alt klasörleri listeler; klasöre işaret eden sembolik linkler de klasör sayılır), sarı klasör simgeleri, alfabetik.
- Açılışta mevcut yola kadar ağaç otomatik açılır ve seçilir. Seçim yol alanını günceller. Alt klasörü olmayan düğüme çift tık = seç.
- Okunamayan klasörde kırmızı “(okunamadı: hata)” düğümü.
- Düğmeler: “Vazgeç”, “Bu klasörü seç” (Enter).
- Yerel klasör için standart `NSOpenPanel` (sadece klasör, yeni klasör oluşturulabilir, başlık “Eşitlenecek yerel klasörü seç”).

## 10. Dosya listesi (ana tablo)

Sütunlar: **DURUM** | **DOSYA** (esnek genişlik) | **YEREL (boyut · tarih)** | **SUNUCU (boyut · tarih)** | **İŞLEM**. Satır yüksekliği 36 pt, çoklu seçim, binlerce satırda akıcı (SwiftUI `Table` veya `NSTableView`).

- **DURUM** hap (pill) rozetleri: Aynı (yeşil), Sadece yerelde / Yerel daha yeni (mavi), Sadece sunucuda / Sunucu daha yeni (yeşil-zümrüt), Boyut farklı (turuncu), Hata (kırmızı).
- **DOSYA:** klasör kısmı gri, dosya adı kalın; sığmazsa ortadan kısaltma. İpucu: tam yol (hata varsa “Hata: …”).
- **YEREL/SUNUCU:** “12,4 KB   ·   26.11.2026 14:32” (tr-TR biçimi, yerel saat). Olmayan taraf “—” açık gri. Daha yeni olan taraf koyu ve kalın, diğeri gri.
- **İŞLEM** hapları: “↑ Upload” (mavi zemin, beyaz yazı), “↓ Download” (yeşil zemin), “✕ Sunucudan sil” / “✕ Yerelden sil” (kırmızı zemin), “■ Sürekli atla” (gri), “– Atla” (açık gri). Durumu “Aynı” ve işlemi Atla ise sadece soluk “—”. İşlem önerilenden farklıysa (sürekli atla hariç) hapın sağında **küçük turuncu nokta**.
- İşlem hücresine **tıklamak** izin verilen işlemler arasında döngü yapar: Upload → Download → Sil → Atla → Sürekli atla (izin verilmeyenler atlanır).
- **Klavye kısayolları** (seçili satırlara): `U` upload, `D` download, `⌫/Delete` sil, `S` atla, `A` sürekli atla (⌘A tümünü seçer, ona dokunma), `Space` döngü.
- **Sağ tık menüsü:** “↑ Upload (sunucuya gönder) U”, “↓ Download (yerele indir) D”, “✕ Sil (sadece olduğu taraftan) ⌫” (kırmızı), ayraç, “– Atla (bu sefer) S”, “■ Sürekli atla (hep hatırla) A”, ayraç, “Yerel dosyayı Finder'da göster” (`NSWorkspace.activateFileViewerSelecting`; dosya yoksa klasörünü aç). Sağ tıklanan satır seçili değilse önce o seçilir. İşlem sürerken menü açılmaz.
- Sütun başlığına tıklayınca sıralama (tekrar tıklayınca ters). Durum sırası: Sadece yerelde, Yerel daha yeni, Sadece sunucuda, Sunucu daha yeni, Boyut farklı, Aynı; hatalılar en üstte. İkincil sıralama her zaman yol.
- Tablonun üstünde: “Dosya ara…” arama kutusu (yolda harf duyarsız arar), “Aynı olanları gizle” kutusu (hatalı olanlar yine görünür), sağda toplu düğmeler:
  - “↑ Tümü upload” (mavi yazı) — görünen ve farklı olan tüm dosyalara (izin verilenlere) Upload
  - “↓ Tümü download” (yeşil yazı)
  - “Hepsini atla”
  - “↺ Önerilen” — her dosyayı önerilen işleme döndürür
  - Toplu düğmeler “Sürekli atla” olanlara **dokunmaz**; seçili satırlara verilen açık komutlar ise dokunur.
- **Boş liste mesajı** (ortada gri): hiç karşılaştırma yoksa “Bağlan, klasörleri seç ve “Karşılaştır”a tıkla.\nHiçbir dosya sen “Uygula” demeden değişmez.”; fark yoksa “Farklı dosya yok — iki klasör eşit ✓”; filtre sonucu boşsa “Bu filtreye uyan dosya yok.”
- **Sürekli atla kalıcılığı:** her işlem değişikliğinde, bu sunucu+klasör çifti anahtarı altında “sürekli atla” yolları ayarlara yazılır (artık sürekli atla olmayanlar çıkarılır, liste boşsa anahtar silinir).

## 11. Ekran düzeni

Yukarıdan aşağı, kenar boşlukları 16 pt:

1. **Başlık şeridi** (72 pt): soldan sağa mor degrade (#4338CA → #7E22CE). Solda yarı saydam “cam” logo kutusu, “SFTP Eşitle” (15 pt yarı kalın, beyaz) ve altında “Yerel klasör ↔ sunucu klasörü · ne yükleneceğine / indirileceğine sen karar ver” (açık lavanta).
2. **İki kart yan yana** (234 pt yükseklik, %44 / %56):
   - **Sunucu bağlantısı** kartı: başlık + sağ üstte durum etiketi; IP | Port; Kullanıcı | Şifre; altta “Şifreyi hatırla” ve “Bağlan” düğmesi.
   - **Klasörler** kartı: başlık + sağda “Nokta ile başlayanları atla (.git, .codex, .agent…)” kutusu (ipucu: “.htaccess, .env gibi dosyalar da atlanır.”); Site [açılır liste] [Siteler ▾]; Yerel klasör [alan] [Gözat…]; Sunucu klasörü [alan] [Gözat…]; Hariç tut [alan] (ipucu: “Boşluk veya noktalı virgülle ayır. Örnek: database vendor *.log storage/cache — İsim her seviyede eşleşir; /database sadece en üsttekini atlar.”).
3. **Araç çubuğu** (56 pt): “⇄ Karşılaştır” birincil düğme + özet hapları: “↑ N upload · X MB” (mavi), “↓ N download · X MB” (yeşil), “✕ N silinecek” (0 iken açık kırmızı, >0 iken dolu kırmızı zemin beyaz yazı), “– N atlanacak” (gri), “✓ N aynı” (yeşil), hata varsa “! N hata” (kırmızı).
4. **Dosya listesi kartı** (kalan alan).
5. **GÜNLÜK kartı** (124 pt): eş aralıklı yazı (SF Mono), her satır “HH:mm:ss   mesaj”, saat gri, mesaj renkli; otomatik en alta kayar. Açılışta: “Hazır. Sunucu bilgilerini gir, klasörleri seç ve “Karşılaştır”a tıkla.”
6. **Alt çubuk** (62 pt): solda ince (6 pt) yuvarlak mor degrade ilerleme çubuğu + altında ilerleme etiketi; sağda “Üzerine yazmadan önce .bak yedeği al”, “Yeni dosyalara klasörün sahibini ver” (ipucu: “Sunucuda yeni oluşan dosya/klasörler root yerine üst klasörün sahibine (örn. www-data) ait olur.”), “Durdur” (kırmızı yazılı ikincil, sadece iş sürerken aktif), “✓ Uygula (N)” birincil düğme (N = upload+download+sil sayısı; 0 iken pasif).

İşlem sürerken tüm giriş alanları, düğmeler ve kutular pasifleşir (Durdur hariç).

## 12. Renk ve tipografi

| Token | Renk |
|---|---|
| Arka plan | #F3F5FA |
| Kart | #FFFFFF, kenarlık #DEE2EC, köşe yarıçapı ~10 |
| Metin | #1C212E |
| Soluk metin | #6C7486 |
| Vurgu (birincil düğme) | #4F46E5 (hover #4338CA, basılı #3730A3), seçim zemini #ECEEFF |
| Upload | #2563EB / zemin #DEEAFE |
| Download | #059669 / zemin #D4F4E6 |
| Uyarı | #CA6C04 / zemin #FEEFD2 |
| Tehlike | #DC2626 / zemin #FEE2E2 |
| Atla | #70788A / zemin #ECEEF3 |
| Tamam | #16964A / zemin #DCF6E4 |

Yazı: sistem fontu (SF Pro) — normal 13, küçük 12, kart başlığı 14 yarı kalın; günlük SF Mono 12. Düğmeler yuvarlatılmış; ikincil düğmeler beyaz zemin + ince kenarlık. **Karanlık mod** için aynı tonların koyu karşılıklarını tanımla (isteğe bağlı ama tercih ederim).

**Uygulama simgesi:** mor degrade (#6366F1 → #9333EA, 45°) yuvarlak kare, üst yarıda hafif parlama; içinde beyaz, yuvarlak uçlu iki ok: solda yukarı ok, sağda aşağı ok. macOS simge boyutlarının hepsi için `AppIcon.appiconset` üret.

## 13. Diğer davranışlar

- Ayarlar şu anlarda kaydedilir: başarılı bağlantı, karşılaştırma başlangıcı, site işlemleri, “sürekli atla” değişimi, kapanış.
- Kapanırken işlem sürüyorsa: “Bir işlem sürüyor. Yine de çıkılsın mı?” Evet → iptal et, kaydet, bağlantıyı kapat.
- Bilgi/uyarı pencereleri başlığı “SFTP Eşitle”; tüm metinler Türkçe.
- Boyut biçimi: <1024 → “B”, sonra KB/MB/GB/TB; değer <10 ise bir ondalık (“1,2 MB”), değilse tam sayı; Türkçe ondalık virgülü.
- macOS'a özel: `.DS_Store` varsayılan hariç listesinde; yerel yollar için `~` gösterimi gerekmez, tam yol yeter. Uygulama **sandbox'sız** çalışabilir (kullanıcının seçtiği herhangi bir klasöre ve ağa erişmesi gerektiği için); sandbox kullanacaksan security-scoped bookmark ile kayıtlı klasörlere erişimi kalıcı yap.

## 14. Kod yapısı önerisi

```
SftpEsitle/
  App/SftpEsitleApp.swift
  Models/SyncItem.swift, AppSettings.swift, ExcludeMatcher.swift
  Services/SftpService.swift (bağlantı, host key, listeleme, aktarım)
           SyncEngine.swift (compare + apply, saf mantık)
           KeychainStore.swift
  ViewModels/MainViewModel.swift (@Observable)
  Views/MainView.swift, ConnectionCard.swift, FolderCard.swift, FileTable.swift,
        SummaryBar.swift, LogView.swift, FooterBar.swift,
        ConfirmSheet.swift, RemoteFolderPicker.swift, Theme.swift
  Resources/Assets.xcassets (AppIcon)
Tests/ (ExcludeMatcher, classify, yol normalize için birim testleri)
```

## 15. Teslim ve doğrulama

1. Önce planını ve seçtiğin SFTP kütüphanesinin yukarıdaki yetenekleri karşıladığını kısaca yaz, sonra kodla.
2. `ExcludeMatcher`, `classify` (2 sn tolerans dahil) ve `normalizeRemote` için birim testleri yaz ve çalıştır.
3. Projeyi derle; derleme hatası kalmasın.
4. Mümkünse yerel bir test SFTP sunucusuna (ör. Docker `atmoz/sftp`) karşı karşılaştırma, upload, download, sil ve .bak akışlarını dene; deneyemediklerini açıkça belirt.
5. Bitince: nasıl derlenip çalıştırılacağını ve `.app`/`.dmg` nasıl üretileceğini anlat.
