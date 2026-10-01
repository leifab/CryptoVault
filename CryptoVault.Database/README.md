# CryptoVault Database (`CryptoVault.Database`)

Declarative database project managed via **Microsoft.Build.Sql** (SSDT / DacFx) targeting **Microsoft SQL Server 2025** (`Sql170DatabaseSchemaProvider`).

---

## 1. Architectural Standards & Design

- **Entity & Naming Conventions:**
  - Table names are strictly singular PascalCase (`VaultFile`, `FileMetadata`, `VaultConfig`, `FileFingerprint`, `MetadataSearchIndex`).
  - Zero Hungarian notation or object prefixes (`tbl`, `sp_`, `usp_`, `vw_`).
  - Zero underscores in tables, columns, indexes, and constraint names.
  - Primary keys are explicitly declared with `PK<TableName>` naming. Natural domain keys are actively preferred over surrogate auto-incrementing IDs.
- **100% Stored Procedure Architecture:**
  - Zero inline SQL permitted in application layers. All operations are encapsulated in dedicated stored procedures.
  - Every stored procedure begins with `SET NOCOUNT ON;` and explicitly projects column names.
  - Set-based processing via Table-Valued Parameters (TVPs: `FileFingerprintType`, `SearchTermHashType`).
- **FileTable Win32 Kernel Ownership Chaining:**
  - Standard SQL Server schema ownership chaining breaks across the Win32 filesystem boundary for FileTables. Accessing FileTable internals or streaming functions requires explicit context elevation.
  - Every procedure querying or modifying `[dbo].[VaultFile]` includes `WITH EXECUTE AS OWNER` so non-dbo roles (`[CryptoVaultUser]`) operate securely without direct table grants.
- **Isolation Invariant:**
  - Read Committed Snapshot Isolation (RCSI) is incompatible with FileTables (`NON_TRANSACTED_ACCESS = FULL`), causing cross-driver deadlocks with the Windows kernel filter driver (`RsFx`). `READ_COMMITTED_SNAPSHOT` and `ALLOW_SNAPSHOT_ISOLATION` remain explicitly `OFF`.

---

## 2. Directory Structure

```
CryptoVault.Database/
├── Security/
│   ├── Permissions.sql              # Explicit GRANT EXECUTE grants for [CryptoVaultUser]
│   └── Roles/
│       └── CryptoVaultUser.sql      # Application database role
├── Storage/
│   └── Filegroups/
│       └── FS.sql                   # FILESTREAM filegroup declaration
├── StoredProcedures/                # 20 PascalCase stored procedures (Get*, Save*, Delete*, Match*)
├── Tables/                          # Relational metadata, config, and FileTable declarations
├── UserDefinedTypes/                # Table-Valued Parameter types (TVPs)
├── CryptoVault.Database.sqlproj     # SDK-style SQL project (Microsoft.Build.Sql)
└── CryptoVault.publish.example.xml  # Deployment publish profile template
```

---

## 3. Build & Deployment

### Host SQL Server Prerequisites (FILESTREAM Configuration)

Before deploying the `.dacpac`, the target SQL Server instance **must have FILESTREAM enabled with full non-transacted Win32 streaming access**. While DacFx configures database-level options (`NON_TRANSACTED_ACCESS = FULL`), it cannot alter host Windows service settings:

1. **SQL Server Configuration Manager:**
   - Open **SQL Server Configuration Manager** on the database host.
   - Right-click the **SQL Server (MSSQLSERVER)** service -> **Properties** -> **FILESTREAM** tab.
   - Check **Enable FILESTREAM for Transact-SQL access**.
   - Check **Enable FILESTREAM for file I/O access** (enables the Windows SMB share).
   - Check **Allow remote clients access to FILESTREAM data** (if connecting across the network).
   - Click **Apply** and restart the SQL Server service.

2. **Instance-Level Access Configuration (T-SQL):**
   - Ensure the server-wide FILESTREAM access level is set to `2` (Full T-SQL and streaming I/O access):
   ```sql
   EXEC sp_configure 'show advanced options', 1;
   RECONFIGURE;
   GO
   EXEC sp_configure 'filestream access level', 2;
   RECONFIGURE;
   GO
   ```

> [!IMPORTANT]
> If `filestream access level` is not set to `2` at the instance level prior to deployment, DacFx publish will fail with Error 5591 / Error 5571 when creating the FileTable and applying `NON_TRANSACTED_ACCESS = FULL`.

### SqlPackage CLI Installation
If `sqlpackage` is not already installed on your system, install it via dotnet tools:
```powershell
dotnet tool install -g microsoft.sqlpackage
```

### Build DacPac
Compile the declarative schema into a deployable `.dacpac` package:
```powershell
dotnet build CryptoVault.Database/CryptoVault.Database.sqlproj
```

### Publish to SQL Server
1. Copy the example publish profile:
   ```powershell
   Copy-Item CryptoVault.publish.example.xml CryptoVault.publish.xml
   ```
2. Update `TargetConnectionString` and `TargetDatabaseName` in `CryptoVault.publish.xml` with your target server details.
3. Deploy using `sqlpackage`:
   ```powershell
   sqlpackage /Action:Publish /SourceFile:bin/Debug/CryptoVault.Database.dacpac /Profile:CryptoVault.publish.xml
   ```

### SqlPackage CLI Installation
If `sqlpackage` is not already installed on your system, install it via dotnet tools:
```powershell
dotnet tool install -g microsoft.sqlpackage
```
