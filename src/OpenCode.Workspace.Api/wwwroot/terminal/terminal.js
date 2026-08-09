"use strict";

const sessionId = decodeURIComponent(location.pathname.split("/").filter(Boolean).at(-1) || "");
const title = document.querySelector("#title");
const state = document.querySelector("#state");
const runtimeLabel = document.querySelector("#runtime");
const terminal = document.querySelector("#terminal");
const message = document.querySelector("#message");
const attachButton = document.querySelector("#attach");
const takeoverButton = document.querySelector("#takeover");
const detachButton = document.querySelector("#detach");
const stopButton = document.querySelector("#stop");
const encoder = new TextEncoder();
const clientInstanceId = crypto.randomUUID();
const terminalId = "local-terminal";
let socket;
let attachment;
let runtime;
let pendingOutput;
let afterSequence = 0;
let sequenceStorageKey = "";

globalThis.DotNet = {
  invokeMethod() { return true; },
  invokeMethodAsync(_assembly, method, id, value) {
    if (id !== terminalId || !socket || socket.readyState !== WebSocket.OPEN) return Promise.resolve();
    if (method === "OnData") socket.send(encoder.encode(value));
    if (method === "OnBinary") socket.send(Uint8Array.from(value, character => character.charCodeAt(0) & 0xff));
    if (method === "OnResize") socket.send(JSON.stringify({ type: "resize", columns: value.columns, rows: value.rows }));
    return Promise.resolve();
  }
};
XtermBlazor.registerTerminal(terminalId, terminal, {
  allowProposedApi: false,
  convertEol: false,
  cursorBlink: true,
  cursorStyle: "bar",
  fontFamily: '"Cascadia Mono", "JetBrainsMono Nerd Font", Consolas, monospace',
  fontSize: 14,
  scrollback: 5000,
  theme: { background: "#080c0a", foreground: "#d9e8df", cursor: "#91d3ad", selectionBackground: "#355544" }
}, []);
const xterm = XtermBlazor.getTerminalById(terminalId);

function setState(value, text) {
  state.dataset.state = value;
  state.lastChild.textContent = text;
}

async function api(path, options = {}) {
  const response = await fetch(`/api/v1${path}`, {
    ...options,
    headers: { "Content-Type": "application/json", ...(options.headers || {}) },
    cache: "no-store"
  });
  const body = await response.json();
  if (!response.ok) {
    const error = new Error(body.message || body.code || `HTTP ${response.status}`);
    error.code = body.code;
    throw error;
  }
  return body.data;
}

async function refreshCanonicalState() {
  const session = await api(`/interactive-agent-sessions/${encodeURIComponent(sessionId)}`);
  title.textContent = session.title || "Interactive session";
  runtime = await api(`/local-host/interactive-agent-sessions/${encodeURIComponent(sessionId)}/terminal`);
  sequenceStorageKey = `terminal-sequence:${sessionId}:${runtime.runtimeId}`;
  afterSequence = Number(sessionStorage.getItem(sequenceStorageKey) || 0);
  runtimeLabel.textContent = `Runtime ${runtime.runtimeId} | PID ${runtime.processId ?? "-"}`;
  stopButton.disabled = !socket || socket.readyState !== WebSocket.OPEN;
}

async function attach(takeover = false) {
  setState("idle", takeover ? "Taking over" : "Attaching");
  message.textContent = "Requesting exclusive presentation authority...";
  try {
    await refreshCanonicalState();
    attachment = await api(`/local-host/interactive-agent-sessions/${encodeURIComponent(sessionId)}/attachments`, {
      method: "POST",
      body: JSON.stringify({ sessionId, commandId: crypto.randomUUID(), clientInstanceId, attachmentKind: 1, requestTransfer: takeover })
    });
    openSocket();
  } catch (error) {
    setState("error", "Not attached");
    takeoverButton.hidden = error.code !== "already_attached";
    message.textContent = error.code === "already_attached" ? "Another terminal owns this runtime. Take over only when that handoff is intentional." : error.message;
  }
}

function openSocket() {
  const scheme = location.protocol === "https:" ? "wss" : "ws";
  socket = new WebSocket(`${scheme}://${location.host}/api/v1/local-host/interactive-agent-sessions/${encodeURIComponent(sessionId)}/terminal/ws`, "opencode-terminal-v1");
  socket.binaryType = "arraybuffer";
  socket.onopen = () => socket.send(JSON.stringify({
    type: "hello",
    interactiveAgentSessionId: sessionId,
    terminalRuntimeId: runtime.runtimeId,
    attachmentId: attachment.attachment.attachmentId,
    attachmentToken: attachment.attachmentToken,
    afterSequence
  }));
  socket.onmessage = event => {
    if (typeof event.data === "string") {
      const control = JSON.parse(event.data);
      if (control.type === "attached") {
        setState("connected", "Connected");
        detachButton.disabled = false;
        stopButton.disabled = false;
        takeoverButton.hidden = true;
        message.textContent = "Input is sent only to the active canonical attachment.";
        xterm.focus();
        socket.send(JSON.stringify({ type: "resize", columns: xterm.cols, rows: xterm.rows }));
      } else if (control.type === "output") {
        pendingOutput = control;
      } else if (control.type === "gap") {
        afterSequence = Math.max(0, control.earliestAvailableSequence - 1);
        xterm.writeln("\r\n[Earlier terminal output is no longer buffered.]");
      } else if (control.type === "detach") {
        message.textContent = control.message;
      } else if (control.type === "runtime_state") {
        message.textContent = `Runtime ended with state ${control.runtimeStatus}.`;
      } else if (control.type === "error") {
        setState("error", "Rejected");
        message.textContent = `${control.code}: ${control.message}`;
      }
    } else if (pendingOutput) {
      const completedOutput = pendingOutput;
      pendingOutput = undefined;
      xterm.write(new Uint8Array(event.data), () => {
        afterSequence = completedOutput.sequence;
        sessionStorage.setItem(sequenceStorageKey, String(afterSequence));
        if (socket.readyState === WebSocket.OPEN) socket.send(JSON.stringify({ type: "ack", sequence: afterSequence }));
      });
    }
  };
  socket.onclose = () => {
    detachButton.disabled = true;
    stopButton.disabled = true;
    if (state.dataset.state !== "error") setState("idle", "Disconnected");
  };
  socket.onerror = () => { setState("error", "Connection error"); };
}

new ResizeObserver(entries => {
  const box = entries[0].contentRect;
  const columns = Math.max(20, Math.min(500, Math.floor((box.width - 40) / 8.4)));
  const rows = Math.max(5, Math.min(300, Math.floor((box.height - 40) / 20.3)));
  if (columns !== xterm.cols || rows !== xterm.rows) xterm.resize(columns, rows);
}).observe(terminal);

attachButton.addEventListener("click", () => attach(false));
takeoverButton.addEventListener("click", () => attach(true));
detachButton.addEventListener("click", () => { if (socket?.readyState === WebSocket.OPEN) socket.send(JSON.stringify({ type: "detach" })); });
stopButton.addEventListener("click", async () => {
  if (!confirm("Stop the LocalHost-owned terminal runtime? The durable conversation remains available.")) return;
  if (socket?.readyState === WebSocket.OPEN) socket.send(JSON.stringify({ type: "stop" }));
});

refreshCanonicalState().catch(error => { setState("error", "Unavailable"); message.textContent = error.message; });
