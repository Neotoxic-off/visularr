# visularr

A simplified, fast media server for the files modularr (the Sonarr/Radarr replacement) downloads.
The server (`server/`) is [Jellyfin](https://github.com/jellyfin/jellyfin) 10.11.9 moved to .NET 10, so every Jellyfin app
(Swiftfin, Findroid, Infuse, Jellyfin for Android TV...) keeps working against it; the web client (`web/`) is visularr's own.

Accounts are managed in linkarr, the hub: it provisions users through Jellyfin's own user API with an API key and registers
itself at `/Visularr/Managed`. The web client then links to linkarr for requests ("Request … in linkarr") and to modularr.

## Run with Docker, next to modularr

```sh
docker build -t visularr .
docker run -d --name visularr -p 8096:8096 \
  -v visularr-config:/config -v visularr-cache:/cache \
  -v D:/Code/Github/modularr/media:/media \
  visularr
```

The container runs as uid/gid 1000, not root: the media folder must be readable by it, and an existing `/config` volume
created by a root container needs `chown -R 1000:1000` once (or pass `--user` with your own ids).
For hardware transcoding pass the device and its groups, e.g. `--device /dev/dri --group-add "$(getent group render | cut -d: -f3)" --group-add video`.

Mount the same media folder as modularr. Open `http://localhost:8096`, run the setup wizard and add libraries under `/media`.
modularr's `[tmdbid-N]` / `[tvdbid-N]` folder names are matched by Jellyfin directly.

### Behind Docker Desktop or a reverse proxy

Until the setup wizard is finished, any LAN address may run it and create the administrator. Docker Desktop (Windows, macOS)
and a reverse proxy make every request look like it comes from a LAN address (the Docker gateway or the proxy), so finish
the wizard before you publish the port or put the proxy in front. Then list the proxy under Dashboard → Networking →
Known proxies (`KnownProxies` in `/config/config/network.xml`), so visularr sees the real client address from
`X-Forwarded-For`; without it every remote client counts as local.

## Connect modularr

1. In visularr: Dashboard → API Keys → create a key (e.g. "modularr").
2. In modularr: Settings → Connections → Jellyfin, URL `http://<host>:8096` and that API key.
   modularr then refreshes the library (`POST /Library/Refresh`) after each import.
3. For accounts and requests: create another key (e.g. "linkarr") and give it to linkarr.

## Build without Docker

```sh
dotnet build server/Jellyfin.sln -c Release
(cd web && npm ci && npm run build)
dotnet run --project server/Jellyfin.Server -c Release -- --webdir web/out   # or set JELLYFIN_WEB_DIR
```

## License

GPL-2.0, as Jellyfin: see [LICENSE](LICENSE) and [server/LICENSE](server/LICENSE).
