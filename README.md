# Selim Bank Asistanı

Türkçe konuşan bir bankacılık asistanı: RAG, araç çağıran bir agent ve PII maskeleme. Portföy projesidir; banka ve tüm veriler kurgusaldır. Ayrıntılı README 3. haftada (adım 3.6) yazılacak; plan için [docs/PLAN.md](docs/PLAN.md).

## Çalıştırma

```
dotnet run --project src/SelimBank.AppHost
```

Docker çalışıyor olmalı (Qdrant için). NVIDIA NIM anahtarı `Nim:ApiKey` adıyla AppHost'un user-secrets'ında durur.

## Audit kaydı için takma kimlik anahtarı

Audit kaydında müşteri numarası yerine, gizli bir anahtarla üretilmiş bir takma kimlik (HMAC) tutulur. Anahtarı AppHost projesinin user-secrets'ına ekle; AppHost onu web uygulamasına geçirir.

1. Rastgele bir anahtar üret ve kaydet. Komutlar anahtarı ekrana yazdırmaz.

   PowerShell:

   ```powershell
   $bytes = New-Object byte[] 32
   [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
   dotnet user-secrets set "Audit:PseudonymKey" ([Convert]::ToBase64String($bytes)) --project src/SelimBank.AppHost
   ```

   Git Bash:

   ```bash
   dotnet user-secrets set "Audit:PseudonymKey" "$(openssl rand -base64 32)" --project src/SelimBank.AppHost
   ```

2. AppHost'u yeniden başlat.

**Anahtar yoksa** uygulama yine çalışır: her açılışta rastgele bir anahtar üretir ve bir uyarı loglar. Bu geliştirme için yeterlidir, ama takma kimlikler her yeniden başlatmada değişir. Aynı nedenle anahtarı değiştirirsen eski satırlar yeni kimliklerle eşleşmez.

Anahtarı koda, `appsettings.json`'a veya loglara yazma.

## Audit kaydının yeri

Kayıtlar varsayılan olarak `src/Assistant.Web/audit.db` dosyasındaki `AuditEntries` tablosuna yazılır (git'e girmez). Yeri `Audit:ConnectionString` ayarıyla değiştirilebilir. Satırlarda araç argümanları maskelenmiş haldedir; araç sonuçları ve engellenen mesajların metni saklanmaz.
