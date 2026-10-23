// Typed client for the visularr server (the Jellyfin API, PascalCase JSON).

// ---------- DTOs ----------

export interface UserPolicy {
  IsAdministrator: boolean;
  EnableSubtitleManagement?: boolean;
}

export interface UserDto {
  Id: string;
  Name: string;
  HasPassword: boolean;
  PrimaryImageTag?: string | null;
  LastActivityDate?: string | null;
  Policy?: UserPolicy;
}

export interface AuthenticationResult {
  User: UserDto;
  AccessToken: string;
  ServerId: string;
}

export interface PublicSystemInfo {
  ServerName: string;
  Version: string;
  Id: string;
  StartupWizardCompleted?: boolean | null;
}

export interface SystemInfo extends PublicSystemInfo {
  OperatingSystemDisplayName?: string | null;
  HasPendingRestart?: boolean;
  CachePath?: string | null;
}

export interface UserItemData {
  PlaybackPositionTicks: number;
  PlayCount: number;
  IsFavorite: boolean;
  // Thumbs up (true), down (false) or neither (null).
  Likes?: boolean | null;
  Played: boolean;
  PlayedPercentage?: number | null;
  UnplayedItemCount?: number | null;
}

export type ImageType = "Primary" | "Backdrop" | "Thumb" | "Logo" | "Banner";
export type BlurHashes = Partial<Record<ImageType, Record<string, string>>>;

export interface Person {
  Id: string;
  Name: string;
  Role?: string | null;
  Type: string;
  PrimaryImageTag?: string | null;
  ImageBlurHashes?: BlurHashes;
}

export type StreamType = "Audio" | "Video" | "Subtitle" | "EmbeddedImage" | "Data" | "Lyric";
export type DeliveryMethod = "Encode" | "Embed" | "External" | "Hls" | "VideoSideData" | "Drop";

export interface MediaStream {
  Index: number;
  Type: StreamType;
  Codec?: string | null;
  Language?: string | null;
  DisplayTitle?: string | null;
  Title?: string | null;
  IsDefault: boolean;
  IsExternal: boolean;
  IsTextSubtitleStream?: boolean;
  DeliveryMethod?: DeliveryMethod | null;
  DeliveryUrl?: string | null;
}

export interface MediaSource {
  Id: string;
  Container?: string | null;
  SupportsDirectPlay: boolean;
  SupportsDirectStream: boolean;
  SupportsTranscoding: boolean;
  TranscodingUrl?: string | null;
  MediaStreams: MediaStream[];
  DefaultAudioStreamIndex?: number | null;
  DefaultSubtitleStreamIndex?: number | null;
  RunTimeTicks?: number | null;
}

export interface Item {
  Id: string;
  Name: string;
  Type: string;
  ProductionYear?: number | null;
  PremiereDate?: string | null;
  EndDate?: string | null;
  Status?: string | null;
  OfficialRating?: string | null;
  CommunityRating?: number | null;
  CriticRating?: number | null;
  Overview?: string | null;
  Taglines?: string[];
  Genres?: string[];
  RunTimeTicks?: number | null;
  CollectionType?: string | null;
  ChildCount?: number | null;
  ImageTags?: Partial<Record<ImageType, string>>;
  BackdropImageTags?: string[];
  ImageBlurHashes?: BlurHashes;
  ParentBackdropItemId?: string | null;
  ParentBackdropImageTags?: string[] | null;
  ParentThumbItemId?: string | null;
  ParentThumbImageTag?: string | null;
  ParentLogoItemId?: string | null;
  ParentLogoImageTag?: string | null;
  SeriesId?: string | null;
  SeriesName?: string | null;
  SeriesPrimaryImageTag?: string | null;
  SeasonId?: string | null;
  IndexNumber?: number | null;
  ParentIndexNumber?: number | null;
  UserData?: UserItemData;
  // Set on playlist entries: the entry, as opposed to the item it points at.
  PlaylistItemId?: string | null;
  MediaType?: string | null;
  People?: Person[];
  MediaSources?: MediaSource[];
  ProviderIds?: Record<string, string | null>;
  LocalTrailerCount?: number | null;
  RemoteTrailers?: { Url?: string | null; Name?: string | null }[] | null;
}

export interface QueryResult<T> {
  Items: T[];
  TotalRecordCount: number;
  StartIndex: number;
}

export interface PlaybackInfoResponse {
  MediaSources: MediaSource[];
  PlaySessionId: string;
  ErrorCode?: string | null;
}

export type SegmentType = "Unknown" | "Commercial" | "Preview" | "Recap" | "Outro" | "Intro";
export interface MediaSegment {
  Id: string;
  ItemId: string;
  Type: SegmentType;
  StartTicks: number;
  EndTicks: number;
}

export interface VirtualFolder {
  Name: string;
  Locations: string[];
  CollectionType?: string | null;
  ItemId: string;
  RefreshStatus?: string | null;
  RefreshProgress?: number | null;
}

export interface FileSystemEntry {
  Name: string;
  Path: string;
  Type: "File" | "Directory" | "NetworkComputer" | "NetworkShare";
}

export interface SessionInfo {
  Id: string;
  UserId: string;
  UserName?: string | null;
  Client?: string | null;
  DeviceName?: string | null;
  LastActivityDate: string;
  NowPlayingItem?: Item | null;
  PlayState?: { PositionTicks?: number | null; IsPaused: boolean; PlayMethod?: string | null } | null;
}

export interface ApiKey {
  Id: number;
  AccessToken: string;
  AppName?: string | null;
  DateCreated: string;
}

export interface QuickConnectResult {
  Secret: string;
  Code: string;
  Authenticated: boolean;
}

export interface StartupConfiguration {
  ServerName?: string | null;
  UICulture?: string | null;
  MetadataCountryCode?: string | null;
  PreferredMetadataLanguage?: string | null;
}

export interface Culture {
  DisplayName: string;
  Name: string;
  TwoLetterISOLanguageName: string;
  ThreeLetterISOLanguageName?: string | null;
}

export interface PlaylistShare { UserId: string; CanEdit: boolean }
export interface Playlist { OpenAccess: boolean; Shares: PlaylistShare[]; ItemIds: string[] }

export interface Country {
  DisplayName: string;
  Name: string;
  TwoLetterISORegionName: string;
}

// The hub (linkarr) that manages the users; empty when they are managed here.
export interface Managed { name?: string; url?: string }

export interface PlaybackReport {
  ItemId: string;
  MediaSourceId: string;
  PlaySessionId: string;
  PositionTicks: number;
  IsPaused?: boolean;
  IsMuted?: boolean;
  PlayMethod?: "DirectPlay" | "DirectStream" | "Transcode";
  AudioStreamIndex?: number | null;
  SubtitleStreamIndex?: number | null;
  CanSeek?: boolean;
}

export interface PlaybackRequest {
  UserId: string;
  StartTimeTicks: number;
  MediaSourceId?: string;
  AudioStreamIndex?: number | null;
  SubtitleStreamIndex?: number | null;
  EnableDirectPlay: boolean;
  EnableDirectStream: boolean;
  EnableTranscoding: boolean;
  AutoOpenLiveStream: boolean;
  DeviceProfile: DeviceProfile;
}

export interface DeviceProfile {
  MaxStreamingBitrate: number;
  MaxStaticBitrate: number;
  MusicStreamingTranscodingBitrate: number;
  DirectPlayProfiles: { Container: string; Type: "Video" | "Audio"; VideoCodec?: string; AudioCodec?: string }[];
  TranscodingProfiles: {
    Container: string; Type: "Video"; VideoCodec: string; AudioCodec: string; Context: "Streaming"; Protocol: "hls";
    MaxAudioChannels: string; MinSegments: number; BreakOnNonKeyFrames: boolean;
  }[];
  ContainerProfiles: never[];
  CodecProfiles: never[];
  SubtitleProfiles: { Format: string; Method: "External" | "Encode" | "Embed" }[];
}

// ---------- Session storage ----------

const SERVER: string = (process.env.NEXT_PUBLIC_SERVER ?? "").replace(/\/$/, "");
const TOKEN_KEY: string = "visularr.token";
const USER_KEY: string = "visularr.user";
const DEVICE_KEY: string = "visularr.device";

export const UNAUTHORIZED: string = "visularr:unauthorized";

function read(key: string): string | null {
  try { return localStorage.getItem(key); } catch { return null; }
}

function write(key: string, value: string | null): void {
  try {
    if (value === null) localStorage.removeItem(key);
    else localStorage.setItem(key, value);
  } catch { /* private mode: the session lasts this tab only */ }
}

export const session: { token: () => string | null; userId: () => string | null; save: (r: AuthenticationResult) => void; clear: () => void } = {
  token: (): string | null => read(TOKEN_KEY),
  userId: (): string | null => read(USER_KEY),
  save: (r: AuthenticationResult): void => { write(TOKEN_KEY, r.AccessToken); write(USER_KEY, r.User.Id); },
  clear: (): void => { write(TOKEN_KEY, null); write(USER_KEY, null); },
};

function deviceId(): string {
  let id: string | null = read(DEVICE_KEY);
  if (!id) {
    id = crypto.randomUUID();
    write(DEVICE_KEY, id);
  }
  return id;
}

function deviceName(): string {
  const ua: string = typeof navigator === "undefined" ? "" : navigator.userAgent;
  return /Edg\//.test(ua) ? "Edge" : /Firefox\//.test(ua) ? "Firefox" : /Chrome\//.test(ua) ? "Chrome" : /Safari\//.test(ua) ? "Safari" : "Browser";
}

export function authHeader(): string {
  const token: string | null = session.token();
  return `MediaBrowser Client="visularr", Device="${deviceName()}", DeviceId="${deviceId()}", Version="0.1.0"${token ? `, Token="${token}"` : ""}`;
}

export const url = (path: string): string => `${SERVER}${path}`;

// ---------- Fetch wrapper ----------

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

export function errorMessage(e: unknown): string {
  if (e instanceof ApiError) return e.message;
  if (e instanceof Error) return e.message;
  return String(e);
}

export type Query = Record<string, string | number | boolean | null | undefined | readonly string[]>;

export function qs(query: Query): string {
  const params: URLSearchParams = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === null || value === undefined || value === "") continue;
    params.set(key, Array.isArray(value) ? value.join(",") : String(value));
  }
  const text: string = params.toString();
  return text ? `?${text}` : "";
}

export async function request<T>(method: string, path: string, body?: unknown, raw?: { type: string; data: string }): Promise<T> {
  const headers: Record<string, string> = { Authorization: authHeader() };
  let payload: string | undefined;
  if (raw) {
    headers["Content-Type"] = raw.type;
    payload = raw.data;
  } else if (body !== undefined) {
    headers["Content-Type"] = "application/json";
    payload = JSON.stringify(body);
  }
  let response: Response;
  try {
    response = await fetch(url(path), { method, headers, body: payload });
  } catch {
    throw new ApiError(0, "Can’t reach the server.");
  }
  // A 401 from sign-in is a wrong password, not an expired session.
  const passwordCheck: boolean = /^\/Users\/Authenticate/.test(path);
  if (response.status === 401 && session.token() && !passwordCheck) {
    session.clear();
    window.dispatchEvent(new Event(UNAUTHORIZED));
  }
  if (!response.ok) {
    const text: string = await response.text().catch((): string => "");
    throw new ApiError(response.status, text.length > 0 && text.length < 300 && !text.startsWith("<") ? text.replace(/^"|"$/g, "") : `Request failed (${response.status}).`);
  }
  if (response.status === 204) return undefined as T;
  const type: string = response.headers.get("content-type") ?? "";
  if (!type.includes("json")) return undefined as T;
  // The one place a response body becomes a typed value.
  return (await response.json()) as T;
}

export const get = <T>(path: string): Promise<T> => request<T>("GET", path);
export const post = <T = void>(path: string, body?: unknown): Promise<T> => request<T>("POST", path, body);
export const del = (path: string): Promise<void> => request<void>("DELETE", path);

// ---------- Endpoints ----------


// Survives the page being closed, so the server stops the transcode and saves the position.
export function reportStopped(r: PlaybackReport): void {
  void fetch(url("/Sessions/Playing/Stopped"), {
    method: "POST",
    keepalive: true,
    headers: { Authorization: authHeader(), "Content-Type": "application/json" },
    body: JSON.stringify(r),
  }).catch((): void => undefined);
}

// ---------- Images ----------

export interface Art { src: string; hash: string | null; srcSet?: string; sizes?: string }

// width is the 2x size; 1x screens get half of it. sizes defaults to the 1x width, callers override it for full-bleed art.
export function image(id: string, type: ImageType, tag: string | null | undefined, width: number, hashes?: BlurHashes): Art | null {
  if (!tag) return null;
  const at = (w: number): string => url(`/Items/${encodeURIComponent(id)}/Images/${type}${qs({ fillWidth: w, quality: 80, tag })}`);
  const half: number = Math.round(width / 2);
  return { src: at(width), srcSet: `${at(half)} ${half}w, ${at(width)} ${width}w`, sizes: `${half}px`, hash: hashes?.[type]?.[tag] ?? null };
}

export function userImage(user: UserDto, width: number = 96): string | null {
  return user.PrimaryImageTag ? url(`/Users/${encodeURIComponent(user.Id)}/Images/Primary${qs({ fillWidth: width, quality: 80, tag: user.PrimaryImageTag })}`) : null;
}

export const poster = (item: Item, width: number = 360): Art | null =>
  image(item.Id, "Primary", item.ImageTags?.Primary, width, item.ImageBlurHashes)
  ?? (item.SeriesId ? image(item.SeriesId, "Primary", item.SeriesPrimaryImageTag, width, item.ImageBlurHashes) : null);

export function backdrop(item: Item, width: number = 1920): Art | null {
  const own: string | undefined = item.BackdropImageTags?.[0];
  if (own) return image(item.Id, "Backdrop", own, width, item.ImageBlurHashes);
  const parent: string | undefined = item.ParentBackdropImageTags?.[0];
  return item.ParentBackdropItemId && parent ? image(item.ParentBackdropItemId, "Backdrop", parent, width, item.ImageBlurHashes) : null;
}

// Landscape art for continue watching: the episode still, else the show's thumb, else a backdrop.
export function landscape(item: Item, width: number = 640): Art | null {
  if (item.Type === "Episode" && item.ImageTags?.Primary) return image(item.Id, "Primary", item.ImageTags.Primary, width, item.ImageBlurHashes);
  return image(item.Id, "Thumb", item.ImageTags?.Thumb, width, item.ImageBlurHashes)
    ?? (item.ParentThumbItemId ? image(item.ParentThumbItemId, "Thumb", item.ParentThumbImageTag, width, item.ImageBlurHashes) : null)
    ?? backdrop(item, width);
}

export function logo(item: Item, width: number = 600): Art | null {
  return image(item.Id, "Logo", item.ImageTags?.Logo, width, item.ImageBlurHashes)
    ?? (item.ParentLogoItemId ? image(item.ParentLogoItemId, "Logo", item.ParentLogoImageTag, width, item.ImageBlurHashes) : null);
}

// ---------- Helpers ----------

// Ids from the URL become API path segments: only a Jellyfin guid (N or dashed form) gets through.
export const itemId = (raw: string | null): string | null =>
  raw && /^(?:[0-9a-f]{32}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$/i.test(raw) ? raw : null;

export const TICKS_PER_SECOND: number = 10_000_000;

export function itemHref(item: Item): string {
  if (item.Type === "CollectionFolder" || item.Type === "UserView") return `/library/?id=${item.Id}`;
  if (item.Type === "Playlist") return `/playlists/?id=${item.Id}`;
  if (item.Type === "Season" && item.SeriesId) return `/item/?id=${item.SeriesId}&season=${item.Id}`;
  return `/item/?id=${item.Id}`;
}

// list: a playlist to play through, in order.
export const playHref = (id: string, list?: string | null): string => `/play/?id=${id}${list ? `&list=${list}` : ""}`;

export function episodeCode(item: Item): string {
  if (item.ParentIndexNumber == null || item.IndexNumber == null) return "";
  return `S${item.ParentIndexNumber}:E${item.IndexNumber}`;
}

export function runtime(ticks: number | null | undefined): string {
  if (!ticks) return "";
  const minutes: number = Math.round(ticks / TICKS_PER_SECOND / 60);
  return minutes >= 60 ? `${Math.floor(minutes / 60)} h ${minutes % 60} min` : `${minutes} min`;
}

export function clock(seconds: number): string {
  const s: number = Math.max(0, Math.floor(seconds));
  const h: number = Math.floor(s / 3600);
  const m: number = Math.floor((s % 3600) / 60);
  const rest: string = String(s % 60).padStart(2, "0");
  return h > 0 ? `${h}:${String(m).padStart(2, "0")}:${rest}` : `${m}:${rest}`;
}

export function progressOf(item: Item): number {
  const pct: number | null | undefined = item.UserData?.PlayedPercentage;
  if (pct) return pct / 100;
  const pos: number = item.UserData?.PlaybackPositionTicks ?? 0;
  return pos && item.RunTimeTicks ? pos / item.RunTimeTicks : 0;
}
