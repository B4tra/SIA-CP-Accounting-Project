# AGENTS.md — Aturan Proyek SIA (Sistem Informasi Akuntansi Toko Aki/Baterai)

Acuan utama: `docs/implementation_plan.md`. Jika aturan ini bertentangan dengan plan, tanyakan dulu ke saya sebelum melanjutkan.

## Stack
- .NET 8, Blazor Web App (Server render mode), MudBlazor
- EF Core + MySQL (Pomelo), ASP.NET Core Identity
- Test: xUnit + Moq

## Struktur & arsitektur (Clean Architecture)
- `src/SIA.Domain`, `src/SIA.Application`, `src/SIA.Infrastructure`, `src/SIA.Web`, `tests/SIA.Tests`
- Arah dependensi: Domain <- Application <- Infrastructure <- Web. Domain tidak punya dependensi eksternal.
- Interface service dan DTO ada di Application; implementasi di Infrastructure; registrasi di `DependencyInjection.cs`.
- Layer Web TIDAK boleh mengakses `AppDbContext` langsung; semua lewat service Application.

## Aturan domain & data
- Semua entity turunan `BaseEntity`: `int Id` (auto-increment, bukan Guid), `CreatedAt`, `UpdatedAt`, `IsDeleted`, `DeletedAt`.
- Soft delete: penghapusan diubah menjadi `IsDeleted = true`; global query filter aktif untuk semua `BaseEntity`.
- `AuditLog` ditulis otomatis lewat EF Core `SaveChangesInterceptor`.
- Nomor transaksi dibuat oleh `ITransactionNumberService` dengan format `{Prefix}-{Year}-{Seq:0000}` dan reset per tahun. Jangan membuat nomor secara manual di tempat lain.
- Semua nilai uang bertipe `decimal`. Mata uang hanya Rupiah. Tanpa PPN dan tanpa multi-currency.
- Jurnal umum DIINPUT MANUAL. Jangan pernah memposting jurnal otomatis dari penjualan, pembelian, atau pembayaran.
- `Account.Balance` disimpan dan diperbarui saat jurnal diposting; sediakan fitur Rekalkulasi Saldo dari `SUM(JournalEntryLine)`.
- Stok otomatis berubah dari penjualan (Out), pembelian saat status Received (In), retur penjualan (ReturnIn), dan retur pembelian (ReturnOut); setiap perubahan wajib menulis `StockCard`.
- `Payment` bersifat polimorfik (`ReferenceType` + `ReferenceId`, tanpa FK); integritas divalidasi di `IPaymentService`. Setelah pembayaran, perbarui `PaidAmount`, `RemainingAmount`, dan `PaymentStatus` (Unpaid, PartiallyPaid, Paid) pada Sale/PurchaseOrder.
- `Sale` dan `PurchaseOrder` memiliki `DateTime? DueDate` (jatuh tempo); wajib diisi jika transaksi kredit.
- Alur PO: Draft -> Approved -> Received (atau Cancelled). Hanya Admin yang boleh approve.

## Role & hak akses (ASP.NET Core Identity Roles + Policy)
- Admin: akses penuh, approve PO, kelola user, konfigurasi nomor transaksi, lihat audit log.
- Staff: penjualan, buat PO draft (tidak bisa approve), inventori, customer, supplier, pembayaran.
- Viewer: read-only semua data, input jurnal umum, lihat dan cetak laporan keuangan.
- Otorisasi WAJIB ditegakkan di sisi server (service/policy), bukan hanya menyembunyikan elemen UI.

## UI & tampilan
- Seluruh teks antarmuka berbahasa Indonesia; culture `id-ID`; uang ditulis `Rp 1.250.000`.
- Gunakan MudBlazor. Jangan menambah library UI/chart lain.
- Tema satu pintu lewat `MudTheme` terpusat. Proporsi warna: 50% hijau muda, 30% hitam, 20% putih. Hijau muda #BBF7D0 (latar), #86EFAC dan #4ADE80 (aksen); hitam #0B0B0B (sidebar, app bar, header tabel, teks); putih #FFFFFF (kartu, tabel, input). Hanya mode terang.
- Warna status kecil yang diizinkan: merah #DC2626, amber #F59E0B, hijau tua #15803D. Jangan memakai warna lain, dan jangan menulis kode hex langsung di komponen (gunakan token tema).

## Testing
- Setiap logika bisnis baru wajib punya unit test xUnit + Moq di `tests/SIA.Tests`.
- Sebelum melapor selesai, jalankan `dotnet build SIA.sln` dan `dotnet test SIA.sln`.

## Cara kerja agent
1. Untuk setiap tugas, buat implementation plan singkat (file yang dibuat/diubah) dan TUNGGU persetujuan saya sebelum menulis kode.
2. Kerjakan hanya cakupan tugas yang diminta; jangan mengubah hal lain.
3. Jika plan ambigu atau saling bertentangan, tanyakan dulu.
4. Di akhir, laporkan file yang dibuat/diubah dan hasil build/test.

## Larangan
- Dilarang menambah paket NuGet tanpa izin saya.
- Dilarang mengubah nama entity atau enum yang sudah ada.
- Dilarang memakai `Guid` sebagai primary key.
- Dilarang membuat migration yang menghapus data tanpa konfirmasi.
- Dilarang commit rahasia (connection string asli, password) ke Git; gunakan user-secrets atau environment variable.
