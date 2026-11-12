import { useEffect, useRef, useState } from "react";
import { Logo } from "./brand";

const opens = (name: string) => /\.(exe|json)$/i.test(name);

/**
 * Drop the exported installer (or a scan .json) anywhere on the window to restore from it. The whole window shows
 * where it will land while a file is over it. Only the file object is handed on; the host reads its path.
 */
export function DropToOpen({ onFile, onRefuse }: { onFile: (f: File) => void; onRefuse: (message: string) => void }) {
  const [over, setOver] = useState(false);
  const depth = useRef(0);

  useEffect(() => {
    const hasFiles = (e: DragEvent) => e.dataTransfer?.types.includes("Files") ?? false;
    const enter = (e: DragEvent) => {
      if (!hasFiles(e)) return;
      e.preventDefault();
      depth.current++;
      setOver(true);
    };
    const overIt = (e: DragEvent) => {
      // Always, so a stray drop never asks the web view to navigate to the file.
      e.preventDefault();
      if (e.dataTransfer) e.dataTransfer.dropEffect = hasFiles(e) ? "copy" : "none";
    };
    const leave = (e: DragEvent) => {
      if (!hasFiles(e)) return;
      depth.current = Math.max(0, depth.current - 1);
      if (depth.current === 0) setOver(false);
    };
    const drop = (e: DragEvent) => {
      e.preventDefault();
      depth.current = 0;
      setOver(false);
      const files = Array.from(e.dataTransfer?.files ?? []);
      if (files.length === 0) return;
      if (files.length > 1) onRefuse("Drop one file at a time.");
      else if (!opens(files[0].name)) onRefuse("ReHome opens the ReHome installer you exported on the old PC, or a scan .json file.");
      else onFile(files[0]);
    };
    window.addEventListener("dragenter", enter);
    window.addEventListener("dragover", overIt);
    window.addEventListener("dragleave", leave);
    window.addEventListener("drop", drop);
    return () => {
      window.removeEventListener("dragenter", enter);
      window.removeEventListener("dragover", overIt);
      window.removeEventListener("dragleave", leave);
      window.removeEventListener("drop", drop);
    };
  }, [onFile, onRefuse]);

  if (!over) return null;
  return (
    <div className="drop" aria-hidden="true">
      <div className="drop-target">
        <Logo size={56} />
        <p className="drop-title">Drop to restore</p>
        <p className="drop-detail">The ReHome installer you exported on the old PC</p>
      </div>
    </div>
  );
}
