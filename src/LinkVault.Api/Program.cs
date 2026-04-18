using LinkVault.Api.Contracts;
using LinkVault.Api.Options;
using LinkVault.Api.Serialization;
using LinkVault.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Text;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});

var listenUrls = LinkVaultConfiguration.GetValidatedListenUrls(builder.Configuration);
var allowedOrigins = LinkVaultConfiguration.GetAllowedExtensionOrigins(builder.Configuration);

builder.WebHost.UseUrls(listenUrls);

builder.Services.Configure<LinkVaultOptions>(options =>
{
    options.DataPath = LinkVaultConfiguration.GetDataPath(builder.Configuration);
    options.AllowedExtensionOrigins = allowedOrigins;
});
builder.Services.ConfigureHttpJsonOptions(options => UtcDateTimeOffsetJsonConverters.Apply(options.SerializerOptions));

builder.Services.AddSingleton<LinkStore>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("ExtensionOnly", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .WithMethods("GET", "POST", "PATCH", "DELETE");
    });
});

var app = builder.Build();

app.UseCors("ExtensionOnly");

await app.Services.GetRequiredService<LinkStore>()
    .LogStartupStorageStatusAsync(app.Logger, app.Lifetime.ApplicationStopping);

app.MapGet("/health", () => Results.Text("OK", "text/plain"));

app.MapGet("/links", async Task<Results<ContentHttpResult, ProblemHttpResult>>(LinkStore store, CancellationToken cancellationToken) =>
{
    var result = await store.GetAllAsync(cancellationToken);
    if (!result.Success)
    {
        return TypedResults.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Storage failure",
            detail: "The link store is unavailable. Check the API logs for details.");
    }

    return TypedResults.Content(LinkVaultPageRenderer.Render(result.Items), "text/html");
});

app.MapPost("/links", async Task<Results<JsonHttpResult<SaveLinkResponse>, ProblemHttpResult>> (
    SaveLinkRequest request,
    LinkStore store,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("LinkVault.SaveEndpoint");

    if (!SaveLinkRequest.TryNormalize(request, out var normalized, out var validationMessage))
    {
        logger.LogWarning("Rejected invalid save request for URL '{Url}'", request.Url);
        return TypedResults.Json(SaveLinkResponse.Invalid(validationMessage));
    }

    var result = await store.SaveAsync(normalized, cancellationToken);
    return result switch
    {
        SaveResult.Saved => TypedResults.Json(SaveLinkResponse.Saved()),
        SaveResult.Duplicate => TypedResults.Json(SaveLinkResponse.Duplicate()),
        SaveResult.StorageFailure => TypedResults.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Storage failure",
            detail: "The link store is unavailable. Check the API logs for details."),
        _ => TypedResults.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Unexpected failure",
            detail: "The API failed to process the request.")
    };
});

app.MapPatch("/links", async Task<Results<JsonHttpResult<TouchLinkResponse>, ProblemHttpResult>> (
    TouchLinkRequest request,
    LinkStore store,
    CancellationToken cancellationToken) =>
{
    if (!TouchLinkRequest.TryNormalize(request, out var normalizedUrl))
    {
        return TypedResults.Json(TouchLinkResponse.Invalid());
    }

    var result = await store.TouchAsync(normalizedUrl, cancellationToken);
    return result switch
    {
        TouchResult.Updated => TypedResults.Json(TouchLinkResponse.Updated()),
        TouchResult.Ignored => TypedResults.Json(TouchLinkResponse.Ignored()),
        TouchResult.StorageFailure => TypedResults.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Storage failure",
            detail: "The link store is unavailable. Check the API logs for details."),
        _ => TypedResults.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Unexpected failure",
            detail: "The API failed to process the request.")
    };
});

app.MapDelete("/links", async Task<Results<JsonHttpResult<DeleteLinkResponse>, ProblemHttpResult>> (
    Guid id,
    LinkStore store,
    CancellationToken cancellationToken) =>
{
    if (id == Guid.Empty)
    {
        return TypedResults.Json(DeleteLinkResponse.Invalid());
    }

    var result = await store.DeleteAsync(id, cancellationToken);
    return result switch
    {
        DeleteResult.Deleted => TypedResults.Json(DeleteLinkResponse.Deleted()),
        DeleteResult.Ignored => TypedResults.Json(DeleteLinkResponse.Ignored()),
        DeleteResult.StorageFailure => TypedResults.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Storage failure",
            detail: "The link store is unavailable. Check the API logs for details."),
        _ => TypedResults.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Unexpected failure",
            detail: "The API failed to process the request.")
    };
});

app.Run();

public partial class Program;

internal static class LinkVaultPageRenderer
{
    public static string Render(IReadOnlyList<LinkVault.Api.Models.UrlItem> items)
    {
        var encoder = HtmlEncoder.Default;
        var markup = new StringBuilder();

        foreach (var item in items.OrderByDescending(link => link.CreatedAt))
        {
            markup.Append("<article class=\"card\" data-link-id=\"");
            markup.Append(encoder.Encode(item.Id.ToString()));
            markup.Append("\">");
            markup.Append("<div class=\"card-header\">");
            markup.Append("<div class=\"meta-group\">");
            markup.Append("<div class=\"meta\">Saved ");
            markup.Append(encoder.Encode(item.CreatedAt.ToString("u")));
            markup.Append("</div>");

            if (item.UpdatedAt is not null)
            {
                markup.Append("<div class=\"meta\">Opened ");
                markup.Append(encoder.Encode(item.UpdatedAt.Value.ToString("u")));
                markup.Append("</div>");
            }
            markup.Append("</div>");
            markup.Append("<button class=\"delete-button\" type=\"button\" data-delete-id=\"");
            markup.Append(encoder.Encode(item.Id.ToString()));
            markup.Append("\">Delete</button>");
            markup.Append("</div>");

            if (!string.IsNullOrWhiteSpace(item.Title))
            {
                markup.Append("<h2>");
                markup.Append(encoder.Encode(item.Title));
                markup.Append("</h2>");
            }

            markup.Append("<a class=\"url\" href=\"");
            markup.Append(encoder.Encode(item.Url));
            markup.Append("\">");
            markup.Append(encoder.Encode(item.Url));
            markup.Append("</a>");

            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                markup.Append("<p>");
                markup.Append(encoder.Encode(item.Description));
                markup.Append("</p>");
            }

            if (item.Tags.Length > 0)
            {
                markup.Append("<ul class=\"tags\">");
                foreach (var tag in item.Tags)
                {
                    markup.Append("<li>");
                    markup.Append(encoder.Encode(tag));
                    markup.Append("</li>");
                }
                markup.Append("</ul>");
            }

            markup.Append("</article>");
        }

        var content = markup.Length == 0
            ? "<p class=\"empty\">No links saved yet.</p>"
            : markup.ToString();

        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Link Vault</title>
  <style>
    :root {
      color-scheme: light;
      --bg: #f5efe4;
      --paper: #fffdf8;
      --ink: #1f2228;
      --accent: #b6532f;
      --line: #decdb0;
      --muted: #6e6658;
      --danger: #9c2f1a;
      --danger-bg: #fff2ed;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: Georgia, "Times New Roman", serif;
      color: var(--ink);
      background: radial-gradient(circle at top, #fff8eb 0%, var(--bg) 70%);
    }
    main {
      max-width: 920px;
      margin: 0 auto;
      padding: 28px 18px 56px;
    }
    h1 {
      margin: 0;
      font-size: clamp(2rem, 5vw, 3.5rem);
      line-height: 1;
    }
    .eyebrow {
      margin: 0 0 8px;
      text-transform: uppercase;
      letter-spacing: 0.12em;
      font-size: 0.75rem;
      color: var(--accent);
    }
    .subtitle {
      margin: 12px 0 26px;
      color: var(--muted);
      font-size: 1rem;
    }
    .grid {
      display: grid;
      gap: 14px;
    }
    .card {
      background: var(--paper);
      border: 1px solid var(--line);
      border-radius: 16px;
      padding: 16px 18px;
      box-shadow: 0 8px 24px rgba(70, 43, 20, 0.06);
    }
    .card-header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 16px;
      margin-bottom: 8px;
    }
    .meta-group {
      min-width: 0;
    }
    .meta {
      color: var(--muted);
      font-size: 0.85rem;
      margin-bottom: 6px;
    }
    .delete-button {
      flex: 0 0 auto;
      border: 1px solid rgba(156, 47, 26, 0.28);
      background: var(--danger-bg);
      color: var(--danger);
      border-radius: 999px;
      padding: 8px 12px;
      font: inherit;
      font-size: 0.85rem;
      cursor: pointer;
      transition: background-color 120ms ease, transform 120ms ease;
    }
    .delete-button:hover {
      background: #ffe5dc;
      transform: translateY(-1px);
    }
    .delete-button:disabled {
      opacity: 0.6;
      cursor: wait;
      transform: none;
    }
    h2 {
      margin: 0 0 8px;
      font-size: 1.25rem;
      line-height: 1.2;
    }
    .url {
      display: inline-block;
      margin-bottom: 10px;
      color: var(--accent);
      text-decoration: none;
      word-break: break-word;
    }
    .url:hover { text-decoration: underline; }
    p {
      margin: 0 0 12px;
      line-height: 1.5;
    }
    .tags {
      list-style: none;
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      padding: 0;
      margin: 0;
    }
    .tags li {
      border: 1px solid var(--line);
      border-radius: 999px;
      padding: 4px 10px;
      background: #fff7e6;
      font-size: 0.85rem;
      color: var(--muted);
    }
    .empty {
      padding: 20px;
      border: 1px dashed var(--line);
      border-radius: 16px;
      background: rgba(255,255,255,0.6);
    }
    @media (max-width: 640px) {
      .card-header {
        flex-direction: column;
        align-items: stretch;
      }
      .delete-button {
        align-self: flex-start;
      }
    }
  </style>
</head>
<body>
  <main>
    <p class="eyebrow">Link Vault</p>
    <h1>Saved links</h1>
    <p class="subtitle">Local view of the store at %LOCALAPPDATA%\LinkVault\links.json.</p>
    <section class="grid" id="links-grid">
      {{content}}
    </section>
  </main>
  <script>
    const grid = document.getElementById("links-grid");

    async function deleteLink(id, button) {
      button.disabled = true;
      button.textContent = "Deleting...";

      try {
        const response = await fetch(`/links?id=${encodeURIComponent(id)}`, {
          method: "DELETE"
        });

        if (!response.ok) {
          throw new Error("Delete request failed.");
        }

        const result = await response.json();
        if (result.status === "deleted") {
          document.querySelector(`[data-link-id="${CSS.escape(id)}"]`)?.remove();
          ensureEmptyState();
          return;
        }

        button.disabled = false;
        button.textContent = "Delete";
      } catch {
        button.disabled = false;
        button.textContent = "Delete";
        window.alert("Failed to delete link.");
      }
    }

    function ensureEmptyState() {
      if (grid.querySelector(".card")) {
        return;
      }

      if (!grid.querySelector(".empty")) {
        const empty = document.createElement("p");
        empty.className = "empty";
        empty.textContent = "No links saved yet.";
        grid.appendChild(empty);
      }
    }

    grid.addEventListener("click", event => {
      const button = event.target.closest("[data-delete-id]");
      if (!button) {
        return;
      }

      const id = button.getAttribute("data-delete-id");
      if (!id || !window.confirm("Delete this link from the vault?")) {
        return;
      }

      deleteLink(id, button);
    });
  </script>
</body>
</html>
""";
    }
}
