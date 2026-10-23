"use client";

import type Hls from "hls.js";
import type { ErrorData } from "hls.js";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useRef, useState } from "react";
import type { JSX, ReactNode, RefObject } from "react";
import { ApiError, authHeader, clock, episodeCode, errorMessage, landscape, playHref, poster, qs, reportStopped, session, TICKS_PER_SECOND, url } from "@/lib/api";
import * as api from "@/lib/endpoints";
import type { Art, Item, MediaSegment, MediaSource, MediaStream, PlaybackInfoResponse, PlaybackReport, QueryResult, UserItemData } from "@/lib/api";
import { deviceProfile } from "@/lib/profile";
import { loadQueue, saveQueue } from "@/lib/queue";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import type { IconName } from "@/components/icons";
import { useUserData } from "@/components/ItemActions";
import { Alert, Button, Segmented, Spinner } from "@/components/ui";

const PROGRESS_EVERY_MS: number = 10_000;
const HIDE_CONTROLS_MS: number = 3000;
const SKIP_SECONDS: number = 10;
const UP_NEXT_SECONDS: number = 40;
const RESUME_MIN_SECONDS: number = 30;
const DOUBLE_TAP_MS: number = 300;
const RING: number = 2 * Math.PI * 21;

type Router = ReturnType<typeof useRouter>;
type FullscreenVideo = HTMLVideoElement & { webkitEnterFullscreen?: () => void };
type Timer = ReturnType<typeof setTimeout> | undefined;
interface TrackMemory { audio?: string; subtitle?: string }
interface SkipTarget { label: string; to: number }
interface Nudge { side: -1 | 1; at: number }
type CueSize = "s" | "m" | "l" | "xl";
type CueBackground = "none" | "shadow" | "box";
interface CueStyle { size: CueSize; background: CueBackground }
interface SyncState { running: boolean; message: string; failed?: boolean }

const CUE_SIZES: Record<CueSize, string> = { s: "80%", m: "100%", l: "130%", xl: "160%" };
const CUE_BACKGROUNDS: Record<CueBackground, { background: string; shadow: string }> = {
  none: { background: "transparent", shadow: "none" },
  shadow: { background: "transparent", shadow: "0 0 4px #000, 0 0 2px #000, 1px 1px 2px #000" },
  box: { background: "rgb(0 0 0 / 0.75)", shadow: "none" },
};
const OFFSET_STEPS: readonly number[] = [-1, -0.1, 0.1, 1];

const SHORTCUTS: [keys: string, action: string][] = [
  ["Space / K", "Play or pause"], ["← / →", "Back or forward 10 seconds"], ["↑ / ↓", "Volume"], ["0–9", "Jump to 0–90%"],
  ["S / Enter", "Skip intro or credits"], ["N", "Next episode"], ["C", "Audio and subtitles"], ["F", "Full screen"], ["M", "Mute"],
  ["?", "This list"], ["Esc", "Close the picker, else the player"],
];

// A <track> cannot send the auth header and streams now require a login: the token rides in the query unless the server added it.
const withToken = (path: string): string =>
  /[?&](api_key|ApiKey)=/.test(path) ? path : `${path}${path.includes("?") ? "&" : "?"}api_key=${encodeURIComponent(session.token() ?? "")}`;
const streamLabel = (s: MediaStream): string => s.DisplayTitle || s.Title || s.Language || `Track ${s.Index}`;

function mediaErrorMessage(code: number): string {
  if (code === 2) return "The connection to the server dropped while loading the video.";
  if (code === 3) return "This browser couldn’t decode the video.";
  if (code === 4) return "This browser can’t play this video format.";
  return "Playback stopped unexpectedly.";
}

function hlsErrorMessage(data: ErrorData): string {
  const status: number | undefined = data.response?.code;
  if (status === 401 || status === 403) return "The server refused the stream. Sign in again, then try again.";
  const kind: string = data.type;
  const details: string = data.details;
  if (kind === "networkError" && /^(manifest|level)/.test(details)) return "The server couldn’t start the stream. It may still be preparing it, so try again in a moment.";
  if (kind === "networkError") return "The connection to the server dropped.";
  if (kind === "mediaError") return "This browser couldn’t decode the stream.";
  return "Playback stopped unexpectedly.";
}

// The audio and subtitle language last picked for a series, so the next episode starts the same way.
const memoryKey = (seriesId: string): string => `visularr.tracks.${seriesId}`;
function recall(seriesId: string): TrackMemory {
  try { return JSON.parse(sessionStorage.getItem(memoryKey(seriesId)) ?? "{}") as TrackMemory; } catch { return {}; }
}
function remember(seriesId: string | null | undefined, patch: TrackMemory): void {
  if (!seriesId) return;
  try { sessionStorage.setItem(memoryKey(seriesId), JSON.stringify({ ...recall(seriesId), ...patch })); } catch { /* storage blocked: nothing to remember */ }
}
function rememberedTracks(current: Item): [audio: number | null, subtitle: number | null] {
  if (!current.SeriesId) return [null, null];
  const memory: TrackMemory = recall(current.SeriesId);
  const streams: MediaStream[] = current.MediaSources?.[0]?.MediaStreams ?? [];
  const find = (type: "Audio" | "Subtitle", language: string | undefined): number | null => {
    const matches: MediaStream[] = language ? streams.filter((s: MediaStream) => s.Type === type && s.Language === language) : [];
    return (matches.find((s: MediaStream) => s.IsDefault) ?? matches[0])?.Index ?? null;
  };
  return [find("Audio", memory.audio), memory.subtitle === "off" ? -1 : find("Subtitle", memory.subtitle)];
}

// Subtitle timing per title and the subtitle look, kept across sessions in this browser.
const offsetKey = (itemId: string): string => `visularr.subtitleOffset.${itemId}`;
function readOffset(itemId: string): number {
  try { return Number(localStorage.getItem(offsetKey(itemId))) || 0; } catch { return 0; }
}
function writeOffset(itemId: string, seconds: number): void {
  try {
    if (seconds === 0) localStorage.removeItem(offsetKey(itemId));
    else localStorage.setItem(offsetKey(itemId), String(seconds));
  } catch { /* storage blocked: the offset lasts this session */ }
}
const STYLE_KEY: string = "visularr.subtitleStyle";
function readCueStyle(): CueStyle {
  try {
    const saved: Partial<CueStyle> = JSON.parse(localStorage.getItem(STYLE_KEY) ?? "{}") as Partial<CueStyle>;
    return { size: saved.size && saved.size in CUE_SIZES ? saved.size : "m", background: saved.background && saved.background in CUE_BACKGROUNDS ? saved.background : "box" };
  } catch { return { size: "m", background: "box" }; }
}
function writeCueStyle(style: CueStyle): void {
  try { localStorage.setItem(STYLE_KEY, JSON.stringify(style)); } catch { /* storage blocked */ }
}

// Full-screen playback: direct play when the browser can, the server's HLS remux or transcode otherwise.
// With list (a playlist id), next and previous walk the playlist's play order.
export function Player({ id, start = null, list = null }: { id: string; start?: number | null; list?: string | null }): JSX.Element {
  const router: Router = useRouter();
  const { user } = useSession();
  const root: RefObject<HTMLDivElement | null> = useRef<HTMLDivElement>(null);
  const video: RefObject<HTMLVideoElement | null> = useRef<HTMLVideoElement>(null);
  const skipButton: RefObject<HTMLButtonElement | null> = useRef<HTMLButtonElement>(null);
  const hls: RefObject<Hls | null> = useRef<Hls | null>(null);
  const report: RefObject<PlaybackReport | null> = useRef<PlaybackReport | null>(null);
  const started: RefObject<boolean> = useRef(false);
  const pendingSeek: RefObject<number> = useRef(0);
  // Set once direct play fails: every later load of this item goes through the server.
  const noDirect: RefObject<boolean> = useRef(false);
  // The last known position: on unmount the <video> ref is already gone when the stop report is sent.
  const lastTicks: RefObject<number> = useRef(0);
  const hideTimer: RefObject<Timer> = useRef<Timer>(undefined);
  const touch: RefObject<boolean> = useRef(false);
  const lastTap: RefObject<number> = useRef(0);
  const tapTimer: RefObject<Timer> = useRef<Timer>(undefined);
  const trackEl: RefObject<HTMLTrackElement | null> = useRef<HTMLTrackElement>(null);
  // Each cue's own times, so offsets are applied from the original rather than stacked.
  const cueTimes: RefObject<WeakMap<TextTrackCue, [number, number]>> = useRef(new WeakMap<TextTrackCue, [number, number]>());

  const [item, setItem] = useState<Item | null>(null);
  const [source, setSource] = useState<MediaSource | null>(null);
  const [audio, setAudio] = useState<number | null>(null);
  const [subtitle, setSubtitle] = useState<number>(-1);
  const [error, setError] = useState<string | null>(null);
  const [paused, setPaused] = useState(true);
  const [waiting, setWaiting] = useState(true);
  const [time, setTime] = useState(0);
  const [duration, setDuration] = useState(0);
  const [volume, setVolume] = useState(1);
  const [muted, setMuted] = useState(false);
  const [fullscreen, setFullscreen] = useState(false);
  const [chrome, setChrome] = useState(true);
  const [tracks, setTracks] = useState(false);
  const [help, setHelp] = useState(false);
  const [nudge, setNudge] = useState<Nudge | null>(null);
  const [segments, setSegments] = useState<MediaSegment[]>([]);
  const [next, setNext] = useState<Item | null>(null);
  const [upNextDismissed, setUpNextDismissed] = useState(false);
  const [previous, setPrevious] = useState<string | null>(null);
  const [ended, setEnded] = useState(false);
  const [offset, setOffset] = useState<number>(() => readOffset(id));
  const [cueStyle, setCueStyle] = useState<CueStyle>(readCueStyle);
  const [sync, setSync] = useState<SyncState | null>(null);

  const position = (): number => (video.current ? Math.round(video.current.currentTime * TICKS_PER_SECOND) : lastTicks.current);

  // Asks the server how to play, then wires the <video> to a static file or an HLS stream.
  const load: (current: Item, startTicks: number, audioIndex: number | null, subtitleIndex: number | null) => Promise<void> = useCallback(async (current: Item, startTicks: number, audioIndex: number | null, subtitleIndex: number | null): Promise<void> => {
    const el: HTMLVideoElement | null = video.current;
    if (!el) return;
    const previous: PlaybackReport | null = report.current;
    if (previous) await api.stopped({ ...previous, PositionTicks: startTicks }).catch((): void => undefined);
    hls.current?.destroy();
    hls.current = null;
    report.current = null;
    started.current = false;
    lastTicks.current = startTicks;
    setWaiting(true);
    setError(null);

    const original: MediaSource | undefined = current.MediaSources?.[0];
    // Browsers cannot switch audio tracks in a file they play directly, so another track means going through the server.
    const transcode: boolean = noDirect.current || (audioIndex !== null && audioIndex !== (original?.DefaultAudioStreamIndex ?? audioIndex));
    try {
      const info: PlaybackInfoResponse = await api.playbackInfo(current.Id, {
        UserId: user.Id, StartTimeTicks: startTicks, MediaSourceId: original?.Id,
        AudioStreamIndex: audioIndex, SubtitleStreamIndex: subtitleIndex,
        EnableDirectPlay: !transcode, EnableDirectStream: true, EnableTranscoding: true, AutoOpenLiveStream: true,
        DeviceProfile: deviceProfile(),
      });
      const chosen: MediaSource | undefined = info.MediaSources[0];
      if (info.ErrorCode || !chosen) throw new Error(info.ErrorCode === "NotAllowed" ? "You’re not allowed to play this." : "The server can’t play this video.");
      setSource(chosen);
      setAudio(audioIndex ?? chosen.DefaultAudioStreamIndex ?? null);
      setSubtitle(subtitleIndex ?? chosen.DefaultSubtitleStreamIndex ?? -1);

      const startSeconds: number = startTicks / TICKS_PER_SECOND;
      let method: PlaybackReport["PlayMethod"];
      if (chosen.SupportsDirectPlay && !transcode) {
        method = "DirectPlay";
        pendingSeek.current = startSeconds;
        el.src = url(`/Videos/${encodeURIComponent(current.Id)}/stream${qs({ static: true, mediaSourceId: chosen.Id, api_key: session.token() })}`);
      } else if (chosen.TranscodingUrl) {
        method = "Transcode";
        const playlist: string = url(chosen.TranscodingUrl);
        const { default: HlsPlayer } = await import("hls.js");
        if (HlsPlayer.isSupported()) {
          const player: Hls = new HlsPlayer({
            startPosition: startSeconds,
            // The token travels as a header, not in segment URLs.
            xhrSetup: (xhr: XMLHttpRequest): void => xhr.setRequestHeader("Authorization", authHeader()),
          });
          let recovered: boolean = false;
          player.on(HlsPlayer.Events.ERROR, (_event: string, data: ErrorData) => {
            if (!data.fatal) return;
            // A decode hiccup usually clears after one media recovery.
            if (data.type === HlsPlayer.ErrorTypes.MEDIA_ERROR && !recovered) { recovered = true; player.recoverMediaError(); return; }
            setWaiting(false);
            setError(hlsErrorMessage(data));
          });
          player.loadSource(playlist);
          player.attachMedia(el);
          hls.current = player;
        } else {
          pendingSeek.current = startSeconds;
          el.src = playlist;
        }
      } else {
        throw new Error("This video can’t be played in this browser.");
      }
      report.current = {
        ItemId: current.Id, MediaSourceId: chosen.Id, PlaySessionId: info.PlaySessionId, PositionTicks: startTicks, PlayMethod: method,
        AudioStreamIndex: audioIndex ?? chosen.DefaultAudioStreamIndex, SubtitleStreamIndex: subtitleIndex ?? chosen.DefaultSubtitleStreamIndex, CanSeek: true,
      };
      await el.play().catch((): void => setPaused(true));
    } catch (e) {
      setWaiting(false);
      setError(errorMessage(e));
    }
  }, [user.Id]);

  // The item, its segments and the next episode, then playback from ?t=, else the saved position.
  useEffect(() => {
    let live: boolean = true;
    api.item(user.Id, id).then((current: Item) => {
      if (!live) return;
      setItem(current);
      const saved: number = current.UserData?.PlaybackPositionTicks ?? 0;
      const resume: number = current.UserData?.Played || saved < RESUME_MIN_SECONDS * TICKS_PER_SECOND ? 0 : saved;
      const [audioIndex, subtitleIndex] = rememberedTracks(current);
      void load(current, start !== null ? Math.round(start * TICKS_PER_SECOND) : resume, audioIndex, subtitleIndex);
      api.segments(current.Id).then((r: QueryResult<MediaSegment>) => live && setSegments(r.Items)).catch((): void => undefined);
      if (list) {
        const walk = (order: string[]): void => {
          const at: number = order.indexOf(current.Id);
          setPrevious(order[at - 1] ?? null);
          const following: string | undefined = order[at + 1];
          if (following) api.item(user.Id, following).then((n: Item) => live && setNext(n)).catch((): void => undefined);
        };
        const saved: string[] | null = loadQueue(list);
        if (saved?.includes(current.Id)) walk(saved);
        else {
          api.playlistItems(user.Id, list).then((r: QueryResult<Item>) => {
            const order: string[] = r.Items.map((e: Item) => e.Id);
            saveQueue(list, order);
            if (live) walk(order);
          }).catch((): void => undefined);
        }
      } else if (current.Type === "Episode" && current.SeriesId) {
        api.episodes(user.Id, current.SeriesId, undefined, current.Id, 2)
          .then((r: QueryResult<Item>) => live && setNext(r.Items.find((e: Item) => e.Id !== current.Id) ?? null))
          .catch((): void => undefined);
      }
    }).catch((e: unknown) => { setWaiting(false); setError(errorMessage(e)); });
    const player: RefObject<Hls | null> = hls;
    const stop = (): void => {
      if (report.current) reportStopped({ ...report.current, PositionTicks: position() });
      report.current = null;
    };
    window.addEventListener("pagehide", stop);
    return () => {
      live = false;
      window.removeEventListener("pagehide", stop);
      stop();
      player.current?.destroy();
    };
  }, [id, start, list, user.Id, load]);

  // Progress every 10 s while a session runs.
  useEffect(() => {
    const timer: ReturnType<typeof setInterval> = setInterval(() => {
      const el: HTMLVideoElement | null = video.current;
      if (report.current && started.current && el) void api.progress({ ...report.current, PositionTicks: position(), IsPaused: el.paused, IsMuted: el.muted }).catch((): void => undefined);
    }, PROGRESS_EVERY_MS);
    return () => clearInterval(timer);
  }, []);

  // Controls hide after a while of playback, but not while the keyboard is on one of them.
  const showChrome: () => void = useCallback((): void => {
    setChrome(true);
    const hideLater = (): void => {
      clearTimeout(hideTimer.current);
      hideTimer.current = setTimeout(() => {
        const active: Element | null = document.activeElement;
        if (active && root.current?.contains(active) && active.closest("[data-chrome]") && active.matches(":focus-visible")) hideLater();
        else if (video.current && !video.current.paused) setChrome(false);
      }, HIDE_CONTROLS_MS);
    };
    hideLater();
  }, []);

  const toggle: () => void = useCallback((): void => {
    const el: HTMLVideoElement | null = video.current;
    if (!el) return;
    if (el.paused) void el.play().catch((): void => undefined);
    else el.pause();
  }, []);

  const seek: (seconds: number) => void = useCallback((seconds: number): void => {
    const el: HTMLVideoElement | null = video.current;
    if (el) el.currentTime = Math.max(0, Math.min(seconds, el.duration || seconds));
  }, []);

  const toggleFullscreen: () => void = useCallback((): void => {
    const el: FullscreenVideo | null = video.current;
    if (document.fullscreenElement) void document.exitFullscreen();
    else if (root.current?.requestFullscreen) void root.current.requestFullscreen().catch((): void => undefined);
    // iPhone Safari has no element fullscreen, only the video's own.
    else el?.webkitEnterFullscreen?.();
  }, []);

  const togglePip = (): void => {
    if (document.pictureInPictureElement) void document.exitPictureInPicture().catch((): void => undefined);
    else void video.current?.requestPictureInPicture().catch((): void => undefined);
  };

  const close: () => void = useCallback((): void => {
    if (window.history.length > 1) router.back();
    else router.replace(item?.SeriesId ? `/item/?id=${item.SeriesId}` : `/item/?id=${id}`);
  }, [router, item, id]);

  const playNext: () => void = useCallback((): void => {
    if (next) router.replace(playHref(next.Id, list));
  }, [router, next, list]);

  const playPrevious: () => void = useCallback((): void => {
    if (previous) router.replace(playHref(previous, list));
  }, [router, previous, list]);

  const setUserData = (data: UserItemData): void => setItem((i: Item | null) => i && { ...i, UserData: { ...i.UserData, ...data } });
  const toggleUserData: (kind: "liked" | "favorite") => Promise<void> = useUserData(item, setUserData);

  // Offsets move every cue of the shown text track; the originals stay in cueTimes.
  const applyOffset: () => void = useCallback((): void => {
    const cues: TextTrackCueList | null | undefined = trackEl.current?.track.cues;
    if (!cues) return;
    for (const cue of Array.from(cues)) {
      let base: [number, number] | undefined = cueTimes.current.get(cue);
      if (!base) {
        base = [cue.startTime, cue.endTime];
        cueTimes.current.set(cue, base);
      }
      cue.startTime = Math.max(0, base[0] + offset);
      cue.endTime = Math.max(0, base[1] + offset);
    }
  }, [offset]);

  const shiftOffset = (delta: number | null): void => {
    const value: number = delta === null ? 0 : Math.round((offset + delta) * 10) / 10;
    setOffset(value);
    writeOffset(id, value);
  };

  const changeCueStyle = (patch: Partial<CueStyle>): void => {
    const value: CueStyle = { ...cueStyle, ...patch };
    setCueStyle(value);
    writeCueStyle(value);
  };

  useEffect(() => {
    const change = (): void => setFullscreen(!!document.fullscreenElement);
    document.addEventListener("fullscreenchange", change);
    return () => document.removeEventListener("fullscreenchange", change);
  }, []);

  const streams: MediaStream[] = source?.MediaStreams ?? [];
  const audioStreams: MediaStream[] = streams.filter((s: MediaStream) => s.Type === "Audio");
  const subtitleStreams: MediaStream[] = streams.filter((s: MediaStream) => s.Type === "Subtitle");
  const textTrack: MediaStream | undefined = subtitleStreams.find((s: MediaStream) => s.Index === subtitle && s.DeliveryMethod === "External" && s.DeliveryUrl);
  const trackKey: string | null = textTrack ? `${source?.Id}-${textTrack.Index}` : null;
  // Autosync aligns text subtitles only; picture ones (PGS, VobSub) have no text to align.
  const syncable: boolean = subtitleStreams.some((s: MediaStream) => s.Index === subtitle && s.IsTextSubtitleStream !== false && s.DeliveryMethod !== "Encode");
  const hasTracks: boolean = audioStreams.length > 1 || subtitleStreams.length > 0;

  const within = (s: MediaSegment): boolean => time >= s.StartTicks / TICKS_PER_SECOND && time < s.EndTicks / TICKS_PER_SECOND - 1;
  const intro: MediaSegment | undefined = segments.find((s: MediaSegment) => s.Type === "Intro" && within(s));
  const outro: MediaSegment | undefined = segments.find((s: MediaSegment) => s.Type === "Outro");
  // With a next episode the credits hand over to Up next; without one they can be skipped.
  const credits: MediaSegment | undefined = !next && outro && within(outro) ? outro : undefined;
  const skip: SkipTarget | null = intro ? { label: "Skip intro", to: intro.EndTicks / TICKS_PER_SECOND }
    : credits ? { label: "Skip credits", to: credits.EndTicks / TICKS_PER_SECOND } : null;
  const nextFrom: number = Math.min(outro ? outro.StartTicks / TICKS_PER_SECOND : Infinity, duration - UP_NEXT_SECONDS);
  const showNext: boolean = !!next && duration > 0 && time >= nextFrom && !upNextDismissed;
  const countdown: number = Math.max(0, Math.ceil(UP_NEXT_SECONDS - (time - nextFrom)));
  const title: string = item ? (item.Type === "Episode" ? item.SeriesName ?? item.Name : item.Name) : "";
  const subtitleLine: string = item?.Type === "Episode" ? `${episodeCode(item)} · ${item.Name}` : item?.ProductionYear ? String(item.ProductionYear) : "";
  const visible: boolean = chrome || paused || tracks || help || ended || !!error;
  const nextLabel: string = next ? (next.Type === "Episode" ? `${episodeCode(next)} · ${next.Name}` : next.Name) : "";
  const skipLabel: string | null = skip?.label ?? null;
  const skipTo: number | null = skip?.to ?? null;
  const pipEnabled: boolean = typeof document !== "undefined" && document.pictureInPictureEnabled;

  // The offset follows the track: applied when its cues load and whenever it changes.
  useEffect(() => {
    applyOffset();
    const el: HTMLTrackElement | null = trackEl.current;
    el?.addEventListener("load", applyOffset);
    return () => el?.removeEventListener("load", applyOffset);
  }, [applyOffset, trackKey]);

  // Up next plays by itself when its countdown runs out.
  useEffect(() => {
    if (showNext && countdown <= 0) playNext();
  }, [showNext, countdown, playNext]);

  // A skip offer takes the focus so Enter takes it.
  useEffect(() => {
    if (skipLabel) skipButton.current?.focus({ preventScroll: true });
  }, [skipLabel]);

  // Space or k, ←/→ 10 s, ↑/↓ volume, 0-9 percent, s/Enter skip, n next, c tracks, f fullscreen, m mute, ? help, Escape.
  useEffect(() => {
    const key = (e: KeyboardEvent): void => {
      if (e.ctrlKey || e.metaKey || e.altKey) return;
      const el: HTMLVideoElement | null = video.current;
      if (!el) return;
      const target: HTMLElement = e.target as HTMLElement;
      // The skip button holds the focus while it shows: Space still pauses there.
      const onControl: boolean = target.dataset.skip === undefined && (target.tagName === "BUTTON" || target.getAttribute("role") === "radio");
      const onSlider: boolean = target.tagName === "INPUT";
      const k: string = e.key;
      if ((k === " " || k === "k") && !onControl) { e.preventDefault(); toggle(); }
      else if (k === "ArrowLeft" && !onSlider) { e.preventDefault(); seek(el.currentTime - SKIP_SECONDS); }
      else if (k === "ArrowRight" && !onSlider) { e.preventDefault(); seek(el.currentTime + SKIP_SECONDS); }
      else if ((k === "ArrowUp" || k === "ArrowDown") && !onSlider) {
        e.preventDefault();
        el.volume = Math.min(1, Math.max(0, Math.round((el.volume + (k === "ArrowUp" ? 0.1 : -0.1)) * 10) / 10));
        el.muted = false;
      }
      else if ((k === "s" || (k === "Enter" && !onControl)) && skipTo !== null) { e.preventDefault(); seek(skipTo); }
      else if (k === "n" && next) playNext();
      else if (k === "c" && hasTracks) setTracks((open: boolean) => !open);
      else if (/^[0-9]$/.test(k) && duration > 0) seek((duration * Number(k)) / 10);
      else if (k === "f") toggleFullscreen();
      else if (k === "m") el.muted = !el.muted;
      else if (k === "?") setHelp((open: boolean) => !open);
      else if (k === "Escape") {
        if (tracks) setTracks(false);
        else if (help) setHelp(false);
        else close();
      }
      else return;
      showChrome();
    };
    window.addEventListener("keydown", key);
    return () => window.removeEventListener("keydown", key);
  }, [toggle, seek, toggleFullscreen, showChrome, close, playNext, skipTo, next, hasTracks, duration, tracks, help]);

  // Lock screen, headset and OS media controls.
  useEffect(() => {
    if (!item || !("mediaSession" in navigator)) return;
    const media: MediaSession = navigator.mediaSession;
    const art: Art[] = [landscape(item, 640), poster(item, 512)].filter((a: Art | null): a is Art => a !== null);
    media.metadata = new MediaMetadata({
      title: item.Name,
      artist: item.Type === "Episode" ? item.SeriesName ?? "" : item.ProductionYear ? String(item.ProductionYear) : "",
      album: item.Type === "Episode" ? episodeCode(item) : "",
      artwork: art.map((a: Art) => ({ src: a.src })),
    });
    const now = (): number => video.current?.currentTime ?? 0;
    const handlers: [MediaSessionAction, MediaSessionActionHandler | null][] = [
      ["play", () => void video.current?.play().catch((): void => undefined)],
      ["pause", () => video.current?.pause()],
      ["seekbackward", (d: MediaSessionActionDetails) => seek(now() - (d.seekOffset ?? SKIP_SECONDS))],
      ["seekforward", (d: MediaSessionActionDetails) => seek(now() + (d.seekOffset ?? SKIP_SECONDS))],
      ["seekto", (d: MediaSessionActionDetails) => { if (d.seekTime !== undefined) seek(d.seekTime); }],
      ["nexttrack", next ? playNext : null],
      ["previoustrack", previous ? playPrevious : null],
    ];
    for (const [action, handler] of handlers) {
      try { media.setActionHandler(action, handler); } catch { /* the browser does not know this action */ }
    }
    return () => {
      media.metadata = null;
      for (const [action] of handlers) {
        try { media.setActionHandler(action, null); } catch { /* the browser does not know this action */ }
      }
    };
  }, [item, next, previous, seek, playNext, playPrevious]);

  const syncPosition = (el: HTMLVideoElement): void => {
    if (!("mediaSession" in navigator) || !Number.isFinite(el.duration) || el.duration <= 0) return;
    try { navigator.mediaSession.setPositionState({ duration: el.duration, position: Math.min(el.currentTime, el.duration), playbackRate: el.playbackRate }); } catch { /* not supported */ }
  };

  // Touch: one tap shows or hides the controls, a double tap on either half seeks.
  function tap(e: React.PointerEvent<HTMLVideoElement>): void {
    if (e.pointerType === "mouse") return;
    const now: number = e.timeStamp;
    clearTimeout(tapTimer.current);
    if (now - lastTap.current < DOUBLE_TAP_MS) {
      lastTap.current = 0;
      const rect: DOMRect = e.currentTarget.getBoundingClientRect();
      const side: -1 | 1 = e.clientX - rect.left < rect.width / 2 ? -1 : 1;
      seek(e.currentTarget.currentTime + side * SKIP_SECONDS);
      setNudge({ side, at: now });
      setTimeout(() => setNudge((n: Nudge | null) => (n?.at === now ? null : n)), 700);
      return;
    }
    lastTap.current = now;
    const shown: boolean = visible;
    tapTimer.current = setTimeout(() => {
      if (shown && !paused) { clearTimeout(hideTimer.current); setChrome(false); }
      else showChrome();
    }, DOUBLE_TAP_MS);
  }

  function pickAudio(index: number): void {
    setTracks(false);
    remember(item?.SeriesId, { audio: audioStreams.find((s: MediaStream) => s.Index === index)?.Language ?? undefined });
    if (item && index !== audio) void load(item, position(), index, subtitle);
  }

  function pickSubtitle(index: number): void {
    setTracks(false);
    remember(item?.SeriesId, { subtitle: index === -1 ? "off" : subtitleStreams.find((s: MediaStream) => s.Index === index)?.Language ?? undefined });
    if (!item || index === subtitle) return;
    const burned = (i: number): boolean => subtitleStreams.find((s: MediaStream) => s.Index === i)?.DeliveryMethod === "Encode";
    // Picture subtitles are burned into the transcode, so switching to or from one restarts the stream.
    if (burned(index) || burned(subtitle)) void load(item, position(), audio, index);
    else {
      setSubtitle(index);
      if (report.current) report.current.SubtitleStreamIndex = index;
    }
  }

  // Aligns the picked subtitle to the audio on the server, then plays on with the new "(synced)" track.
  async function autoSync(): Promise<void> {
    if (!item || !source || subtitle < 0) return;
    setSync({ running: true, message: "Matching the subtitles to the audio. This can take a few minutes…" });
    try {
      const result: { StreamIndex?: number | null } = await api.syncSubtitle(item.Id, source.Id, subtitle);
      if (result.StreamIndex == null) {
        setSync({ running: false, message: "Synced and saved. Reopen the player to pick the synced subtitles." });
        return;
      }
      setSync({ running: false, message: "Synced. Now showing the synced subtitles." });
      shiftOffset(null);
      void load(item, position(), audio, result.StreamIndex);
    } catch (e) {
      setSync({ running: false, failed: true, message: e instanceof ApiError && e.status === 501 ? "Autosync is not available on this platform." : errorMessage(e) });
    }
  }

  const nextArt: Art | null = next ? landscape(next, 320) : null;

  return (
    <div ref={root} onFocusCapture={showChrome}
      onPointerMove={(e: React.PointerEvent<HTMLDivElement>) => { if (e.pointerType === "mouse") showChrome(); }}
      onPointerDown={(e: React.PointerEvent<HTMLDivElement>) => { touch.current = e.pointerType !== "mouse"; if (e.target !== video.current) showChrome(); }}
      className={`fixed inset-0 z-40 bg-black text-ink select-none ${visible ? "" : "cursor-none"}`}>
      <h1 className="sr-only">{title ? `Playing ${title}` : "Player"}</h1>
      {/* Direct play carries the token in src: keep "copy video address" out of easy reach. */}
      <video ref={video} playsInline crossOrigin="anonymous" className="player-video absolute inset-0 h-full w-full touch-manipulation"
        style={{ "--cue-size": CUE_SIZES[cueStyle.size], "--cue-bg": CUE_BACKGROUNDS[cueStyle.background].background, "--cue-shadow": CUE_BACKGROUNDS[cueStyle.background].shadow } as React.CSSProperties}
        controlsList="nodownload" onContextMenu={(e: React.MouseEvent<HTMLVideoElement>) => e.preventDefault()}
        onClick={() => { if (!touch.current) toggle(); }}
        onDoubleClick={() => { if (!touch.current) toggleFullscreen(); }}
        onPointerUp={tap}
        onError={(e: React.SyntheticEvent<HTMLVideoElement>) => {
          const el: HTMLVideoElement = e.currentTarget;
          // hls.js reports its own errors; an emptied src or an abort is not a failure.
          if (hls.current || !el.error || el.error.code === 1 || !el.getAttribute("src")) return;
          if (report.current?.PlayMethod === "DirectPlay" && !noDirect.current && item) {
            noDirect.current = true;
            void load(item, lastTicks.current, audio, subtitle);
            return;
          }
          setWaiting(false);
          setError(mediaErrorMessage(el.error.code));
        }}
        onLoadedMetadata={(e: React.SyntheticEvent<HTMLVideoElement>) => {
          setDuration(e.currentTarget.duration || (source?.RunTimeTicks ?? 0) / TICKS_PER_SECOND);
          if (pendingSeek.current > 0) { e.currentTarget.currentTime = pendingSeek.current; pendingSeek.current = 0; }
        }}
        onDurationChange={(e: React.SyntheticEvent<HTMLVideoElement>) => { if (Number.isFinite(e.currentTarget.duration)) setDuration(e.currentTarget.duration); syncPosition(e.currentTarget); }}
        onTimeUpdate={(e: React.SyntheticEvent<HTMLVideoElement>) => { setTime(e.currentTarget.currentTime); lastTicks.current = Math.round(e.currentTarget.currentTime * TICKS_PER_SECOND); }}
        onSeeked={(e: React.SyntheticEvent<HTMLVideoElement>) => syncPosition(e.currentTarget)}
        onPlay={(e: React.SyntheticEvent<HTMLVideoElement>) => { setPaused(false); showChrome(); syncPosition(e.currentTarget); }}
        onPause={(e: React.SyntheticEvent<HTMLVideoElement>) => {
          setPaused(true);
          syncPosition(e.currentTarget);
          if (report.current && started.current) void api.progress({ ...report.current, PositionTicks: position(), IsPaused: true }).catch((): void => undefined);
        }}
        onPlaying={() => {
          setWaiting(false);
          if (report.current && !started.current) {
            started.current = true;
            void api.playing({ ...report.current, PositionTicks: position() }).catch((): void => undefined);
          }
        }}
        onWaiting={() => setWaiting(true)}
        onCanPlay={() => setWaiting(false)}
        onVolumeChange={(e: React.SyntheticEvent<HTMLVideoElement>) => { setVolume(e.currentTarget.volume); setMuted(e.currentTarget.muted); }}
        onEnded={() => (next && !upNextDismissed ? playNext() : setEnded(true))}>
        {textTrack?.DeliveryUrl && (
          <track ref={trackEl} key={trackKey} kind="subtitles" default src={url(withToken(textTrack.DeliveryUrl))}
            srcLang={textTrack.Language ?? undefined} label={streamLabel(textTrack)} />
        )}
      </video>

      {waiting && !error && <div className="pointer-events-none absolute inset-0 grid place-items-center"><Spinner className="size-10 border-[3px] text-white" /></div>}

      {nudge && (
        <div key={nudge.at} aria-hidden className={`pointer-events-none absolute top-1/2 grid size-20 -translate-y-1/2 animate-fade place-items-center rounded-full bg-black/50 ${nudge.side < 0 ? "left-[15%]" : "right-[15%]"}`}>
          <Icon name={nudge.side < 0 ? "back10" : "forward10"} size={30} />
        </div>
      )}

      {error && (
        <div className="absolute inset-0 grid place-items-center p-6">
          <div className="glass-pop max-w-md space-y-4 rounded-tile p-5">
            <Alert>{error}</Alert>
            <div className="flex justify-end gap-2">
              <Button variant="ghost" onClick={close}>Close</Button>
              {item && <Button variant="primary" icon="refresh" onClick={() => void load(item, position(), audio, subtitle)}>Try again</Button>}
            </div>
          </div>
        </div>
      )}

      {/* Top bar */}
      <div data-chrome className={`absolute inset-x-0 top-0 flex items-center gap-3 bg-gradient-to-b from-black/80 to-transparent px-3 pt-[max(12px,env(safe-area-inset-top))] pb-10 transition duration-300 sm:px-5 ${visible ? "opacity-100" : "pointer-events-none opacity-0"}`}>
        <ControlButton icon="chevronLeft" label="Close player" onClick={close} />
        <div className="min-w-0">
          <p className="truncate text-title font-semibold">{title}</p>
          {subtitleLine && <p className="truncate text-meta text-ink-2">{subtitleLine}</p>}
        </div>
      </div>

      {/* Skip and up next sit above the controls */}
      <div className={`absolute right-3 flex flex-col items-end gap-2 transition-[bottom] duration-300 sm:right-6 ${visible ? "bottom-36" : "bottom-8"}`}>
        {skip && (
          <button ref={skipButton} type="button" data-skip onClick={() => seek(skip.to)}
            className="glass-pop inline-flex h-12 cursor-pointer items-center gap-2 rounded-pill px-5 text-body font-semibold hover:bg-white hover:text-canvas focus-visible:bg-white focus-visible:text-canvas">
            <Icon name="skip" size={18} />{skip.label}
          </button>
        )}
        {showNext && next && (
          <div role="status" className="glass-pop flex w-[min(400px,calc(100vw-24px))] animate-rise items-center gap-3 rounded-tile p-3">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            {nextArt && <img src={nextArt.src} alt="" className="aspect-video w-24 shrink-0 rounded-ctl object-cover sm:w-28" />}
            <div className="min-w-0 flex-1">
              <p className="caption">Up next</p>
              <p className="line-clamp-2 text-body font-semibold">{nextLabel}</p>
              <button type="button" onClick={() => setUpNextDismissed(true)} className="-ml-2 mt-1 h-9 cursor-pointer rounded-pill px-2 text-meta font-medium text-ink-2 hover:bg-fill-2 hover:text-ink">Cancel</button>
            </div>
            <button type="button" onClick={playNext} aria-label={`Play next, starts in ${countdown} seconds`} title="Play next"
              className="relative grid size-12 shrink-0 cursor-pointer place-items-center rounded-full bg-white text-canvas transition active:scale-95">
              <svg viewBox="0 0 48 48" className="absolute inset-0 size-full -rotate-90" aria-hidden>
                <circle cx="24" cy="24" r="21" fill="none" stroke="currentColor" strokeOpacity={0.15} strokeWidth="3" />
                <circle cx="24" cy="24" r="21" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round"
                  strokeDasharray={RING} strokeDashoffset={RING * (1 - countdown / UP_NEXT_SECONDS)} className="transition-[stroke-dashoffset] duration-1000 ease-linear" />
              </svg>
              <Icon name="play" size={18} fill="currentColor" />
            </button>
          </div>
        )}
      </div>

      {/* Bottom controls */}
      <div data-chrome className={`absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/85 via-black/50 to-transparent px-3 pt-16 pb-[max(12px,env(safe-area-inset-bottom))] transition duration-300 sm:px-5 ${visible ? "opacity-100" : "pointer-events-none opacity-0"}`}>
        <div className="flex items-center gap-3">
          <span className="w-14 text-right text-meta text-ink-2 tabular-nums">{clock(time)}</span>
          <input type="range" className="slider min-w-0 flex-1" min={0} max={duration || 0} step={1} value={Math.min(time, duration || 0)}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => seek(Number(e.target.value))}
            aria-label="Seek" aria-valuetext={`${clock(time)} of ${clock(duration)}`}
            style={{ "--fill": `${duration ? (time / duration) * 100 : 0}%` } as React.CSSProperties} />
          <span className="w-14 text-meta text-ink-2 tabular-nums">-{clock(duration - time)}</span>
        </div>
        <div className="mt-1 flex items-center gap-1 sm:gap-2">
          <ControlButton icon={paused ? "play" : "pause"} label={paused ? "Play" : "Pause"} onClick={toggle} big />
          <ControlButton icon="back10" label="Back 10 seconds" onClick={() => seek(time - SKIP_SECONDS)} />
          <ControlButton icon="forward10" label="Forward 10 seconds" onClick={() => seek(time + SKIP_SECONDS)} />
          <ControlButton icon={muted || volume === 0 ? "mute" : "volume"} label={muted ? "Unmute" : "Mute"}
            onClick={() => { if (video.current) video.current.muted = !video.current.muted; }} />
          <input type="range" className="slider w-24 max-sm:hidden" min={0} max={1} step={0.05} value={muted ? 0 : volume}
            onChange={(e: React.ChangeEvent<HTMLInputElement>) => { if (video.current) { video.current.volume = Number(e.target.value); video.current.muted = false; } }}
            aria-label="Volume" aria-valuetext={`${Math.round((muted ? 0 : volume) * 100)}%`}
            style={{ "--fill": `${(muted ? 0 : volume) * 100}%` } as React.CSSProperties} />
          <div className="ml-auto flex items-center gap-1 sm:gap-2">
            {previous && <ControlButton icon="previous" label="Previous" onClick={playPrevious} />}
            {next && <ControlButton icon="skip" label={list ? "Next" : "Next episode"} onClick={playNext} />}
            {hasTracks && <ControlButton icon="subtitles" label="Audio and subtitles" onClick={() => setTracks(!tracks)} expanded={tracks} />}
            <ControlButton icon="keyboard" label="Keyboard shortcuts" onClick={() => setHelp(!help)} expanded={help} className="pointer-coarse:hidden" />
            {pipEnabled && (
              <ControlButton label="Picture in picture" onClick={togglePip}
                glyph={<svg viewBox="0 0 24 24" width={22} height={22} fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" aria-hidden><path d="M21 11V5H3v14h7M14 14h7v6h-7z" /></svg>} />
            )}
            <ControlButton icon={fullscreen ? "minimize" : "maximize"} label={fullscreen ? "Exit full screen" : "Full screen"} onClick={toggleFullscreen} />
          </div>
        </div>
      </div>

      {tracks && (
        <div data-chrome role="dialog" aria-label="Audio and subtitles"
          className="glass-pop absolute right-3 bottom-28 grid max-h-[60dvh] w-[min(520px,calc(100vw-24px))] animate-rise gap-4 overflow-y-auto rounded-tile p-4 sm:right-5 sm:grid-cols-2">
          <TrackList label="Audio" icon="volume" value={audio ?? -2} onPick={pickAudio}
            options={audioStreams.map((s: MediaStream) => ({ value: s.Index, label: streamLabel(s) }))} />
          <TrackList label="Subtitles" icon="subtitles" value={subtitle} onPick={pickSubtitle}
            options={[{ value: -1, label: "Off" }, ...subtitleStreams.map((s: MediaStream) => ({ value: s.Index, label: `${streamLabel(s)}${s.DeliveryMethod === "Encode" ? " (burned in)" : ""}` }))]} />
          {subtitle >= 0 && (
            <div className="space-y-3 border-t border-hair-2 pt-3 sm:col-span-2">
              {textTrack && (
                <div role="group" aria-label="Subtitle timing" className="flex flex-wrap items-center gap-1.5">
                  <span className="caption mr-1 w-full px-2 sm:w-auto">Timing</span>
                  {OFFSET_STEPS.filter((d: number) => d < 0).map((d: number) => <OffsetButton key={d} delta={d} onClick={() => shiftOffset(d)} />)}
                  <output aria-live="polite" className="w-20 text-center text-body font-semibold tabular-nums">{offset > 0 ? "+" : ""}{offset.toFixed(1)} s</output>
                  {OFFSET_STEPS.filter((d: number) => d > 0).map((d: number) => <OffsetButton key={d} delta={d} onClick={() => shiftOffset(d)} />)}
                  <button type="button" onClick={() => shiftOffset(null)} disabled={offset === 0}
                    className="h-9 cursor-pointer rounded-pill px-3 text-meta font-medium text-ink-2 hover:bg-fill-2 hover:text-ink disabled:opacity-40">Reset</button>
                </div>
              )}
              {textTrack && (
                <div className="flex flex-wrap items-center gap-2">
                  <Segmented label="Subtitle size" value={cueStyle.size} onChange={(size: CueSize) => changeCueStyle({ size })}
                    options={[{ value: "s", label: "S" }, { value: "m", label: "M" }, { value: "l", label: "L" }, { value: "xl", label: "XL" }]} />
                  <Segmented label="Subtitle background" value={cueStyle.background} onChange={(background: CueBackground) => changeCueStyle({ background })}
                    options={[{ value: "none", label: "None" }, { value: "shadow", label: "Shadow" }, { value: "box", label: "Box" }]} />
                </div>
              )}
              {syncable && (
                <div className="flex flex-wrap items-center gap-3">
                  <Button size="sm" icon="refresh" loading={!!sync?.running} onClick={() => void autoSync()}>Auto-sync</Button>
                  <p role="status" className={`min-w-0 flex-1 text-meta ${sync?.failed ? "text-bad" : "text-ink-3"}`}>
                    {sync?.message ?? "Matches these subtitles to the audio and saves them as a new “(synced)” track."}
                  </p>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {ended && (
        <div data-chrome role="dialog" aria-label="Finished" className="absolute inset-0 grid place-items-center bg-black/70 p-6">
          <div className="glass-pop w-[min(440px,calc(100vw-24px))] animate-rise space-y-4 rounded-tile p-5 text-center">
            <p className="caption">Finished</p>
            <p className="text-title font-semibold">{title}</p>
            {subtitleLine && <p className="-mt-3 text-meta text-ink-2">{subtitleLine}</p>}
            <div className="flex flex-wrap justify-center gap-2">
              <Button icon="thumbsUp" aria-pressed={!!item?.UserData?.Likes} onClick={() => void toggleUserData("liked")}
                className={item?.UserData?.Likes ? "!bg-white !text-canvas" : ""}>{item?.UserData?.Likes ? "Liked" : "Like"}</Button>
              <Button icon="undo" onClick={() => { setEnded(false); seek(0); void video.current?.play().catch((): void => undefined); }}>Play again</Button>
              {next && <Button variant="primary" icon="play" onClick={playNext}>Play next</Button>}
              <Button variant="ghost" onClick={close}>Close</Button>
            </div>
          </div>
        </div>
      )}

      {help && (
        <div data-chrome role="dialog" aria-label="Keyboard shortcuts"
          className="glass-pop absolute right-3 bottom-28 w-[min(400px,calc(100vw-24px))] animate-rise rounded-tile p-4 sm:right-5">
          <p className="caption mb-2 flex items-center gap-2 px-1"><Icon name="keyboard" size={14} />Keyboard shortcuts</p>
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 px-1 text-meta">
            {SHORTCUTS.map(([keys, action]: [string, string]) => (
              <div key={keys} className="contents">
                <dt className="font-semibold text-ink tabular-nums">{keys}</dt>
                <dd className="text-ink-2">{action}</dd>
              </div>
            ))}
          </dl>
        </div>
      )}
    </div>
  );
}

function ControlButton({ icon, glyph, label, onClick, big = false, expanded, className = "" }: {
  icon?: IconName; glyph?: ReactNode; label: string; onClick: () => void; big?: boolean; expanded?: boolean; className?: string;
}): JSX.Element {
  return (
    <button type="button" onClick={onClick} aria-label={label} title={label} aria-expanded={expanded}
      className={`grid shrink-0 cursor-pointer place-items-center rounded-full text-ink transition hover:bg-white/15 active:scale-95 ${big ? "size-12" : "size-11"} ${className}`}>
      {glyph ?? (icon && <Icon name={icon} size={big ? 26 : 22} fill={icon === "play" || icon === "pause" ? "currentColor" : "none"} />)}
    </button>
  );
}

function OffsetButton({ delta, onClick }: { delta: number; onClick: () => void }): JSX.Element {
  const text: string = `${delta > 0 ? "+" : "−"}${Math.abs(delta)} s`;
  return (
    <button type="button" onClick={onClick} aria-label={`Subtitles ${delta > 0 ? "later" : "earlier"} by ${Math.abs(delta)} seconds`}
      className="h-9 min-w-14 cursor-pointer rounded-pill bg-fill-2 px-3 text-meta font-semibold text-ink tabular-nums hover:bg-fill-3">{text}</button>
  );
}

function TrackList({ label, icon, value, options, onPick }: {
  label: string; icon: IconName; value: number; options: { value: number; label: string }[]; onPick: (value: number) => void;
}): ReactNode {
  if (options.length === 0) return null;
  return (
    <div role="radiogroup" aria-label={label}>
      <p className="caption mb-2 flex items-center gap-2 px-2"><Icon name={icon} size={14} />{label}</p>
      {options.map((o: { value: number; label: string }) => (
        <button key={o.value} type="button" role="radio" aria-checked={o.value === value} onClick={() => onPick(o.value)}
          className="flex min-h-11 w-full cursor-pointer items-center gap-2 rounded-ctl px-3 text-left text-body text-ink-2 hover:bg-fill-2 hover:text-ink aria-checked:text-ink">
          <span className="min-w-0 flex-1 truncate">{o.label}</span>
          {o.value === value && <Icon name="check" size={16} bold />}
        </button>
      ))}
    </div>
  );
}
