importScripts("config.js");

const SAVE_TIMEOUT_MS = 4000;
const SUCCESS_CLOSE_OUTCOMES = new Set(["saved", "duplicate"]);
const KNOWN_OUTCOMES = new Set(["saved", "duplicate", "invalid"]);
const CONTEXT_MENU_ID = "save-link-to-vault";
const NOTIFICATION_ICON = "data:image/png;base64,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";
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
    await chrome.tabs.create({ url: buildApiUrl(LINK_VAULT_EXTENSION_CONFIG.routes.links) });
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
    return await postJson("POST", buildApiUrl(LINK_VAULT_EXTENSION_CONFIG.routes.links), payload, true);
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
    await postJson("PATCH", buildApiUrl(LINK_VAULT_EXTENSION_CONFIG.routes.links), { url: linkUrl }, false);
  } catch {
  }
}

function buildApiUrl(path) {
  return `${LINK_VAULT_EXTENSION_CONFIG.apiOrigin}${path}`;
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
