// Mirrors the Snapshot JSON the CLI writes (format 1). One contract for the CLI, the app and agents.

export type Status = "Ready" | "Guess" | "Launcher" | "Manual" | "Companion";

export interface CatalogRef {
  id: string;
  source: "winget" | "msstore";
}

export interface Signer {
  organization?: string;
  name?: string;
  subject: string;
  from: "msi" | "exe" | "msix";
  valid: boolean;
}

// A part of an app's configuration that can be carried over. Paths are templates like {AppData}\Code\User\settings.json.
export interface Setting {
  id: string;
  label: string;
  files: { path: string; size: number }[];
}

// The vendor's own pages. `source` is the winget-pkgs manifest folder or the Microsoft Store listing.
export interface Links {
  homepage?: string;
  publisher?: string;
  support?: string;
  license?: string;
  privacy?: string;
  source?: string;
}

export type LinkKind = keyof Links;

export interface App {
  name: string;
  publisher?: string;
  version?: string;
  status: Status;
  package?: CatalogRef;
  signer?: Signer;
  parent?: string;
  confirmed?: boolean;
  // A driver, runtime or part of Windows. Starts unchecked. Display only.
  system?: boolean;
  settings?: Setting[];
  links?: Links;
  icon?: string;
  localId: string;
}

export interface Snapshot {
  format: number;
  machine: string;
  taken: string;
  winget: string;
  apps: App[];
  // SHA-256 of the file, checked by the host when the scan is opened.
  check?: string;
}

// Restore progress, pushed by the host while verifying and installing.
export type RunState =
  | "verifying"
  | "verified"
  | "review"
  | "installing"
  | "installed"
  | "present"
  | "restart"
  | "failed"
  | "skipped";

export interface RunEvent {
  localId: string;
  state: RunState;
  message?: string;
  progress?: number;
}

export interface ReviewItem {
  localId: string;
  verdict: "unsigned" | "signer";
  host?: string;
  sha256?: string;
  signedBy?: string;
  expected?: string;
}

export interface Summary {
  installed: number;
  present: number;
  restart: number;
  failed: number;
  skipped: number;
}

export type Theme = "system" | "light" | "dark";

export interface Boot {
  version: string;
  theme: Theme;
  plan?: Snapshot;
  error?: string;
}

export interface About {
  version: string;
  winget?: string;
  log: string;
}
