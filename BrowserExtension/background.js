const HOST_NAME = "com.betterwintab.tabs";
let port;
let reconnectTimer;
let pendingTabsTimer;

function browserName() {
  if (navigator.brave) return "Brave";
  const userAgent = navigator.userAgent;
  if (userAgent.includes("Edg/")) return "Edge";
  if (userAgent.includes("Brave")) return "Brave";
  return "Chrome";
}

function connect() {
  if (port) return;

  try {
    port = chrome.runtime.connectNative(HOST_NAME);
    port.onMessage.addListener(handleNativeMessage);
    port.onDisconnect.addListener(() => {
      port = undefined;
      clearTimeout(reconnectTimer);
      reconnectTimer = setTimeout(connect, 3000);
    });
    sendHello();
  } catch {
    port = undefined;
    clearTimeout(reconnectTimer);
    reconnectTimer = setTimeout(connect, 3000);
  }
}

function send(message) {
  if (!port) {
    connect();
    return;
  }

  try {
    port.postMessage(message);
  } catch {
    port = undefined;
    connect();
  }
}

async function sendHello() {
  send({ type: "hello", browser: browserName() });
  await publishTabs();
}

async function publishTabs() {
  const tabs = await chrome.tabs.query({});
  send({
    type: "tabs",
    browser: browserName(),
    tabs: tabs
      .filter(tab => Number.isInteger(tab.id))
      .map(tab => ({
        id: tab.id,
        windowId: tab.windowId,
        title: tab.title || "",
        url: tab.url || "",
        favIconUrl: getFaviconUrl(tab),
        active: Boolean(tab.active),
        index: tab.index
      }))
  });
}

function getFaviconUrl(tab) {
  if (tab.favIconUrl && /^https?:\/\//i.test(tab.favIconUrl)) {
    return tab.favIconUrl;
  }

  if (tab.url && /^https?:\/\//i.test(tab.url)) {
    return `https://www.google.com/s2/favicons?sz=64&domain_url=${encodeURIComponent(tab.url)}`;
  }

  return "";
}

function schedulePublish() {
  clearTimeout(pendingTabsTimer);
  pendingTabsTimer = setTimeout(publishTabs, 80);
}

function handleNativeMessage(message) {
  if (!message || message.type !== "activate") return;
  const tabId = Number(message.tabId);
  if (!Number.isInteger(tabId)) return;

  chrome.tabs.update(tabId, { active: true })
    .then(() => chrome.windows.update(Number(message.windowId), { focused: true }))
    .catch(() => undefined);
}

chrome.tabs.onCreated.addListener(schedulePublish);
chrome.tabs.onRemoved.addListener(schedulePublish);
chrome.tabs.onUpdated.addListener(schedulePublish);
chrome.tabs.onActivated.addListener(schedulePublish);
chrome.tabs.onAttached.addListener(schedulePublish);
chrome.tabs.onDetached.addListener(schedulePublish);
chrome.windows.onCreated.addListener(schedulePublish);
chrome.windows.onRemoved.addListener(schedulePublish);
chrome.windows.onFocusChanged.addListener(schedulePublish);

connect();
