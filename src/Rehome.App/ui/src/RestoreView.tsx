import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { call, on } from "./bridge";
import { Footer } from "./Footer";
import { Check, Details, Disclosure, Fold, Icon, Plain, choosable, displayName, groupOf, groupTitles, hasDetails, siteOf } from "./list";
import { Banner, FoldHeading, Search, Site } from "./ScanView";
import type { App, ReviewItem, RunEvent, Snapshot, Summary } from "./types";

type Phase = "idle" | "running" | "done";
interface Entry {
  at: Date;
  name: string;
  state: RunEvent["state"];
  message?: string;
}

const installable = (a: App) => a.package && (a.status === "Ready" || (a.status === "Guess" && a.confirmed));
const byName = (xs: App[]) => [...xs].sort((a, b) => displayName(a.name).localeCompare(displayName(b.name)));
const finished = new Set(["installed", "present", "restart", "failed", "skipped"]);
const plural = (n: number, one: string, many = one + "s") => `${n} ${n === 1 ? one : many}`;

/**
 * How far one app is, out of 2: checking is the first half, installing the second. Store apps skip the download,
 * so they jump straight to 1. Anything finished counts in full, including a failure: it's done being worked on.
 */
function score(ev: RunEvent | undefined): number {
  if (!ev) return 0;
  if (finished.has(ev.state)) return 2;
  if (ev.state === "verifying") return Math.min(ev.progress ?? 0, 1);
  if (ev.state === "installing") return 1.5;
  return 1; // verified, review
}

export function RestoreView({ plan, menu, onBack }: { plan: Snapshot; menu: React.ReactNode; onBack: () => void }) {
  const [present, setPresent] = useState<Set<string> | null>(null);
  // Everything the scan kept starts checked, except drivers and runtimes: a scan saved without the app may still
  // carry them, and a new PC gets them from Windows Update or the app that needs them.
  const [selected, setSelected] = useState<Set<string>>(
    () => new Set(plan.apps.filter((a) => choosable(a) && !a.system).map((a) => a.localId)),
  );
  const [states, setStates] = useState<Record<string, RunEvent>>({});
  const [phase, setPhase] = useState<Phase>("idle");
  const [stopping, setStopping] = useState(false);
  const [review, setReview] = useState<ReviewItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [expanded, setExpanded] = useState<string | null>(null);
  const [activity, setActivity] = useState<Entry[]>([]);
  const [showLog, setShowLog] = useState(false);
  const runSet = useRef<string[]>([]);
  const current = useRef<string | null>(null);

  const apps = plan.apps;
  const names = useMemo(() => new Map(apps.map((a) => [a.localId, displayName(a.name)])), [apps]);

  // What's already on this PC is skipped. Checking takes a few seconds; the list is usable meanwhile.
  useEffect(() => {
    let live = true;
    call("present").then(
      ({ ids }) => {
        if (!live) return;
        const here = new Set(ids);
        setPresent(here);
        setSelected((prev) => new Set([...prev].filter((id) => !here.has(id))));
      },
      (e: Error) => {
        if (!live) return;
        setPresent(new Set());
        setError(e.message);
      },
    );
    return () => {
      live = false;
    };
  }, [plan]);

  useEffect(() => {
    const offRun = on("run", (e) => {
      setStates((prev) => ({ ...prev, [e.localId]: e }));
      if (e.state === "verifying" || e.state === "installing") current.current = e.localId;
      // The log keeps results: what each check proved, and how each install ended. Starts and ticks aren't news.
      if (e.state !== "verifying" && e.state !== "installing")
        setActivity((prev) => [...prev, { at: new Date(), name: names.get(e.localId) ?? e.localId, state: e.state, message: e.message }]);
    });
    const offReview = on("review", ({ items }) => setReview(items));
    return () => {
      offRun();
      offReview();
    };
  }, [names]);

  const visible = useMemo(() => {
    const q = query.trim().toLowerCase();
    return q ? apps.filter((a) => displayName(a.name).toLowerCase().includes(q)) : apps;
  }, [apps, query]);

  const alreadyHere = visible.filter((a) => installable(a) && present?.has(a.localId));
  const toInstall = apps.filter((a) => installable(a) && !present?.has(a.localId));
  const count = toInstall.filter((a) => selected.has(a.localId)).length;
  const vendor = apps.filter((a) => groupOf(a, true) === "vendor");

  const start = useCallback(
    async (only?: string[]) => {
      const ids = only ?? toInstall.filter((a) => selected.has(a.localId)).map((a) => a.localId);
      if (ids.length === 0) return;
      const keep = new Set(ids);
      runSet.current = ids;
      current.current = null;
      setStates((prev) => Object.fromEntries(Object.entries(prev).filter(([id]) => !keep.has(id))));
      setError(null);
      setStopping(false);
      setPhase("running");
      try {
        const skip = apps.filter((a) => installable(a) && !keep.has(a.localId)).map((a) => a.localId);
        await call("install", { skip });
        setPhase("done");
      } catch (e) {
        setError((e as Error).message);
        setPhase("idle");
      } finally {
        setReview(null);
      }
    },
    [apps, toInstall, selected],
  );

  const stop = () => {
    setStopping(true);
    call("stop").catch((e: Error) => setError(e.message));
  };

  const decide = (confirm: string[]) => {
    setReview(null);
    call("decide", { confirm }).catch((e: Error) => setError(e.message));
  };

  const run = runSet.current;
  const total = run.length;
  const done = run.filter((id) => finished.has(states[id]?.state ?? "")).length;
  const failed = run.filter((id) => states[id]?.state === "failed");
  const summary = summarize(states);
  const locked = phase !== "idle";
  // Never shows 100% before the host says the run is over: the last installer can take a while to exit.
  const progress = total === 0 ? 0 : Math.min(run.reduce((sum, id) => sum + score(states[id]), 0) / (total * 2), 0.99);
  // The app being worked on right now. A checked app waiting for the others isn't "now".
  const nowState = current.current ? states[current.current]?.state : undefined;
  const now = nowState === "verifying" || nowState === "installing" ? current.current : null;
  const installing = run.some((id) => ["installing", "installed", "restart"].includes(states[id]?.state ?? ""));

  const title =
    phase === "running"
      ? stopping
        ? "Stopping…"
        : installing
          ? "Installing your apps"
          : "Checking installers"
      : phase === "done"
        ? summary.failed > 0
          ? `${plural(summary.failed, "app")} didn't install`
          : stopping
            ? "Stopped"
            : summary.restart > 0
              ? "Restart to finish"
              : "Your apps are back"
        : present === null
          ? "Checking this PC…"
          : toInstall.length === 0
            ? "Everything's already here"
            : count === 0
              ? "Nothing selected"
              : `${plural(count, "app")} to install`;

  let hint: React.ReactNode;
  let tone: "warn" | "danger" | "success" | undefined;
  if (phase === "running") {
    hint = stopping
      ? now
        ? `Finishing ${names.get(now)}, then stopping`
        : "Stopping"
      : review
        ? "Waiting for your answer"
        : now
          ? `${nowState === "installing" ? "Installing" : "Checking"} ${names.get(now)} · ${done} of ${total}`
          : `${done} of ${total}`;
  } else if (phase === "done") {
    hint = summaryText(summary);
    tone = summary.failed > 0 ? "danger" : summary.restart > 0 ? "warn" : "success";
  } else if (present === null) {
    hint = "Looking for apps that are already installed";
  } else if (toInstall.length === 0) {
    hint = vendor.length > 0 ? `${plural(vendor.length, "app")} still come from vendor sites` : undefined;
  } else {
    // Said before the prompt appears, so the UAC dialog isn't a surprise.
    hint = `${alreadyHere.length > 0 ? `${alreadyHere.length} already here. ` : ""}Windows asks once for admin approval, then each installer is checked before it runs.`;
  }

  return (
    <main className="restore">
      <header>
        <h1 aria-live="polite">{title}</h1>
        <Search value={query} onChange={setQuery} />
        {menu}
      </header>
      {error && <Banner message={error} onClose={() => setError(null)} />}

      <div className="list">
        {(["apps", "store", "vendor", "system"] as const).map((g) => (
          <Group
            key={g}
            title={groupTitles[g]}
            apps={byName(visible.filter((a) => groupOf(a, true) === g && !(installable(a) && present?.has(a.localId))))}
            folded={g === "system" && !visible.some((a) => groupOf(a, true) === g && installable(a) && selected.has(a.localId))}
            selected={selected}
            onSelect={setSelected}
            states={states}
            locked={locked}
            expanded={expanded}
            onExpand={setExpanded}
            onError={setError}
            action={g === "vendor" && <OpenSites apps={apps.filter((a) => groupOf(a, true) === "vendor" && selected.has(a.localId))} onError={setError} />}
          />
        ))}
        <Fold title="Already installed" apps={byName(alreadyHere)} icons={{}} />
        <Fold title={groupTitles.steam} apps={visible.filter((a) => a.status === "Launcher")} icons={{}} />
        {visible.length === 0 && <p className="empty">No apps match “{query}”</p>}
      </div>

      <Footer
        onBack={onBack}
        backDisabled={phase === "running"}
        hint={hint}
        tone={tone}
        progress={phase === "running" ? progress : present === null ? null : undefined}
      >
        {activity.length > 0 && phase !== "running" && (
          <button className="ghost" onClick={() => setShowLog(true)}>
            View log
          </button>
        )}
        {phase === "running" ? (
          <button onClick={stop} disabled={stopping}>
            Stop
          </button>
        ) : phase === "done" && failed.length > 0 ? (
          <button className="primary" onClick={() => void start(failed)}>
            Retry {failed.length}
          </button>
        ) : phase === "done" ? (
          <button className="primary" onClick={onBack}>
            Done
          </button>
        ) : (
          <button className="primary" onClick={() => void start()} disabled={count === 0 || present === null}>
            {count > 0 ? `Install ${count}` : "Install"}
          </button>
        )}
      </Footer>

      {review && <Review items={review} apps={apps} onDecide={decide} />}
      {showLog && <Activity entries={activity} onClose={() => setShowLog(false)} onError={setError} />}
    </main>
  );
}

// Counts every row this window has touched, so a retry adds to the first run instead of replacing it.
function summarize(states: Record<string, RunEvent>): Summary {
  const summary: Summary = { installed: 0, present: 0, restart: 0, failed: 0, skipped: 0 };
  for (const ev of Object.values(states)) if (ev.state in summary) summary[ev.state as keyof Summary]++;
  return summary;
}

function summaryText(s: Summary) {
  const parts = [
    s.installed && `${s.installed} installed`,
    s.present && `${s.present} already here`,
    s.restart && `${s.restart} ${s.restart === 1 ? "needs" : "need"} a restart`,
    s.failed && `${s.failed} failed`,
    s.skipped && `${s.skipped} skipped`,
  ].filter(Boolean);
  return parts.join(" · ") || "Nothing was installed";
}

const said: Record<string, string> = {
  verifying: "Checking",
  verified: "Checked",
  review: "Needs a decision",
  installing: "Installing",
  installed: "Installed",
  present: "Already installed",
  restart: "Installed, restart needed",
  failed: "Failed",
  skipped: "Skipped",
};

/** What happened this session, newest last. The same outcomes are in the log file the button opens. */
function Activity({ entries, onClose, onError }: { entries: Entry[]; onClose: () => void; onError: (m: string) => void }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    ref.current?.querySelector<HTMLElement>("button")?.focus();
    const list = ref.current?.querySelector(".log");
    if (list) list.scrollTop = list.scrollHeight;
  }, []);
  const time = (d: Date) => d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
  return (
    <div className="scrim" onClick={(e) => e.target === e.currentTarget && onClose()}>
      <div className="dialog wide" role="dialog" aria-modal="true" aria-labelledby="log-title" ref={ref} onKeyDown={(e) => e.key === "Escape" && onClose()}>
        <h2 id="log-title">Log</h2>
        <ol className="log">
          {entries.map((e, i) => (
            <li key={i} className={e.state}>
              <time>{time(e.at)}</time>
              <span className="who">{e.name}</span>
              <span className="what">
                {said[e.state] ?? e.state}
                {e.message && e.message !== "Skipped" ? `: ${e.message}` : ""}
              </span>
            </li>
          ))}
        </ol>
        <div className="actions">
          <button className="ghost" onClick={() => call("showLog").catch((e: Error) => onError(e.message))}>
            Open log file
          </button>
          <button className="primary" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}

interface GroupProps {
  title: string;
  apps: App[];
  folded: boolean;
  selected: Set<string>;
  onSelect: (f: (prev: Set<string>) => Set<string>) => void;
  states: Record<string, RunEvent>;
  locked: boolean;
  expanded: string | null;
  onExpand: (id: string | null) => void;
  onError: (message: string) => void;
  action?: React.ReactNode;
}

/** One category. Installs lock while a run is going; vendor links never do. */
function Group({ title, apps, folded, selected, onSelect, states, locked, expanded, onExpand, onError, action }: GroupProps) {
  const [open, setOpen] = useState(!folded);
  if (apps.length === 0) return null;
  const ids = apps.filter(choosable).map((a) => a.localId);
  const lockedFor = (a: App) => locked && a.status !== "Manual";
  const on = ids.filter((id) => selected.has(id)).length;
  const toggle = (list: string[], value: boolean) =>
    onSelect((prev) => {
      const next = new Set(prev);
      for (const id of list) {
        if (value) next.add(id);
        else next.delete(id);
      }
      return next;
    });

  return (
    <section>
      {folded ? (
        <FoldHeading title={title} count={apps.length} open={open} onToggle={() => setOpen(!open)} />
      ) : (
        <div className="heading-bar">
          <Check
            className="row heading"
            label={`All ${title}`}
            checked={on > 0 && on === ids.length ? true : on === 0 ? false : "mixed"}
            disabled={ids.length === 0 || apps.some(lockedFor)}
            onToggle={() => toggle(ids, on !== ids.length)}
          >
            <span className="name">{title}</span>
            <span className="count">{apps.length}</span>
          </Check>
          {action}
        </div>
      )}
      {open &&
        apps.map((a) => {
          const isOpen = expanded === a.localId;
          const name = displayName(a.name);
          const ev = states[a.localId];
          if (!choosable(a))
            return (
              <div key={a.localId}>
                <Plain app={a} expanded={isOpen} onExpand={(o) => onExpand(o ? a.localId : null)} />
                {isOpen && <Details app={a} onError={onError} />}
              </div>
            );
          return (
            <div key={a.localId}>
              <Check
                className={lockedFor(a) && !ev ? "row idle" : "row"}
                label={name}
                checked={selected.has(a.localId)}
                disabled={lockedFor(a)}
                onToggle={() => toggle([a.localId], !selected.has(a.localId))}
                onExpand={hasDetails(a) ? (o) => onExpand(o ? a.localId : null) : undefined}
                trailing={
                  <>
                    {ev && <State ev={ev} />}
                    {a.status === "Manual" && <Site app={a} />}
                    {hasDetails(a) && <Disclosure open={isOpen} label={`${name} details`} onToggle={() => onExpand(isOpen ? null : a.localId)} />}
                  </>
                }
              >
                <Icon app={a} />
                <span className="name">{name}</span>
              </Check>
              {ev?.message && ev.state === "failed" && <p className="note">{ev.message}</p>}
              {isOpen && <Details app={a} onError={onError} />}
            </div>
          );
        })}
    </section>
  );
}

/**
 * Apps no official catalog carries. Rehome never downloads these: it opens the page the vendor's own uninstall
 * entry named, and each row shows that page's host first.
 */
function OpenSites({ apps, onError }: { apps: App[]; onError: (message: string) => void }) {
  const withSite = apps.filter((a) => siteOf(a));
  const n = withSite.length;
  return (
    <button
      className="small"
      disabled={n === 0}
      onClick={async () => {
        for (const a of withSite) {
          try {
            await call("link", { localId: a.localId, kind: siteOf(a)!.kind });
          } catch (e) {
            onError((e as Error).message);
            return;
          }
        }
      }}
    >
      Open {n} {n === 1 ? "site" : "sites"}
    </button>
  );
}

function State({ ev }: { ev: RunEvent }) {
  const label: Record<string, string> = {
    verifying: ev.progress !== undefined && ev.progress < 1 ? `Checking ${Math.round(ev.progress * 100)}%` : "Checking",
    verified: "Verified",
    review: "Waiting",
    installing: "Installing",
    installed: "Installed",
    present: "Already installed",
    restart: "Restart needed",
    failed: "Failed",
    skipped: ev.message ?? "Skipped",
  };
  return (
    <span className={`state ${ev.state}`} title={ev.message}>
      {label[ev.state] ?? ev.state}
    </span>
  );
}

/**
 * The one question a restore can ask. Everything here passed winget's hash check, but who made it couldn't be
 * confirmed. Nothing is preselected: installing one of these takes a deliberate click.
 */
function Review({ items, apps, onDecide }: { items: ReviewItem[]; apps: App[]; onDecide: (ids: string[]) => void }) {
  const [yes, setYes] = useState<Set<string>>(new Set());
  const ref = useRef<HTMLDivElement>(null);
  const name = (id: string) => displayName(apps.find((a) => a.localId === id)?.name ?? id);

  useEffect(() => {
    ref.current?.querySelector<HTMLElement>("[tabindex='0']")?.focus();
  }, []);

  return (
    <div className="scrim">
      <div
        className="dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="review-title"
        ref={ref}
        onKeyDown={(e) => e.key === "Escape" && onDecide([])}
      >
        <h2 id="review-title">{items.length === 1 ? "Install this one?" : `Install these ${items.length}?`}</h2>
        <div className="review">
          {items.map((it) => (
            <Check
              key={it.localId}
              className="row review-row"
              label={name(it.localId)}
              checked={yes.has(it.localId)}
              onToggle={() =>
                setYes((prev) => {
                  const next = new Set(prev);
                  if (next.has(it.localId)) next.delete(it.localId);
                  else next.add(it.localId);
                  return next;
                })
              }
            >
              <span className="name">
                <span>{name(it.localId)}</span>
                <span className="why">
                  {it.verdict === "unsigned" ? "Not signed" : `Signed by ${it.signedBy ?? "someone else"}, was ${it.expected ?? "unknown"}`}
                  {it.host && ` · ${it.host}`}
                </span>
                {it.sha256 && <code className="hash">{it.sha256}</code>}
              </span>
            </Check>
          ))}
        </div>
        <div className="actions">
          <button onClick={() => onDecide([])}>Skip</button>
          <button className="primary" disabled={yes.size === 0} onClick={() => onDecide([...yes])}>
            Install {yes.size || ""}
          </button>
        </div>
      </div>
    </div>
  );
}

