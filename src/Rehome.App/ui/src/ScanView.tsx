import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { call } from "./bridge";
import { Footer } from "./Footer";
import { useNotices } from "./notices";
import { Check, Details, Disclosure, Fold, Icon, Plain, choosable, navigate, displayName, groupOf, groupTitles, hasDetails, rows, siteOf } from "./list";
import type { App, Setting, Snapshot } from "./types";

type Phase = { kind: "scanning" } | { kind: "ready"; snapshot: Snapshot } | { kind: "failed"; message: string };
type Exported = { file: string };

const settingKey = (app: App, s: Setting) => `${app.localId}\n${s.id}`;

export function ScanView({ menu, rescan, onBack }: { menu: React.ReactNode; rescan: number; onBack: () => void }) {
  const [phase, setPhase] = useState<Phase>({ kind: "scanning" });
  const [selected, setSelected] = useState<Set<string>>(new Set());
  // Settings start included. This holds the ones a person turned off.
  const [skipped, setSkipped] = useState<Set<string>>(new Set());
  const [icons, setIcons] = useState<Record<string, string>>({});
  const [query, setQuery] = useState("");
  const { fail, notify } = useNotices();
  const [expanded, setExpanded] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);
  const [exported, setExported] = useState<Exported | null>(null);

  const scan = useCallback(async () => {
    setPhase({ kind: "scanning" });
    try {
      const snapshot = await call("scan");
      // Exact matches and vendor apps start included. Guesses need a yes. Drivers and runtimes start out.
      setSelected(new Set(snapshot.apps.filter((a) => (a.status === "Ready" || a.status === "Manual") && !a.system && choosable(a)).map((a) => a.localId)));
      setSkipped(new Set());
      setPhase({ kind: "ready", snapshot });
      call("icons").then(setIcons, () => {});
    } catch (e) {
      setPhase({ kind: "failed", message: (e as Error).message });
    }
  }, []);

  useEffect(() => {
    void scan();
  }, [scan, rescan]);

  const apps = phase.kind === "ready" ? phase.snapshot.apps : [];

  // What goes in the exported exe.
  const choices = useCallback(() => {
    const skip = apps.filter((a) => choosable(a) && !selected.has(a.localId)).map((a) => a.localId);
    const confirm = apps.filter((a) => a.status === "Guess" && selected.has(a.localId)).map((a) => a.localId);
    const skipSettings: Record<string, string[]> = {};
    for (const a of apps) {
      const off = (a.settings ?? []).filter((s) => skipped.has(settingKey(a, s))).map((s) => s.id);
      if (off.length > 0 && selected.has(a.localId)) skipSettings[a.localId] = off;
    }
    return { skip, confirm, skipSettings };
  }, [apps, selected, skipped]);

  const doExport = useCallback(async () => {
    if (phase.kind !== "ready" || exporting) return;
    setExporting(true);
    try {
      const { exported: done, file } = await call("export", choices());
      if (done && file) setExported({ file });
    } catch (e) {
      fail(e);
    } finally {
      setExporting(false);
    }
  }, [choices, exporting, phase.kind, fail]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const key = e.key.toLowerCase();
      if (e.ctrlKey && key === "s") {
        e.preventDefault();
        void doExport();
      } else if (e.key === "F5" || (e.ctrlKey && key === "r")) {
        e.preventDefault();
        void scan();
      } else if (e.ctrlKey && key === "f") {
        e.preventDefault();
        document.getElementById("search")?.focus();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [doExport, scan]);

  const visible = useMemo(() => {
    const q = query.trim().toLowerCase();
    return q ? apps.filter((a) => displayName(a.name).toLowerCase().includes(q)) : apps;
  }, [apps, query]);

  const flip = (set: (f: (prev: Set<string>) => Set<string>) => void) => (ids: string[], on: boolean) =>
    set((prev) => {
      const next = new Set(prev);
      for (const id of ids) {
        if (on) next.add(id);
        else next.delete(id);
      }
      return next;
    });
  const setApps = flip(setSelected);
  const setSkips = flip(setSkipped);

  if (phase.kind === "scanning")
    return (
      <main>
        <header>
          <h1>Scanning this PC…</h1>
          {menu}
        </header>
        <div className="list" aria-busy="true">
          <Skeleton />
        </div>
        <Footer onBack={onBack} hint="Reading installed apps and matching them to winget and the Store. Nothing is changed." progress={null} />
      </main>
    );

  if (phase.kind === "failed")
    return (
      <main>
        <header>
          <h1>Scan</h1>
          {menu}
        </header>
        <div className="centered">
          <p className="error">{phase.message}</p>
          <button className="ghost" onClick={() => call("showLog").catch(() => {})}>
            Open log
          </button>
        </div>
        <Footer onBack={onBack}>
          <button className="primary" onClick={() => void scan()}>
            Try again
          </button>
        </Footer>
      </main>
    );

  const group = (g: ReturnType<typeof groupOf>) => visible.filter((a) => groupOf(a) === g);
  const count = apps.filter((a) => selected.has(a.localId)).length;
  const section = {
    selected,
    skipped,
    icons,
    expanded,
    onExpand: setExpanded,
    onError: fail,
    onApps: setApps,
    onSetting: (app: App, s: Setting, on: boolean) => setSkips([settingKey(app, s)], !on),
  };

  return (
    <main>
      <header>
        <h1>
          {count} {count === 1 ? "app" : "apps"}
        </h1>
        <Search value={query} onChange={setQuery} />
        {menu}
      </header>

      <div className="list">
        {(["confirm", "apps", "store", "vendor"] as const).map((g) => (
          <Section key={g} title={groupTitles[g]} apps={group(g)} {...section} />
        ))}
        <Section title={groupTitles.system} apps={group("system")} folded {...section} />
        <Fold title={groupTitles.steam} apps={group("steam")} icons={icons} />
        {visible.length === 0 && <p className="empty">No apps match “{query}”</p>}
      </div>
      <Footer onBack={onBack} hint="Export makes one ReHome installer with these apps inside, for a USB drive.">
        <button className="primary" onClick={() => void doExport()} disabled={exporting || count === 0}>
          {exporting ? "Exporting…" : "Export for new PC"}
        </button>
      </Footer>
      {exported && (
        <ExportDone
          {...exported}
          onClose={() => {
            setExported(null);
            notify({ level: "success", title: "Installer saved", detail: "Copy it to the new PC and open it there." });
          }}
          onError={fail}
        />
      )}
    </main>
  );
}

export function Search({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  return (
    <input
      id="search"
      type="search"
      placeholder="Search"
      aria-label="Search apps"
      value={value}
      onChange={(e) => onChange(e.target.value)}
      onKeyDown={(e) => {
        if (e.key === "Escape") onChange("");
        else if (e.key === "ArrowDown") {
          e.preventDefault();
          rows()[0]?.focus();
        }
      }}
      spellCheck={false}
      autoComplete="off"
    />
  );
}

/** Grey rows in the shape of the list, so the window has its layout while the scan runs. */
function Skeleton() {
  const widths = [46, 62, 38, 55, 70, 42, 58, 35, 66, 48];
  return (
    <section className="skeleton" aria-hidden="true">
      <div className="row heading">
        <span className="bone" style={{ width: 90 }} />
      </div>
      {widths.map((w, i) => (
        <div className="row" key={i} style={{ animationDelay: `${i * 60}ms` }}>
          <span className="bone icon" />
          <span className="bone" style={{ width: `${w}%` }} />
        </div>
      ))}
    </section>
  );
}

interface SectionProps {
  title: string;
  folded?: boolean;
  apps: App[];
  selected: Set<string>;
  skipped: Set<string>;
  icons: Record<string, string>;
  expanded: string | null;
  onExpand: (id: string | null) => void;
  onError: (e: unknown) => void;
  onApps: (ids: string[], on: boolean) => void;
  onSetting: (app: App, setting: Setting, on: boolean) => void;
}

function Section({ title, folded, apps, selected, skipped, icons, expanded, onExpand, onError, onApps, onSetting }: SectionProps) {
  const [open, setOpen] = useState(!folded);
  if (apps.length === 0) return null;
  const ids = apps.filter(choosable).map((a) => a.localId);
  const on = ids.filter((id) => selected.has(id)).length;
  const all = on === ids.length ? true : on === 0 ? false : "mixed";

  return (
    <section>
      {folded ? (
        <FoldHeading title={title} count={apps.length} open={open} onToggle={() => setOpen(!open)} />
      ) : (
        <Check className="row heading" label={`All ${title}`} checked={all} disabled={ids.length === 0} onToggle={() => onApps(ids, all !== true)}>
          <span className="name">{title}</span>
          <span className="count">{apps.length}</span>
        </Check>
      )}
      {open && apps.map((a) => {
        if (!choosable(a))
          return (
            <div key={a.localId}>
              <Plain app={a} icon={icons[a.localId]} expanded={expanded === a.localId} onExpand={(o) => onExpand(o ? a.localId : null)} />
              {expanded === a.localId && <Details app={a} onError={onError} />}
            </div>
          );
        const appOn = selected.has(a.localId);
        const isOpen = expanded === a.localId;
        const name = displayName(a.name);
        return (
          <div key={a.localId}>
            <Check
              className="row"
              label={name}
              checked={appOn}
              onToggle={() => onApps([a.localId], !appOn)}
              onExpand={hasDetails(a) ? (o) => onExpand(o ? a.localId : null) : undefined}
              trailing={
                <>
                  {a.status === "Manual" && <Site app={a} />}
                  {hasDetails(a) && <Disclosure open={isOpen} label={`${name} details`} onToggle={() => onExpand(isOpen ? null : a.localId)} />}
                </>
              }
            >
              <Icon app={a} src={icons[a.localId]} />
              <span className="name">{name}</span>
            </Check>
            {isOpen && <Details app={a} onError={onError} />}
            {a.settings?.map((s) => {
              const settingOn = !skipped.has(settingKey(a, s));
              return (
                <Check
                  key={s.id}
                  className="row sub"
                  label={`${name}, ${s.label}`}
                  checked={settingOn}
                  disabled={!appOn}
                  onToggle={() => onSetting(a, s, !settingOn)}
                >
                  <span className="name">{s.label}</span>
                </Check>
              );
            })}
          </div>
        );
      })}
    </section>
  );
}

/** The host a vendor app's link goes to. Nothing when the uninstall entry named no site. */
export function Site({ app }: { app: App }) {
  const site = siteOf(app);
  return site ? <span className="state">{site.host}</span> : null;
}

export function FoldHeading({ title, count, open, onToggle }: { title: string; count: number; open: boolean; onToggle: () => void }) {
  return (
    <div
      className="row heading fold"
      role="button"
      tabIndex={0}
      aria-expanded={open}
      aria-label={`${title}, ${count}`}
      onClick={onToggle}
      onKeyDown={(e) => {
        if (navigate(e)) return;
        if (e.key === " " || e.key === "Enter" || (e.key === "ArrowRight" && !open) || (e.key === "ArrowLeft" && open)) {
          e.preventDefault();
          onToggle();
        }
      }}
    >
      <span className={open ? "chev open" : "chev"} aria-hidden="true" />
      <span className="name">{title}</span>
      <span className="count">{count}</span>
    </div>
  );
}

/** What to do with the exe Export just wrote. */
function ExportDone({ file, onClose, onError }: Exported & { onClose: () => void; onError: (e: unknown) => void }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    ref.current?.querySelector<HTMLElement>("button.primary")?.focus();
    return () => previous?.focus();
  }, []);
  return (
    <div className="scrim">
      <div className="dialog" role="dialog" aria-modal="true" aria-labelledby="export-title" ref={ref} onKeyDown={(e) => e.key === "Escape" && onClose()}>
        <h2 id="export-title">Ready for your new PC</h2>
        <p className="lead">
          Copy <strong>{file.slice(file.lastIndexOf("\\") + 1)}</strong> to the new PC and open it. Your apps will be
          listed and ready to install. Nothing else needs to come along.
        </p>
        <p className="path">{file.slice(0, file.lastIndexOf("\\") + 1)}</p>
        <p className="fine left">
          The scan inside is sealed with a SHA-256 hash. If the file gets damaged on the way, the new PC won't install
          from it.
        </p>
        <div className="actions">
          <button className="ghost" onClick={() => call("showExport").catch(onError)}>
            Show in folder
          </button>
          <button className="primary" onClick={onClose}>
            Done
          </button>
        </div>
      </div>
    </div>
  );
}
