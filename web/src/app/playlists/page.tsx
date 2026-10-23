"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback, useEffect, useState } from "react";
import type { ChangeEvent, DragEvent, FormEvent, JSX, ReactNode } from "react";
import { episodeCode, errorMessage, itemHref, itemId, landscape, playHref, poster, runtime } from "@/lib/api";
import * as api from "@/lib/endpoints";
import type { Item, Playlist, PlaylistShare, QueryResult, UserDto } from "@/lib/api";
import { saveQueue, shuffled } from "@/lib/queue";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { Img, PosterCard, PosterGrid, PosterSkeleton } from "@/components/Poster";
import { useToast } from "@/components/Toasts";
import type { Toast } from "@/components/Toasts";
import { Alert, Button, Empty, IconButton, Input, Modal, PageHeader, Select, Skeleton, Switch, useAction } from "@/components/ui";
import type { Action } from "@/components/ui";

// Static export has no dynamic segments: /playlists/ lists them, /playlists/?id= opens one.
function Playlists(): JSX.Element {
  const raw: string | null = useSearchParams().get("id");
  const id: string | null = itemId(raw);
  if (raw && !id) return <Empty icon="search" title="This link does not point to a playlist" />;
  return id ? <PlaylistView key={id} id={id} /> : <PlaylistList />;
}

function PlaylistList(): JSX.Element {
  const { user } = useSession();
  const router: ReturnType<typeof useRouter> = useRouter();
  const [lists, setLists] = useState<Item[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  useEffect(() => {
    api.playlists(user.Id).then((r: QueryResult<Item>) => setLists(r.Items)).catch((e: unknown) => setError(errorMessage(e)));
  }, [user.Id]);

  return (
    <>
      <PageHeader title="Playlists" description={lists ? `${lists.length} ${lists.length === 1 ? "playlist" : "playlists"}` : " "}>
        <Button variant="primary" icon="plus" onClick={() => setCreating(true)}>New playlist</Button>
      </PageHeader>
      {error && <div className="mb-4"><Alert>{error}</Alert></div>}
      {lists && lists.length === 0 ? (
        <Empty icon="list" title="No playlists yet">Use “Add to playlist” on any title or episode, or start a new one here.</Empty>
      ) : (
        <PosterGrid>
          {lists ? lists.map((list: Item) => <PosterCard key={list.Id} item={list} />) : Array.from({ length: 6 }, (_: unknown, i: number) => <PosterSkeleton key={i} />)}
        </PosterGrid>
      )}
      {creating && (
        <NameDialog title="New playlist" action="Create" initial="" onClose={() => setCreating(false)}
          onSave={async (name: string) => { const made: { Id: string } = await api.createPlaylist(user.Id, name, []); router.push(`/playlists/?id=${made.Id}`); }} />
      )}
    </>
  );
}

type Dialog = "rename" | "share" | "delete" | null;

function PlaylistView({ id }: { id: string }): JSX.Element {
  const { user } = useSession();
  const router: ReturnType<typeof useRouter> = useRouter();
  const toast: (t: Toast) => void = useToast();
  const [list, setList] = useState<Item | null>(null);
  const [entries, setEntries] = useState<Item[] | null>(null);
  const [owner, setOwner] = useState(false);
  const [canEdit, setCanEdit] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [dialog, setDialog] = useState<Dialog>(null);
  const [dragged, setDragged] = useState<number | null>(null);

  const loadEntries: () => void = useCallback((): void => {
    api.playlistItems(user.Id, id).then((r: QueryResult<Item>) => setEntries(r.Items)).catch((e: unknown) => setError(errorMessage(e)));
  }, [user.Id, id]);

  useEffect(() => {
    api.item(user.Id, id).then(setList).catch((e: unknown) => setError(errorMessage(e)));
    void Promise.resolve().then(loadEntries);
    // The owner sees who it is shared with; a share says whether this user may edit.
    api.playlistUsers(id).then(() => { setOwner(true); setCanEdit(true); }).catch((): void => undefined);
    api.playlistAccess(id, user.Id).then((s: PlaylistShare) => { if (s.CanEdit) setCanEdit(true); }).catch((): void => undefined);
  }, [user.Id, id, loadEntries]);

  const fail = (e: unknown): void => {
    toast({ key: `playlist-${id}`, icon: "alert", tone: "bad", title: "Couldn’t change the playlist", message: errorMessage(e) });
    loadEntries();
  };

  function play(order: Item[]): void {
    const first: Item | undefined = order[0];
    if (!first) return;
    saveQueue(id, order.map((e: Item) => e.Id));
    router.push(playHref(first.Id, id));
  }

  function move(from: number, to: number): void {
    if (!entries || to < 0 || to >= entries.length || from === to) return;
    const moved: Item | undefined = entries[from];
    if (!moved) return;
    const next: Item[] = [...entries];
    next.splice(from, 1);
    next.splice(to, 0, moved);
    setEntries(next);
    api.movePlaylistItem(id, moved.Id, to).catch(fail);
  }

  function remove(entry: Item): void {
    if (!entry.PlaylistItemId) return;
    setEntries((list: Item[] | null) => list?.filter((e: Item) => e.PlaylistItemId !== entry.PlaylistItemId) ?? null);
    api.removeFromPlaylist(id, [entry.PlaylistItemId]).catch(fail);
  }

  if (error) return <Alert>{error}</Alert>;

  return (
    <>
      <PageHeader title={list?.Name ?? " "} description={entries ? `${entries.length} ${entries.length === 1 ? "item" : "items"}` : " "}>
        <div className="flex flex-wrap gap-2">
          <Button variant="primary" icon="play" disabled={!entries?.length} onClick={() => entries && play(entries)}>Play all</Button>
          <Button icon="shuffle" disabled={!entries?.length} onClick={() => entries && play(shuffled(entries))}>Shuffle</Button>
          {canEdit && <IconButton icon="pencil" label="Rename playlist" onClick={() => setDialog("rename")} />}
          {owner && <IconButton icon="share" label="Share playlist" onClick={() => setDialog("share")} />}
          {owner && <IconButton icon="trash" label="Delete playlist" onClick={() => setDialog("delete")} />}
        </div>
      </PageHeader>
      <p className="-mt-3 mb-5"><Link href="/playlists/" className="inline-flex items-center gap-1 text-meta text-ink-2 hover:text-ink"><Icon name="chevronLeft" size={14} />All playlists</Link></p>

      {entries && entries.length === 0 && <Empty icon="list" title="This playlist is empty">Add titles or episodes with “Add to playlist”.</Empty>}
      <ol className="space-y-2" aria-label="Playlist items">
        {!entries
          ? Array.from({ length: 4 }, (_: unknown, i: number) => <li key={i}><Skeleton className="h-20 rounded-card" /></li>)
          : entries.map((entry: Item, i: number) => (
            <Entry key={entry.PlaylistItemId ?? `${entry.Id}-${i}`} entry={entry} index={i} count={entries.length} listId={id} canEdit={canEdit}
              dragging={dragged === i}
              onMove={(to: number) => move(i, to)} onRemove={() => remove(entry)}
              onDragStart={() => setDragged(i)} onDragEnd={() => setDragged(null)}
              onDrop={() => { if (dragged !== null) move(dragged, i); setDragged(null); }} />
          ))}
      </ol>

      {dialog === "rename" && list && (
        <NameDialog title="Rename playlist" action="Save" initial={list.Name} onClose={() => setDialog(null)}
          onSave={async (name: string) => { await api.updatePlaylist(id, { Name: name }); setList({ ...list, Name: name }); }} />
      )}
      {dialog === "share" && <ShareDialog id={id} onClose={() => setDialog(null)} />}
      {dialog === "delete" && list && <DeleteDialog list={list} onClose={() => setDialog(null)} onDeleted={() => router.replace("/playlists/")} />}
    </>
  );
}

function Entry({ entry, index, count, listId, canEdit, dragging, onMove, onRemove, onDragStart, onDragEnd, onDrop }: {
  entry: Item; index: number; count: number; listId: string; canEdit: boolean; dragging: boolean;
  onMove: (to: number) => void; onRemove: () => void; onDragStart: () => void; onDragEnd: () => void; onDrop: () => void;
}): JSX.Element {
  const title: string = entry.Type === "Episode" ? entry.SeriesName ?? entry.Name : entry.Name;
  const line: string = [entry.Type === "Episode" ? `${episodeCode(entry)} · ${entry.Name}` : entry.ProductionYear, runtime(entry.RunTimeTicks)].filter(Boolean).join(" · ");
  return (
    <li draggable={canEdit} onDragStart={(e: DragEvent<HTMLLIElement>) => { e.dataTransfer.effectAllowed = "move"; onDragStart(); }} onDragEnd={onDragEnd}
      onDragOver={(e: DragEvent<HTMLLIElement>) => { if (canEdit) e.preventDefault(); }} onDrop={(e: DragEvent<HTMLLIElement>) => { e.preventDefault(); onDrop(); }}
      className={`well group flex items-center gap-3 rounded-card p-2 transition ${dragging ? "opacity-40" : ""}`}>
      {canEdit && <Icon name="grip" size={18} bold className="shrink-0 cursor-grab text-ink-3 pointer-coarse:hidden" />}
      <span className="w-6 shrink-0 text-right text-meta font-semibold text-ink-3 tabular-nums">{index + 1}</span>
      <Link href={playHref(entry.Id, listId)} aria-label={`Play ${title}`} className="relative w-28 shrink-0 rounded-chip sm:w-36">
        <Img art={landscape(entry, 320) ?? poster(entry, 320)} icon="film" className="aspect-video rounded-chip" />
      </Link>
      <div className="min-w-0 flex-1">
        <Link href={itemHref(entry)} className="block truncate text-body font-semibold hover:underline">{title}</Link>
        <p className="truncate text-meta text-ink-3">{line || " "}</p>
      </div>
      {canEdit && (
        <div className="flex shrink-0 items-center">
          <IconButton variant="ghost" icon="up" size={16} label={`Move ${entry.Name} up`} disabled={index === 0} onClick={() => onMove(index - 1)} />
          <IconButton variant="ghost" icon="down" size={16} label={`Move ${entry.Name} down`} disabled={index === count - 1} onClick={() => onMove(index + 1)} />
          <IconButton variant="ghost" icon="x" size={16} label={`Remove ${entry.Name} from the playlist`} onClick={onRemove} />
        </div>
      )}
    </li>
  );
}

function NameDialog({ title, action: label, initial, onClose, onSave }: {
  title: string; action: string; initial: string; onClose: () => void; onSave: (name: string) => Promise<void>;
}): ReactNode {
  const action: Action = useAction();
  const [name, setName] = useState(initial);
  const submit = (e: FormEvent): void => {
    e.preventDefault();
    const trimmed: string = name.trim();
    if (trimmed) void action.run(async () => { await onSave(trimmed); onClose(); });
  };
  return (
    <Modal title={title} onClose={onClose}
      footer={<><Button variant="ghost" onClick={onClose}>Cancel</Button><Button variant="primary" type="submit" form="playlist-name" loading={action.busy} disabled={!name.trim()}>{label}</Button></>}>
      <form id="playlist-name" onSubmit={submit} className="space-y-3 px-5 pb-5">
        {action.error && <Alert>{action.error}</Alert>}
        <Input autoFocus value={name} maxLength={200} aria-label="Playlist name" placeholder="Playlist name" onChange={(e: ChangeEvent<HTMLInputElement>) => setName(e.target.value)} />
      </form>
    </Modal>
  );
}

type Access = "none" | "view" | "edit";

// Public (anyone signed in can open it) and per-user shares, view or edit.
function ShareDialog({ id, onClose }: { id: string; onClose: () => void }): ReactNode {
  const { user, admin } = useSession();
  const action: Action = useAction();
  const [info, setInfo] = useState<Playlist | null>(null);
  const [people, setPeople] = useState<UserDto[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    api.playlist(id).then(setInfo).catch((e: unknown) => setLoadError(errorMessage(e)));
    api.users(admin).then((list: UserDto[]) => setPeople(list.filter((u: UserDto) => u.Id !== user.Id))).catch((): void => setPeople([]));
  }, [id, admin, user.Id]);

  const access = (userId: string): Access => {
    const share: PlaylistShare | undefined = info?.Shares.find((s: PlaylistShare) => s.UserId.replace(/-/g, "") === userId.replace(/-/g, ""));
    return !share ? "none" : share.CanEdit ? "edit" : "view";
  };
  const setAccess = (userId: string, value: Access): Promise<void> => action.run(async () => {
    if (value === "none") await api.unsharePlaylist(id, userId);
    else await api.sharePlaylist(id, userId, value === "edit");
    setInfo(await api.playlist(id));
  });
  const setPublic = (open: boolean): Promise<void> => action.run(async () => {
    await api.updatePlaylist(id, { IsPublic: open });
    setInfo((i: Playlist | null) => i && { ...i, OpenAccess: open });
  });

  return (
    <Modal title="Share playlist" onClose={onClose} footer={<Button variant="primary" onClick={onClose}>Done</Button>}>
      <div className="space-y-4 px-5 pb-5">
        {(action.error ?? loadError) && <Alert>{action.error ?? loadError}</Alert>}
        <Switch checked={!!info?.OpenAccess} disabled={!info || action.busy} onChange={(v: boolean) => void setPublic(v)}
          label="Public" description="Everyone on this server can open and play it" />
        {people === null ? <Skeleton className="h-11" /> : people.length === 0 ? (
          <p className="text-meta text-ink-3">No other users to share with.</p>
        ) : (
          <ul className="space-y-2" aria-label="Users">
            {people.map((p: UserDto) => (
              <li key={p.Id} className="flex items-center gap-3">
                <span className="min-w-0 flex-1 truncate text-body font-medium">{p.Name}</span>
                <Select aria-label={`Access for ${p.Name}`} value={access(p.Id)} disabled={!info || action.busy} className="w-36"
                  onChange={(e: { target: { value: string } }) => void setAccess(p.Id, e.target.value as Access)}>
                  <option value="none">Not shared</option>
                  <option value="view">Can view</option>
                  <option value="edit">Can edit</option>
                </Select>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Modal>
  );
}

function DeleteDialog({ list, onClose, onDeleted }: { list: Item; onClose: () => void; onDeleted: () => void }): ReactNode {
  const action: Action = useAction();
  return (
    <Modal title="Delete playlist?" description={`“${list.Name}” goes for everyone it is shared with. The titles in it stay in the library.`} onClose={onClose}
      footer={<><Button variant="ghost" onClick={onClose}>Cancel</Button><Button variant="danger" icon="trash" loading={action.busy}
        onClick={() => void action.run(async () => { await api.deletePlaylist(list.Id); onDeleted(); })}>Delete</Button></>}>
      {action.error ? <div className="px-5 pb-5"><Alert>{action.error}</Alert></div> : null}
    </Modal>
  );
}

export default function PlaylistsPage(): JSX.Element {
  return <Suspense><Playlists /></Suspense>;
}
