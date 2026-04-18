using System.Text;
using System.Text.Encodings.Web;

namespace LinkVault.Api;

internal static class LinkVaultPageRenderer
{
    private static readonly Lazy<string> PageTemplate = new(() => ReadTemplate("links.html"));

    private static readonly Lazy<string> ItemTemplate = new(() => ReadTemplate("link-item.html"));

    public static string Render(IReadOnlyList<LinkVault.Api.Models.UrlItem> items)
    {
        var encoder = HtmlEncoder.Default;
        var markup = new StringBuilder();

        foreach (var item in items.OrderByDescending(link => link.CreatedAt))
        {
            markup.Append(RenderItemTemplate(item, encoder));
        }

        var content = markup.Length == 0
            ? "<p class=\"empty\">No links saved yet.</p>"
            : markup.ToString();

        return PageTemplate.Value
            .Replace("{{content}}", content, StringComparison.Ordinal)
            .Replace("{{linksRoute}}", AppRoutes.Links, StringComparison.Ordinal);
    }

    private static string RenderItemTemplate(LinkVault.Api.Models.UrlItem item, HtmlEncoder encoder)
    {
        return ItemTemplate.Value
            .Replace("{{id}}", encoder.Encode(item.Id.ToString()), StringComparison.Ordinal)
            .Replace("{{createdAt}}", encoder.Encode(item.CreatedAt.ToString("u")), StringComparison.Ordinal)
            .Replace("{{updatedAtBlock}}", RenderUpdatedAtBlock(item, encoder), StringComparison.Ordinal)
            .Replace("{{titleBlock}}", RenderTitleBlock(item, encoder), StringComparison.Ordinal)
            .Replace("{{url}}", encoder.Encode(item.Url), StringComparison.Ordinal)
            .Replace("{{descriptionBlock}}", RenderDescriptionBlock(item, encoder), StringComparison.Ordinal)
            .Replace("{{tagsBlock}}", RenderTagsBlock(item, encoder), StringComparison.Ordinal);
    }

    private static string RenderUpdatedAtBlock(LinkVault.Api.Models.UrlItem item, HtmlEncoder encoder)
    {
        return item.UpdatedAt is null
            ? string.Empty
            : $"<div class=\"meta\">Opened {encoder.Encode(item.UpdatedAt.Value.ToString("u"))}</div>";
    }

    private static string RenderTitleBlock(LinkVault.Api.Models.UrlItem item, HtmlEncoder encoder)
    {
        return string.IsNullOrWhiteSpace(item.Title)
            ? string.Empty
            : $"<h2>{encoder.Encode(item.Title)}</h2>";
    }

    private static string RenderDescriptionBlock(LinkVault.Api.Models.UrlItem item, HtmlEncoder encoder)
    {
        return string.IsNullOrWhiteSpace(item.Description)
            ? string.Empty
            : $"<p>{encoder.Encode(item.Description)}</p>";
    }

    private static string RenderTagsBlock(LinkVault.Api.Models.UrlItem item, HtmlEncoder encoder)
    {
        if (item.Tags.Length == 0)
        {
            return string.Empty;
        }

        var tags = new StringBuilder();
        tags.Append("<ul class=\"tags\">");
        foreach (var tag in item.Tags)
        {
            tags.Append("<li>");
            tags.Append(encoder.Encode(tag));
            tags.Append("</li>");
        }
        tags.Append("</ul>");
        return tags.ToString();
    }

    private static string ReadTemplate(string fileName)
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "Templates", fileName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Could not find template '{fileName}' in '{filePath}'.");
        }

        return File.ReadAllText(filePath);
    }
}
