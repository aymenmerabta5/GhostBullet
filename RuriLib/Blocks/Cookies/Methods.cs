using RuriLib.Attributes;
using RuriLib.Helpers.Cookies;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RuriLib.Blocks.Cookies
{
    [BlockCategory("Cookies", "Blocks for loading and managing cookies", "#d4a574")]
    public static class Methods
    {
        /// <summary>
        /// Loads cookies from a file and adds them to the bot's cookie jar, filtered by domain.
        /// Supports Netscape cookie format and simple name=value or name: value formats.
        /// </summary>
        /// <param name="data">The BotData instance.</param>
        /// <param name="cookiePath">Path to the cookie file (typically from a Cookies wordlist using COOKIEPATH variable).</param>
        /// <param name="domain">Domain to filter cookies by (e.g., "example.com"). Use "*" to load all cookies.</param>
        /// <param name="clearExisting">Whether to clear existing cookies before loading new ones.</param>
        /// <returns>The number of cookies loaded.</returns>
        [Block("Loads cookies from a file filtered by domain", 
            name = "Load Cookies",
            extraInfo = "Parses Netscape format (tab-separated) or simple name=value/name: value formats.\nUsage: Set cookiePath to <input.COOKIEPATH> from Cookies wordlist, domain to target site (e.g., 'example.com').\nCookies are added to data.COOKIES and used by subsequent HTTP requests.")]
        public static int LoadCookies(BotData data, [Interpolated] string cookiePath, [Interpolated] string domain, bool clearExisting = false)
        {
            data.Logger.LogHeader();

            data.Logger.Log($"Cookie path: {cookiePath}", LogColors.Wheat);
            data.Logger.Log($"Domain filter: {domain}", LogColors.Wheat);

            if (string.IsNullOrWhiteSpace(cookiePath))
            {
                data.Logger.Log("Cookie path is empty, skipping", LogColors.Coral);
                return 0;
            }

            if (!File.Exists(cookiePath))
            {
                data.Logger.Log($"Cookie file not found: {cookiePath}", LogColors.Coral);
                return 0;
            }

            if (clearExisting)
            {
                data.COOKIES.Clear();
                data.Logger.Log("Cleared existing cookies", LogColors.Wheat);
            }

            // First, parse all cookies from the file for debugging
            var allCookies = CookieParser.ParseFile(cookiePath);
            data.Logger.Log($"Total cookies in file: {allCookies.Count}", LogColors.Wheat);
            
            // Show unique domains found in the file
            var domains = allCookies.Select(c => c.Domain).Distinct().Take(10).ToList();
            if (domains.Any())
            {
                data.Logger.Log($"Domains in file: {string.Join(", ", domains)}", LogColors.Wheat);
            }

            Dictionary<string, string> cookies;

            if (domain == "*" || string.IsNullOrWhiteSpace(domain))
            {
                // Load all cookies without domain filtering
                cookies = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var cookie in allCookies)
                {
                    cookies[cookie.Name] = cookie.Value; // Later cookies override earlier ones
                }
                data.Logger.Log($"Loading all cookies (no domain filter) from: {Path.GetFileName(cookiePath)}", LogColors.Wheat);
            }
            else
            {
                // Load cookies filtered by domain
                cookies = CookieParser.FilterByDomain(allCookies, domain);
                data.Logger.Log($"Loading cookies for domain '{domain}' from: {Path.GetFileName(cookiePath)}", LogColors.Wheat);
            }

            foreach (var cookie in cookies)
            {
                data.COOKIES[cookie.Key] = cookie.Value;
            }

            data.Logger.Log($"Loaded {cookies.Count} cookies to data.COOKIES:", LogColors.GreenYellow);
            foreach (var cookie in cookies.Take(10)) // Show first 10
            {
                data.Logger.Log($"  {cookie.Key}={TruncateValue(cookie.Value, 50)}", LogColors.Khaki);
            }

            if (cookies.Count > 10)
            {
                data.Logger.Log($"  ... and {cookies.Count - 10} more", LogColors.Khaki);
            }

            // Show current state of data.COOKIES
            data.Logger.Log($"data.COOKIES now has {data.COOKIES.Count} cookies total", LogColors.GreenYellow);

            return cookies.Count;
        }

        /// <summary>
        /// Loads cookies from a file, extracting only cookies with the specified name.
        /// </summary>
        /// <param name="data">The BotData instance.</param>
        /// <param name="cookiePath">Path to the cookie file.</param>
        /// <param name="domain">Domain to filter cookies by.</param>
        /// <param name="cookieName">Specific cookie name to extract.</param>
        /// <returns>The cookie value if found, empty string otherwise.</returns>
        [Block("Gets a specific cookie value from a file",
            name = "Get Cookie",
            extraInfo = "Extracts a single cookie by name from a cookie file, filtered by domain.")]
        public static string GetCookie(BotData data, [Interpolated] string cookiePath, [Interpolated] string domain, [Interpolated] string cookieName)
        {
            data.Logger.LogHeader();

            if (string.IsNullOrWhiteSpace(cookiePath) || !File.Exists(cookiePath))
            {
                data.Logger.Log($"Cookie file not found or path empty: {cookiePath}", LogColors.Coral);
                return string.Empty;
            }

            var cookies = CookieParser.ParseFileForDomain(cookiePath, domain);

            if (cookies.TryGetValue(cookieName, out var value))
            {
                data.Logger.Log($"Found cookie '{cookieName}' = {TruncateValue(value, 100)}", LogColors.GreenYellow);
                return value;
            }

            data.Logger.Log($"Cookie '{cookieName}' not found for domain '{domain}'", LogColors.Coral);
            return string.Empty;
        }

        /// <summary>
        /// Exports current cookies to Netscape format string.
        /// </summary>
        /// <param name="data">The BotData instance.</param>
        /// <param name="domain">Domain to use for the exported cookies.</param>
        /// <returns>Netscape format cookie string.</returns>
        [Block("Exports current cookies to Netscape format",
            name = "Export Cookies Netscape",
            extraInfo = "Converts data.COOKIES to Netscape format string for saving or debugging.")]
        public static string ExportCookiesNetscape(BotData data, [Interpolated] string domain)
        {
            data.Logger.LogHeader();

            var lines = new List<string>();
            lines.Add("# Netscape HTTP Cookie File");
            lines.Add("# https://curl.se/docs/http-cookies.html");
            lines.Add("");

            foreach (var cookie in data.COOKIES)
            {
                // Format: domain \t flag \t path \t secure \t expiration \t name \t value
                var line = $"{domain}\tTRUE\t/\tFALSE\t0\t{cookie.Key}\t{cookie.Value}";
                lines.Add(line);
            }

            var result = string.Join("\n", lines);
            data.Logger.Log($"Exported {data.COOKIES.Count} cookies to Netscape format", LogColors.GreenYellow);

            return result;
        }

        private static string TruncateValue(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }
            return value.Substring(0, maxLength) + "...";
        }
    }
}
