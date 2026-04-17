using LinkVault.Api.Contracts;
using LinkVault.Api.Options;
using LinkVault.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

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

builder.Services.AddSingleton<LinkStore>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("ExtensionOnly", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .WithMethods("GET", "POST");
    });
});

var app = builder.Build();

app.UseCors("ExtensionOnly");

await app.Services.GetRequiredService<LinkStore>()
    .LogStartupStorageStatusAsync(app.Logger, app.Lifetime.ApplicationStopping);

app.MapGet("/health", () => Results.Text("OK", "text/plain"));

app.MapPost("/save", async Task<Results<JsonHttpResult<SaveLinkResponse>, ProblemHttpResult>> (
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

app.Run();

public partial class Program;