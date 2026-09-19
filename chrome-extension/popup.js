const statusBadge = document.querySelector("#statusBadge");
const statusText = document.querySelector("#statusText");
const pairingCode = document.querySelector("#pairingCode");
const appVersion = document.querySelector("#appVersion");
const lastQuery = document.querySelector("#lastQuery");
const resultCount = document.querySelector("#resultCount");

function render(state) {
  pairingCode.value = state.pairingCode || pairingCode.value;
  appVersion.textContent = state.appVersion ? `v${state.appVersion}` : "—";
  lastQuery.textContent = state.lastQuery || "—";
  resultCount.textContent = state.lastResultCount || 0;
  const labels = {
    connected: ["CONNECTED", "เชื่อมต่อกับแอปแล้ว"],
    searching: ["SEARCHING", "กำลังค้นเว็บและส่งผลกลับแอป"],
    connecting: ["CONNECTING", "กำลังเชื่อมต่อ localhost:37821"],
    rejected: ["REJECTED", state.error || "รหัสจับคู่ไม่ถูกต้อง"],
    error: ["ERROR", state.error || "เชื่อมต่อไม่สำเร็จ"],
    needs_code: ["PAIR CODE", "ใส่รหัส 6 หลักจากหน้า Browser Search Sync"],
    waiting: ["WAITING", "กำลังรอแอปหรือพยายามเชื่อมต่อใหม่"]
  };
  const [badge, text] = labels[state.status] || labels.waiting;
  statusBadge.textContent = badge;
  statusBadge.dataset.status = state.status;
  statusText.textContent = text;
}

document.querySelector("#connectButton").addEventListener("click", async () => {
  const code = pairingCode.value.replace(/\D/g, "").slice(0, 6);
  if (code.length !== 6) {
    statusText.textContent = "รหัสจับคู่ต้องมี 6 หลัก";
    return;
  }
  await chrome.runtime.sendMessage({ type: "save_pairing_code", pairingCode: code });
});

document.querySelector("#reconnectButton").addEventListener("click", () => {
  chrome.runtime.sendMessage({ type: "reconnect" });
});

pairingCode.addEventListener("keydown", event => {
  if (event.key === "Enter") {
    document.querySelector("#connectButton").click();
  }
});

chrome.runtime.onMessage.addListener(message => {
  if (message.type === "status_changed") {
    render(message.state);
  }
});

chrome.runtime.sendMessage({ type: "get_status" }).then(render);
