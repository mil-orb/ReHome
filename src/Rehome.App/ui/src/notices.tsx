import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { call } from "./bridge";

/**
 * Every message the window shows goes through here, so they all look and behave the same: a card in the corner
 * that says what happened and what to do next. Errors stay until they're closed. Anything else closes itself after
 * a few seconds, unless the pointer or focus is on it. Screen readers hear each new one once.
 */
export type Level = "error" | "warning" | "success" | "info";

export interface Notice {
  id: number;
  level: Level;
  title: string;
  detail?: string;
  action?: { label: string; run: () => void };
}

type Input = Omit<Notice, "id" | "level"> & { level?: Level };

interface Api {
  notify: (n: Input) => number;
  /** Shorthand for a caught error: its message is already written for people. Offers the log. */
  fail: (e: unknown) => void;
  dismiss: (id: number) => void;
}

const Context = createContext<Api | null>(null);
const AUTO_CLOSE_MS = 8000;
const MAX_SHOWN = 4;

export function useNotices(): Api {
  const api = useContext(Context);
  if (!api) throw new Error("useNotices outside NoticeProvider");
  return api;
}

const openLog = () => call("showLog").catch(() => {});
const messageOf = (e: unknown) => (e instanceof Error ? e.message : String(e));

export function NoticeProvider({ children }: { children: React.ReactNode }) {
  const [notices, setNotices] = useState<Notice[]>([]);
  const nextId = useRef(1);

  const dismiss = useCallback((id: number) => setNotices((all) => all.filter((n) => n.id !== id)), []);

  const notify = useCallback((n: Input) => {
    const id = nextId.current++;
    const notice: Notice = { level: "info", ...n, id };
    setNotices((all) => {
      // The same message twice in a row is one card, moved to the front.
      const rest = all.filter((x) => !(x.title === notice.title && x.detail === notice.detail));
      return [...rest, notice].slice(-MAX_SHOWN);
    });
    return id;
  }, []);

  const fail = useCallback(
    (e: unknown) => notify({ level: "error", title: messageOf(e), action: { label: "Open log", run: openLog } }),
    [notify],
  );

  const api = useMemo(() => ({ notify, fail, dismiss }), [notify, fail, dismiss]);

  return (
    <Context.Provider value={api}>
      {children}
      <Stack notices={notices} onDismiss={dismiss} />
    </Context.Provider>
  );
}

function Stack({ notices, onDismiss }: { notices: Notice[]; onDismiss: (id: number) => void }) {
  return (
    <>
      {/* Mounted from the start: a screen reader only announces changes to a live region that already exists. */}
      <div className="sr-only" role="status" aria-live="polite" aria-atomic="false">
        {notices.map((n) => (
          <p key={n.id}>
            {n.title}
            {n.detail ? `. ${n.detail}` : ""}
          </p>
        ))}
      </div>
      {notices.length > 0 && (
        <div className="notices" aria-label="Messages">
          {notices.map((n) => (
            <Card key={n.id} notice={n} onDismiss={() => onDismiss(n.id)} />
          ))}
        </div>
      )}
    </>
  );
}

function Card({ notice, onDismiss }: { notice: Notice; onDismiss: () => void }) {
  const [held, setHeld] = useState(false);
  const sticky = notice.level === "error";

  useEffect(() => {
    if (sticky || held) return;
    const t = setTimeout(onDismiss, AUTO_CLOSE_MS);
    return () => clearTimeout(t);
  }, [sticky, held, onDismiss]);

  return (
    <div
      className={`notice tone-${notice.level}`}
      onMouseEnter={() => setHeld(true)}
      onMouseLeave={() => setHeld(false)}
      onFocus={() => setHeld(true)}
      onBlur={(e) => !e.currentTarget.contains(e.relatedTarget as Node) && setHeld(false)}
      onKeyDown={(e) => e.key === "Escape" && onDismiss()}
    >
      <Badge level={notice.level} />
      <div className="notice-body">
        <p className="notice-title">{notice.title}</p>
        {notice.detail && <p className="notice-detail">{notice.detail}</p>}
        {notice.action && (
          <button
            className="link"
            onClick={() => {
              notice.action!.run();
              if (!sticky) onDismiss();
            }}
          >
            {notice.action.label}
          </button>
        )}
      </div>
      <button className="ghost icon-button close" aria-label="Close message" onClick={onDismiss}>
        <svg viewBox="0 0 16 16" aria-hidden="true">
          <path d="M4 4l8 8M12 4l-8 8" />
        </svg>
      </button>
    </div>
  );
}

/** The shape says the level too, so color isn't the only cue. */
export function Badge({ level }: { level: Level }) {
  return (
    <svg className="badge" viewBox="0 0 16 16" aria-hidden="true">
      <circle cx="8" cy="8" r="7" />
      {level === "success" ? (
        <path d="M5 8.2l2 2 4-4.2" />
      ) : level === "info" ? (
        <path d="M8 7.2V11.5M8 4.6v.1" />
      ) : (
        <path d="M8 4.5v4M8 11.2v.1" />
      )}
    </svg>
  );
}
