import type { About, Boot, LinkKind, ReviewItem, RunEvent, Snapshot, Summary, Theme } from "./types";

// The only way the page talks to the machine. Every method and shape the host accepts is listed here.
interface Methods {
  boot: { params: undefined; result: Boot };
  scan: { params: undefined; result: Snapshot };
  icons: { params: undefined; result: Record<string, string> };
  export: {
    params: { skip: string[]; confirm: string[]; skipSettings: Record<string, string[]> };
    result: { exported: boolean; file?: string };
  };
  showExport: { params: undefined; result: object };
  open: { params: undefined; result: Snapshot | null };
  present: { params: undefined; result: { ids: string[] } };
  install: { params: { skip: string[] }; result: Summary };
  decide: { params: { confirm: string[] }; result: object };
  stop: { params: undefined; result: object };
  link: { params: { localId: string; kind: LinkKind }; result: object };
  about: { params: undefined; result: About };
  setPreferences: { params: { theme: Theme }; result: { theme: Theme } };
  showLog: { params: undefined; result: object };
  log: { params: { message: string }; result: object };
}

interface Events {
  run: RunEvent;
  review: { items: ReviewItem[] };
}

type Method = keyof Methods;
type EventName = keyof Events;

interface WebView {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (e: MessageEvent) => void): void;
}

const host: WebView | undefined = (window as unknown as { chrome?: { webview?: WebView } }).chrome?.webview;

let next = 1;
const pending = new Map<number, { resolve: (v: unknown) => void; reject: (e: Error) => void }>();
const listeners = new Map<EventName, Set<(data: never) => void>>();

function dispatch(name: string, data: unknown) {
  listeners.get(name as EventName)?.forEach((fn) => (fn as (d: unknown) => void)(data));
}

host?.addEventListener("message", (e) => {
  const msg = e.data as { id?: number; result?: unknown; error?: string; event?: string; data?: unknown };
  if (msg.event) return dispatch(msg.event, msg.data);
  const waiter = msg.id === undefined ? undefined : pending.get(msg.id);
  if (!waiter) return;
  pending.delete(msg.id!);
  if (msg.error !== undefined) waiter.reject(new Error(msg.error));
  else waiter.resolve(msg.result);
});

export function on<E extends EventName>(name: E, fn: (data: Events[E]) => void): () => void {
  const set = listeners.get(name) ?? new Set();
  set.add(fn as (data: never) => void);
  listeners.set(name, set);
  return () => set.delete(fn as (data: never) => void);
}

export function call<M extends Method>(method: M, params?: Methods[M]["params"]): Promise<Methods[M]["result"]> {
  if (!host) return dev(method, params) as Promise<Methods[M]["result"]>;

  const id = next++;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve: resolve as (v: unknown) => void, reject });
    host.postMessage({ id, method, params });
  });
}

/** Errors the page catches go to the host's log, so a bug report has something in it. */
export function report(error: unknown) {
  const message = error instanceof Error ? `${error.message}\n${error.stack ?? ""}` : String(error);
  call("log", { message }).catch(() => {});
}

// ---- Dev only ----
// In a plain browser (npm run dev) there's no host. This simulates one so every screen, including a full
// restore with a review step and a failure, can be built and checked without touching the machine.
// Vite drops all of this from production builds.

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));
let devStop = false;
let devDecision: ((ids: string[]) => void) | null = null;

async function dev(method: Method, params: unknown): Promise<unknown> {
  if (!import.meta.env.DEV) throw new Error("Rehome has to run inside its app window.");
  const fixture = async () => (await import("../dev/fixture.json")).default as unknown as Snapshot;

  switch (method) {
    case "boot":
      return { version: "0.1.0-dev", theme: "system" };
    case "scan":
      await sleep(700);
      return fixture();
    case "open":
      await sleep(150);
      return fixture();
    case "export":
      await sleep(600);
      return { exported: true, file: "E:\\Rehome 7-Zip Brave Discord +16 more Installer.exe" };
    case "showExport":
      console.info("show exported file");
      return {};
    case "present": {
      await sleep(900);
      const plan = await fixture();
      return { ids: plan.apps.filter((a) => a.package?.id === "Git.Git").map((a) => a.localId) };
    }
    case "install":
      return devInstall((params as { skip: string[] }).skip);
    case "decide":
      devDecision?.((params as { confirm: string[] }).confirm);
      return {};
    case "stop":
      devStop = true;
      devDecision?.([]);
      return {};
    case "about":
      return { version: "0.1.0-dev", winget: "1.29.380", log: "C:\\Users\\you\\AppData\\Local\\Rehome\\rehome.log" };
    case "setPreferences":
      return { theme: (params as { theme: Theme }).theme };
    case "link":
      console.info("open link", params);
      return {};
    default:
      return {};
  }
}

async function devInstall(skip: string[]): Promise<Summary> {
  devStop = false;
  const plan = await fixture();
  const apps = plan.apps.filter(
    (a) => (a.status === "Ready" || (a.status === "Guess" && a.confirmed)) && !skip.includes(a.localId) && a.package?.id !== "Git.Git",
  );
  const counts: Summary = { installed: 0, present: 0, restart: 0, failed: 0, skipped: 0 };
  const emit = (e: RunEvent) => dispatch("run", e);

  for (const a of apps) {
    if (devStop) break;
    emit({ localId: a.localId, state: "verifying" });
    for (const p of [0.3, 0.7]) {
      await sleep(90);
      emit({ localId: a.localId, state: "verifying", progress: p });
    }
    const unsigned = a.package?.id === "7zip.7zip";
    emit({
      localId: a.localId,
      state: unsigned ? "review" : "verified",
      message: unsigned
        ? undefined
        : a.package?.source === "msstore"
          ? "From the Microsoft Store. Windows checks the Store's signature when it installs."
          : `SHA-256 matches the winget catalog. Signed by ${(a.publisher ?? "the publisher").replace(/\.$/, "")}. From github.com.`,
    });
  }

  const questions = apps.filter((a) => a.package?.id === "7zip.7zip");
  let approved = apps.filter((a) => !questions.includes(a));
  if (questions.length && !devStop) {
    dispatch("review", {
      items: questions.map((q) => ({
        localId: q.localId,
        verdict: "unsigned",
        host: "github.com",
        sha256: "6dc822569e777d9a3ecd5ffcbc22b55600af87e6c5d9810a64e8566cf5a4af9f",
      })),
    });
    const yes = await new Promise<string[]>((r) => (devDecision = r));
    devDecision = null;
    for (const q of questions) {
      if (yes.includes(q.localId)) approved = [q, ...approved];
      else {
        emit({ localId: q.localId, state: "skipped", message: "Skipped" });
        counts.skipped++;
      }
    }
  }

  for (const a of approved) {
    if (devStop) {
      emit({ localId: a.localId, state: "skipped", message: "Stopped" });
      counts.skipped++;
      continue;
    }
    emit({ localId: a.localId, state: "installing" });
    await sleep(500);
    if (a.package?.id === "Docker.DockerDesktop") {
      emit({ localId: a.localId, state: "restart", message: "Installed. Restart to finish." });
      counts.restart++;
    } else if (a.package?.id === "Spotify.Spotify") {
      emit({ localId: a.localId, state: "failed", message: "The app is open. Close it and try again." });
      counts.failed++;
    } else {
      emit({ localId: a.localId, state: "installed" });
      counts.installed++;
    }
  }
  return counts;

  async function fixture() {
    return (await import("../dev/fixture.json")).default as unknown as Snapshot;
  }
}
