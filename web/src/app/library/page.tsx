"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback, useEffect, useRef, useState } from "react";
import type { JSX, RefObject } from "react";
import { errorMessage, itemId } from "@/lib/api";
import * as api from "@/lib/endpoints";
import type { Item, QueryResult } from "@/lib/api";
import { useSession } from "@/components/AuthGate";
import { PosterCard, PosterGrid, PosterSkeleton } from "@/components/Poster";
import { useShared } from "@/components/Shell";
import { Alert, Chip, Empty, PageHeader, Segmented, Select, Spinner } from "@/components/ui";

type Sort = "added" | "title" | "year" | "rating";

const SORTS: readonly { value: Sort; label: string }[] = [
  { value: "added", label: "Added" },
  { value: "title", label: "Title" },
  { value: "year", label: "Year" },
  { value: "rating", label: "Rating" },
];

const sortQuery: Record<Sort, { SortBy: string; SortOrder: string }> = {
  added: { SortBy: "DateCreated,SortName", SortOrder: "Descending" },
  title: { SortBy: "SortName", SortOrder: "Ascending" },
  year: { SortBy: "ProductionYear,PremiereDate,SortName", SortOrder: "Descending" },
  rating: { SortBy: "CommunityRating,SortName", SortOrder: "Descending" },
};

const PAGE_SIZE: number = 60;

const itemTypes = (collection: string | null | undefined): string =>
  collection === "movies" ? "Movie" : collection === "tvshows" ? "Series" : collection === "music" ? "MusicAlbum" : collection === "boxsets" ? "BoxSet" : "Movie,Series,Video";

// Loaded pages and scroll position per library and filter set, so Back from a title lands where you were.
interface Cached { items: Item[]; total: number }

function readCache<T>(key: string): T | null {
  try {
    const raw: string | null = sessionStorage.getItem(key);
    return raw ? (JSON.parse(raw) as T) : null;
  } catch { return null; }
}

function writeCache(key: string, value: unknown): void {
  try { sessionStorage.setItem(key, JSON.stringify(value)); } catch { /* full or private: Back reloads the first page */ }
}

// Static export has no dynamic segments, so the library comes from ?id=.
function Library(): JSX.Element {
  const id: string | null = itemId(useSearchParams().get("id"));
  const { views } = useShared();
  const view: Item | undefined = views.find((v: Item) => v.Id === id);
  if (!id) return <Empty icon="search" title="This link does not point to a library" />;
  if (!view) return views.length ? <Empty icon="search" title="This library is not available to you" /> : <Spinner className="text-ink-2" />;
  return <Grid key={id} view={view} />;
}

function Grid({ view }: { view: Item }): JSX.Element {
  const { user } = useSession();
  const router: ReturnType<typeof useRouter> = useRouter();
  const search: URLSearchParams = new URLSearchParams(useSearchParams().toString());
  // Filters live in the querystring: shareable, and Back restores them.
  const sortParam: string | null = search.get("sort");
  const sort: Sort = SORTS.find((s: { value: Sort }) => s.value === sortParam)?.value ?? "added";
  const genre: string = search.get("genre") ?? "";
  const unwatched: boolean = search.get("unwatched") === "1";
  const mine: boolean = search.get("mine") === "1";
  const key: string = `visularr.library:${view.Id}|${sort}|${genre}|${unwatched ? 1 : 0}|${mine ? 1 : 0}`;

  const [restored] = useState<Cached | null>(() => readCache<Cached>(key));
  // The key whose pages came from the cache; cleared once the user changes a filter.
  const skip: RefObject<string | null> = useRef<string | null>(restored ? key : null);
  const [genres, setGenres] = useState<string[]>([]);
  const [items, setItems] = useState<Item[] | null>(restored?.items ?? null);
  const [total, setTotal] = useState(restored?.total ?? 0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const sentinel: RefObject<HTMLDivElement | null> = useRef<HTMLDivElement>(null);
  // Bumped on every filter change so a slow page from the old query is dropped.
  const generation: RefObject<number> = useRef(0);

  function update(changes: Record<string, string | null>): void {
    for (const [k, v] of Object.entries(changes)) {
      if (v) search.set(k, v);
      else search.delete(k);
    }
    skip.current = null;
    // Cleared so the old filter's pages are never cached under the new filter's key.
    setItems(null);
    router.replace(`/library/?${search.toString()}`, { scroll: false });
  }

  useEffect(() => {
    api.genres(user.Id, view.Id).then((r: QueryResult<Item>) => setGenres(r.Items.map((g: Item) => g.Name))).catch((): void => undefined);
  }, [user.Id, view.Id]);

  const load: (start: number) => Promise<void> = useCallback(async (start: number): Promise<void> => {
    const ticket: number = generation.current;
    setLoading(true);
    try {
      const page: QueryResult<Item> = await api.items({
        userId: user.Id, ParentId: view.Id, Recursive: true, IncludeItemTypes: itemTypes(view.CollectionType),
        ...sortQuery[sort], Genres: genre || undefined, Filters: [unwatched ? "IsUnplayed" : "", mine ? "IsFavorite" : ""].filter(Boolean).join(","),
        StartIndex: start, Limit: PAGE_SIZE, EnableTotalRecordCount: true,
      });
      if (ticket !== generation.current) return;
      setItems((list: Item[] | null) => (start === 0 ? page.Items : [...(list ?? []), ...page.Items]));
      setTotal(page.TotalRecordCount);
      setError(null);
    } catch (e) {
      if (ticket === generation.current) setError(errorMessage(e));
    } finally {
      if (ticket === generation.current) setLoading(false);
    }
  }, [user.Id, view.Id, view.CollectionType, sort, genre, unwatched, mine]);

  useEffect(() => {
    generation.current += 1;
    if (skip.current === key) {
      const y: number = readCache<number>(`${key}#y`) ?? 0;
      requestAnimationFrame(() => window.scrollTo(0, y));
      return;
    }
    void Promise.resolve().then(() => load(0));
  }, [load, key]);

  useEffect(() => {
    if (items) writeCache(key, { items, total } satisfies Cached);
  }, [key, items, total]);

  useEffect(() => {
    let timer: ReturnType<typeof setTimeout> | undefined;
    const save = (): void => {
      clearTimeout(timer);
      timer = setTimeout(() => writeCache(`${key}#y`, window.scrollY), 150);
    };
    window.addEventListener("scroll", save, { passive: true });
    return () => { clearTimeout(timer); window.removeEventListener("scroll", save); };
  }, [key]);

  // Infinite scroll: the next page loads as the sentinel nears the viewport.
  const count: number = items?.length ?? 0;
  const more: boolean = !!items && count < total;
  useEffect(() => {
    const el: HTMLDivElement | null = sentinel.current;
    if (!el || !more || loading) return;
    const observer: IntersectionObserver = new IntersectionObserver((entries: IntersectionObserverEntry[]) => {
      if (entries.some((e: IntersectionObserverEntry) => e.isIntersecting)) void load(count);
    }, { rootMargin: "800px" });
    observer.observe(el);
    return () => observer.disconnect();
  }, [more, loading, count, load]);

  const filtered: boolean = !!genre || unwatched || mine;

  return (
    <>
      <PageHeader title={view.Name} description={items ? `${total} ${total === 1 ? "title" : "titles"}` : " "} />
      <div className="mb-6 flex flex-wrap items-center gap-2">
        <Segmented label="Sort by" value={sort} options={SORTS} onChange={(s: Sort) => update({ sort: s === "added" ? null : s })} />
        {genres.length > 0 && (
          <Select aria-label="Genre" value={genre} onChange={(e: { target: { value: string } }) => update({ genre: e.target.value || null })} className="w-44">
            <option value="">All genres</option>
            {genres.map((g: string) => <option key={g} value={g}>{g}</option>)}
          </Select>
        )}
        <Chip selected={unwatched} onClick={() => update({ unwatched: unwatched ? null : "1" })}>Unwatched</Chip>
        <Chip selected={mine} onClick={() => update({ mine: mine ? null : "1" })}>My list</Chip>
      </div>
      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {items && items.length === 0 ? (
        <Empty title={filtered ? "Nothing matches these filters" : "This library is empty"}>
          {filtered ? "Clear a filter to see more." : "Titles appear here once the server has scanned the library’s folders."}
        </Empty>
      ) : (
        <PosterGrid>
          {items?.map((item: Item) => <PosterCard key={item.Id} item={item} />)}
          {(!items || more) && Array.from({ length: items ? 6 : 18 }, (_: unknown, i: number) => <PosterSkeleton key={`s${i}`} />)}
        </PosterGrid>
      )}
      <div ref={sentinel} aria-hidden="true" />
    </>
  );
}

export default function LibraryPage(): JSX.Element {
  return <Suspense><Library /></Suspense>;
}
