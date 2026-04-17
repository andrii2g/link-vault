const API_BASE_URL = "http://localhost:5678";
const SAVE_TIMEOUT_MS = 4000;
const SUCCESS_CLOSE_OUTCOMES = new Set(["saved", "duplicate"]);
const KNOWN_OUTCOMES = new Set(["saved", "duplicate", "invalid"]);

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
  const tabs = await resolveTabs(message.action);
  const results = [];

  for (const tab of tabs) {
    const shouldClose = message.action === "save-and-close" || message.action === "save-all";
    const result = await saveTab(tab, shouldClose);
    results.push(result);
  }

  return { action: message.action, results };
}

async function resolveTabs(action) {
  if (action === "save-all") {
    return chrome.tabs.query({ currentWindow: true });
  }

  const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
  return tabs.length > 0 ? [tabs[0]] : [];
}

async function saveTab(tab, closeOnSuccess) {
  const payload = {
    url: tab.url ?? "",
    title: tab.title ?? null,
    description: null,
    tags: []
  };

  let result;
  try {
    result = await postSave(payload);
  } catch (error) {
    return {
      outcome: "failed",
      url: payload.url,
      title: payload.title,
      detail: error instanceof Error ? error.message : String(error)
    };
  }

  const response = {
    outcome: result.status,
    url: payload.url,
    title: payload.title,
    detail: result.message
  };

  if (closeOnSuccess && SUCCESS_CLOSE_OUTCOMES.has(result.status) && typeof tab.id === "number") {
    try {
      await chrome.tabs.remove(tab.id);
    } catch (error) {
      console.error("Failed to close tab", error);
    }
  }

  return response;
}

async function postSave(payload) {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), SAVE_TIMEOUT_MS);

  try {
    const response = await fetch(`${API_BASE_URL}/save`, {
      method: "POST",
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
    if (!body || !KNOWN_OUTCOMES.has(body.status) || typeof body.message !== "string") {
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