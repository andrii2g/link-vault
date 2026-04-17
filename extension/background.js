const API_BASE_URL = "http://localhost:5678";
const SAVE_TIMEOUT_MS = 4000;
const SUCCESS_CLOSE_OUTCOMES = new Set(["saved", "duplicate"]);
const KNOWN_OUTCOMES = new Set(["saved", "duplicate", "invalid"]);
const CONTEXT_MENU_ID = "save-link-to-vault";
const NOTIFICATION_ICON = chrome.runtime.getURL("icons/icon-128.png");
const NOTIFICATION_MESSAGES = {
  saved: "Link saved to Vault",
  duplicate: "Link already exists in Vault",
  invalid: "Link is not a valid web URL",
  failed: "Failed to save link to Vault"
};

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({
      id: CONTEXT_MENU_ID,
      title: "Save link to Vault",
      contexts: ["link"]
    }, () => {
      if (chrome.runtime.lastError) {
        console.error("Failed to create Link Vault context menu", chrome.runtime.lastError.message);
      }
    });
  });
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.status !== "complete") {
    return;
  }

  void touchLinkUrl(tab.url);
});

chrome.contextMenus.onClicked.addListener((info) => {
  if (info.menuItemId !== CONTEXT_MENU_ID) {
    return;
  }

  handleContextMenuClick(info).catch((error) => {
    console.error("Context menu save failed", error);
    void showOutcomeNotification("failed");
  });
});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type !== "link-vault-action") {
    return false;
  }

  handleAction(message)
    .then(sendResponse)
    .catch((error) => {
      console.error("Link Vault action failed", error);
      sendResponse({
        action: message.action,
        results: [{
          outcome: "failed",
          url: null,
          detail: error instanceof Error ? error.message : String(error)
        }]
      });
    });

  return true;
});

async function handleAction(message) {
  if (message.action === "view-links") {
    await chrome.tabs.create({ url: `${API_BASE_URL}/links` });
    return { action: message.action, results: [] };
  }

  const tabs = await resolveTabs(message.action);
  const results = [];

  for (const tab of tabs) {
    const shouldClose = message.action === "save-and-close";
    const result = await saveTab(tab, shouldClose);
    results.push(result);
  }

  return { action: message.action, results };
}

async function handleContextMenuClick(info) {
  const outcome = await saveLinkUrl(info.linkUrl);
  await showOutcomeNotification(outcome);
}

async function resolveTabs(action) {
  if (action === "save-all") {
    return chrome.tabs.query({ currentWindow: true });
  }

  const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
  return tabs.length > 0 ? [tabs[0]] : [];
}

async function saveTab(tab, closeOnSuccess) {
  const outcome = await saveLinkPayload({
    url: tab.url ?? "",
    title: tab.title ?? null,
    description: null,
    tags: []
  });

  const response = {
    outcome: outcome.status,
    url: tab.url ?? "",
    title: tab.title ?? null,
    detail: outcome.message
  };

  if (closeOnSuccess && SUCCESS_CLOSE_OUTCOMES.has(outcome.status) && typeof tab.id === "number") {
    try {
      await chrome.tabs.remove(tab.id);
    } catch (error) {
      console.error("Failed to close tab", error);
    }
  }

  return response;
}

async function saveLinkUrl(linkUrl) {
  return (await saveLinkPayload({
    url: linkUrl ?? "",
    title: null,
    description: null,
    tags: []
  })).status;
}

async function saveLinkPayload(payload) {
  if (!isSupportedHttpUrl(payload.url)) {
    return {
      status: "invalid",
      message: NOTIFICATION_MESSAGES.invalid
    };
  }

  try {
    return await postJson("POST", `${API_BASE_URL}/links`, payload, true);
  } catch (error) {
    return {
      status: "failed",
      message: error instanceof Error ? error.message : NOTIFICATION_MESSAGES.failed
    };
  }
}

async function touchLinkUrl(linkUrl) {
  if (!isSupportedHttpUrl(linkUrl)) {
    return;
  }

  try {
    await postJson("PATCH", `${API_BASE_URL}/links`, { url: linkUrl }, false);
  } catch {
  }
}

function isSupportedHttpUrl(url) {
  if (!url || typeof url !== "string") {
    return false;
  }

  try {
    const parsed = new URL(url);
    return parsed.protocol === "http:" || parsed.protocol === "https:";
  } catch {
    return false;
  }
}

async function postJson(method, url, payload, strictBusinessContract) {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), SAVE_TIMEOUT_MS);

  try {
    const response = await fetch(url, {
      method,
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify(payload),
      signal: controller.signal
    });

    if (!response.ok) {
      throw new Error(`Server returned ${response.status}`);
    }

    const body = await response.json();
    if (!body || typeof body.status !== "string") {
      throw new Error("API returned an invalid JSON contract.");
    }

    if (strictBusinessContract && !KNOWN_OUTCOMES.has(body.status)) {
      throw new Error("API returned an invalid JSON contract.");
    }

    return body;
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      throw new Error("Request timed out after 4 seconds.");
    }

    throw error;
  } finally {
    clearTimeout(timeoutId);
  }
}

async function showOutcomeNotification(outcome) {
  const message = NOTIFICATION_MESSAGES[outcome] ?? NOTIFICATION_MESSAGES.failed;

  await chrome.notifications.create({
    type: "basic",
    iconUrl: NOTIFICATION_ICON,
    title: "Link Vault",
    message
  });
}