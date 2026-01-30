using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RuriLib.Helpers.Cookies
{
    /// <summary>
    /// Parses cookie files in various formats (Netscape, name=value, name: value).
    /// </summary>
    public static class CookieParser
    {
        /// <summary>
        /// Represents a parsed cookie entry.
        /// </summary>
        public class CookieEntry
        {
            public string Domain { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public string Path { get; set; } = "/";
            public bool Secure { get; set; } = false;
            public long Expiration { get; set; } = 0;
            public bool HttpOnly { get; set; } = false;
        }

        /// <summary>
        /// Parses a cookie file and returns all cookie entries.
        /// Supports Netscape format and simple name=value or name: value formats.
        /// </summary>
        /// <param name="filePath">Path to the cookie file.</param>
        /// <returns>List of parsed cookie entries.</returns>
        public static List<CookieEntry> ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return new List<CookieEntry>();
            }

            var lines = File.ReadAllLines(filePath);
            return ParseLines(lines);
        }

        /// <summary>
        /// Parses cookie lines and returns all cookie entries.
        /// </summary>
        /// <param name="lines">Lines from a cookie file.</param>
        /// <returns>List of parsed cookie entries.</returns>
        public static List<CookieEntry> ParseLines(IEnumerable<string> lines)
        {
            var cookies = new List<CookieEntry>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                // Skip empty lines
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                // Handle #HttpOnly_ prefix (Netscape format extension for HttpOnly cookies)
                // Example: #HttpOnly_.chatgpt.com	TRUE	/	TRUE	...
                var isHttpOnly = false;
                var lineToParse = trimmed;
                
                if (trimmed.StartsWith("#HttpOnly_", StringComparison.OrdinalIgnoreCase))
                {
                    isHttpOnly = true;
                    lineToParse = trimmed.Substring(10); // Remove "#HttpOnly_" prefix
                }
                else if (trimmed.StartsWith("#"))
                {
                    // Skip comment lines (but not #HttpOnly_ lines)
                    continue;
                }

                // Try Netscape format first (tab-separated, 7 fields)
                var cookie = TryParseNetscape(lineToParse);
                
                if (cookie != null)
                {
                    cookie.HttpOnly = isHttpOnly;
                }
                else
                {
                    // Try simple formats (name=value or name: value)
                    cookie = TryParseSimple(lineToParse);
                }

                if (cookie != null)
                {
                    cookies.Add(cookie);
                }
            }

            return cookies;
        }

        /// <summary>
        /// Parses a cookie file and returns cookies filtered by domain.
        /// </summary>
        /// <param name="filePath">Path to the cookie file.</param>
        /// <param name="domain">Domain to filter by (e.g., "example.com").</param>
        /// <returns>Dictionary of cookie name to value for matching domain.</returns>
        public static Dictionary<string, string> ParseFileForDomain(string filePath, string domain)
        {
            var allCookies = ParseFile(filePath);
            return FilterByDomain(allCookies, domain);
        }

        /// <summary>
        /// Filters cookies by domain, returning a name-value dictionary.
        /// </summary>
        /// <param name="cookies">List of cookie entries.</param>
        /// <param name="domain">Domain to filter by.</param>
        /// <returns>Dictionary of cookie name to value.</returns>
        public static Dictionary<string, string> FilterByDomain(List<CookieEntry> cookies, string domain)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var normalizedDomain = domain.TrimStart('.').ToLowerInvariant();

            foreach (var cookie in cookies)
            {
                if (DomainMatches(cookie.Domain, normalizedDomain))
                {
                    // Later cookies with same name override earlier ones
                    result[cookie.Name] = cookie.Value;
                }
            }

            return result;
        }

        /// <summary>
        /// Checks if a cookie domain matches the target domain.
        /// Handles Netscape domain format (leading dot means subdomain match).
        /// </summary>
        private static bool DomainMatches(string cookieDomain, string targetDomain)
        {
            if (string.IsNullOrEmpty(cookieDomain))
            {
                return false;
            }

            // Wildcard domain matches everything (used for simple format cookies)
            if (cookieDomain == "*")
            {
                return true;
            }

            var normalizedCookieDomain = cookieDomain.TrimStart('.').ToLowerInvariant();

            // Exact match
            if (normalizedCookieDomain == targetDomain)
            {
                return true;
            }

            // Subdomain match: target "sub.example.com" matches cookie ".example.com"
            if (targetDomain.EndsWith("." + normalizedCookieDomain))
            {
                return true;
            }

            // Cookie domain might be a subdomain of target
            if (normalizedCookieDomain.EndsWith("." + targetDomain))
            {
                return true;
            }

            // Partial match: "chatgpt.com" should match "auth0.openai.com" if user specified "chatgpt" 
            // or domain contains the target
            if (normalizedCookieDomain.Contains(targetDomain) || targetDomain.Contains(normalizedCookieDomain))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to parse a line in Netscape cookie format.
        /// Format: domain\tflag\tpath\tsecure\texpiration\tname\tvalue
        /// </summary>
        private static CookieEntry TryParseNetscape(string line)
        {
            var parts = line.Split('\t');
            
            if (parts.Length < 7)
            {
                return null;
            }

            try
            {
                return new CookieEntry
                {
                    Domain = parts[0],
                    HttpOnly = parts[1].Equals("TRUE", StringComparison.OrdinalIgnoreCase),
                    Path = parts[2],
                    Secure = parts[3].Equals("TRUE", StringComparison.OrdinalIgnoreCase),
                    Expiration = long.TryParse(parts[4], out var exp) ? exp : 0,
                    Name = parts[5],
                    Value = parts.Length > 6 ? parts[6] : string.Empty
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Tries to parse a line in simple format (name=value or name: value).
        /// </summary>
        private static CookieEntry TryParseSimple(string line)
        {
            // Try name=value format
            var eqIndex = line.IndexOf('=');
            if (eqIndex > 0)
            {
                var name = line.Substring(0, eqIndex).Trim();
                var value = line.Substring(eqIndex + 1).Trim();
                
                if (!string.IsNullOrEmpty(name))
                {
                    return new CookieEntry
                    {
                        Name = name,
                        Value = value,
                        Domain = "*" // Wildcard domain for simple format
                    };
                }
            }

            // Try name: value format
            var colonIndex = line.IndexOf(':');
            if (colonIndex > 0)
            {
                var name = line.Substring(0, colonIndex).Trim();
                var value = line.Substring(colonIndex + 1).Trim();
                
                if (!string.IsNullOrEmpty(name))
                {
                    return new CookieEntry
                    {
                        Name = name,
                        Value = value,
                        Domain = "*" // Wildcard domain for simple format
                    };
                }
            }

            return null;
        }
    }
}
