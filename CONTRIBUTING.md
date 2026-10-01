# Contributing to CryptoVault

Thank you for contributing to CryptoVault! To maintain high code quality, security, and architectural integrity across the repository, please adhere to the following standards and practices.

---

## 1. General Principles & Quality Discipline

- **Zero-Warning Discipline:**
  - Every pull request must compile cleanly with **0 Warnings and 0 Errors**:
    ```powershell
    dotnet build CryptoVault.slnx
    ```
  - Code must strictly pass automated style enforcement:
    ```powershell
    dotnet format --verify-no-changes
    ```
- **Boundary & Security Isolation:**
  - Never commit credentials, connection strings, private keys, or `.env` files. Always use `.env.example` or `*.publish.example.xml` templates.
  - Never query or execute DDL/DML directly against production or remote SQL instances outside of declarative SSDT deployment scripts.

---

## 2. C# & .NET Standards (`CryptoVault.App`)

- **EditorConfig Enforcement:**
  - Adhere strictly to the root `.editorconfig` (Allman braces, 4-space indentation, CRLF line endings, file-scoped namespaces).
- **Class Member Layout Hierarchy:**
  Order class members strictly by category from top to bottom, sorted alphabetically within each bucket:
  1. **Constants** (`const`)
  2. **Fields** (Static `s_`/`t_`, then instance `_` fields; alphabetized)
  3. **Constructors** (Static, then instance constructors)
  4. **Properties** (Alphabetized A–Z by name; visibility does NOT partition sorting)
  5. **Methods** (Alphabetized A–Z by name; overloads remain contiguous)
  6. **Nested Types** (Structs, records, enums)
- **Naming Conventions:**
  - **Zero Underscores in Methods:** All methods, including XAML event handlers, must use PascalCase (`OnWindowDrop`, `OnConnectRepoClick`, `OnAutoRenameClick`). Never use legacy WinForms `Button_Click` naming.
  - **Async Suffix:** Methods returning awaitable types (`Task`, `ValueTask`) must end with `Async`. Void event handlers (`async void` / `void`) must not end with `Async`.
- **WPF Dialog Hygiene:**
  - Always set `Owner = this` (or parent window) before calling `.ShowDialog()`.
  - In dialog constructors, invoke `WindowExtensions.RemoveIcon(this)` to remove the top-left icon.
  - Configure dialogs with `WindowStartupLocation="CenterOwner"`, `ResizeMode="NoResize"`, and `ShowInTaskbar="False"`.

---

## 3. Database Standards (`CryptoVault.Database`)

- **Engine Target:** Microsoft SQL Server 2025 (`Sql170DatabaseSchemaProvider`) managed declaratively via SSDT (`CryptoVault.Database.sqlproj`).
- **Entity & Table Design:**
  - **Singular PascalCase:** Table names must be singular (`VaultFile`, `FileMetadata`, `VaultConfig`). Never pluralize table names.
  - **Zero Prefixes & Zero Underscores:** No Hungarian notation (`tbl`, `sp_`, `usp_`, `vw_`) and zero underscores in table, column, index, or constraint names.
  - **Primary Keys:** Every table must have an explicitly named primary key constraint (`PK<TableName>`). Natural keys are preferred over arbitrary auto-incrementing integer IDs.
- **100% Stored Procedure Architecture:**
  - Strictly forbid inline SQL (`SELECT`, `INSERT`, `UPDATE`, `DELETE`) in application code.
  - Every stored procedure must start with `SET NOCOUNT ON;` and explicitly enumerate projected column names (never `SELECT *`).
  - Stored procedures querying or modifying `[dbo].[VaultFile]` must include `WITH EXECUTE AS OWNER` due to SQL Server FileTable schema ownership chaining boundaries across the Win32 filesystem filter driver.
- **Least Privilege Security:**
  - Application role: `[CryptoVaultUser]`.
  - Every new stored procedure must have `GRANT EXECUTE ON OBJECT::[dbo].[ProcName] TO [CryptoVaultUser];` added to `Security/Permissions.sql`.
  - Every new User-Defined Table Type (TVP) must have `GRANT EXECUTE ON TYPE::[dbo].[TypeName] TO [CryptoVaultUser];` added to `Security/Permissions.sql`.

---

## 4. Pull Request & Commit Guidelines

- **Atomic Commits:** Keep commits focused on a single logical change with clear, imperative commit messages (e.g., `Add chunked 2MB streaming decryption to CryptoService`).
- **Pre-Commit Verification Checklist:**
  - [ ] `dotnet build CryptoVault.slnx` builds with 0 warnings and 0 errors.
  - [ ] `dotnet format --verify-no-changes` reports 0 formatting changes.
  - [ ] No `.env`, connection strings, or personal server hostnames are committed.
  - [ ] New database objects have explicit permissions granted in `Security/Permissions.sql`.

