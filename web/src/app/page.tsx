"use client";

import { useEffect, useState } from "react";
import type { JSX, ReactNode } from "react";
import { backdrop, episodeCode, itemHref, logo, playHref, progressOf, runtime } from "@/lib/api";
import * as api from "@/lib/endpoints";
import type { Art, Item, QueryResult } from "@/lib/api";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { Img, Rail } from "@/components/Poster";
import { useBackdrop, useShared } from "@/components/Shell";
import { ButtonLink, Rating, Skeleton } from "@/components/ui";

interface Shelf { view: Item; items: Item[] }

interface Rows {
  resume: Item[];
  nextUp: Item[];
  myList: Item[];
  latest: Shelf[];
  collections: Shelf[];
}

const MEDIA_VIEWS: ReadonlySet<string> = new Set(["movies", "tvshows", "homevideos", "mixed", ""]);

export default function Home(): JSX.Element {
  const { user, admin } = useSession();
  const { views } = useShared();
  const [rows, setRows] = useState<Rows | null>(null);

  useEffect(() => {
    const media: Item[] = views.filter((v: Item) => MEDIA_VIEWS.has(v.CollectionType ?? ""));
    const sets: Item[] = views.filter((v: Item) => v.CollectionType === "boxsets");
    const empty: QueryResult<Item> = { Items: [], TotalRecordCount: 0, StartIndex: 0 };
    const none = (view: Item): Shelf => ({ view, items: [] });
    Promise.all([
      api.resume(user.Id).catch((): QueryResult<Item> => empty),
      api.nextUp(user.Id).catch((): QueryResult<Item> => empty),
      api.myList(user.Id).catch((): QueryResult<Item> => empty),
      Promise.all(media.map((view: Item) => api.latest(user.Id, view.Id).then((items: Item[]): Shelf => ({ view, items })).catch(() => none(view)))),
      Promise.all(sets.map((view: Item) => api.items({ userId: user.Id, ParentId: view.Id, SortBy: "DateCreated,SortName", SortOrder: "Descending", Limit: 18 })
        .then((r: QueryResult<Item>): Shelf => ({ view, items: r.Items })).catch(() => none(view)))),
    ]).then(([resume, nextUp, myList, latest, collections]: [QueryResult<Item>, QueryResult<Item>, QueryResult<Item>, Shelf[], Shelf[]]) =>
      setRows({ resume: resume.Items, nextUp: nextUp.Items, myList: myList.Items, latest, collections }));
  }, [user.Id, views]);

  // The hero: what you were watching, else the newest thing with artwork.
  const featured: Item | undefined = rows ? [...rows.resume, ...rows.latest.flatMap((l: Shelf) => l.items)].find((i: Item) => backdrop(i)) : undefined;
  const nothing: boolean = !!rows && !featured && rows.resume.length === 0 && rows.nextUp.length === 0 && rows.latest.every((l: Shelf) => l.items.length === 0);

  return (
    <div className="space-y-10">
      <h1 tabIndex={-1} className="sr-only">Home</h1>
      {!rows ? <Skeleton className="aspect-[16/9] max-h-[62vh] w-full rounded-panel sm:aspect-[21/9]" /> : featured && <Hero key={featured.Id} item={featured} />}
      {nothing && (
        <div className="glass flex flex-col items-center rounded-tile px-6 py-12 text-center">
          <span className="mb-4 grid size-12 place-items-center rounded-full bg-fill-2 text-ink-2"><Icon name="film" size={20} /></span>
          <p className="text-title font-semibold">Nothing to watch yet</p>
          <p className="mt-1.5 max-w-sm text-meta text-ink-2">{admin ? "Add a library in Settings, or let modularr fill your folders." : "Your libraries are empty for now."}</p>
          {admin && <ButtonLink href="/settings/#libraries" className="mt-4" icon="plus">Add a library</ButtonLink>}
        </div>
      )}
      <Rail title="Continue watching" wide items={rows?.resume ?? []} loading={!rows} />
      <Rail title="Next up" wide items={rows?.nextUp ?? []} loading={!rows} />
      <Rail title="My list" items={rows?.myList ?? []} />
      {rows?.latest.map(({ view, items }: Shelf) => (
        <Rail key={view.Id} title={`New in ${view.Name}`} href={`/library/?id=${view.Id}`} items={items} />
      ))}
      {rows?.collections.map(({ view, items }: Shelf) => (
        <Rail key={view.Id} title={view.Name || "Collections"} href={`/library/?id=${view.Id}`} items={items} />
      ))}
    </div>
  );
}

function Hero({ item: card }: { item: Item }): JSX.Element {
  const { user } = useSession();
  // Cards carry no overview or logo: the hero fetches its one title with them.
  const [full, setFull] = useState<Item | null>(null);
  useEffect(() => {
    api.featured(user.Id, card.Id).then((i: Item | undefined) => setFull(i ?? null)).catch((): void => undefined);
  }, [user.Id, card.Id]);
  const item: Item = full ?? card;
  const art: Art | null = backdrop(item, 1920);
  const mark: Art | null = logo(item);
  useBackdrop(art?.src ?? null);
  const progress: number = progressOf(item);
  const title: string = item.Type === "Episode" ? item.SeriesName ?? item.Name : item.Name;
  const meta: ReactNode[] = [
    item.Type === "Episode" ? `${episodeCode(item)} · ${item.Name}` : item.ProductionYear,
    item.OfficialRating,
    runtime(item.RunTimeTicks),
  ].filter(Boolean);
  return (
    <section aria-label="Featured" className="relative isolate overflow-hidden rounded-panel ring-1 ring-white/8">
      <Img art={art} eager sizes="100vw" className="aspect-[4/5] w-full sm:aspect-[21/9] sm:max-h-[62vh]" />
      <div aria-hidden="true" className="absolute inset-0 bg-gradient-to-t from-black via-black/40 to-transparent sm:bg-gradient-to-r sm:from-black/85 sm:via-black/30" />
      <div className="absolute inset-x-0 bottom-0 flex max-w-2xl flex-col gap-3 p-5 sm:p-8 lg:p-10">
        {progress > 0 && <p className="caption text-ink-2">Continue watching</p>}
        {mark ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={mark.src} alt={title} className="max-h-24 w-auto max-w-[70%] self-start object-contain object-left sm:max-h-32" />
        ) : <h2 className="font-display text-[34px] leading-none scrim-text sm:text-[48px]">{title}</h2>}
        <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-meta text-ink-2">
          {meta.map((m: ReactNode, i: number) => <span key={i}>{m}</span>)}
          <Rating value={item.CommunityRating} className="text-meta" />
        </p>
        {item.Overview && <p className="line-clamp-2 max-w-xl text-body text-ink-2 scrim-text sm:line-clamp-3">{item.Overview}</p>}
        <div className="mt-1 flex flex-wrap gap-2">
          <ButtonLink href={playHref(item.Id)} variant="primary" icon="play">{progress > 0 ? "Resume" : "Play"}</ButtonLink>
          {progress > 0 && <ButtonLink href={`${playHref(item.Id)}&t=0`} icon="undo">Start over</ButtonLink>}
          <ButtonLink href={item.Type === "Episode" && item.SeriesId ? `/item/?id=${item.SeriesId}` : itemHref(item)} icon="info">More info</ButtonLink>
        </div>
      </div>
      {progress > 0 && (
        <span aria-hidden="true" className="absolute inset-x-0 bottom-0 h-1 bg-white/20"><span className="block h-full bg-white" style={{ width: `${progress * 100}%` }} /></span>
      )}
    </section>
  );
}
