"use client";

import { createContext, useCallback, useContext, useEffect, useState } from "react";
import type { Context, FormEvent, JSX, ReactNode } from "react";
import { errorMessage, session, UNAUTHORIZED } from "@/lib/api";
import * as api from "@/lib/endpoints";
import type { AuthenticationResult, PublicSystemInfo, UserDto } from "@/lib/api";
import { Ambient } from "@/components/Ambient";
import { Setup } from "@/components/Setup";
import { Alert, Button, Field, Input, Logo, Spinner } from "@/components/ui";

export interface Session {
  user: UserDto;
  admin: boolean;
  server: PublicSystemInfo;
  // After the user changes their own picture.
  refreshUser: () => void;
  logout: () => Promise<void>;
}

const SessionContext: Context<Session | null> = createContext<Session | null>(null);

export function useSession(): Session {
  const value: Session | null = useContext(SessionContext);
  if (!value) throw new Error("useSession outside AuthGate");
  return value;
}

type State =
  | { phase: "loading" }
  | { phase: "error"; message: string }
  | { phase: "setup"; server: PublicSystemInfo }
  | { phase: "login"; server: PublicSystemInfo }
  | { phase: "ready"; server: PublicSystemInfo; user: UserDto };

// The whole app is behind this gate: first-run setup, then sign-in, then the session.
export function AuthGate({ children }: { children: ReactNode }): JSX.Element {
  const [state, setState] = useState<State>({ phase: "loading" });
  const [notice, setNotice] = useState<string | null>(null);

  const refresh: () => Promise<void> = useCallback(async (): Promise<void> => {
    try {
      const server: PublicSystemInfo = await api.publicInfo();
      if (server.StartupWizardCompleted === false) return setState({ phase: "setup", server });
      if (!session.token()) return setState({ phase: "login", server });
      try {
        setState({ phase: "ready", server, user: await api.me() });
      } catch {
        session.clear();
        setState({ phase: "login", server });
      }
    } catch (e) {
      setState({ phase: "error", message: errorMessage(e) });
    }
  }, []);

  useEffect(() => {
    // Async on purpose: the first state comes from the server, not from render.
    void Promise.resolve().then(refresh);
    const signedOut = (): void => void refresh();
    window.addEventListener(UNAUTHORIZED, signedOut);
    return () => window.removeEventListener(UNAUTHORIZED, signedOut);
  }, [refresh]);

  const signedIn: (result: AuthenticationResult) => void = useCallback((result: AuthenticationResult): void => {
    session.save(result);
    setNotice(null);
    void refresh();
  }, [refresh]);

  const logout: () => Promise<void> = useCallback(async (): Promise<void> => {
    // Clear locally either way, but say so when the server may still honour the token.
    const ok: boolean = await api.logout().then((): boolean => true, (): boolean => false);
    session.clear();
    setNotice(ok ? null : "Signed out here; the server could not be reached, the session may still be valid.");
    await refresh();
  }, [refresh]);

  const refreshUser: () => void = useCallback((): void => {
    api.me().then((user: UserDto) => setState((s: State) => (s.phase === "ready" ? { ...s, user } : s))).catch((): void => undefined);
  }, []);

  if (state.phase === "error") return <Centered><Alert>Can’t reach the visularr server: {state.message}</Alert></Centered>;
  if (state.phase === "loading") return <Centered><Spinner className="text-ink-2" /></Centered>;
  if (state.phase === "setup") return <Setup onSignedIn={signedIn} />;
  if (state.phase === "login") return <Login notice={notice} onSignedIn={signedIn} />;

  const value: Session = { user: state.user, admin: !!state.user.Policy?.IsAdministrator, server: state.server, refreshUser, logout };
  return <SessionContext value={value}>{children}</SessionContext>;
}

// Same sign-in card as modularr and linkarr: one account, three apps.
function Login({ notice, onSignedIn }: { notice: string | null; onSignedIn: (result: AuthenticationResult) => void }): JSX.Element {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(notice);

  async function submit(event: FormEvent): Promise<void> {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      onSignedIn(await api.login(username, password));
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="grid min-h-dvh place-items-center sm:p-4">
      <Ambient src={null} />
      <form onSubmit={submit} className="glass flex min-h-dvh w-full animate-rise flex-col justify-center gap-5 rounded-none border-0 p-6 pt-[max(24px,env(safe-area-inset-top))] sm:min-h-0 sm:max-w-sm sm:rounded-panel sm:border sm:p-8">
        <div className="space-y-6">
          <Logo />
          <div>
            <h1 className="font-display text-[40px] leading-none">Welcome back.</h1>
            <p className="mt-2 text-meta text-ink-2">Sign in with your linkarr account.</p>
          </div>
        </div>
        {error && <Alert>{error}</Alert>}
        <Field label="Username">
          <Input value={username} onChange={(e: React.ChangeEvent<HTMLInputElement>) => setUsername(e.target.value)} autoComplete="username" autoFocus required />
        </Field>
        <Field label="Password">
          <Input type="password" value={password} onChange={(e: React.ChangeEvent<HTMLInputElement>) => setPassword(e.target.value)} autoComplete="current-password" required />
        </Field>
        <Button variant="primary" className="w-full" loading={busy}>Sign in</Button>
      </form>
    </main>
  );
}

export function Centered({ children }: { children: ReactNode }): JSX.Element {
  return <div className="flex min-h-dvh items-center justify-center p-4"><Ambient src={null} />{children}</div>;
}
