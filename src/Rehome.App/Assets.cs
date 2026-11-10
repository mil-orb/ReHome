using Microsoft.Web.WebView2.Core;

namespace Rehome.Shell;

/// <summary>Serves the UI compiled into this exe. Anything not in the build is a 404.</summary>
static class Assets
{
    // No inline scripts, nothing remote, no frames, no forms. Icons arrive as data: images.
    const string Csp =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'";

    static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".woff2"] = "font/woff2",
    };

    public static CoreWebView2WebResourceResponse Serve(CoreWebView2Environment env, string uri, string origin)
    {
        var path = new Uri(uri).AbsolutePath.TrimStart('/');
        var stream = typeof(Assets).Assembly.GetManifestResourceStream("ui/" + path);

        if (stream is null || !Types.TryGetValue(Path.GetExtension(path), out var type))
        {
            stream?.Dispose();
            return env.CreateWebResourceResponse(null, 404, "Not Found", "");
        }

        var headers =
            $"Content-Type: {type}\r\n" +
            $"Content-Security-Policy: {Csp}\r\n" +
            "X-Content-Type-Options: nosniff\r\n" +
            "Cache-Control: no-store";

        return env.CreateWebResourceResponse(stream, 200, "OK", headers);
    }
}
