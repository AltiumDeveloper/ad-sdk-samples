using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace AdSamples.ComponentManagerPlaceComponent
{
    /// <summary>One entry of the server's parameter list (ParameterName_i / ParameterType_i / ParameterValue_i).</summary>
    internal sealed record ComponentParameter(string Name, string Type, string Value);

    /// <summary>A downloaded library file and the component metadata that came with it.</summary>
    internal sealed class DownloadedLibrary
    {
        public string FilePath { get; init; }
        public string Id { get; init; }
        public string ComponentType { get; init; }
        public string AssembleTech { get; init; }
        public string LibraryPath { get; init; }
        public string LibraryRef { get; init; }
        public string FootprintPath { get; init; }
        public string FootprintRef { get; init; }
        public IReadOnlyList<ComponentParameter> Parameters { get; init; }
        public long Bytes { get; init; }
    }

    /// <summary>
    /// The client side of the component management server protocol:
    /// GET &lt;base&gt;/Home/DownloadPcbLibFile?componentId=..&amp;sessionId=.. (or DownloadSchLibFile) returns the
    /// library file as the body, and the component's metadata as ';'-separated key=value pairs in the
    /// Content-Disposition header. Plain .NET (HttpClient); no Altium API here.
    /// </summary>
    internal static class ComponentServer
    {
        // The required metadata keys, in protocol order. Values of all but filename and id are URL-encoded.
        private static readonly string[] RequiredKeys =
            { "filename", "id", "componenttype", "assembletech", "librarypath", "libraryref", "footprintpath", "footprintref", "ParameterCount" };

        /// <summary>
        /// Downloads the PcbLib (<paramref name="pcbLib"/> true) or SchLib of a component into
        /// &lt;cacheFolder&gt;\PcbLib or \SchLib and returns its metadata. Failures raise a UserException.
        /// </summary>
        public static DownloadedLibrary Download(CommandContext ctx, string url, string componentId, string sessionId,
                                                 bool pcbLib, string cacheFolder, int timeoutSeconds)
        {
            Uri requestUri = RequestUri(url, componentId, sessionId, pcbLib);
            string kind = pcbLib ? "PcbLib" : "SchLib";
            ctx.Log($"GET {requestUri}");

            byte[] body;
            string disposition;
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) })
            {
                HttpResponseMessage response;
                try
                {
                    // Called from AD's UI thread: run the request on the thread pool and wait for it, so no
                    // continuation needs the UI thread's synchronization context while the UI thread is blocked.
                    response = Task.Run(() => client.GetAsync(requestUri)).GetAwaiter().GetResult();
                }
                catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
                {
                    throw new UserException($"Downloading the {kind} failed: {requestUri}: {e.Message}");
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                        throw new UserException($"Downloading the {kind} failed: {requestUri} answered " +
                                                $"{(int)response.StatusCode} {response.ReasonPhrase}.");

                    // The raw header, not HttpContentHeaders.ContentDisposition: the server's header is not an
                    // RFC 6266 value (custom keys, unquoted URL-encoded values). NonValidated returns it as sent.
                    disposition = response.Content.Headers.NonValidated.TryGetValues("Content-Disposition", out var values)
                        ? string.Join(";", values)
                        : null;
                    body = Task.Run(() => response.Content.ReadAsByteArrayAsync()).GetAwaiter().GetResult();
                }
            }

            if (string.IsNullOrEmpty(disposition))
                throw new UserException($"The {kind} response of {requestUri} has no Content-Disposition header (no component metadata).");

            Dictionary<string, string> meta = ParseDisposition(disposition);
            string missing = string.Join(", ", RequiredKeys.Where(k => !meta.ContainsKey(k)));
            if (missing.Length > 0)
                throw new UserException($"The {kind} response's Content-Disposition lacks {missing}: '{disposition}'.");

            string fileName = SafeFileName(meta["filename"], pcbLib ? ".PcbLib" : ".SchLib");
            if (!int.TryParse(meta["ParameterCount"], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count < 0)
                throw new UserException($"ParameterCount='{meta["ParameterCount"]}' in the {kind} response is not a number.");

            var parameters = new List<ComponentParameter>();
            for (int i = 0; i < count; i++)
            {
                string n = i.ToString(CultureInfo.InvariantCulture);
                if (!meta.TryGetValue("ParameterName_" + n, out string name) ||
                    !meta.TryGetValue("ParameterType_" + n, out string type) ||
                    !meta.TryGetValue("ParameterValue_" + n, out string value))
                    throw new UserException($"The {kind} response announces {count} parameters but parameter {n} is incomplete.");
                parameters.Add(new ComponentParameter(Decode(name), Decode(type), Decode(value)));
            }

            string folder = Path.Combine(cacheFolder, kind);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, fileName);
            File.WriteAllBytes(path, body);

            return new DownloadedLibrary
            {
                FilePath = path,
                Bytes = body.LongLength,
                Id = meta["id"],
                ComponentType = Decode(meta["componenttype"]),
                AssembleTech = Decode(meta["assembletech"]),
                LibraryPath = Decode(meta["librarypath"]),
                LibraryRef = Decode(meta["libraryref"]),
                FootprintPath = Decode(meta["footprintpath"]),
                FootprintRef = Decode(meta["footprintref"]),
                Parameters = parameters,
            };
        }

        /// <summary>
        /// Resolves the relative path "Home/..." against the Url: "http://host/app/Index" gives
        /// "http://host/app/Home/...", and "http://host:port" gives "http://host:port/Home/..." (cutting the Url
        /// after its last '/' would have left "http://").
        /// </summary>
        public static Uri RequestUri(string url, string componentId, string sessionId, bool pcbLib)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri baseUri) ||
                (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
                throw new UserException($"Url='{url}' is not an absolute http(s) URL.");
            string action = pcbLib ? "Home/DownloadPcbLibFile" : "Home/DownloadSchLibFile";
            return new Uri(baseUri, $"{action}?componentId={Uri.EscapeDataString(componentId)}&sessionId={Uri.EscapeDataString(sessionId)}");
        }

        /// <summary>
        /// "attachment; filename=x.PcbLib; id=7; librarypath=a%20b" to a case-insensitive dictionary. Tokens without
        /// '=' (attachment) are ignored, the first occurrence of a key wins, surrounding quotes are removed.
        /// </summary>
        public static Dictionary<string, string> ParseDisposition(string header)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = part.Substring(0, eq).Trim();
                string value = part.Substring(eq + 1).Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = value.Substring(1, value.Length - 2);
                result.TryAdd(key, value);
            }
            return result;
        }

        /// <summary>
        /// Percent-decoding. '+' is left as it is: whether a server encodes spaces as '+' or %20 is not known.
        /// </summary>
        public static string Decode(string value) => Uri.UnescapeDataString(value ?? string.Empty);

        /// <summary>
        /// The file name comes from the server, so it must not be able to point outside the cache folder.
        /// </summary>
        private static string SafeFileName(string name, string extension)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name == "." || name == "..")
                throw new UserException($"The server sent an unusable file name '{name}' (empty, or with a path or invalid characters).");
            if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                throw new UserException($"The server sent '{name}', expected a {extension} file.");
            return name;
        }
    }
}
