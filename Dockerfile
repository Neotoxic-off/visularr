# visularr: the Jellyfin based server (server/) with the visularr web client (web/), on debian trixie-slim with
# jellyfin-ffmpeg from the official Jellyfin apt repository. Multi-arch: docker buildx build --platform linux/amd64,linux/arm64 .
#
# Compared to the official jellyfin/jellyfin image this drops: the Intel OpenCL compute runtime (OpenCL tone mapping on
# Intel; VA-API/QSV decode, encode and VPP tone mapping still work through jellyfin-ffmpeg's bundled drivers), the
# en_US.UTF-8 locale (C.UTF-8 is used) and the Noto CJK fonts (DejaVu is the fallback for subtitle burn-in; ASS subtitles
# with embedded fonts are unaffected).

# Base images are pinned by digest (multi-arch index); refresh with: docker buildx imagetools inspect <image:tag>.
# Build stages run on the build machine's platform and cross-compile for the target.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS server-build
ARG TARGETARCH
WORKDIR /src
COPY server/ server/
RUN case "$TARGETARCH" in amd64) rid=linux-x64 ;; arm64) rid=linux-arm64 ;; *) echo "unsupported arch $TARGETARCH" >&2; exit 1 ;; esac \
    && dotnet publish server/Jellyfin.Server -c Release -r "$rid" --self-contained -p:PublishReadyToRun=true -o /out/server

FROM --platform=$BUILDPLATFORM node:22.23.4@sha256:63538dfe497cc29dfafdda25ada09b46db68a3f4cc205abf1f5d6bf0913f3384 AS web-build
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# The Jellyfin apt key, accepted only when it is exactly the Jellyfin Team key below; gpg stays in this stage.
FROM --platform=$BUILDPLATFORM debian:trixie-slim@sha256:62bd423aeee912639ca8ee681c2b762cb3e25137e0a92b20be455898a9f034ad AS jellyfin-key
ARG DEBIAN_FRONTEND=noninteractive
ARG JELLYFIN_KEY_FINGERPRINT=4918AABC486CA052358D778D49023CD01DE21A7B
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl gpg \
    && curl -fsSL https://repo.jellyfin.org/jellyfin_team.gpg.key -o /tmp/jellyfin.asc \
    && test "$(gpg --show-keys --with-colons /tmp/jellyfin.asc | awk -F: '$1 == "pub" { getline; print $10 }')" = "$JELLYFIN_KEY_FINGERPRINT" \
    && gpg --dearmor -o /jellyfin.gpg /tmp/jellyfin.asc

FROM debian:trixie-slim@sha256:62bd423aeee912639ca8ee681c2b762cb3e25137e0a92b20be455898a9f034ad
ARG DEBIAN_FRONTEND=noninteractive
ARG JELLYFIN_FFMPEG_VERSION=7.1.4-3-trixie
COPY --from=jellyfin-key /jellyfin.gpg /etc/apt/keyrings/jellyfin.gpg
# libicu: .NET globalization; libfontconfig1/libfreetype6: SkiaSharp images and ffmpeg subtitle burn-in; libjemalloc2: like the official image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && printf 'Types: deb\nURIs: https://repo.jellyfin.org/debian\nSuites: trixie\nComponents: main\nArchitectures: %s\nSigned-By: /etc/apt/keyrings/jellyfin.gpg\n' "$(dpkg --print-architecture)" > /etc/apt/sources.list.d/jellyfin.sources \
    && apt-get update \
    && apt-get install -y --no-install-recommends "jellyfin-ffmpeg7=$JELLYFIN_FFMPEG_VERSION" libicu76 libfontconfig1 libfreetype6 fonts-dejavu-core libjemalloc2 tzdata \
    && mkdir -p /usr/lib/jellyfin \
    && ln -s "$(dpkg -L libjemalloc2 | grep '/libjemalloc.so.2$')" /usr/lib/jellyfin/libjemalloc.so.2 \
    && rm -rf /var/lib/apt/lists/* /var/cache/apt/*

ENV HEALTHCHECK_URL=http://localhost:8096/health \
    LANG=C.UTF-8 \
    LC_ALL=C.UTF-8 \
    JELLYFIN_DATA_DIR=/config \
    JELLYFIN_CACHE_DIR=/cache \
    JELLYFIN_CONFIG_DIR=/config/config \
    JELLYFIN_LOG_DIR=/config/log \
    JELLYFIN_WEB_DIR=/jellyfin/jellyfin-web \
    JELLYFIN_FFMPEG=/usr/lib/jellyfin-ffmpeg/ffmpeg \
    XDG_CACHE_HOME=/cache \
    MALLOC_TRIM_THRESHOLD_=131072 \
    NVIDIA_VISIBLE_DEVICES=all \
    NVIDIA_DRIVER_CAPABILITIES=compute,video,utility \
    LD_PRELOAD=/usr/lib/jellyfin/libjemalloc.so.2

COPY --from=server-build /out/server /jellyfin
COPY --from=web-build /src/web/out /jellyfin/jellyfin-web

# Not root: a fixed uid/gid 1000 owning the writable volumes.
RUN groupadd --gid 1000 visularr \
    && useradd --uid 1000 --gid 1000 --no-create-home --home-dir /config --shell /usr/sbin/nologin visularr \
    && mkdir -p /config /cache /media \
    && chown 1000:1000 /config /cache
USER 1000:1000
EXPOSE 8096
VOLUME ["/config", "/cache", "/media"]
HEALTHCHECK --interval=30s --timeout=30s --start-period=10s --retries=3 \
    CMD curl --noproxy 'localhost' -Lk -fsS "${HEALTHCHECK_URL}" || exit 1
ENTRYPOINT ["/jellyfin/jellyfin"]
