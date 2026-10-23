"use client";

import { useEffect, useState } from "react";
import type { ChangeEvent, FormEvent, ReactNode } from "react";
import * as api from "@/lib/endpoints";
import type { Culture, Item } from "@/lib/api";
import { useToast } from "@/components/Toasts";
import type { Toast } from "@/components/Toasts";
import { Alert, Button, Field, Modal, Select, Switch, useAction } from "@/components/ui";
import type { Action } from "@/components/ui";

const FORMATS: ReadonlySet<string> = new Set(["srt", "vtt", "ass", "ssa"]);
const MAX_BYTES: number = 10 * 1024 * 1024;

// The file as base64, which is what Jellyfin's upload takes.
function base64(file: File): Promise<string> {
  return new Promise<string>((resolve: (value: string) => void, reject: (reason: unknown) => void) => {
    const reader: FileReader = new FileReader();
    reader.onload = (): void => resolve(String(reader.result).replace(/^data:[^,]*,/, ""));
    reader.onerror = (): void => reject(reader.error ?? new Error("Couldn’t read the file."));
    reader.readAsDataURL(file);
  });
}

// Adds an external subtitle through Jellyfin's POST /Videos/{id}/Subtitles (admins, or users allowed to manage subtitles).
export function UploadSubtitle({ item, onClose }: { item: Item; onClose: () => void }): ReactNode {
  const toast: (t: Toast) => void = useToast();
  const action: Action = useAction();
  const [file, setFile] = useState<File | null>(null);
  const [languages, setLanguages] = useState<Culture[]>([]);
  const [language, setLanguage] = useState("eng");
  const [forced, setForced] = useState(false);
  const [hearingImpaired, setHearingImpaired] = useState(false);

  useEffect(() => {
    api.cultures().then((list: Culture[]) => {
      const seen: Set<string> = new Set<string>();
      setLanguages(list.filter((c: Culture) => !!c.ThreeLetterISOLanguageName && !seen.has(c.ThreeLetterISOLanguageName) && !!seen.add(c.ThreeLetterISOLanguageName)));
    }).catch((): void => undefined);
  }, []);

  const format: string = file?.name.split(".").pop()?.toLowerCase() ?? "";
  const invalid: string | null = !file ? null
    : !FORMATS.has(format) ? "Pick an .srt, .vtt or .ass file."
      : file.size > MAX_BYTES ? "That file is over 10 MB, too big for a subtitle." : null;

  const submit = (e: FormEvent): void => {
    e.preventDefault();
    if (!file || invalid) return;
    void action.run(async () => {
      await api.uploadSubtitle(item.Id, { Language: language, Format: format, IsForced: forced, IsHearingImpaired: hearingImpaired, Data: await base64(file) });
      toast({ key: `subtitle-${item.Id}`, icon: "check", tone: "ok", title: "Subtitle added", message: "It shows up in the player once the server has refreshed the title." });
      onClose();
    });
  };

  return (
    <Modal title="Add subtitle file" description={item.Name} onClose={onClose}
      footer={<><Button variant="ghost" onClick={onClose}>Cancel</Button><Button variant="primary" icon="upload" type="submit" form="subtitle-upload" loading={action.busy} disabled={!file || !!invalid}>Upload</Button></>}>
      <form id="subtitle-upload" onSubmit={submit} className="space-y-4 px-5 pb-5">
        {(action.error || invalid) && <Alert>{action.error ?? invalid}</Alert>}
        <Field label="File" hint="SubRip (.srt), WebVTT (.vtt) or Advanced SubStation (.ass)">
          <input type="file" accept=".srt,.vtt,.ass,.ssa" onChange={(e: ChangeEvent<HTMLInputElement>) => setFile(e.target.files?.[0] ?? null)}
            className="block w-full text-meta text-ink-2 file:mr-3 file:h-9 file:cursor-pointer file:rounded-pill file:border-0 file:bg-fill-2 file:px-4 file:font-semibold file:text-ink" />
        </Field>
        <Field label="Language">
          <Select aria-label="Language" value={language} onChange={(e: { target: { value: string } }) => setLanguage(e.target.value)}>
            {languages.length === 0 && <option value="eng">English</option>}
            {languages.map((c: Culture) => <option key={c.ThreeLetterISOLanguageName ?? c.Name} value={c.ThreeLetterISOLanguageName ?? ""}>{c.DisplayName}</option>)}
          </Select>
        </Field>
        <Switch checked={forced} onChange={setForced} label="Forced" description="Only the parts in another language" />
        <Switch checked={hearingImpaired} onChange={setHearingImpaired} label="For the hearing impaired" description="Includes sound descriptions" />
      </form>
    </Modal>
  );
}
