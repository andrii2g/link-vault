using LinkVault.Api;
using LinkVault.Api.Contracts;
using LinkVault.Api.Options;
using LinkVault.Api.Serialization;
using LinkVault.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;

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

app.MapGet(AppRoutes.Health, () => Results.Text("OK", "text/plain"));

app.MapGet(AppRoutes.Links, async Task<Results<ContentHttpResult, ProblemHttpResult>>(LinkStore store, CancellationToken cancellationToken) =>
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

app.MapPost(AppRoutes.Links, async Task<Results<JsonHttpResult<SaveLinkResponse>, ProblemHttpResult>> (
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

app.MapPatch(AppRoutes.Links, async Task<Results<JsonHttpResult<TouchLinkResponse>, ProblemHttpResult>> (
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

app.MapDelete(AppRoutes.Links, async Task<Results<JsonHttpResult<DeleteLinkResponse>, ProblemHttpResult>> (
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
