# Link Vault

Local-first URL capture for Chrome. The extension sends links to a .NET 10 API running at `http://localhost:5678`.

## Setup

1. Open `chrome://extensions`.
2. Turn on **Developer mode**.
3. Click **Load unpacked** and select `path-to\link-vault\extension`.
4. Copy the extension ID shown on the `Link Vault` card.
5. Start the API.

Preferred: set the extension origin in `src/LinkVault.Api/appsettings.json` under `LinkVault:AllowedExtensionOrigins`.
Fallback: set `LINK_VAULT_ALLOWED_EXTENSION_ORIGINS` as an environment variable.

```powershell
dotnet run --project .\src\LinkVault.Api\LinkVault.Api.csproj
```

6. Check health:

```powershell
Invoke-WebRequest http://localhost:5678/health | Select-Object -ExpandProperty Content
```

7. Use the extension popup:
- `Save`
- `Save & Close`
- `Save All Tabs` (saves sequentially and does not close tabs)
- `View Links` (opens the read-only links page)

8. You can also right-click any hyperlink and choose `Save link to Vault` to save the target URL directly without opening it in a tab.

## Notes

- Default storage path: `%LOCALAPPDATA%\LinkVault\links.json`
- Read-only browser view: `http://localhost:5678/links`
- Links are created with `POST /links`.
- Opened pages can update `updatedAt` through `PATCH /links` by URL.
- Preferred config key: `LinkVault:DataPath`
- Fallback env var: `LINK_VAULT_DATA_PATH`
- If the extension ID changes after reload/reinstall, update `LinkVault:AllowedExtensionOrigins` or `LINK_VAULT_ALLOWED_EXTENSION_ORIGINS`.
- If the ID is wrong, Chrome blocks the request with CORS.