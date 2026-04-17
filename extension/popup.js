const buttons = {
  save: document.getElementById("save-button"),
  saveAndClose: document.getElementById("save-close-button"),
  saveAll: document.getElementById("save-all-button"),
  viewLinks: document.getElementById("view-links-button")
};
const statusNode = document.getElementById("status");
const resultsNode = document.getElementById("results");

buttons.save.addEventListener("click", () => runAction("save"));
buttons.saveAndClose.addEventListener("click", () => runAction("save-and-close"));
buttons.saveAll.addEventListener("click", () => runAction("save-all"));
buttons.viewLinks.addEventListener("click", openLinksView);

async function runAction(action) {
  setBusy(true);
  statusNode.textContent = "Saving...";
  resultsNode.replaceChildren();

  try {
    const response = await chrome.runtime.sendMessage({ type: "link-vault-action", action });
    renderResults(response?.results ?? []);
    statusNode.textContent = summarize(response?.results ?? []);
  } catch (error) {
    statusNode.textContent = error instanceof Error ? error.message : String(error);
  } finally {
    setBusy(false);
  }
}

async function openLinksView() {
  setBusy(true);
  statusNode.textContent = "Opening links...";
  resultsNode.replaceChildren();

  try {
    await chrome.runtime.sendMessage({ type: "link-vault-action", action: "view-links" });
    statusNode.textContent = "Opened links view.";
  } catch (error) {
    statusNode.textContent = error instanceof Error ? error.message : String(error);
  } finally {
    setBusy(false);
  }
}

function renderResults(results) {
  if (results.length === 0) {
    return;
  }

  for (const result of results) {
    const item = document.createElement("li");
    const outcome = document.createElement("span");
    outcome.className = "outcome";
    outcome.textContent = result.outcome;
    item.appendChild(outcome);

    if (result.detail) {
      const detail = document.createElement("div");
      detail.textContent = result.detail;
      item.appendChild(detail);
    }

    if (result.url) {
      const url = document.createElement("span");
      url.className = "url";
      url.textContent = result.url;
      item.appendChild(url);
    }

    resultsNode.appendChild(item);
  }
}

function summarize(results) {
  if (results.length === 0) {
    return "No tabs found.";
  }

  const counts = results.reduce((accumulator, result) => {
    accumulator[result.outcome] = (accumulator[result.outcome] || 0) + 1;
    return accumulator;
  }, {});

  return Object.entries(counts)
    .map(([outcome, count]) => `${count} ${outcome}`)
    .join(", ");
}

function setBusy(isBusy) {
  Object.values(buttons).forEach((button) => {
    button.disabled = isBusy;
  });
}