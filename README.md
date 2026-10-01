# CryptoVault

A high-performance, zero-knowledge encrypted file vault and media streaming system backed by Microsoft SQL Server 2025 FileTable.

---

## Architecture Overview

CryptoVault consists of three integrated components:

```
CryptoVault/
├── CryptoVault.App/        # Windows Desktop Client (WPF / .NET 10)
└── CryptoVault.Database/   # Declarative Database Project (SSDT / SQL Server 2025)
```

### 1. CryptoVault.App (Desktop Client)
- **Framework:** .NET 10, C# 13, WPF.
- **Styling:** Windows 11 immersive dark mode with DWM title bar integration.
- **Features:**
  - AES-256-CBC file encryption with PBKDF2 key derivation (100,000 SHA-256 rounds).
  - Blind HMAC-SHA256 search token generation for zero-knowledge indexed search.
  - Perceptual file fingerprinting (MD5 block deduplication).
  - Drag-and-drop ingestion with automatic file classification and sanitized naming.

### 2. CryptoVault.Database (Declarative SQL Server 2025)
- **Engine:** Microsoft SQL Server 2025 (`Sql170DatabaseSchemaProvider`).
- **Storage:** SQL Server FileTable (`NON_TRANSACTED_ACCESS = FULL`) for kernel-level Windows SMB filesystem streaming backed by relational ACID metadata.
- **Architecture:** 100% Stored Procedure Architecture adhering to modern standards and best practices.
- **Security:** Strict `[CryptoVaultUser]` role isolation with schema ownership chaining.

---

## Getting Started

### Prerequisites
- **Windows 10/11** or **Windows Server 2022/2025**
- **.NET 10 SDK**
- **SQL Server 2025** (with FILESTREAM and FileTable enabled)

### Building the Solution
Build the desktop client and database DacFx package with zero warnings:
```powershell
dotnet build CryptoVault.slnx
```

Verify formatting and analyzer compliance:
```powershell
dotnet format --verify-no-changes
```

---

## Contributing & Coding Standards

Please review [CONTRIBUTING.md](CONTRIBUTING.md) for architectural rules, database naming conventions, and pull request guidelines.

---

## License

All rights reserved.
This project is licensed under the [GNU Affero General Public License v3.0](LICENSE) (AGPL-3.0).

