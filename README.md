# SIA — Sistem Informasi Akuntansi Toko Aki/Baterai Kendaraan

Proyek capstone Sistem Informasi Akuntansi untuk UMKM toko aki/baterai kendaraan. Mencakup penjualan, pembelian (dengan approval PO), inventori dan kartu stok, retur, piutang/utang, jurnal umum manual, serta laporan keuangan (Neraca, Laba Rugi, Arus Kas).

## Konteks Bisnis

| Aspek | Detail |
|---|---|
| Skala | UMKM — 2 staf, 10–50 transaksi/hari |
| Mata uang | Rupiah (IDR) saja |
| Pajak (PPN) | Tidak digunakan |
| Jurnal umum | Diinput manual |
| Stok | Otomatis dari penjualan, pembelian, dan retur |
| Role | Admin, Staff, Viewer |

## Teknologi

- .NET 8, Blazor Web App (Server render mode), MudBlazor
- Entity Framework Core + MySQL (Pomelo), ASP.NET Core Identity
- xUnit + Moq
- GitHub Actions (CI)

## Struktur Solution (Clean Architecture)

```
src/
├── SIA.Domain/          Entity, enum, BaseEntity (tanpa dependensi eksternal)
├── SIA.Application/     Interface, service contract, DTO
├── SIA.Infrastructure/  EF Core, repository, implementasi service
└── SIA.Web/             Antarmuka Blazor Server
tests/
└── SIA.Tests/           Unit test xUnit + Moq
docs/
└── implementation_plan.md
```

Arah dependensi: `Domain ← Application ← Infrastructure ← Web`.

## Prasyarat

- .NET SDK 8.0 (versi dikunci lewat `global.json`)
- MySQL 8.x (dibutuhkan pada tahap database)

## Menjalankan

```powershell
dotnet build SIA.sln
dotnet test SIA.sln
dotnet run --project src/SIA.Web
```

> Connection string dan kredensial **tidak** disimpan di repository. Gunakan `dotnet user-secrets` atau environment variable.

## Dokumentasi

- Rencana implementasi: [docs/implementation_plan.md](docs/implementation_plan.md)
- Aturan proyek: [AGENTS.md](AGENTS.md)
