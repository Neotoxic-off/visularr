// Every server call, one typed function each: import * as api from "@/lib/endpoints".
import { del, get, post, qs, request } from "@/lib/api";
import type {
  ApiKey, AuthenticationResult, Country, Culture, FileSystemEntry, Item, Managed, MediaSegment, PlaybackInfoResponse, PlaybackReport,
  PlaybackRequest, PublicSystemInfo, QueryResult, Query, QuickConnectResult, SessionInfo, StartupConfiguration, SystemInfo, UserDto, UserItemData,
  VirtualFolder,
} from "@/lib/api";

export const ITEM_FIELDS: string = "Overview,Genres,PrimaryImageAspectRatio,ImageBlurHashes,ParentId,Taglines,People,MediaSources";
// Cards render art, title, year and user data (sent by default); Overview and Logo only the hero and detail fetch.
const CARD_FIELDS: string = "PrimaryImageAspectRatio,ImageBlurHashes";
const IMAGE_TYPES: string = "Primary,Backdrop,Thumb";
const CARD: Query = { Fields: CARD_FIELDS, EnableImageTypes: IMAGE_TYPES, ImageTypeLimit: 1 };

export const publicInfo = (): Promise<PublicSystemInfo> => get<PublicSystemInfo>("/System/Info/Public");
export const systemInfo = (): Promise<SystemInfo> => get<SystemInfo>("/System/Info");

// First run
export const startupConfig = (): Promise<StartupConfiguration> => get<StartupConfiguration>("/Startup/Configuration");
export const saveStartupConfig = (c: StartupConfiguration): Promise<void> => post("/Startup/Configuration", c);
export const startupUser = (): Promise<{ Name: string }> => get<{ Name: string }>("/Startup/User");
export const saveStartupUser = (name: string, password: string): Promise<void> => post("/Startup/User", { Name: name, Password: password });
export const completeStartup = (): Promise<void> => post("/Startup/Complete");
export const cultures = (): Promise<Culture[]> => get<Culture[]>("/Localization/Cultures");
export const countries = (): Promise<Country[]> => get<Country[]>("/Localization/Countries");

// Users and auth
export const publicUsers = (): Promise<UserDto[]> => get<UserDto[]>("/Users/Public");
export const me = (): Promise<UserDto> => get<UserDto>("/Users/Me");
export const login = (username: string, password: string): Promise<AuthenticationResult> =>
  post<AuthenticationResult>("/Users/AuthenticateByName", { Username: username, Pw: password });
export const logout = (): Promise<void> => post("/Sessions/Logout");
export const quickConnectEnabled = (): Promise<boolean> => get<boolean>("/QuickConnect/Enabled");
export const quickConnectInitiate = (): Promise<QuickConnectResult> => post<QuickConnectResult>("/QuickConnect/Initiate");
export const quickConnectState = (secret: string): Promise<QuickConnectResult> => get<QuickConnectResult>(`/QuickConnect/Connect${qs({ secret })}`);
export const quickConnectAuthorize = (code: string, userId: string): Promise<boolean> => post<boolean>(`/QuickConnect/Authorize${qs({ code, userId })}`);
export const quickConnectLogin = (secret: string): Promise<AuthenticationResult> =>
  post<AuthenticationResult>("/Users/AuthenticateWithQuickConnect", { Secret: secret });
export const uploadAvatar = (userId: string, type: string, base64: string): Promise<void> =>
  request<void>("POST", `/Users/${encodeURIComponent(userId)}/Images/Primary`, undefined, { type, data: base64 });
export const removeAvatar = (userId: string): Promise<void> => del(`/Users/${encodeURIComponent(userId)}/Images/Primary`);

// Browsing
export const views = (): Promise<QueryResult<Item>> => get<QueryResult<Item>>("/UserViews");
export const item = (userId: string, id: string): Promise<Item> => get<Item>(`/Items/${encodeURIComponent(id)}${qs({ userId })}`);
export const items = (query: Query): Promise<QueryResult<Item>> =>
  get<QueryResult<Item>>(`/Items${qs({ ...CARD, ...query })}`);
// The hero: one title with its overview and logo.
export const featured = async (userId: string, id: string): Promise<Item | undefined> =>
  (await get<QueryResult<Item>>(`/Items${qs({ userId, Ids: id, Fields: "Overview,ImageBlurHashes", EnableImageTypes: "Primary,Backdrop,Thumb,Logo", ImageTypeLimit: 1 })}`)).Items[0];
export const resume = (userId: string, limit: number = 16): Promise<QueryResult<Item>> =>
  get<QueryResult<Item>>(`/UserItems/Resume${qs({ userId, Limit: limit, MediaTypes: "Video", ...CARD })}`);
export const nextUp = (userId: string, seriesId?: string): Promise<QueryResult<Item>> =>
  get<QueryResult<Item>>(`/Shows/NextUp${qs({ userId, seriesId, Limit: 16, ...CARD })}`);
export const latest = (userId: string, parentId: string): Promise<Item[]> =>
  get<Item[]>(`/Items/Latest${qs({ userId, parentId, Limit: 18, ...CARD })}`);
export const myList = (userId: string): Promise<QueryResult<Item>> =>
  items({ userId, Filters: "IsFavorite", Recursive: true, IncludeItemTypes: "Movie,Series", SortBy: "DateCreated,SortName", SortOrder: "Descending", Limit: 24 });
export const recentlyPlayed = (userId: string): Promise<QueryResult<Item>> =>
  items({ userId, Filters: "IsPlayed", Recursive: true, IncludeItemTypes: "Movie,Episode", SortBy: "DatePlayed", SortOrder: "Descending", Limit: 8 });
export const localTrailers = (userId: string, id: string): Promise<Item[]> => get<Item[]>(`/Items/${encodeURIComponent(id)}/LocalTrailers${qs({ userId })}`);
// Movies carry their TMDB collection id; the matching box set has it as its own TMDB id.
export const boxSets = (userId: string): Promise<QueryResult<Item>> =>
  get<QueryResult<Item>>(`/Items${qs({ userId, IncludeItemTypes: "BoxSet", Recursive: true, Fields: "ProviderIds", EnableImages: false, EnableUserData: false })}`);

// "movie|tmdb:603" or "series|tvdb:81189" to item id, for deep links.
// shortcut: lists every movie and series id once per session (the server has no provider-id filter); add one server-side if libraries get huge.
let providerMap: Promise<Map<string, string>> | null = null;
export function providerIndex(userId: string, fresh: boolean = false): Promise<Map<string, string>> {
  if (!providerMap || fresh) {
    providerMap = get<QueryResult<Item>>(`/Items${qs({ userId, Recursive: true, IncludeItemTypes: "Movie,Series", Fields: "ProviderIds", EnableImages: false, EnableUserData: false })}`)
      .then((r: QueryResult<Item>) => {
        const map: Map<string, string> = new Map<string, string>();
        for (const i of r.Items) {
          const kind: string = i.Type === "Series" ? "series" : "movie";
          for (const [key, value] of Object.entries(i.ProviderIds ?? {})) if (value) map.set(`${kind}|${key.toLowerCase()}:${value}`, i.Id);
        }
        return map;
      })
      .catch((e: unknown) => { providerMap = null; throw e; });
  }
  return providerMap;
}
export const genres = (userId: string, parentId: string): Promise<QueryResult<Item>> => get<QueryResult<Item>>(`/Genres${qs({ userId, parentId, SortBy: "SortName" })}`);
export const seasons = (userId: string, seriesId: string): Promise<QueryResult<Item>> =>
  get<QueryResult<Item>>(`/Shows/${encodeURIComponent(seriesId)}/Seasons${qs({ userId, Fields: "ImageBlurHashes", EnableImageTypes: IMAGE_TYPES, ImageTypeLimit: 1 })}`);
export const episodes = (userId: string, seriesId: string, seasonId?: string, startItemId?: string, limit?: number): Promise<QueryResult<Item>> =>
  get<QueryResult<Item>>(`/Shows/${encodeURIComponent(seriesId)}/Episodes${qs({ userId, seasonId, startItemId, Limit: limit, Fields: "Overview,ImageBlurHashes", EnableImageTypes: IMAGE_TYPES, ImageTypeLimit: 1 })}`);
export const similar = (userId: string, id: string): Promise<QueryResult<Item>> =>
  get<QueryResult<Item>>(`/Items/${encodeURIComponent(id)}/Similar${qs({ userId, Limit: 12, ...CARD })}`);

export const setPlayed = (id: string, played: boolean): Promise<UserItemData> =>
  request<UserItemData>(played ? "POST" : "DELETE", `/UserPlayedItems/${encodeURIComponent(id)}`);
export const setFavorite = (id: string, favorite: boolean): Promise<UserItemData> =>
  request<UserItemData>(favorite ? "POST" : "DELETE", `/UserFavoriteItems/${encodeURIComponent(id)}`);

// Playback
export const playbackInfo = (id: string, body: PlaybackRequest): Promise<PlaybackInfoResponse> => post<PlaybackInfoResponse>(`/Items/${encodeURIComponent(id)}/PlaybackInfo`, body);
export const segments = (id: string): Promise<QueryResult<MediaSegment>> => get<QueryResult<MediaSegment>>(`/MediaSegments/${encodeURIComponent(id)}`);
export const playing = (r: PlaybackReport): Promise<void> => post("/Sessions/Playing", r);
export const progress = (r: PlaybackReport): Promise<void> => post("/Sessions/Playing/Progress", r);
export const stopped = (r: PlaybackReport): Promise<void> => post("/Sessions/Playing/Stopped", r);

// Administration
export const libraries = (): Promise<VirtualFolder[]> => get<VirtualFolder[]>("/Library/VirtualFolders");
export const addLibrary = (name: string, collectionType: string, paths: string[]): Promise<void> =>
  post(`/Library/VirtualFolders${qs({ name, collectionType, paths, refreshLibrary: true })}`, {
    // Trickplay is extracted by the nightly task, not during the scan, so new titles show up fast.
    LibraryOptions: { EnableRealtimeMonitor: true, ExtractTrickplayImagesDuringLibraryScan: false, EnableTrickplayImageExtraction: true, ExtractChapterImagesDuringLibraryScan: false },
  });
export const removeLibrary = (name: string): Promise<void> => del(`/Library/VirtualFolders${qs({ name, refreshLibrary: true })}`);
export const scanAll = (): Promise<void> => post("/Library/Refresh");
export const drives = (): Promise<FileSystemEntry[]> => get<FileSystemEntry[]>("/Environment/Drives");
export const directory = (path: string): Promise<FileSystemEntry[]> => get<FileSystemEntry[]>(`/Environment/DirectoryContents${qs({ path, includeDirectories: true })}`);
export const parentPath = (path: string): Promise<string | null> => get<string | null>(`/Environment/ParentPath${qs({ path })}`);
export const sessions = (): Promise<SessionInfo[]> => get<SessionInfo[]>(`/Sessions${qs({ ActiveWithinSeconds: 960 })}`);
export const keys = (): Promise<QueryResult<ApiKey>> => get<QueryResult<ApiKey>>("/Auth/Keys");
export const createKey = (app: string): Promise<void> => post(`/Auth/Keys${qs({ app })}`);
export const deleteKey = (key: string): Promise<void> => del(`/Auth/Keys/${encodeURIComponent(key)}`);

// linkarr, when it manages the users
export const managed = (): Promise<Managed> => get<Managed>("/Visularr/Managed");
