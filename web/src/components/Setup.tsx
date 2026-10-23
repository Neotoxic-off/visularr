"use client";

import { useEffect, useState } from "react";
import type { FormEvent, JSX } from "react";
import { ApiError, errorMessage } from "@/lib/api";
import * as api from "@/lib/endpoints";
import type { AuthenticationResult, Country, Culture, StartupConfiguration, VirtualFolder } from "@/lib/api";
import { SUGGESTED_LIBRARIES } from "@/lib/libraries";
import type { SuggestedLibrary } from "@/lib/libraries";
import { Ambient } from "@/components/Ambient";
import { FolderPicker } from "@/components/FolderPicker";
import { Icon } from "@/components/icons";
import { Alert, Button, Field, Input, Logo, Select, Spinner, Switch } from "@/components/ui";

const STEPS: readonly string[] = ["Basics", "Admin", "Libraries"];

type Draft = SuggestedLibrary & { enabled: boolean };

// First run: server defaults, the admin account, then the libraries; it finishes signed in.
export function Setup({ onSignedIn }: { onSignedIn: (result: AuthenticationResult) => void }): JSX.Element {
  const [step, setStep] = useState(0);
  const [config, setConfig] = useState<StartupConfiguration | null>(null);
  const [cultures, setCultures] = useState<Culture[]>([]);
  const [countries, setCountries] = useState<Country[]>([]);
  const [name, setName] = useState("");
  const [password, setPassword] = useState("");
  const [libraries, setLibraries] = useState<Draft[]>(SUGGESTED_LIBRARIES.map((l: SuggestedLibrary) => ({ ...l, enabled: true })));
  const [existing, setExisting] = useState<VirtualFolder[]>([]);
  const [picking, setPicking] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([api.startupConfig(), api.cultures(), api.countries(), api.startupUser(), api.libraries()])
      .then(([c, cu, co, user, folders]: [StartupConfiguration, Culture[], Country[], { Name: string }, VirtualFolder[]]) => {
        setConfig({ ...c, ServerName: c.ServerName || "visularr", PreferredMetadataLanguage: c.PreferredMetadataLanguage || "en", MetadataCountryCode: c.MetadataCountryCode || "US" });
        setCultures(cu);
        setCountries(co);
        setName(user.Name);
        setExisting(folders);
      })
      .catch((e: unknown) => setError(errorMessage(e)));
  }, []);

  async function next(event: FormEvent): Promise<void> {
    event.preventDefault();
    if (!config) return;
    setBusy(true);
    setError(null);
    try {
      if (step === 0) await api.saveStartupConfig({ ...config, UICulture: config.UICulture || "en-US" });
      if (step === 1) await api.saveStartupUser(name.trim(), password);
      if (step === 2) {
        // Read fresh: a retry after a failed folder must not add the libraries that already went through again
        // (the server would name the copy "Movies2").
        const current: VirtualFolder[] = await api.libraries();
        const taken: Set<string> = new Set(current.map((f: VirtualFolder) => f.Name.toLowerCase()));
        const takenPaths: Set<string> = new Set(current.flatMap((f: VirtualFolder) => f.Locations));
        for (const library of libraries) {
          if (!library.enabled || taken.has(library.name.toLowerCase()) || takenPaths.has(library.path)) continue;
          try {
            await api.addLibrary(library.name, library.type, [library.path]);
          } catch (e) {
            // A missing folder comes back as a bare 400 "Error processing request."
            if (!(e instanceof ApiError) || e.status !== 400) throw e;
            throw new Error(`${library.name}: ${library.path} was not found on the server. Pick another folder or untick it.`);
          }
        }
        await api.completeStartup();
        onSignedIn(await api.login(name.trim(), password));
        return;
      }
      setStep(step + 1);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  const update = (index: number, change: Partial<Draft>): void =>
    setLibraries((list: Draft[]) => list.map((l: Draft, i: number) => (i === index ? { ...l, ...change } : l)));

  return (
    <main className="grid min-h-dvh place-items-center sm:p-4">
      <Ambient src={null} />
      <form onSubmit={next} className="glass flex min-h-dvh w-full animate-rise flex-col gap-6 rounded-none border-0 p-6 pt-[max(24px,env(safe-area-inset-top))] sm:min-h-0 sm:max-w-lg sm:rounded-panel sm:border sm:p-8">
        <div className="flex items-center justify-between gap-4">
          <Logo />
          <ol className="flex gap-1.5" aria-label="Setup steps">
            {STEPS.map((label: string, i: number) => (
              <li key={label} aria-current={i === step ? "step" : undefined} className={`h-1.5 w-8 rounded-full ${i <= step ? "bg-white" : "bg-fill-3"}`}>
                <span className="sr-only">{label}{i < step ? ", done" : ""}</span>
              </li>
            ))}
          </ol>
        </div>
        <div>
          <p className="caption">Step {step + 1} of {STEPS.length}</p>
          <h1 className="mt-2 font-display text-[36px] leading-none">
            {step === 0 ? "Welcome to visularr." : step === 1 ? "Create the admin." : "Point it at your media."}
          </h1>
          <p className="mt-2 text-meta text-ink-2">
            {step === 0 ? "Name the server and pick the language metadata is fetched in."
              : step === 1 ? "This account manages libraries and API keys."
              : "These follow modularr’s folders. Change a path or switch a library off; you can add more later."}
          </p>
        </div>
        {error && <Alert>{error}</Alert>}

        {!config ? <Spinner className="text-ink-2" /> : step === 0 ? (
          <div className="space-y-5">
            <Field label="Server name">
              <Input value={config.ServerName ?? ""} onChange={(e: React.ChangeEvent<HTMLInputElement>) => setConfig({ ...config, ServerName: e.target.value })} required autoFocus />
            </Field>
            <div className="space-y-2">
              <span className="text-body font-medium">Metadata language</span>
              <Select aria-label="Metadata language" value={config.PreferredMetadataLanguage ?? ""} onChange={(e: { target: { value: string } }) => setConfig({ ...config, PreferredMetadataLanguage: e.target.value })}>
                {cultures.map((c: Culture) => <option key={c.Name} value={c.TwoLetterISOLanguageName}>{c.DisplayName}</option>)}
              </Select>
            </div>
            <div className="space-y-2">
              <span className="text-body font-medium">Country</span>
              <Select aria-label="Country" value={config.MetadataCountryCode ?? ""} onChange={(e: { target: { value: string } }) => setConfig({ ...config, MetadataCountryCode: e.target.value })}>
                {countries.map((c: Country) => <option key={c.Name} value={c.TwoLetterISORegionName}>{c.DisplayName}</option>)}
              </Select>
            </div>
          </div>
        ) : step === 1 ? (
          <div className="space-y-5">
            <Field label="Username">
              <Input value={name} onChange={(e: React.ChangeEvent<HTMLInputElement>) => setName(e.target.value)} autoComplete="username" required autoFocus />
            </Field>
            <Field label="Password" hint="Required: the server is reachable from your network.">
              <Input type="password" value={password} onChange={(e: React.ChangeEvent<HTMLInputElement>) => setPassword(e.target.value)} autoComplete="new-password" required />
            </Field>
          </div>
        ) : (
          <ul className="space-y-2">
            {existing.map((f: VirtualFolder) => (
              <li key={f.ItemId} className="well flex items-center gap-3 rounded-card px-4 py-3">
                <Icon name="check" size={16} className="text-ok" />
                <span className="min-w-0 flex-1"><span className="block font-semibold">{f.Name}</span><span className="block truncate font-mono text-meta text-ink-3">{f.Locations.join(", ")}</span></span>
              </li>
            ))}
            {libraries.map((l: Draft, i: number) => (
              <li key={l.name} className="well flex items-center gap-3 rounded-card py-2 pr-2 pl-4">
                <div className="min-w-0 flex-1">
                  <p className="font-semibold">{l.name}</p>
                  <button type="button" onClick={() => setPicking(i)} className="flex max-w-full cursor-pointer items-center gap-1.5 truncate font-mono text-meta text-ink-2 hover:text-ink">
                    <Icon name="folder" size={14} className="shrink-0" /><span className="truncate">{l.path}</span><span className="sr-only">, change folder</span>
                  </button>
                </div>
                <Switch hideLabel label={`Add ${l.name}`} checked={l.enabled} onChange={(enabled: boolean) => update(i, { enabled })} />
              </li>
            ))}
          </ul>
        )}

        <div className="mt-auto flex justify-between gap-3 sm:mt-2">
          <Button type="button" variant="ghost" disabled={step === 0 || busy} onClick={() => setStep(step - 1)}>Back</Button>
          <Button variant="primary" loading={busy} disabled={!config}>{step === 2 ? "Finish" : "Continue"}</Button>
        </div>
      </form>
      {picking !== null && (
        <FolderPicker start={libraries[picking]?.path ?? ""} onClose={() => setPicking(null)}
          onPick={(path: string) => { update(picking, { path }); setPicking(null); }} />
      )}
    </main>
  );
}
