// Library kinds the server understands (CollectionTypeOptions) and modularr's default folder layout.
export interface LibraryKind { value: string; label: string }

export const LIBRARY_KINDS: readonly LibraryKind[] = [
  { value: "movies", label: "Movies" },
  { value: "tvshows", label: "Shows" },
  { value: "music", label: "Music" },
  { value: "homevideos", label: "Home videos" },
  { value: "mixed", label: "Mixed" },
];

export interface SuggestedLibrary { name: string; type: string; path: string }

export const SUGGESTED_LIBRARIES: readonly SuggestedLibrary[] = [
  { name: "Movies", type: "movies", path: "/media/movies" },
  { name: "Shows", type: "tvshows", path: "/media/series" },
  { name: "Anime", type: "tvshows", path: "/media/anime" },
];

export const kindLabel = (type: string | null | undefined): string =>
  LIBRARY_KINDS.find((k: LibraryKind) => k.value === type)?.label ?? "Mixed";
