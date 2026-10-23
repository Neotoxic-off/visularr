"use client";

import { useEffect, useState } from "react";
import type { FormEvent, JSX } from "react";
import { errorMessage } from "@/lib/api";
import * as api from "@/lib/endpoints";
import type { FileSystemEntry } from "@/lib/api";
import { Icon } from "@/components/icons";
import { Alert, Button, Input, Modal, Spinner } from "@/components/ui";

interface Listing { path: string | null; entries: FileSystemEntry[] }

// Browses the server's filesystem (the container's, when run in Docker).
export function FolderPicker({ start, onPick, onClose }: { start: string; onPick: (path: string) => void; onClose: () => void }): JSX.Element {
  const [path, setPath] = useState<string | null>(start || null);
  const [typed, setTyped] = useState(start);
  const [listing, setListing] = useState<Listing | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let live: boolean = true;
    (path ? api.directory(path) : api.drives())
      .then((entries: FileSystemEntry[]) => { if (live) { setListing({ path, entries }); setError(null); setTyped(path ?? ""); } })
      // A not-yet-created start folder falls back to the roots.
      .catch((e: unknown) => { if (live) { if (path === start && start) setPath(null); else setError(errorMessage(e)); } });
    return () => { live = false; };
  }, [path, start]);

  async function up(): Promise<void> {
    if (!listing?.path) return;
    try {
      setPath(await api.parentPath(listing.path) || null);
    } catch {
      setPath(null);
    }
  }

  function go(event: FormEvent): void {
    event.preventDefault();
    setPath(typed.trim() || null);
  }

  return (
    <Modal onClose={onClose} title="Choose a folder" description="Folders on the server, not on this device."
      footer={<>
        <Button variant="ghost" onClick={onClose}>Cancel</Button>
        <Button variant="primary" disabled={!listing?.path} onClick={() => listing?.path && onPick(listing.path)}>Use this folder</Button>
      </>}>
      <form onSubmit={go} className="flex gap-2 px-5 pb-3">
        <Input value={typed} onChange={(e: React.ChangeEvent<HTMLInputElement>) => setTyped(e.target.value)} aria-label="Folder path" placeholder="/media" className="flex-1 font-mono" />
        <Button type="submit">Go</Button>
      </form>
      {error && <div className="px-5 pb-3"><Alert>{error}</Alert></div>}
      {!listing ? <div className="p-5"><Spinner /></div> : (
        <ul className="max-h-[50vh] overflow-y-auto border-t border-hair py-1">
          {listing.path !== null && (
            <li>
              <button onClick={up} className="flex min-h-11 w-full cursor-pointer items-center gap-3 px-5 text-body text-ink-2 hover:bg-fill-2">
                <Icon name="up" size={16} />Parent folder
              </button>
            </li>
          )}
          {listing.entries.length === 0 && <li className="px-5 py-3 text-body text-ink-3">No subfolders.</li>}
          {listing.entries.map((d: FileSystemEntry) => (
            <li key={d.Path}>
              <button onClick={() => setPath(d.Path)} className="flex min-h-11 w-full cursor-pointer items-center gap-3 px-5 text-left text-body hover:bg-fill-2">
                <Icon name="folder" size={16} className="shrink-0" /><span className="truncate">{d.Name || d.Path}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </Modal>
  );
}
