using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Repertoire.Api.StaticApp;

/// <summary>
/// Serves the frontend — index.html, views/, engine/ — from the same origin as /api, exactly as
/// the Python backend did. Same origin is not a convenience here: the app fetches "/api/..."
/// relatively, and the COEP/CORP headers below only hold together within one origin.
/// </summary>
public static class StaticAppExtensions
{
    public static WebApplication UseRepertoireStaticApp(this WebApplication app)
    {
        string root = ResolveRoot(app.Environment, app.Configuration);
        app.Logger.LogInformation("Serving the frontend from {StaticRoot}", root);

        if (!Directory.Exists(root))
        {
            // Not fatal: the API is still useful on its own, and this is far easier to diagnose
            // from a warning than from a wall of 404s.
            app.Logger.LogWarning("Static root {StaticRoot} does not exist — the app will 404", root);
            return app;
        }

        PhysicalFileProvider files = new(root);

        FileExtensionContentTypeProvider contentTypes = new();
        // Browsers refuse to instantiate WASM served as octet-stream. This mapping ships in the
        // default provider, but Stockfish silently dying is an expensive thing to leave to chance.
        contentTypes.Mappings[".wasm"] = "application/wasm";
        contentTypes.Mappings[".js"] = "text/javascript";

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            ContentTypeProvider = contentTypes,
            ServeUnknownFileTypes = false
        });

        return app;
    }

    /// <summary>
    /// Adds the headers the Python backend set on every response.
    ///
    /// COOP+COEP are what make SharedArrayBuffer available, which a future multi-threaded
    /// Stockfish needs; COEP in turn requires CORP on the assets themselves. The no-cache trio
    /// means a freshly edited index.html is never served stale — this app has no build step, so
    /// "edit the file, reload" has to actually work.
    /// </summary>
    public static IApplicationBuilder UseCrossOriginIsolationAndNoCache(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            IHeaderDictionary headers = context.Response.Headers;
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Embedder-Policy"] = "require-corp";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
            headers.Pragma = "no-cache";
            headers.Expires = "0";

            await next();
        });

    /// <summary>
    /// Where the frontend lives. Configured wins (the container sets StaticApp__Root=/app/wwwroot);
    /// otherwise walk up from the content root until index.html turns up, which finds the repo root
    /// from dotnet/src/Api/Repertoire.Api during local development without hard-coding "../../../..".
    /// </summary>
    internal static string ResolveRoot(IWebHostEnvironment environment, IConfiguration configuration)
    {
        string? configured = configuration["StaticApp:Root"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        for (DirectoryInfo? directory = new(environment.ContentRootPath); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "index.html")))
            {
                return directory.FullName;
            }
        }

        return environment.ContentRootPath;
    }
}
