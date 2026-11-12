import { Component, useCallback, useEffect, useRef, useState } from "react";
import { Logo } from "./brand";
import { call, openDropped, report } from "./bridge";
import { DropToOpen } from "./drop";
import { NoticeProvider, useNotices } from "./notices";
import { RestoreView } from "./RestoreView";
import { ScanView } from "./ScanView";
import type { About as AboutInfo, Snapshot, Theme } from "./types";

type View = { kind: "start" } | { kind: "scan"; n: number } | { kind: "restore"; plan: Snapshot; n: number };
type Dialog = "about" | "preferences" | null;
const themes: Theme[] = ["system", "light", "dark"];

export function App() {
  return (
    <NoticeProvider>
      <Shell />
    </NoticeProvider>
  );
}

/** Screen changes cross-fade where the web view supports it. Reduced motion gets an instant swap. */
function transition(update: () => void) {
  const doc = document as Document & { startViewTransition?: (cb: () => void) => unknown };
  if (!doc.startViewTransition || matchMedia("(prefers-reduced-motion: reduce)").matches) update();
  else doc.startViewTransition(update);
}

function Shell() {
  const [view, setViewNow] = useState<View>({ kind: "start" });
  const [dialog, setDialog] = useState<Dialog>(null);
  const [theme, setTheme] = useState<Theme>("system");
  const [booted, setBooted] = useState(false);
  const { notify, fail } = useNotices();
  const setView = useCallback((next: View | ((v: View) => View)) => transition(() => setViewNow(next)), []);

  useEffect(() => {
    call("boot").then(
      (b) => {
        setTheme(b.theme);
        if (b.plan) setViewNow({ kind: "restore", plan: b.plan, n: 1 });
        if (b.error) fail(b.error);
        setBooted(true);
      },
      (e: Error) => {
        fail(e);
        setBooted(true);
      },
    );
  }, [fail]);

  const home = useCallback(() => setView({ kind: "start" }), [setView]);
  const scan = useCallback(() => setView((v) => ({ kind: "scan", n: v.kind === "scan" ? v.n + 1 : 1 })), [setView]);
  const restore = useCallback(
    (plan: Snapshot) => setView((v) => ({ kind: "restore", plan, n: v.kind === "restore" ? v.n + 1 : 1 })),
    [setView],
  );
  const open = useCallback(async () => {
    try {
      const plan = await call("open");
      if (plan) restore(plan);
    } catch (e) {
      fail(e);
    }
  }, [restore, fail]);
  const dropped = useCallback(
    async (file: File) => {
      try {
        restore(await openDropped([file]));
      } catch (e) {
        fail(e);
      }
    },
    [restore, fail],
  );
  const refuse = useCallback((title: string) => notify({ level: "warning", title }), [notify]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!e.ctrlKey) return;
      const key = e.key.toLowerCase();
      if (key === "o") {
        e.preventDefault();
        void open();
      } else if (key === ",") {
        e.preventDefault();
        setDialog("preferences");
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open]);

  const menu = <Menu onScan={scan} onOpen={() => void open()} onDialog={setDialog} />;

  if (!booted) return null;

  return (
    <>
      {view.kind === "start" && <Start onScan={scan} onOpen={() => void open()} menu={menu} />}
      {view.kind === "scan" && <ScanView menu={menu} rescan={view.n} onBack={home} />}
      {view.kind === "restore" && <RestoreView key={view.n} plan={view.plan} menu={menu} onBack={home} />}
      {dialog === "about" && <About onClose={() => setDialog(null)} onError={fail} />}
      {dialog === "preferences" && <Preferences theme={theme} onTheme={setTheme} onClose={() => setDialog(null)} onError={fail} />}
      <DropToOpen onFile={(f) => void dropped(f)} onRefuse={refuse} />
    </>
  );
}

function Start({ onScan, onOpen, menu }: { onScan: () => void; onOpen: () => void; menu: React.ReactNode }) {
  return (
    <main>
      <header className="bare">
        <span className="spacer" />
        {menu}
      </header>
      <div className="start">
        <div className="intro">
          <Logo size={72} className="mark" />
          <h1>ReHome</h1>
          <p>Your apps on a fresh Windows, a new laptop or a VM</p>
        </div>
        <div className="choices">
          <button className="choice" onClick={onScan} autoFocus>
            <ScanGlyph />
            <span>Scan this PC</span>
            <small>On your old PC</small>
          </button>
          <button className="choice" onClick={onOpen}>
            <OpenGlyph />
            <span>Restore from a scan</span>
            <small>On the new PC or VM</small>
          </button>
        </div>
        <ol className="how" aria-label="How it works">
          <li>Scan your old PC and pick the apps to bring</li>
          <li>Export one ReHome.exe to a USB drive</li>
          <li>Open it on the new PC and press Install</li>
        </ol>
        <p className="fine">
          Installs only from winget and the Microsoft Store. Every installer is checked first.
          <br />
          Already have the installer from your old PC? Drop it on this window.
        </p>
      </div>
    </main>
  );
}

function ScanGlyph() {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <rect x="3" y="4" width="18" height="12" rx="2" />
      <path d="M8 20h8M12 16v4" />
    </svg>
  );
}

function OpenGlyph() {
  return (
    <svg viewBox="0 0 24 24" aria-hidden="true">
      <path d="M12 4v11M7 10l5 5 5-5" />
      <path d="M4 17v2a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-2" />
    </svg>
  );
}

function Menu({ onScan, onOpen, onDialog }: { onScan: () => void; onOpen: () => void; onDialog: (d: Dialog) => void }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    ref.current?.querySelector<HTMLElement>("[role=menuitem]")?.focus();
    const away = (e: MouseEvent) => !ref.current?.parentElement?.contains(e.target as Node) && setOpen(false);
    window.addEventListener("mousedown", away);
    return () => window.removeEventListener("mousedown", away);
  }, [open]);

  const pick = (fn: () => void) => () => {
    setOpen(false);
    button.current?.focus();
    fn();
  };

  const items: [string, string, () => void][] = [
    ["Scan this PC", "", onScan],
    ["Restore from a scan…", "Ctrl+O", onOpen],
    ["Preferences", "Ctrl+,", () => onDialog("preferences")],
    ["About ReHome", "", () => onDialog("about")],
  ];

  return (
    <div className="menu-anchor">
      <button
        ref={button}
        className="ghost icon-button"
        aria-label="Menu"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen(!open)}
      >
        <svg viewBox="0 0 16 16" aria-hidden="true">
          <circle cx="3" cy="8" r="1.3" />
          <circle cx="8" cy="8" r="1.3" />
          <circle cx="13" cy="8" r="1.3" />
        </svg>
      </button>
      {open && (
        <div
          className="menu"
          role="menu"
          ref={ref}
          onKeyDown={(e) => {
            const all = Array.from(ref.current?.querySelectorAll<HTMLElement>("[role=menuitem]") ?? []);
            const at = all.indexOf(document.activeElement as HTMLElement);
            if (e.key === "Escape") {
              setOpen(false);
              button.current?.focus();
            } else if (e.key === "ArrowDown" || e.key === "ArrowUp") {
              e.preventDefault();
              all[(at + (e.key === "ArrowDown" ? 1 : all.length - 1)) % all.length]?.focus();
            } else if (e.key === "Home" || e.key === "End") {
              e.preventDefault();
              all[e.key === "Home" ? 0 : all.length - 1]?.focus();
            } else if (e.key === "Tab") setOpen(false);
          }}
        >
          {items.map(([label, keys, fn], i) => (
            <div key={label}>
              {i === 2 && <div className="sep" role="separator" />}
              <button role="menuitem" tabIndex={-1} onClick={pick(fn)}>
                <span>{label}</span>
                {keys && <kbd>{keys}</kbd>}
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: React.ReactNode }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    ref.current?.querySelector<HTMLElement>("button:not([tabindex='-1']), [tabindex='0']")?.focus();
    return () => previous?.focus();
  }, []);
  return (
    <div className="scrim" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div
        className="dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="dialog-title"
        ref={ref}
        onKeyDown={(e) => e.key === "Escape" && onClose()}
      >
        <h2 id="dialog-title">{title}</h2>
        {children}
        <div className="actions">
          <button onClick={onClose}>Done</button>
        </div>
      </div>
    </div>
  );
}

function Preferences({ theme, onTheme, onClose, onError }: {
  theme: Theme;
  onTheme: (t: Theme) => void;
  onClose: () => void;
  onError: (e: unknown) => void;
}) {
  const set = (t: Theme) => {
    onTheme(t);
    call("setPreferences", { theme: t }).catch(onError);
  };
  return (
    <Modal title="Preferences" onClose={onClose}>
      <div className="field">
        <span id="appearance">Appearance</span>
        <div
          className="segmented"
          role="radiogroup"
          aria-labelledby="appearance"
          onKeyDown={(e) => {
            if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
            e.preventDefault();
            const next = themes[(themes.indexOf(theme) + (e.key === "ArrowRight" ? 1 : themes.length - 1)) % themes.length];
            set(next);
            e.currentTarget.querySelector<HTMLElement>(`[data-theme="${next}"]`)?.focus();
          }}
        >
          {themes.map((t) => (
            <button
              key={t}
              role="radio"
              data-theme={t}
              aria-checked={theme === t}
              tabIndex={theme === t ? 0 : -1}
              className={theme === t ? "on" : ""}
              onClick={() => set(t)}
            >
              {t[0].toUpperCase() + t.slice(1)}
            </button>
          ))}
        </div>
      </div>
      <div className="field">
        <span>Log</span>
        <button onClick={() => call("showLog").catch(onError)}>Show in Explorer</button>
      </div>
    </Modal>
  );
}

function About({ onClose, onError }: { onClose: () => void; onError: (e: unknown) => void }) {
  const [info, setInfo] = useState<AboutInfo | null>(null);
  useEffect(() => {
    call("about").then(setInfo, onError);
  }, [onError]);
  return (
    <Modal title="ReHome" onClose={onClose}>
      <Logo size={48} className="about-mark" />
      <dl className="about">
        <dt>Version</dt>
        <dd>{info?.version ?? "…"}</dd>
        <dt>winget</dt>
        <dd>{info ? info.winget ?? "Not installed" : "…"}</dd>
      </dl>
      <ul className="trust">
        <li>Installs only from winget and the Microsoft Store.</li>
        <li>Checks every installer's hash and signer before it runs.</li>
        <li>Sends nothing anywhere. Scans stay on your PCs.</li>
      </ul>
    </Modal>
  );
}

/** Last line of defence. A render error shows a way out instead of a blank window, and lands in the log. */
export class Boundary extends Component<{ children: React.ReactNode }, { failed: boolean; message: string }> {
  state = { failed: false, message: "" };

  static getDerivedStateFromError(error: unknown) {
    return { failed: true, message: error instanceof Error ? error.message : String(error) };
  }

  componentDidCatch(error: unknown) {
    report(error);
  }

  render() {
    if (!this.state.failed) return this.props.children;
    // Plain markup only, and the bridge call may fail too: the page itself is what broke.
    return (
      <div className="centered crashed" role="alert">
        <Logo size={48} />
        <h1>Something went wrong in this window</h1>
        <p>Reload to carry on. If it happens again, open the log and attach it to a bug report.</p>
        {this.state.message && <code className="crash-detail">{this.state.message}</code>}
        <div className="actions">
          <button className="ghost" onClick={() => call("showLog").catch(() => {})}>
            Open log
          </button>
          <button className="primary" onClick={() => location.reload()}>
            Reload
          </button>
        </div>
      </div>
    );
  }
}
