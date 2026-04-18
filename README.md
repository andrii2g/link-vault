# Link Vault

Local-first URL capture for Chrome. The extension talks to a .NET 10 API published from Docker at `http://localhost:5678`.

## Setup

1. Open `chrome://extensions`.
2. Turn on **Developer mode**.
3. Click **Load unpacked** and select `path-to\link-vault\extension`.
4. Copy the extension ID shown on the `Link Vault` card.
5. Create a `.env` file in the repo root with:

```powershell
LINK_VAULT_ALLOWED_EXTENSION_ORIGINS=chrome-extension://<EXTENSION_ID>
```

You can start from `.env.example`.

6. Start the API with Docker Compose:

```powershell
docker compose up --build -d
```

7. Check health:

```powershell
Invoke-WebRequest http://localhost:5678/health | Select-Object -ExpandProperty Content
```

8. If your API uses another host or port, update `extension/config.js` and change `apiOrigin`, then reload the unpacked extension in `chrome://extensions`.

9. Use the extension popup:
- `Save`
- `Save & Close`
- `Save All Tabs` (saves sequentially and does not close tabs)
- `View Links` (opens the links page, where you can also delete saved links)

10. You can also right-click any hyperlink and choose `Save link to Vault` to save the target URL directly without opening it in a tab.

## Notes

- Docker publishes the API only to `127.0.0.1:5678`, so it stays local to the machine running Chrome.
- Persistent data is stored in the Docker volume mounted at `/data/links.json` inside the container.
- Browser view: `http://localhost:5678/links`
- Links are created with `POST /links`.
- Opened pages can update `updatedAt` through `PATCH /links` by URL.
- Saved links can be removed with `DELETE /links?id=<guid>`.
- The API reads `LinkVault:DataPath` and `LinkVault:AllowedExtensionOrigins` from configuration, with environment variables as fallback.
- If the extension ID changes after reload/reinstall, update `LINK_VAULT_ALLOWED_EXTENSION_ORIGINS` and restart the container.
- If the ID is wrong, Chrome blocks the request with CORS.
