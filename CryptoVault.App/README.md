# CryptoVault Desktop Client (`CryptoVault.App`)

A modern, high-performance Windows desktop application built with **.NET 10**, **C# 13**, and **WPF** for managing and streaming zero-knowledge encrypted vaults backed by **Microsoft SQL Server 2025 FileTable**.

---

## 1. Core Architecture & Capabilities

### Cryptographic Engine (`Services/CryptoService.cs`)
- **Cipher:** AES-256 in CBC mode with PKCS#7 padding.
- **Key Derivation:** PBKDF2 over 100,000 iterations of SHA-256 with a cryptographically secure 16-byte random salt.
- **Stream Header Layout:** 16-byte random salt + 16-byte random IV prepended to the ciphertext payload:
  `[16-byte Salt][16-byte IV][AES-256-CBC Ciphertext...]`
- **Zero-Knowledge Blind Search:**
  Search tokens are hashed with HMAC-SHA256 using an isolated static salt (`"CryptoVault_BlindSearchSalt"`), ensuring search tokens cannot be correlated with file encryption keys.
- **Perceptual Deduplication:**
  MD5 block hashing (`ComputeFileMd5Async`) is used strictly for high-throughput duplicate detection and perceptual fingerprint correlation.

### Relational + FileTable Storage (`Services/VaultRepository.cs` & `Services/FileTableService.cs`)
- **100% Stored Procedure Architecture:** Strictly zero inline SQL. All schema operations, metadata upserts, and blind index lookups are executed via dedicated SQL Server stored procedures under `[CryptoVaultUser]` role boundaries.
- **Windows FileTable Driver Resilience:**
  Accounts for kernel-level Windows SMB filter driver (`RsFx`) latency (50–200ms delay between SMB directory writes and SQL Server relational table visibility) via resilient polling retry loops.
- **Table-Valued Parameters (TVPs):** High-throughput bulk ingest for metadata search indexing and multi-hash correlation.

### Windows 11 UI Hygiene (`Views/` & `Themes/`)
- **Immersive Dark Theme:** Automatically enables Windows 11 Desktop Window Manager (DWM) immersive dark title bars (`DwmSetWindowAttribute`).
- **Modal Hygiene:** All modal dialogs enforce `Owner` binding, center-parent positioning (`CenterOwner`), no taskbar clutter (`ShowInTaskbar="False"`), and use `WindowExtensions.RemoveIcon` to eliminate legacy top-left Win32 icons.
- **Strict Method Naming:** 100% PascalCase with zero underscores on all event trampolines and methods (`OnWindowDrop`, `OnConnectRepoClick`).

---

## 2. Directory Structure

```
CryptoVault.App/
├── Assets/             # High-DPI application branding icons (ICO, PNGs)
├── Models/             # Entity models and DTOs (VaultItem)
├── Services/           # Domain logic (Crypto, Auth, Ingest, Export, Metadata, FileTable)
├── Themes/             # XAML control templates and DarkTheme resource dictionary
├── Utilities/          # Interop helpers (WindowExtensions, VideoUtilities)
├── Views/              # Main application window (MainWindow.xaml)
│   └── Dialogs/        # Modal windows (Connect, Password, Metadata, Collision, etc.)
├── App.xaml / .cs      # WPF application entry point and theme bootstrapping
├── AssemblyInfo.cs     # Assembly-level attributes
└── CryptoVault.csproj  # Modern SDK-style project file (.NET 10 / win-x64)
```

---

## 3. Prerequisites & Development Setup

- **OS:** Windows 10 (Build 19041+) or Windows 11 / Windows Server 2022/2025 (x64)
- **SDK:** [.NET 10 SDK](https://dotnet.microsoft.com/)
- **Workload:** .NET Desktop Development (WPF)

### Build
From the repository root:
```powershell
dotnet build CryptoVault.slnx
```
Or build the desktop project specifically:
```powershell
dotnet build CryptoVault.App/CryptoVault.csproj
```

### Run
Launch the application executable:
```powershell
dotnet run --project CryptoVault.App/CryptoVault.csproj
```

### Enforce Code Style & Roslyn Quality
The project enforces `<AnalysisLevel>latest-recommended</AnalysisLevel>` and `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`:
```powershell
dotnet format --verify-no-changes
```

---

## 4. Key Workflows

1. **Connecting to a Repository:**
   - Launch the client and connect to the SQL Server FileTable share using `ConnectDialog`.
   - Enter the master passphrase in `PasswordDialog` to derive session encryption keys and verify against the stored PBKDF2 token.
2. **Ingesting Files (Drag-and-Drop):**
   - Drag files or directories into `MainWindow`.
   - Files are automatically categorized by `FileClassifier`, filenames are sanitized via `FileNameCleaner`, AES-256 encrypted, and streamed to the FileTable share.
   - Blind HMAC tokens and encrypted metadata are recorded in relational tables.
3. **Collision & Duplicate Handling:**
   - When duplicate or colliding files are detected, `CollisionDialog` and `DuplicateMatchDialog` prompt the user for rename, replace, or skip actions.
4. **Export & Decryption:**
   - Files can be decrypted and exported back to plaintext local storage with authenticated integrity checks.

