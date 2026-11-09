using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpanFlow.Connectors.Http;

/// <summary>
/// The body types an api request can send (E68-T1, D68.2). A request without <c>bodyType</c> sends its
/// <c>body</c> as JSON, exactly as every request did before E68; <c>json</c> is that same behaviour named.
/// </summary>
public static class HttpBodyTypes
{
    public const string None = "none";
    public const string FormData = "formData";
    public const string Urlencoded = "urlencoded";
    public const string Raw = "raw";
    public const string Binary = "binary";
    public const string GraphQl = "graphql";
    public const string Json = "json";

    public static readonly IReadOnlyList<string> All = [None, FormData, Urlencoded, Raw, Binary, GraphQl, Json];
}

/// <summary>
/// Per-request settings (E68-T1, D68.12): <c>{"timeoutSeconds", "followRedirects", "encodeUrl",
/// "sendNoCache", "maxResponseBytes"}</c>. Every field is optional and the defaults are what a request
/// did before E68: no extra timeout, redirects followed, URL parts encoded, no cache header, no size cap.
/// </summary>
public sealed record HttpRequestSettings(
    int? TimeoutSeconds, bool FollowRedirects, bool EncodeUrl, bool SendNoCache, long? MaxResponseBytes)
{
    public static readonly HttpRequestSettings Default = new(null, true, true, false, null);

    public static HttpRequestSettings Parse(JsonNode? node)
    {
        if (node is not JsonObject settings) return Default;
        return new HttpRequestSettings(
            PositiveInt(settings["timeoutSeconds"]),
            Bool(settings["followRedirects"], true),
            Bool(settings["encodeUrl"], true),
            Bool(settings["sendNoCache"], false),
            settings["maxResponseBytes"] is JsonNode m && long.TryParse(m.ToString(), out var max) && max > 0 ? max : null);
    }

    private static int? PositiveInt(JsonNode? node) =>
        node is not null && int.TryParse(node.ToString(), out var value) && value > 0 ? value : null;

    private static bool Bool(JsonNode? node, bool fallback) => node switch
    {
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<string>(out var s) && bool.TryParse(s, out var parsed) => parsed,
        _ => fallback,
    };
}

/// <summary>
/// Builds the URL, headers and body of one api request from its JSON shape (E68-T1, D68.1 to D68.3).
/// Values arrive already rendered (the step runner renders every string of a request against the run
/// before the connector sees it), so each param, header, form field and path variable is one value here,
/// and encoding happens per value: a value holding <c>&amp;</c>, <c>=</c> or a quote can never change the
/// shape of the request. Every v2 key is optional, and a request that uses none of them is built exactly
/// as before.
/// </summary>
public static partial class HttpRequestBuilder
{
    /// <summary>Headers that belong to the body, not the request; a request header row naming one sets it on the content.</summary>
    private static readonly HashSet<string> ContentHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Allow", "Content-Disposition", "Content-Encoding", "Content-Language", "Content-Length",
        "Content-Location", "Content-MD5", "Content-Range", "Content-Type", "Expires", "Last-Modified",
    };

    [GeneratedRegex(@"(?<=/):(?<name>[A-Za-z_][A-Za-z0-9_\-]*)(?=[/?#]|$)")]
    private static partial Regex PathVariable();

    /// <summary>
    /// The request URL: the connection's base URL, the path with <c>:name</c> path variables filled in, then
    /// the enabled <c>params</c> rows (and the <c>query</c> object, its older spelling) appended.
    /// Path variables are replaced only when the request has <c>pathVariables</c>; an unknown name is left
    /// as written, as Postman does.
    /// </summary>
    public static Uri BuildUri(string baseUrl, JsonObject request, HttpRequestSettings settings)
    {
        var path = request["path"]?.GetValue<string>() ?? "";

        if (request["pathVariables"] is JsonNode variablesNode)
        {
            var variables = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value, _) in Rows(variablesNode)) variables[key] = Text(value);
            path = PathVariable().Replace(path, match =>
                variables.TryGetValue(match.Groups["name"].Value, out var value)
                    ? settings.EncodeUrl ? Uri.EscapeDataString(value) : value
                    : match.Value);
        }

        var query = new StringBuilder();
        foreach (var (key, value, _) in Rows(request["params"], expandArrays: true)
                     .Concat(request["query"] is JsonObject legacyQuery ? Rows(legacyQuery, expandArrays: true) : []))
        {
            if (query.Length > 0) query.Append('&');
            var text = Text(value);
            query.Append(settings.EncodeUrl ? Uri.EscapeDataString(key) : key)
                 .Append('=')
                 .Append(settings.EncodeUrl ? Uri.EscapeDataString(text) : text);
        }
        if (query.Length > 0) path += (path.Contains('?') ? "&" : "?") + query;

        var relative = path.TrimStart('/');
        // A first segment holding a colon ("items:batchGet", Google-style custom methods) would otherwise be
        // read as a URL scheme. An absolute http(s) URL (a Link-header next page) is kept as it is.
        var firstSegment = relative.Split('/', '?', '#')[0];
        if (firstSegment.Contains(':') && !IsAbsoluteHttp(relative)) relative = "./" + relative;
        return new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), relative);
    }

    private static bool IsAbsoluteHttp(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// The body. Without <c>bodyType</c>: <paramref name="explicitBody"/> (a deliver's payload) or the
    /// request's <c>body</c>, as JSON, which is what every request did before E68. With a <c>bodyType</c>,
    /// the request's own <c>body</c> wins when it has one; a deliver with no body of its own encodes the
    /// payload in that type instead (an object's fields become form fields).
    /// </summary>
    public static HttpContent? BuildContent(JsonObject request, JsonNode? explicitBody)
    {
        var bodyType = request["bodyType"]?.GetValue<string>();
        if (bodyType is null)
        {
            var legacy = explicitBody ?? request["body"];
            return legacy is null
                ? null
                : new StringContent(legacy.ToJsonString(), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        }

        var body = request["body"] ?? explicitBody;
        var fromPayload = request["body"] is null;
        return bodyType switch
        {
            HttpBodyTypes.None => null,
            HttpBodyTypes.Json => body is null ? null : JsonContent(body),
            HttpBodyTypes.Urlencoded => Urlencoded(body, fromPayload),
            HttpBodyTypes.FormData => FormData(body, fromPayload),
            HttpBodyTypes.Raw => Raw(body, fromPayload),
            HttpBodyTypes.Binary => body is null ? null : Binary(body),
            HttpBodyTypes.GraphQl => body is null ? null : GraphQl(body),
            _ => throw new InvalidOperationException(
                $"bodyType '{bodyType}' is not one of {string.Join(", ", HttpBodyTypes.All)}."),
        };
    }

    /// <summary>
    /// Headers: the connection's first (as before E68), then the request's enabled <c>headers</c> rows, which
    /// replace a connection header of the same name. A row naming a body header (Content-Type, …) is set on
    /// the content; a multipart body keeps its own Content-Type, since the boundary lives in it.
    /// </summary>
    public static void ApplyHeaders(
        HttpRequestMessage message, IReadOnlyDictionary<string, string> connectionHeaders, JsonObject request,
        HttpRequestSettings settings)
    {
        foreach (var (name, value) in connectionHeaders)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        var setByRequest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value, _) in Rows(request["headers"]))
        {
            var text = Text(value);
            if (ContentHeaders.Contains(name))
            {
                if (message.Content is null) continue; // nothing to describe
                if (message.Content is MultipartFormDataContent && name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                message.Content.Headers.Remove(name);
                message.Content.Headers.TryAddWithoutValidation(name, text);
                continue;
            }

            // The first row of a name replaces the connection's value; later rows of the same name add to it.
            if (setByRequest.Add(name)) message.Headers.Remove(name);
            message.Headers.TryAddWithoutValidation(name, text);
        }

        if (settings.SendNoCache && !message.Headers.Contains("Cache-Control"))
        {
            message.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        }
    }

    /// <summary>
    /// Key/value rows from either shape the request may use: a list of <c>{key, value, enabled}</c> rows (what
    /// the editor writes; a row with <c>enabled: false</c> or no key is skipped) or a plain object. With
    /// <paramref name="expandArrays"/>, an object property holding an array becomes one row per item
    /// (<c>{"tag": ["a","b"]}</c> → <c>tag=a&amp;tag=b</c>).
    /// </summary>
    internal static IEnumerable<(string Key, JsonNode? Value, JsonObject? Row)> Rows(JsonNode? node, bool expandArrays = false)
    {
        switch (node)
        {
            case JsonArray rows:
                foreach (var item in rows)
                {
                    if (item is not JsonObject row) continue;
                    if (row["enabled"] is JsonValue e && e.TryGetValue<bool>(out var enabled) && !enabled) continue;
                    var key = row["key"] is JsonValue k ? Text(k) : "";
                    if (key.Length == 0) continue;
                    yield return (key, row["value"], row);
                }
                break;
            case JsonObject properties:
                foreach (var (key, value) in properties)
                {
                    if (expandArrays && value is JsonArray many)
                    {
                        foreach (var one in many) yield return (key, one, null);
                    }
                    else
                    {
                        yield return (key, value, null);
                    }
                }
                break;
        }
    }

    /// <summary>A value as the text it is sent as: a string as itself, a number or boolean as JSON writes it, an object or array as JSON.</summary>
    internal static string Text(JsonNode? value) => value switch
    {
        null => "",
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        _ => value.ToJsonString(),
    };

    private static StringContent JsonContent(JsonNode body) =>
        new(body.ToJsonString(), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

    private static FormUrlEncodedContent? Urlencoded(JsonNode? body, bool fromPayload)
    {
        if (body is null) return null;
        RequireRowsOrObject(body, HttpBodyTypes.Urlencoded, fromPayload);
        return new FormUrlEncodedContent(Rows(body).Select(r => new KeyValuePair<string, string>(r.Key, Text(r.Value))).ToList());
    }

    private static MultipartFormDataContent? FormData(JsonNode? body, bool fromPayload)
    {
        if (body is null) return null;
        RequireRowsOrObject(body, HttpBodyTypes.FormData, fromPayload);

        var content = new MultipartFormDataContent();
        foreach (var (key, value, row) in Rows(body))
        {
            var isFile = row?["type"]?.GetValue<string>() is "file";
            if (isFile)
            {
                var file = FileRef.From(row!["file"] ?? value, $"form field '{key}'");
                var part = new ByteArrayContent(file.Bytes);
                part.Headers.ContentType = MediaTypeHeaderValue.Parse(
                    row["contentType"]?.GetValue<string>() ?? file.ContentType ?? "application/octet-stream");
                content.Add(part, key, file.FileName ?? key);
                continue;
            }

            var text = new StringContent(Text(value), Encoding.UTF8);
            // A plain text field carries no Content-Type, as a browser's form does, unless the row asks for one.
            text.Headers.ContentType = row?["contentType"]?.GetValue<string>() is string type ? MediaTypeHeaderValue.Parse(type) : null;
            content.Add(text, key);
        }
        return content;
    }

    private static readonly Dictionary<string, string> RawMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["json"] = "application/json",
        ["text"] = "text/plain",
        ["xml"] = "application/xml",
        ["html"] = "text/html",
        ["javascript"] = "application/javascript",
    };

    /// <summary>
    /// Raw text: <c>{"language": "json|text|xml|html|javascript", "content": ...}</c>. A string content is sent
    /// as written; a structured content (an object, or a value a single <c>{{ }}</c> rendered to) is sent as JSON.
    /// A bare string body is plain text. A deliver with no body sends the payload as JSON.
    /// </summary>
    private static StringContent? Raw(JsonNode? body, bool fromPayload)
    {
        switch (body)
        {
            case null:
                return null;
            case JsonValue v when v.TryGetValue<string>(out var bare):
                return new StringContent(bare, Encoding.UTF8, "text/plain");
            case JsonObject raw when !fromPayload:
            {
                if (!raw.ContainsKey("content"))
                {
                    throw new InvalidOperationException("a raw body is {\"language\": \"json|text|xml|html|javascript\", \"content\": \"...\"}.");
                }
                var language = raw["language"]?.GetValue<string>() ?? "text";
                if (!RawMediaTypes.TryGetValue(language, out var mediaType))
                {
                    throw new InvalidOperationException($"raw body language '{language}' is not one of {string.Join(", ", RawMediaTypes.Keys)}.");
                }
                var text = raw["content"] is JsonValue c && c.TryGetValue<string>(out var s) ? s : raw["content"]?.ToJsonString() ?? "";
                // JSON goes without a charset, as every JSON body this connector has sent; text types say utf-8.
                return language.Equals("json", StringComparison.OrdinalIgnoreCase)
                    ? new StringContent(text, Encoding.UTF8, new MediaTypeHeaderValue(mediaType))
                    : new StringContent(text, Encoding.UTF8, mediaType);
            }
            default:
                return JsonContent(body);
        }
    }

    private static ByteArrayContent Binary(JsonNode body)
    {
        var file = FileRef.From(body is JsonObject o && o["file"] is JsonNode inner ? inner : body, "the binary body");
        var content = new ByteArrayContent(file.Bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType ?? "application/octet-stream");
        return content;
    }

    /// <summary><c>{"query", "variables"?, "operationName"?}</c> sent as GraphQL's JSON envelope. Variables may be JSON text (the editor's box).</summary>
    private static StringContent GraphQl(JsonNode body)
    {
        if (body is not JsonObject graphql || graphql["query"]?.GetValue<string>() is not string query || query.Trim().Length == 0)
        {
            throw new InvalidOperationException("a graphql body needs a 'query'.");
        }

        var envelope = new JsonObject { ["query"] = query };
        switch (graphql["variables"])
        {
            case JsonValue v when v.TryGetValue<string>(out var text):
                if (text.Trim().Length > 0)
                {
                    try { envelope["variables"] = JsonNode.Parse(text); }
                    catch (System.Text.Json.JsonException ex)
                    {
                        throw new InvalidOperationException($"graphql variables are not valid JSON: {ex.Message}");
                    }
                }
                break;
            case JsonNode variables:
                envelope["variables"] = variables.DeepClone();
                break;
        }
        if (graphql["operationName"]?.GetValue<string>() is string operation && operation.Length > 0)
        {
            envelope["operationName"] = operation;
        }
        return JsonContent(envelope);
    }

    private static void RequireRowsOrObject(JsonNode body, string bodyType, bool fromPayload)
    {
        if (body is JsonArray or JsonObject) return;
        throw new InvalidOperationException(fromPayload
            ? $"a {bodyType} body sends an object's fields, and the payload is not an object."
            : $"a {bodyType} body is a list of {{key, value}} rows.");
    }
}

/// <summary>
/// A file sent in a request (E68-T1): <c>{"contentBase64", "filename"?, "contentType"?}</c>, the artifact a
/// file retrieve with format <c>binary</c> produces, so <c>{{input}}</c> after one is a valid file; also
/// <c>base64</c>/<c>fileName</c> spellings, or <c>text</c> for a text file written inline.
/// </summary>
public sealed record FileRef(byte[] Bytes, string? FileName, string? ContentType)
{
    public static FileRef From(JsonNode? node, string what)
    {
        if (node is not JsonObject file)
        {
            throw new InvalidOperationException(
                $"{what} needs a file: {{\"contentBase64\", \"filename\"?, \"contentType\"?}}, e.g. {{{{input}}}} after a file retrieve with format binary.");
        }

        if (file.ContainsKey("connectionId"))
        {
            throw new InvalidOperationException(
                $"{what}: a file from a file connection is read with a retrieve node (format binary) first; pass its output here as {{{{input}}}}.");
        }

        var name = file["filename"]?.GetValue<string>() ?? file["fileName"]?.GetValue<string>();
        var type = file["contentType"]?.GetValue<string>();
        if ((file["contentBase64"] ?? file["base64"]) is JsonValue b && b.TryGetValue<string>(out var base64))
        {
            try { return new FileRef(Convert.FromBase64String(base64), name, type); }
            catch (FormatException) { throw new InvalidOperationException($"{what}: contentBase64 is not valid base64."); }
        }
        if (file["text"] is JsonValue t && t.TryGetValue<string>(out var text))
        {
            return new FileRef(Encoding.UTF8.GetBytes(text), name, type ?? "text/plain");
        }
        throw new InvalidOperationException($"{what} has no contentBase64 (or text).");
    }
}
