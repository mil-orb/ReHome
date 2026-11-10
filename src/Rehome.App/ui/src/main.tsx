import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App, Boundary } from "./App";
import { report } from "./bridge";
import "./styles.css";

// Anything that escapes a handler still reaches the log.
window.addEventListener("error", (e) => report(e.error ?? e.message));
window.addEventListener("unhandledrejection", (e) => report(e.reason));

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <Boundary>
      <App />
    </Boundary>
  </StrictMode>,
);
