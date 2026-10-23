"use client";

import { useCallback, useEffect, useState } from "react";
import type { ChangeEvent, FormEvent, JSX } from "react";
import { errorMessage } from "@/lib/api";
import * as api from "@/lib/endpoints";
import type { VirtualFolder } from "@/lib/api";
import { kindLabel, LIBRARY_KINDS } from "@/lib/libraries";
import type { LibraryKind } from "@/lib/libraries";
import { FolderPicker } from "@/components/FolderPicker";
import { useShared } from "@/components/Shell";
import { useToast } from "@/components/Toasts";
import type { Toast } from "@/components/Toasts";
import { Alert, Button, Field, IconButton, Input, Section, Select, Skeleton, useAction, WellList } from "@/components/ui";
import type { Action } from "@/components/ui";
import { Confirm } from "@/components/settings/Confirm";

export function Libraries(): JSX.Element {
  const { reload } = useShared();
  const toast: (t: Toast) => void = useToast();
  const [folders, setFolders] = useState<VirtualFolder[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [type, setType] = useState("movies");
  const [path, setPath] = useState("");
  const [picking, setPicking] = useState(false);
  const [removing, setRemoving] = useState<VirtualFolder | null>(null);
  const add: Action = useAction();
  const scan: Action = useAction();

  const load: () => void = useCallback((): void => {
    api.libraries().then((f: VirtualFolder[]) => { setFolders(f); setError(null); }).catch((e: unknown) => setError(errorMessage(e)));
  }, []);
  useEffect(() => { void Promise.resolve().then(load); }, [load]);

  async function submit(event: FormEvent): Promise<void> {
    event.preventDefault();
    await add.run(async () => {
      await api.addLibrary(name.trim(), type, [path.trim()]);
      setName(""); setPath("");
      load();
      reload();
      toast({ key: "library", icon: "check", tone: "ok", title: `${name.trim()} added`, message: "The server is scanning its folder." });
    });
  }

  return (
    <Section id="libraries" title="Libraries" description="Folders on the server that visularr scans for movies and shows."
      actions={<Button size="sm" icon="refresh" loading={scan.busy} onClick={() => void scan.run(async () => {
        await api.scanAll();
        toast({ key: "scan", icon: "refresh", tone: "live", title: "Scanning all libraries" });
      })}>Scan all</Button>}>
      <div className="space-y-6">
        {(error || scan.error) && <Alert>{error ?? scan.error}</Alert>}
        {!folders ? <Skeleton className="h-24 rounded-card" /> : folders.length === 0 ? (
          <p className="text-meta text-ink-3">No libraries yet.</p>
        ) : (
          <WellList label="Libraries">
            {folders.map((f: VirtualFolder) => (
              <li key={f.ItemId} className="flex items-center gap-3 py-2 pr-2 pl-4">
                <div className="min-w-0 flex-1">
                  <p className="font-semibold">{f.Name} <span className="font-normal text-ink-3">· {kindLabel(f.CollectionType)}</span></p>
                  <p className="truncate font-mono text-meta text-ink-3">{f.Locations.join(", ") || "No folder"}</p>
                </div>
                <IconButton variant="ghost" icon="trash" size={16} label={`Remove ${f.Name}`} onClick={() => setRemoving(f)} />
              </li>
            ))}
          </WellList>
        )}

        <form onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
          <h3 className="text-body font-semibold sm:col-span-2">Add a library</h3>
          <Field label="Name">
            <Input value={name} onChange={(e: ChangeEvent<HTMLInputElement>) => setName(e.target.value)} required placeholder="Movies" />
          </Field>
          <div className="space-y-2">
            <span className="text-body font-medium">Content</span>
            <Select aria-label="Content" value={type} onChange={(e: { target: { value: string } }) => setType(e.target.value)}>
              {LIBRARY_KINDS.map((k: LibraryKind) => <option key={k.value} value={k.value}>{k.label}</option>)}
            </Select>
          </div>
          <div className="space-y-2 sm:col-span-2">
            <span className="text-body font-medium">Folder</span>
            <div className="flex gap-2">
              <Input value={path} onChange={(e: ChangeEvent<HTMLInputElement>) => setPath(e.target.value)} required placeholder="/media/movies" aria-label="Folder" className="flex-1 font-mono" />
              <Button type="button" icon="folder" onClick={() => setPicking(true)}>Browse</Button>
            </div>
          </div>
          {add.error && <div className="sm:col-span-2"><Alert>{add.error}</Alert></div>}
          <div className="sm:col-span-2"><Button variant="primary" icon="plus" loading={add.busy}>Add library</Button></div>
        </form>
      </div>
      {picking && <FolderPicker start={path || "/media"} onClose={() => setPicking(false)} onPick={(p: string) => { setPath(p); setPicking(false); }} />}
      {removing && (
        <Confirm title={`Remove ${removing.Name}?`} action="Remove library" onClose={() => setRemoving(null)}
          onConfirm={async () => { await api.removeLibrary(removing.Name); load(); reload(); }}>
          The library and its watch history leave visularr. Files on disk are not touched.
        </Confirm>
      )}
    </Section>
  );
}
