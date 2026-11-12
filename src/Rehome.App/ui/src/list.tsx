import { useState } from "react";
import { call } from "./bridge";
import { useNotices } from "./notices";
import type { App, LinkKind } from "./types";

// "Notepad++ (64-bit x64)" reads as "Notepad++". Versions stay: Python 3.12 and 3.13 are different apps.
const scopeNoise = /\s*\((?:x64|x86|arm64|64-bit|32-bit|64-bit x64|user|machine)\)/gi;
export const displayName = (name: string) => name.replace(scopeNoise, "").trim() || name;

export const rows = () => Array.from(document.querySelectorAll<HTMLElement>(".row[tabindex='0']"));

function moveFocus(from: HTMLElement, step: number) {
  const all = rows();
  all[all.indexOf(from) + step]?.focus();
}

/** Arrow keys move between rows, Home and End jump. Shared by every focusable row. */
export function navigate(e: React.KeyboardEvent<HTMLElement>): boolean {
  if (e.key === "ArrowDown" || e.key === "ArrowUp") {
    moveFocus(e.currentTarget, e.key === "ArrowDown" ? 1 : -1);
  } else if (e.key === "Home" || e.key === "End") {
    const all = rows();
    all[e.key === "Home" ? 0 : all.length - 1]?.focus();
  } else return false;
  e.preventDefault();
  return true;
}

interface CheckProps {
  className: string;
  label: string;
  checked: boolean | "mixed";
  disabled?: boolean;
  onToggle: () => void;
  onExpand?: (open: boolean) => void;
  children: React.ReactNode;
  trailing?: React.ReactNode;
}

/** One row, one checkbox. Space or Enter toggles. Right and Left open and close details where there are any. */
export function Check({ className, label, checked, disabled, onToggle, onExpand, children, trailing }: CheckProps) {
  return (
    <div
      className={className}
      role="checkbox"
      aria-checked={checked}
      aria-disabled={disabled || undefined}
      aria-label={label}
      tabIndex={disabled ? -1 : 0}
      onClick={() => !disabled && onToggle()}
      onKeyDown={(e) => {
        if (navigate(e)) return;
        if (e.key === " " || e.key === "Enter") {
          e.preventDefault();
          if (!disabled) onToggle();
        } else if (onExpand && (e.key === "ArrowRight" || e.key === "ArrowLeft")) {
          e.preventDefault();
          onExpand(e.key === "ArrowRight");
        }
      }}
    >
      {children}
      {trailing}
      <span className={checked === true ? "check on" : checked === "mixed" ? "check mixed" : "check"} aria-hidden="true" />
    </div>
  );
}

/** A row that can't be checked: Manual apps, launcher games, apps already installed. Still opens details. */
export function Plain({ app, icon, expanded, onExpand, trailing }: {
  app: App;
  icon?: string;
  expanded: boolean;
  onExpand: (open: boolean) => void;
  trailing?: React.ReactNode;
}) {
  const has = hasDetails(app);
  return (
    <div
      className="row passive"
      tabIndex={0}
      role={has ? "button" : undefined}
      aria-expanded={has ? expanded : undefined}
      aria-label={displayName(app.name)}
      onClick={() => has && onExpand(!expanded)}
      onKeyDown={(e) => {
        if (navigate(e) || !has) return;
        if (e.key === " " || e.key === "Enter") {
          e.preventDefault();
          onExpand(!expanded);
        } else if (e.key === "ArrowRight" || e.key === "ArrowLeft") {
          e.preventDefault();
          onExpand(e.key === "ArrowRight");
        }
      }}
    >
      <Icon app={app} src={icon} />
      <span className="name">{displayName(app.name)}</span>
      {trailing}
      {has && <span className={expanded ? "chev open" : "chev"} aria-hidden="true" />}
    </div>
  );
}

export function Disclosure({ open, onToggle, label }: { open: boolean; onToggle: () => void; label: string }) {
  return (
    <button
      className={open ? "disclose open" : "disclose"}
      aria-label={label}
      aria-expanded={open}
      tabIndex={-1}
      onClick={(e) => {
        e.stopPropagation();
        onToggle();
      }}
    >
      <span className="chev" aria-hidden="true" />
    </button>
  );
}

export const hasDetails = (a: App) =>
  Boolean(a.publisher || a.version || a.signer || (a.links && Object.values(a.links).some(Boolean)));

const linkNames: [LinkKind, string][] = [
  ["homepage", "Homepage"],
  ["publisher", "Publisher"],
  ["support", "Support"],
  ["license", "License"],
  ["privacy", "Privacy"],
];

/** Who makes it, who signed it, and where to read about it. Every link opens on the vendor's own site or the catalog. */
export function Details({ app, onError }: { app: App; onError: (e: unknown) => void }) {
  const open = (kind: LinkKind) => call("link", { localId: app.localId, kind }).catch(onError);
  const signedBy = app.signer?.organization ?? app.signer?.name;
  const facts = [app.publisher, app.version, signedBy && signedBy !== app.publisher ? `Signed by ${signedBy}` : null].filter(Boolean);
  const links = linkNames.filter(([k]) => app.links?.[k]);
  const source = app.links?.source ? (app.package?.source === "msstore" ? "Microsoft Store" : "winget manifest") : null;

  return (
    <div className="details">
      {facts.length > 0 && <p className="facts">{facts.join(" · ")}</p>}
      {(links.length > 0 || source) && (
        <div className="links">
          {links.map(([kind, label]) => (
            <button key={kind} className="chip" onClick={() => void open(kind)}>
              {label}
            </button>
          ))}
          {source && (
            <button className="chip" onClick={() => void open("source")}>
              {source}
            </button>
          )}
        </div>
      )}
    </div>
  );
}

export function Icon({ app, src }: { app: App; src?: string }) {
  const icon = src ?? app.icon;
  if (icon) return <img className="icon" src={icon} alt="" draggable={false} />;
  return (
    <span className="icon letter" aria-hidden="true">
      {displayName(app.name).charAt(0).toUpperCase()}
    </span>
  );
}

/** A collapsed group at the bottom of a list. Rows inside can still open their details. */
export function Fold({ title, apps, icons, trailing }: {
  title: string;
  apps: App[];
  icons: Record<string, string>;
  trailing?: (a: App) => React.ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const [expanded, setExpanded] = useState<string | null>(null);
  const { fail } = useNotices();
  if (apps.length === 0) return null;
  return (
    <section>
      <div
        className="row heading fold"
        role="button"
        tabIndex={0}
        aria-expanded={open}
        aria-label={`${title}, ${apps.length}`}
        onClick={() => setOpen(!open)}
        onKeyDown={(e) => {
          if (navigate(e)) return;
          if (e.key === " " || e.key === "Enter") {
            e.preventDefault();
            setOpen(!open);
          } else if (e.key === "ArrowRight" || e.key === "ArrowLeft") {
            e.preventDefault();
            setOpen(e.key === "ArrowRight");
          }
        }}
      >
        <span className={open ? "chev open" : "chev"} aria-hidden="true" />
        <span className="name">{title}</span>
        <span className="count">{apps.length}</span>
      </div>
      {open &&
        apps.map((a) => (
          <div key={a.localId}>
            <Plain
              app={a}
              icon={icons[a.localId]}
              expanded={expanded === a.localId}
              onExpand={(o) => setExpanded(o ? a.localId : null)}
              trailing={trailing?.(a)}
            />
            {expanded === a.localId && <Details app={a} onError={fail} />}
          </div>
        ))}
    </section>
  );
}

export type Group = "confirm" | "apps" | "store" | "vendor" | "system" | "steam" | "hidden";

/** One place decides where an app is listed, so the scan and the restore screen always agree. */
export function groupOf(a: App, restoring = false): Group {
  if (a.status === "Companion") return "hidden";
  if (a.status === "Launcher") return "steam";
  if (a.system) return "system";
  if (a.status === "Guess" && !restoring) return "confirm";
  if (a.status === "Manual") return "vendor";
  return a.package?.source === "msstore" ? "store" : "apps";
}

export const groupTitles: Record<Exclude<Group, "hidden">, string> = {
  confirm: "Confirm",
  apps: "Apps",
  store: "Microsoft Store",
  vendor: "From vendor sites",
  system: "Drivers and runtimes",
  steam: "Steam",
};

/** Apps with a package install. Apps without one are kept as a link to the vendor's site, except drivers. */
export const choosable = (a: App) => Boolean(a.package) || (a.status === "Manual" && !a.system);

/** Where a vendor app's own page is. The host is shown on the row, so nobody opens a link blind. */
export function siteOf(a: App): { kind: LinkKind; host: string } | null {
  for (const kind of ["homepage", "support"] as const) {
    const url = a.links?.[kind];
    if (!url) continue;
    try {
      const u = new URL(url);
      if (u.protocol === "https:") return { kind, host: u.hostname.replace(/^www\./, "") };
    } catch {
      // Not a URL. The host validated links when the file was opened, so this is only a guard.
    }
  }
  return null;
}
