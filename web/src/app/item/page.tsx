"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import type { JSX, ReactNode } from "react";
import { backdrop, episodeCode, errorMessage, image, itemId, landscape, logo, playHref, poster, progressOf, runtime, TICKS_PER_SECOND } from "@/lib/api";
import * as api from "@/lib/endpoints";
import type { Art, Item, Person, QueryResult, UserItemData } from "@/lib/api";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { Img, PosterCard, PosterGrid, PosterSkeleton, Rail } from "@/components/Poster";
import { useBackdrop, useShared } from "@/components/Shell";
import { useToast } from "@/components/Toasts";
import type { Toast } from "@/components/Toasts";
import { Alert, Badge, Button, ButtonLink, Empty, IconButton, Modal, Rating, Segmented, Skeleton } from "@/components/ui";

// Static export has no dynamic segments, so the title comes from ?id=[&season=].
function Detail(): JSX.Element {
  const params: URLSearchParams = new URLSearchParams(useSearchParams().toString());
  const id: string | null = itemId(params.get("id"));
  if (!id) return <Empty icon="search" title="This link does not point to a title" />;
  return <ItemView key={id} id={id} season={itemId(params.get("season"))} />;
}

const youtubeId = (url: string | null | undefined): string | null => /(?:[?&]v=|youtu\.be\/|\/embed\/)([\w-]{11})/.exec(url ?? "")?.[1] ?? null;

function ItemView({ id, season }: { id: string; season: string | null }): JSX.Element {
  const { user, admin } = useSession();
  const { hub } = useShared();
  const toast: (t: Toast) => void = useToast();
  const [item, setItem] = useState<Item | null>(null);
  const [next, setNext] = useState<Item | null>(null);
  const [seasons, setSeasons] = useState<Item[] | null>(null);
  const [episodes, setEpisodes] = useState<Item[] | null>(null);
  const [similar, setSimilar] = useState<Item[]>([]);
  const [trailer, setTrailer] = useState<Item | null>(null);
  const [collection, setCollection] = useState<Item | null>(null);
  const [youtube, setYoutube] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  // The id is in the URL, so nothing waits on the item: show requests just fail quietly for a movie.
  useEffect(() => {
    api.item(user.Id, id).then(setItem).catch((e: unknown) => setError(errorMessage(e)));
    api.similar(user.Id, id).then((r: QueryResult<Item>) => setSimilar(r.Items)).catch((): void => undefined);
    api.nextUp(user.Id, id).then((r: QueryResult<Item>) => setNext(r.Items[0] ?? null)).catch((): void => undefined);
    api.seasons(user.Id, id).then((r: QueryResult<Item>) => setSeasons(r.Items)).catch((): void => setSeasons([]));
    // Every episode once; switching seasons filters locally.
    api.episodes(user.Id, id).then((r: QueryResult<Item>) => setEpisodes(r.Items)).catch((): void => setEpisodes([]));
  }, [user.Id, id]);

  const localTrailers: number = item?.LocalTrailerCount ?? 0;
  useEffect(() => {
    if (localTrailers > 0) api.localTrailers(user.Id, id).then((list: Item[]) => setTrailer(list[0] ?? null)).catch((): void => undefined);
  }, [user.Id, id, localTrailers]);

  const tmdbCollection: string | null | undefined = item?.ProviderIds?.TmdbCollection;
  useEffect(() => {
    if (!tmdbCollection) return;
    api.boxSets(user.Id)
      .then((r: QueryResult<Item>) => setCollection(r.Items.find((b: Item) => b.ProviderIds?.Tmdb === tmdbCollection) ?? null))
      .catch((): void => undefined);
  }, [user.Id, tmdbCollection]);

  const art: Art | null = item ? backdrop(item, 1920) : null;
  useBackdrop(art?.src ?? null);

  const setUserData = (data: UserItemData): void => setItem((i: Item | null) => i && { ...i, UserData: { ...i.UserData, ...data } });

  async function toggle(kind: "played" | "favorite"): Promise<void> {
    if (!item) return;
    try {
      setUserData(kind === "played"
        ? await api.setPlayed(item.Id, !item.UserData?.Played)
        : await api.setFavorite(item.Id, !item.UserData?.IsFavorite));
    } catch (e) {
      toast({ key: `toggle-${item.Id}`, icon: "alert", tone: "bad", title: "Couldn’t save that", message: errorMessage(e) });
    }
  }

  async function togglePlayed(episode: Item): Promise<void> {
    const data: UserItemData = await api.setPlayed(episode.Id, !episode.UserData?.Played);
    setEpisodes((list: Item[] | null) => list?.map((e: Item) => (e.Id === episode.Id ? { ...e, UserData: data } : e)) ?? null);
  }

  if (error) return <Alert>{error}</Alert>;
  if (!item) return <DetailSkeleton />;

  const mark: Art | null = logo(item);
  const series: boolean = item.Type === "Series";
  const target: Item = series ? next ?? item : item;
  const progress: number = series ? (next ? progressOf(next) : 0) : progressOf(item);
  const playLabel: string = series
    ? next ? `${progress > 0 ? "Resume" : "Play"} ${episodeCode(next)}` : "Play"
    : progress > 0 ? `Resume from ${Math.round((item.UserData?.PlaybackPositionTicks ?? 0) / TICKS_PER_SECOND / 60)} min` : "Play";
  const facts: ReactNode[] = [
    item.Type === "Episode" ? episodeCode(item) : null,
    item.ProductionYear,
    item.OfficialRating ? <Badge key="rating">{item.OfficialRating}</Badge> : null,
    series ? (item.ChildCount ? `${item.ChildCount} ${item.ChildCount === 1 ? "season" : "seasons"}` : null) : runtime(item.RunTimeTicks),
  ].filter(Boolean);
  const cast: Person[] = (item.People ?? []).filter((p: Person) => p.Type === "Actor").slice(0, 24);
  const directors: Person[] = (item.People ?? []).filter((p: Person) => p.Type === "Director");
  const remote: string | null = localTrailers > 0 ? null : (item.RemoteTrailers ?? []).map((t: { Url?: string | null }) => youtubeId(t.Url)).find(Boolean) ?? null;
  // linkarr resolves the title in modularr from its TMDB (movies) or TVDB (series) id.
  const providerId: string | null | undefined = series ? item.ProviderIds?.Tvdb : item.Type === "Movie" ? item.ProviderIds?.Tmdb : null;
  const manage: string | null = admin && hub?.url && providerId ? `${hub.url}/go/manage?${series ? "tvdb" : "tmdb"}=${encodeURIComponent(providerId)}` : null;

  return (
    <div className="space-y-10">
      <section className="relative -mx-4 -mt-4 overflow-hidden md:mx-0 md:mt-0 md:rounded-panel md:ring-1 md:ring-white/8 lg:-mx-0">
        <Img art={art ?? poster(item, 1280)} eager sizes="100vw" className="aspect-[16/10] w-full sm:aspect-[21/9] sm:max-h-[58vh]" />
        <div aria-hidden="true" className="absolute inset-0 bg-gradient-to-t from-canvas via-black/30 to-transparent sm:bg-gradient-to-r sm:from-black/85 sm:via-black/35" />
        <div className="relative -mt-24 flex flex-col gap-4 px-4 pb-2 sm:absolute sm:inset-x-0 sm:bottom-0 sm:mt-0 sm:max-w-2xl sm:p-8 lg:p-10">
          {item.Type === "Episode" && item.SeriesId && (
            <Link href={`/item/?id=${item.SeriesId}`} className="caption self-start text-ink-2 hover:text-ink">{item.SeriesName}</Link>
          )}
          {collection && (
            <Link href={`/item/?id=${collection.Id}`} className="caption inline-flex items-center gap-1 self-start text-ink-2 hover:text-ink">
              <Icon name="layers" size={12} />Part of {collection.Name}
            </Link>
          )}
          {mark && item.Type !== "Episode" ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={mark.src} alt="" className="max-h-24 w-auto max-w-[75%] self-start object-contain object-left sm:max-h-32" />
          ) : null}
          <h1 tabIndex={-1} className={mark && item.Type !== "Episode" ? "sr-only" : "font-display text-[34px] leading-none outline-none scrim-text sm:text-[48px]"}>{item.Name}</h1>
          <p className="flex flex-wrap items-center gap-x-3 gap-y-1.5 text-meta text-ink-2">
            {facts.map((f: ReactNode, i: number) => <span key={i}>{f}</span>)}
            <Rating value={item.CommunityRating} className="text-meta" />
            {item.CriticRating != null && <span className="font-semibold tabular-nums">{item.CriticRating}%<span className="sr-only"> critics</span></span>}
          </p>
          <div className="flex flex-wrap items-center gap-2">
            {item.Type !== "BoxSet" && (!series || next) ? (
              <ButtonLink href={playHref(target.Id)} variant="primary" icon="play">{playLabel}</ButtonLink>
            ) : null}
            {progress > 0 && <ButtonLink href={`${playHref(target.Id)}&t=0`} icon="undo">Start over</ButtonLink>}
            {trailer && <ButtonLink href={playHref(trailer.Id)} icon="film">Trailer</ButtonLink>}
            {remote && <Button icon="film" onClick={() => setYoutube(remote)}>Trailer</Button>}
            {item.Type !== "BoxSet" && (
              <IconButton icon="check" label={item.UserData?.Played ? "Mark unwatched" : "Mark watched"} aria-pressed={!!item.UserData?.Played}
                onClick={() => void toggle("played")} className={item.UserData?.Played ? "!bg-white !text-canvas" : ""} />
            )}
            <IconButton icon="heart" label={item.UserData?.IsFavorite ? "Remove from My list" : "Add to My list"} aria-pressed={!!item.UserData?.IsFavorite}
              onClick={() => void toggle("favorite")} className={item.UserData?.IsFavorite ? "!text-bad [&_path]:fill-current" : ""} />
            {manage && (
              <a href={manage} target="_blank" rel="noopener noreferrer" className="inline-flex min-h-11 items-center gap-2 rounded-pill px-3 text-meta font-medium text-ink-2 hover:bg-fill hover:text-ink">
                Manage in modularr<Icon name="external" size={14} /><span className="sr-only">(opens in a new tab)</span>
              </a>
            )}
          </div>
        </div>
      </section>

      <div className="grid gap-8 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        <div className="space-y-3">
          {item.Taglines?.[0] && <p className="text-title font-semibold">{item.Taglines[0]}</p>}
          {item.Overview && <p className="max-w-prose text-body leading-relaxed text-ink-2">{item.Overview}</p>}
        </div>
        <dl className="space-y-3 text-meta">
          {!!item.Genres?.length && (
            <div><dt className="caption mb-1.5">Genres</dt><dd className="flex flex-wrap gap-1.5">{item.Genres.map((g: string) => <Badge key={g}>{g}</Badge>)}</dd></div>
          )}
          {directors.length > 0 && <div><dt className="caption mb-1">Directed by</dt><dd className="text-ink-2">{directors.map((p: Person) => p.Name).join(", ")}</dd></div>}
        </dl>
      </div>

      {series && <Seasons series={item} initial={season} seasons={seasons} episodes={episodes} onToggle={(e: Item) => void togglePlayed(e)} />}
      {item.Type === "BoxSet" && <Collection set={item} />}

      {cast.length > 0 && (
        <section aria-label="Cast">
          <h2 className="mb-3 text-title font-semibold">Cast</h2>
          <ul className="rail -mx-4 flex gap-4 overflow-x-auto px-4 pb-2 lg:-mx-5 lg:px-5">
            {cast.map((p: Person) => (
              <li key={`${p.Id}${p.Role ?? ""}`} className="w-24 shrink-0 text-center sm:w-28">
                <Img art={image(p.Id, "Primary", p.PrimaryImageTag, 240, p.ImageBlurHashes)} icon="user" className="mx-auto aspect-square w-full rounded-full ring-1 ring-white/8" />
                <p className="mt-2 truncate text-meta font-semibold" title={p.Name}>{p.Name}</p>
                {p.Role && <p className="truncate text-caption text-ink-3" title={p.Role}>{p.Role}</p>}
              </li>
            ))}
          </ul>
        </section>
      )}

      <Rail title="More like this" items={similar} />

      {youtube && (
        <Modal title={`${item.Name} · Trailer`} size="lg" onClose={() => setYoutube(null)}>
          <iframe src={`https://www.youtube-nocookie.com/embed/${youtube}?autoplay=1&rel=0`} title={`${item.Name} trailer`}
            allow="autoplay; encrypted-media; picture-in-picture; fullscreen" allowFullScreen referrerPolicy="strict-origin-when-cross-origin"
            className="aspect-video w-full border-0" />
        </Modal>
      )}
    </div>
  );
}

function Collection({ set }: { set: Item }): JSX.Element {
  const { user } = useSession();
  const [items, setItems] = useState<Item[] | null>(null);
  useEffect(() => {
    api.items({ userId: user.Id, ParentId: set.Id, SortBy: "ProductionYear,PremiereDate,SortName", SortOrder: "Ascending" })
      .then((r: QueryResult<Item>) => setItems(r.Items)).catch((): void => setItems([]));
  }, [user.Id, set.Id]);
  return (
    <section aria-label="In this collection">
      <h2 className="mb-3 text-title font-semibold">In this collection</h2>
      <PosterGrid>
        {items ? items.map((i: Item) => <PosterCard key={i.Id} item={i} />) : Array.from({ length: 6 }, (_: unknown, i: number) => <PosterSkeleton key={i} />)}
      </PosterGrid>
    </section>
  );
}

function Seasons({ series, initial, seasons, episodes, onToggle }: {
  series: Item; initial: string | null; seasons: Item[] | null; episodes: Item[] | null; onToggle: (episode: Item) => void;
}): ReactNode {
  const router: ReturnType<typeof useRouter> = useRouter();
  const selected: string | null = initial ?? seasons?.find((s: Item) => (s.UserData?.UnplayedItemCount ?? 0) > 0)?.Id ?? seasons?.[0]?.Id ?? null;

  if (seasons && seasons.length === 0) return null;
  const shown: Item[] | null = episodes && selected ? episodes.filter((e: Item) => e.SeasonId === selected) : null;

  return (
    <section aria-label="Episodes" className="space-y-4">
      {seasons && selected ? (
        <Segmented label="Season" value={selected}
          options={seasons.map((s: Item) => ({ value: s.Id, label: s.Name }))}
          onChange={(id: string) => router.replace(`/item/?id=${series.Id}&season=${id}`, { scroll: false })} />
      ) : <Skeleton className="h-11 w-72 rounded-pill" />}
      <ol className="space-y-2">
        {!shown
          ? Array.from({ length: 4 }, (_: unknown, i: number) => <li key={i}><Skeleton className="h-28 rounded-card" /></li>)
          : shown.map((e: Item) => <Episode key={e.Id} episode={e} onToggle={() => onToggle(e)} />)}
      </ol>
    </section>
  );
}

function Episode({ episode, onToggle }: { episode: Item; onToggle: () => void }): JSX.Element {
  const progress: number = progressOf(episode);
  const played: boolean = !!episode.UserData?.Played;
  return (
    <li className="well group flex gap-3 rounded-card p-2 sm:gap-4 sm:p-2.5">
      <Link href={playHref(episode.Id)} aria-label={`Play episode ${episode.IndexNumber ?? ""}, ${episode.Name}`} className="relative w-36 shrink-0 self-start rounded-chip sm:w-52">
        <Img art={landscape(episode, 480)} icon="tv" className="aspect-video rounded-chip" />
        <span aria-hidden="true" className="absolute inset-0 grid place-items-center opacity-0 transition group-hover:opacity-100">
          <span className="grid size-10 place-items-center rounded-full bg-white text-canvas"><Icon name="play" size={16} fill="currentColor" /></span>
        </span>
        {progress > 0 && progress < 1 && (
          <span aria-hidden="true" className="absolute inset-x-2 bottom-2 h-1 overflow-hidden rounded-full bg-white/25"><span className="block h-full bg-white" style={{ width: `${progress * 100}%` }} /></span>
        )}
      </Link>
      <div className="min-w-0 flex-1 py-1">
        <p className="flex items-baseline gap-2">
          <span className="shrink-0 text-meta font-semibold text-ink-3 tabular-nums">{episode.IndexNumber}</span>
          <span className="truncate text-body font-semibold">{episode.Name}</span>
        </p>
        <p className="mt-0.5 text-meta text-ink-3">{[runtime(episode.RunTimeTicks), episode.PremiereDate ? new Date(episode.PremiereDate).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" }) : null].filter(Boolean).join(" · ")}</p>
        {episode.Overview && <p className="mt-1.5 line-clamp-2 text-meta leading-relaxed text-ink-2 max-sm:hidden">{episode.Overview}</p>}
      </div>
      <IconButton variant="ghost" icon="check" size={16} label={played ? `Mark ${episode.Name} unwatched` : `Mark ${episode.Name} watched`} aria-pressed={played}
        onClick={onToggle} className={`self-center ${played ? "!text-ok" : ""}`} />
    </li>
  );
}

function DetailSkeleton(): JSX.Element {
  return (
    <div className="space-y-6" aria-busy="true">
      <Skeleton className="aspect-[16/10] w-full rounded-panel sm:aspect-[21/9] sm:max-h-[58vh]" />
      <Skeleton className="h-5 w-2/3" />
      <Skeleton className="h-5 w-1/2" />
    </div>
  );
}

export default function ItemPage(): JSX.Element {
  return <Suspense><Detail /></Suspense>;
}
