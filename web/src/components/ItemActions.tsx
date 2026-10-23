"use client";

import { useEffect, useState } from "react";
import type { FormEvent, JSX, ReactNode } from "react";
import { errorMessage } from "@/lib/api";
import * as api from "@/lib/endpoints";
import type { Item, QueryResult, UserItemData } from "@/lib/api";
import { useSession } from "@/components/AuthGate";
import { Icon } from "@/components/icons";
import { useToast } from "@/components/Toasts";
import type { Toast } from "@/components/Toasts";
import { Alert, Button, Input, Menu, MenuItem, Modal, Spinner, useAction } from "@/components/ui";
import type { Action } from "@/components/ui";

type Toggle = "liked" | "favorite";

// Like (thumbs up) and My list (favourite) toggles, one place for both so every surface saves and reports the same way.
export function useUserData(item: Item | null, onChange: (data: UserItemData) => void): (kind: Toggle) => Promise<void> {
  const toast: (t: Toast) => void = useToast();
  return async (kind: Toggle): Promise<void> => {
    if (!item) return;
    try {
      onChange(kind === "liked" ? await api.setLiked(item.Id, !item.UserData?.Likes) : await api.setFavorite(item.Id, !item.UserData?.IsFavorite));
    } catch (e) {
      toast({ key: `toggle-${item.Id}`, icon: "alert", tone: "bad", title: "Couldn’t save that", message: errorMessage(e) });
    }
  };
}

// The "…" menu of a card or an episode row: like, My list, add to a playlist.
export function ItemMenu({ item, onChange, className }: { item: Item; onChange: (data: UserItemData) => void; className?: string }): JSX.Element {
  const [adding, setAdding] = useState(false);
  const toggle: (kind: Toggle) => Promise<void> = useUserData(item, onChange);
  const liked: boolean = !!item.UserData?.Likes;
  const listed: boolean = !!item.UserData?.IsFavorite;
  return (
    <>
      <Menu label={`More for ${item.Name}`} className={className}>
        <MenuItem icon="thumbsUp" onClick={() => void toggle("liked")}>{liked ? "Remove like" : "Like"}</MenuItem>
        {item.Type !== "Episode" && <MenuItem icon="heart" onClick={() => void toggle("favorite")}>{listed ? "Remove from My list" : "Add to My list"}</MenuItem>}
        {item.Type !== "Playlist" && <MenuItem icon="listPlus" onClick={() => setAdding(true)}>Add to playlist…</MenuItem>}
      </Menu>
      {adding && <AddToPlaylist ids={[item.Id]} name={item.Name} onClose={() => setAdding(false)} />}
    </>
  );
}

// Pick a playlist to add to, or name a new one.
export function AddToPlaylist({ ids, name, onClose }: { ids: string[]; name: string; onClose: () => void }): ReactNode {
  const { user } = useSession();
  const toast: (t: Toast) => void = useToast();
  const action: Action = useAction();
  const [lists, setLists] = useState<Item[] | null>(null);
  const [title, setTitle] = useState("");

  useEffect(() => {
    // No list to show is not fatal: a new playlist can still be named.
    api.playlists(user.Id).then((r: QueryResult<Item>) => setLists(r.Items)).catch((): void => setLists([]));
  }, [user.Id]);

  const added = (list: string, id: string): void => {
    toast({ key: `playlist-${id}`, icon: "check", tone: "ok", title: `Added to ${list}`, href: `/playlists/?id=${id}` });
    onClose();
  };
  const add = (list: Item): Promise<void> => action.run(async () => { await api.addToPlaylist(user.Id, list.Id, ids); added(list.Name, list.Id); });
  const create = (e: FormEvent): void => {
    e.preventDefault();
    const named: string = title.trim();
    if (named) void action.run(async () => { added(named, (await api.createPlaylist(user.Id, named, ids)).Id); });
  };

  return (
    <Modal title="Add to playlist" description={name} onClose={onClose}>
      <div className="space-y-4 px-5 pb-5">
        {action.error && <Alert>{action.error}</Alert>}
        {!lists ? <Spinner className="text-ink-2" /> : lists.length > 0 && (
          <ul className="space-y-1" aria-label="Your playlists">
            {lists.map((list: Item) => (
              <li key={list.Id}>
                <button type="button" disabled={action.busy} onClick={() => void add(list)}
                  className="flex min-h-11 w-full cursor-pointer items-center gap-3 rounded-ctl px-3 text-left text-body font-medium text-ink-2 hover:bg-fill-2 hover:text-ink disabled:opacity-40">
                  <Icon name="list" size={16} className="shrink-0" />
                  <span className="min-w-0 flex-1 truncate">{list.Name}</span>
                  {list.ChildCount != null && <span className="text-meta text-ink-3 tabular-nums">{list.ChildCount}</span>}
                </button>
              </li>
            ))}
          </ul>
        )}
        <form onSubmit={create} className="flex gap-2">
          <Input value={title} onChange={(e: React.ChangeEvent<HTMLInputElement>) => setTitle(e.target.value)} placeholder="New playlist name" aria-label="New playlist name" maxLength={200} className="flex-1" />
          <Button type="submit" variant="primary" icon="plus" loading={action.busy} disabled={!title.trim()}>Create</Button>
        </form>
      </div>
    </Modal>
  );
}
