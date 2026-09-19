const SOCKET_URL = "ws://127.0.0.1:37821/dota-sync";
const SOURCE_QUERIES = {
  valve: query => `site:dota2.com ${query}`,
  d2pt: query => `site:dota2protracker.com ${query}`,
  dotabuff: query => `site:dotabuff.com ${query}`,
  general: query => query
};

let socket = null;
let reconnectTimer = null;
let heartbeatTimer = null;
let state = {
  status: "waiting",
  paired: false,
  pairingCode: "",
  appVersion: "",
  lastQuery: "",
  lastResultCount: 0,
  error: ""
};

async function loadPairingCode() {
  const saved = await chrome.storage.local.get("pairingCode");
  state.pairingCode = saved.pairingCode || "";
  if (state.pairingCode) {
    connect();
  } else {
    updateState({ status: "needs_code", paired: false });
  }
}

function connect() {
  clearTimeout(reconnectTimer);
  if (!state.pairingCode || socket?.readyState === WebSocket.OPEN || socket?.readyState === WebSocket.CONNECTING) {
    return;
  }

  updateState({ status: "connecting", paired: false, error: "" });
  socket = new WebSocket(SOCKET_URL);
  socket.addEventListener("open", () => {
    send({
      type: "hello",
      pairingCode: state.pairingCode,
      extensionVersion: chrome.runtime.getManifest().version
    });
  });
  socket.addEventListener("message", event => handleMessage(event.data));
  socket.addEventListener("error", () => updateState({ status: "error", paired: false, error: "WebSocket connection failed." }));
  socket.addEventListener("close", () => {
    socket = null;
    stopHeartbeat();
    updateState({ status: state.pairingCode ? "waiting" : "needs_code", paired: false });
    if (state.pairingCode) {
      reconnectTimer = setTimeout(connect, 3000);
    }
  });
}

async function handleMessage(rawMessage) {
  let message;
  try {
    message = JSON.parse(rawMessage);
  } catch {
    return;
  }

  switch (message.type) {
    case "hello_ack":
      updateState({ status: "connected", paired: true, appVersion: message.appVersion || "", error: "" });
      startHeartbeat();
      break;
    case "pair_rejected":
      updateState({ status: "rejected", paired: false, error: message.message || "Pairing code rejected." });
      break;
    case "search_request":
      await runSearch(message);
      break;
    case "heartbeat_ack":
      break;
  }
}

async function runSearch(message) {
  const requestId = message.requestId || "";
  const query = String(message.query || "").trim();
  const sources = Array.isArray(message.sources) ? message.sources.filter(source => SOURCE_QUERIES[source]) : [];
  const maxResults = Math.max(5, Math.min(50, Number(message.maxResults) || 20));
  if (!requestId || !query || sources.length === 0) {
    send({ type: "search_error", requestId, message: "Invalid search request." });
    return;
  }

  updateState({ status: "searching", lastQuery: query, error: "" });
  try {
    const perSource = Math.max(5, Math.ceil(maxResults / sources.length));
    const batches = await Promise.all(sources.map(source => searchBingRss(source, SOURCE_QUERIES[source](query), perSource)));
    const seen = new Set();
    const results = [];
    for (const batch of batches) {
      for (const result of batch) {
        if (!result.url || seen.has(result.url)) {
          continue;
        }

        seen.add(result.url);
        results.push(result);
        if (results.length >= maxResults) {
          break;
        }
      }
      if (results.length >= maxResults) {
        break;
      }
    }

    send({
      type: "search_result",
      requestId,
      query,
      retrievedAt: new Date().toISOString(),
      results
    });
    updateState({ status: "connected", paired: true, lastResultCount: results.length });
  } catch (error) {
    const messageText = error instanceof Error ? error.message : String(error);
    send({ type: "search_error", requestId, message: messageText });
    updateState({ status: "connected", paired: true, error: messageText });
  }
}

async function searchBingRss(source, query, count) {
  const url = `https://www.bing.com/search?format=rss&count=${count}&q=${encodeURIComponent(query)}`;
  const response = await fetch(url, { cache: "no-store" });
  if (!response.ok) {
    throw new Error(`${source} search returned HTTP ${response.status}.`);
  }

  return parseRss(await response.text()).map(result => ({ source, ...result })).slice(0, count);
}

function parseRss(xml) {
  const items = [];
  const itemPattern = /<item>([\s\S]*?)<\/item>/gi;
  let match;
  while ((match = itemPattern.exec(xml)) !== null) {
    const item = match[1];
    items.push({
      title: readTag(item, "title"),
      url: readTag(item, "link"),
      snippet: readTag(item, "description")
    });
  }
  return items;
}

function readTag(xml, tagName) {
  const match = xml.match(new RegExp(`<${tagName}>([\\s\\S]*?)<\\/${tagName}>`, "i"));
  return match ? decodeXml(match[1].replace(/^<!\[CDATA\[|\]\]>$/g, "").replace(/<[^>]+>/g, " ")) : "";
}

function decodeXml(value) {
  return value
    .replace(/&amp;/g, "&")
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&quot;/g, "\"")
    .replace(/&#39;|&apos;/g, "'")
    .replace(/\s+/g, " ")
    .trim();
}

function send(message) {
  if (socket?.readyState === WebSocket.OPEN) {
    socket.send(JSON.stringify(message));
  }
}

function startHeartbeat() {
  stopHeartbeat();
  heartbeatTimer = setInterval(() => send({ type: "heartbeat", at: new Date().toISOString() }), 20000);
}

function stopHeartbeat() {
  clearInterval(heartbeatTimer);
  heartbeatTimer = null;
}

function updateState(patch) {
  state = { ...state, ...patch };
  const connected = state.status === "connected" || state.status === "searching";
  chrome.action.setBadgeText({ text: connected ? "ON" : "" });
  chrome.action.setBadgeBackgroundColor({ color: connected ? "#3CBF86" : "#D84B3E" });
  chrome.runtime.sendMessage({ type: "status_changed", state }).catch(() => {});
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.type === "get_status") {
    sendResponse(state);
    return;
  }

  if (message.type === "save_pairing_code") {
    const pairingCode = String(message.pairingCode || "").replace(/\D/g, "").slice(0, 6);
    chrome.storage.local.set({ pairingCode }).then(() => {
      state.pairingCode = pairingCode;
      if (socket) {
        socket.close();
      } else {
        connect();
      }
      sendResponse({ ok: true });
    });
    return true;
  }

  if (message.type === "reconnect") {
    if (socket) {
      socket.close();
    } else {
      connect();
    }
    sendResponse({ ok: true });
  }
});

chrome.runtime.onInstalled.addListener(loadPairingCode);
chrome.runtime.onStartup.addListener(loadPairingCode);
loadPairingCode();
