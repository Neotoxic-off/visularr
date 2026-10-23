"use client";

import { useCallback, useEffect, useState } from "react";
import type { ChangeEvent, FormEvent, JSX } from "react";
import { errorMessage } from "@/lib/api";
import * as api from "@/lib/endpoints";
import type { ApiKey, QueryResult } from "@/lib/api";
import { useToast } from "@/components/Toasts";
import type { Toast } from "@/components/Toasts";
import { Alert, Button, IconButton, Input, Section, Skeleton, useAction, WellList } from "@/components/ui";
import type { Action } from "@/components/ui";
import { Confirm } from "@/components/settings/Confirm";

// Keys for other apps: linkarr provisions users with one, modularr asks for rescans after an import.
export function ApiKeys(): JSX.Element {
  const toast: (t: Toast) => void = useToast();
  const [keys, setKeys] = useState<ApiKey[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [app, setApp] = useState("linkarr");
  const [revoking, setRevoking] = useState<ApiKey | null>(null);
  const create: Action = useAction();

  const load: () => void = useCallback((): void => {
    api.keys().then((r: QueryResult<ApiKey>) => { setKeys(r.Items); setError(null); }).catch((e: unknown) => setError(errorMessage(e)));
  }, []);
  useEffect(() => { void Promise.resolve().then(load); }, [load]);

  async function submit(event: FormEvent): Promise<void> {
    event.preventDefault();
    await create.run(async () => {
      await api.createKey(app.trim());
      load();
    });
  }

  async function copy(key: ApiKey): Promise<void> {
    try {
      await navigator.clipboard.writeText(key.AccessToken);
      toast({ key: "copy", icon: "copy", tone: "ok", title: "Key copied" });
    } catch {
      toast({ key: "copy", icon: "alert", tone: "bad", title: "Couldn’t copy", message: "Select the key and copy it by hand." });
    }
  }

  return (
    <Section id="keys" title="API keys" description="Give a key to linkarr so it can manage accounts, and to modularr so it can ask visularr to scan new imports.">
      <div className="space-y-6">
        {error && <Alert>{error}</Alert>}
        {!keys ? <Skeleton className="h-16 rounded-card" /> : keys.length === 0 ? (
          <p className="text-meta text-ink-3">No keys yet.</p>
        ) : (
          <WellList label="API keys">
            {keys.map((k: ApiKey) => (
              <li key={k.AccessToken} className="flex items-center gap-3 py-2 pr-2 pl-4">
                <div className="min-w-0 flex-1">
                  <p className="font-semibold">{k.AppName || "Unnamed"}</p>
                  <p className="truncate font-mono text-meta text-ink-3 select-all">{k.AccessToken}</p>
                </div>
                <IconButton variant="ghost" icon="copy" size={16} label={`Copy the ${k.AppName ?? ""} key`} onClick={() => void copy(k)} />
                <IconButton variant="ghost" icon="trash" size={16} label={`Revoke the ${k.AppName ?? ""} key`} onClick={() => setRevoking(k)} />
              </li>
            ))}
          </WellList>
        )}
        <form onSubmit={submit} className="flex max-w-xl flex-wrap items-end gap-2">
          <label className="min-w-48 flex-1 space-y-2">
            <span className="block text-body font-medium">App name</span>
            <Input value={app} onChange={(e: ChangeEvent<HTMLInputElement>) => setApp(e.target.value)} required />
          </label>
          <Button variant="primary" icon="key" loading={create.busy}>Create key</Button>
        </form>
        {create.error && <Alert>{create.error}</Alert>}
      </div>
      {revoking && (
        <Confirm title={`Revoke the ${revoking.AppName || "unnamed"} key?`} action="Revoke key" onClose={() => setRevoking(null)}
          onConfirm={async () => { await api.deleteKey(revoking.AccessToken); load(); }}>
          Anything using this key loses access straight away.
        </Confirm>
      )}
    </Section>
  );
}
