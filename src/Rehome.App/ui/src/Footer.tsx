/**
 * The bottom bar every screen shares: Back on the left, what's happening in the middle, the one next step on the
 * right. Only the next step is colored; when it can't be taken yet it turns grey instead of fading.
 * `progress` is 0..1 for a known amount of work, null for "working, length unknown", undefined for no bar.
 */
export function Footer({
  onBack,
  backDisabled,
  hint,
  tone,
  progress,
  children,
}: {
  onBack?: () => void;
  backDisabled?: boolean;
  hint?: React.ReactNode;
  tone?: "warn" | "danger" | "success";
  progress?: number | null;
  children?: React.ReactNode;
}) {
  return (
    <footer className="bar">
      {progress !== undefined &&
        (progress === null ? (
          <progress className="track" aria-label="Working" />
        ) : (
          <progress className="track" aria-label="Progress" max={100} value={Math.round(progress * 100)} />
        ))}
      <div className="bar-start">
        {onBack && (
          <button className="ghost" onClick={onBack} disabled={backDisabled}>
            Back
          </button>
        )}
      </div>
      <p className={`hint${tone ? " " + tone : ""}`} aria-live="polite">
        {hint}
      </p>
      <div className="bar-end">{children}</div>
    </footer>
  );
}
