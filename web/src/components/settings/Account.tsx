"use client";

import { useEffect, useRef, useState } from "react";
import type { ChangeEvent, FormEvent, JSX, RefObject } from "react";
import { userImage } from "@/lib/api";
import * as api from "@/lib/endpoints";
import type { Managed } from "@/lib/api";
import { useSession } from "@/components/AuthGate";
import { Confirm } from "@/components/settings/Confirm";
import { useToast } from "@/components/Toasts";
import type { Toast } from "@/components/Toasts";
import { Alert, Avatar, Button, Field, Input, Section, useAction } from "@/components/ui";
import type { Action } from "@/components/ui";

const MAX_AVATAR_BYTES: number = 5 * 1024 * 1024;

// The server takes the picture as base64 text with the image's own content type.
function readBase64(file: File): Promise<string> {
  return new Promise((resolve: (value: string) => void, reject: (reason: unknown) => void) => {
    const reader: FileReader = new FileReader();
    reader.onload = (): void => resolve(String(reader.result).split(",")[1] ?? "");
    reader.onerror = (): void => reject(reader.error);
    reader.readAsDataURL(file);
  });
}

export function Account({ hub }: { hub: Managed | null }): JSX.Element {
  const { user, refreshUser } = useSession();
  const toast: (t: Toast) => void = useToast();
  const file: RefObject<HTMLInputElement | null> = useRef<HTMLInputElement>(null);
  const picture: Action = useAction();
  const [confirming, setConfirming] = useState(false);
  const [code, setCode] = useState("");
  const [quickEnabled, setQuickEnabled] = useState(false);

  useEffect(() => {
    api.quickConnectEnabled().then(setQuickEnabled).catch((): void => setQuickEnabled(false));
  }, []);

  async function upload(event: ChangeEvent<HTMLInputElement>): Promise<void> {
    const chosen: File | undefined = event.target.files?.[0];
    event.target.value = "";
    if (!chosen) return;
    await picture.run(async () => {
      if (!chosen.type.startsWith("image/")) throw new Error("Pick an image file.");
      if (chosen.size > MAX_AVATAR_BYTES) throw new Error("Pick an image under 5 MB.");
      await api.uploadAvatar(user.Id, chosen.type, await readBase64(chosen));
      refreshUser();
    });
  }

  // The server does not say which device asked, so the user confirms the code is from their own screen.
  async function authorize(): Promise<void> {
    const ok: boolean = await api.quickConnectAuthorize(code.trim(), user.Id);
    if (!ok) throw new Error("That code was not accepted.");
    setCode("");
    toast({ key: "quick", icon: "check", tone: "ok", title: "Device signed in" });
  }

  const avatar: string | null = userImage(user, 192);

  return (
    <Section id="account" title="Account" description={`Signed in as ${user.Name}.`}>
      <div className="space-y-8">
        <div className="flex flex-wrap items-center gap-4">
          <Avatar name={user.Name} src={avatar} className="size-20 text-[32px]" />
          <div className="flex flex-wrap gap-2">
            <input ref={file} type="file" accept="image/*" className="sr-only" tabIndex={-1} aria-hidden="true" onChange={(e: ChangeEvent<HTMLInputElement>) => void upload(e)} />
            <Button size="sm" icon="user" loading={picture.busy} onClick={() => file.current?.click()}>{avatar ? "Change picture" : "Add a picture"}</Button>
            {avatar && <Button size="sm" variant="ghost" onClick={() => void picture.run(async () => { await api.removeAvatar(user.Id); refreshUser(); })}>Remove</Button>}
          </div>
          {picture.error && <div className="w-full"><Alert>{picture.error}</Alert></div>}
        </div>

        {hub?.url && (
          <p className="max-w-xl text-body text-ink-2">
            Your account is managed in linkarr.{" "}
            <a href={hub.url} target="_blank" rel="noopener noreferrer" className="font-semibold text-ink hover:underline">
              Open linkarr<span className="sr-only"> (opens in a new tab)</span>
            </a>
          </p>
        )}

        {quickEnabled && (
          <form onSubmit={(e: FormEvent) => { e.preventDefault(); setConfirming(true); }} className="grid max-w-xl gap-4">
            <h3 className="text-body font-semibold">Quick Connect</h3>
            <Field label="Code shown on the other device">
              <Input value={code} onChange={(e: ChangeEvent<HTMLInputElement>) => setCode(e.target.value)} inputMode="numeric" autoComplete="one-time-code" required className="tracking-[0.3em] tabular-nums" />
            </Field>
            <div><Button icon="bolt">Sign that device in</Button></div>
          </form>
        )}
        {confirming && (
          <Confirm title="Sign in another device?" action="Sign device in" onClose={() => setConfirming(false)} onConfirm={authorize}>
            <p>This signs a device in as {user.Name}. Only enter a code shown on your own screen.</p>
          </Confirm>
        )}
      </div>
    </Section>
  );
}
