using RuriLib.Models.Proxies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RuriLib.Functions.Http.NativeTlsClient
{
    /// <summary>
    /// Builds TlsClientRequest objects from TlsClientOptions and HTTP request parameters.
    /// </summary>
    public class TlsClientRequestBuilder
    {
        private readonly TlsClientOptions _options;

        public TlsClientRequestBuilder(TlsClientOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Creates a TlsClientRequest from the current options.
        /// </summary>
        public TlsClientRequest Build(
            string url,
            string method,
            Dictionary<string, string> headers,
            Dictionary<string, string> cookies,
            string body = null,
            Proxy proxy = null,
            string sessionId = null,
            int timeoutMs = 30000)
        {
            var request = new TlsClientRequest
            {
                RequestUrl = url,
                RequestMethod = method.ToUpperInvariant(),
                RequestBody = body,
                SessionId = sessionId ?? Guid.NewGuid().ToString("N"),
                TlsClientIdentifier = GetTlsClientIdentifier(_options.BrowserProfile),
                FollowRedirects = _options.AutoRedirect,
                InsecureSkipVerify = _options.InsecureSkipVerify,
                ForceHttp1 = _options.ForceHttp1,
                TimeoutMilliseconds = timeoutMs,
                IsByteRequest = false,
                IsByteResponse = false,
                CatchPanics = true,
                WithDefaultCookieJar = true,
                WithRandomTlsExtensionOrder = _options.RandomizeTlsExtensionOrder
            };

            // Only apply custom TLS config if we have custom JA3 or HTTP/2 settings
            // Otherwise, let the tls-client profile handle everything automatically
            if (!string.IsNullOrEmpty(_options.Ja3Fingerprint))
            {
                request.CustomTlsClient = BuildCustomTlsConfig();
            }

            // Set proxy
            if (proxy != null)
            {
                request.ProxyUrl = BuildProxyUrl(proxy);
            }

            // Build headers with Client Hints
            request.Headers = BuildHeaders(headers, url);
            request.HeaderOrder = GetHeaderOrder(_options.BrowserProfile);

            // Add cookies
            if (cookies?.Count > 0)
            {
                request.RequestCookies = cookies.Select(c => new TlsClientCookie
                {
                    Name = c.Key,
                    Value = c.Value,
                    Domain = new Uri(url).Host,
                    Path = "/"
                }).ToList();
            }

            return request;
        }

        private CustomTlsClientConfig BuildCustomTlsConfig()
        {
            var config = new CustomTlsClientConfig();

            // Use custom JA3 if provided
            if (!string.IsNullOrEmpty(_options.Ja3Fingerprint))
            {
                config.Ja3String = _options.Ja3Fingerprint;
            }

            // Apply HTTP/2 settings
            var http2Settings = _options.Http2Settings?.Count > 0
                ? _options.Http2Settings
                : TlsClientProfileDefaults.GetHttp2Settings(_options.BrowserProfile);

            config.H2Settings = new Http2Settings
            {
                HeaderTableSize = http2Settings.GetValueOrDefault("HEADER_TABLE_SIZE", 65536),
                EnablePush = http2Settings.GetValueOrDefault("ENABLE_PUSH", 0),
                MaxConcurrentStreams = http2Settings.GetValueOrDefault("MAX_CONCURRENT_STREAMS", 1000),
                InitialWindowSize = http2Settings.GetValueOrDefault("INITIAL_WINDOW_SIZE", 6291456),
                MaxFrameSize = http2Settings.GetValueOrDefault("MAX_FRAME_SIZE", 16384),
                MaxHeaderListSize = http2Settings.GetValueOrDefault("MAX_HEADER_LIST_SIZE", 262144)
            };

            config.H2SettingsOrder = GetHttp2SettingsOrder(_options.BrowserProfile);
            config.PseudoHeaderOrder = GetPseudoHeaderOrder(_options.BrowserProfile);

            // Connection flow (Chrome default)
            config.ConnectionFlow = 15663105;

            // ALPN protocols
            config.AlpnProtocols = _options.ForceHttp1
                ? new List<string> { "http/1.1" }
                : new List<string> { "h2", "http/1.1" };

            return config;
        }

        private Dictionary<string, string> BuildHeaders(Dictionary<string, string> customHeaders, string url)
        {
            // Headers that should use profile values (not user overrides) for proper fingerprinting
            var fingerprintHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "User-Agent",
                "Sec-CH-UA",
                "Sec-CH-UA-Mobile",
                "Sec-CH-UA-Platform",
                "Sec-CH-UA-Platform-Version",
                "Sec-CH-UA-Arch",
                "Sec-CH-UA-Bitness",
                "Sec-CH-UA-Full-Version",
                "Sec-CH-UA-Full-Version-List",
                "Sec-CH-UA-Model",
                "Accept",
                "Accept-Language",
                "Accept-Encoding"
            };

            // HTTP/1.1 specific headers that shouldn't be sent over HTTP/2
            var http1OnlyHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Pragma",
                "Connection",
                "Keep-Alive",
                "Transfer-Encoding",
                "TE",
                "Trailer",
                "Upgrade"
            };

            // Start with custom headers (excluding fingerprint and HTTP/1.1 headers when using HTTP/2)
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (customHeaders != null)
            {
                foreach (var header in customHeaders)
                {
                    // Skip fingerprint headers - they'll be set from profile
                    if (fingerprintHeaders.Contains(header.Key))
                        continue;

                    // Skip HTTP/1.1-only headers when using HTTP/2 (they're a fingerprinting signal)
                    if (!_options.ForceHttp1 && http1OnlyHeaders.Contains(header.Key))
                        continue;

                    headers[header.Key] = header.Value;
                }
            }

            // Apply profile defaults (override custom for these)
            var profileHeaders = GetDefaultHeaders(_options.BrowserProfile, url);
            foreach (var header in profileHeaders)
            {
                headers[header.Key] = header.Value;
            }

            // Add Client Hints based on profile (these are critical for anti-bot bypass)
            if (_options.IncludeClientHints)
            {
                var clientHints = GetClientHints(_options.BrowserProfile);
                foreach (var hint in clientHints)
                {
                    headers[hint.Key] = hint.Value;
                }
            }

            return headers;
        }

        private static string GetTlsClientIdentifier(TlsClientProfile profile)
        {
            // Map to tls-client internal profile identifiers
            return profile switch
            {
                TlsClientProfile.Chrome133 => "chrome_133",
                TlsClientProfile.Chrome120 => "chrome_120",
                TlsClientProfile.Firefox => "firefox_120",
                TlsClientProfile.Safari => "safari_ios_17_0",
                TlsClientProfile.Edge => "chrome_133", // Edge uses Chromium
                TlsClientProfile.Custom => "chrome_133",
                _ => "chrome_133"
            };
        }

        private static string BuildProxyUrl(Proxy proxy)
        {
            if (proxy == null) return null;

            var scheme = proxy.Type switch
            {
                ProxyType.Http => "http",
                ProxyType.Socks4 => "socks4",
                ProxyType.Socks4a => "socks4",
                ProxyType.Socks5 => "socks5",
                _ => "http"
            };

            if (proxy.NeedsAuthentication)
            {
                return $"{scheme}://{proxy.Username}:{proxy.Password}@{proxy.Host}:{proxy.Port}";
            }

            return $"{scheme}://{proxy.Host}:{proxy.Port}";
        }

        private static Dictionary<string, string> GetDefaultHeaders(TlsClientProfile profile, string url)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            switch (profile)
            {
                case TlsClientProfile.Chrome133:
                case TlsClientProfile.Chrome120:
                case TlsClientProfile.Edge:
                    // Chrome 133 headers
                    headers["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7";
                    headers["Accept-Language"] = "en-US,en;q=0.9";
                    headers["Accept-Encoding"] = "gzip, deflate, br, zstd";
                    headers["Cache-Control"] = "max-age=0";
                    headers["Sec-Fetch-Dest"] = "document";
                    headers["Sec-Fetch-Mode"] = "navigate";
                    headers["Sec-Fetch-Site"] = "none";
                    headers["Sec-Fetch-User"] = "?1";
                    headers["Upgrade-Insecure-Requests"] = "1";
                    headers["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36";
                    headers["Priority"] = "u=0, i";
                    break;

                case TlsClientProfile.Firefox:
                    headers["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8";
                    headers["Accept-Language"] = "en-US,en;q=0.5";
                    headers["Accept-Encoding"] = "gzip, deflate, br";
                    headers["Sec-Fetch-Dest"] = "document";
                    headers["Sec-Fetch-Mode"] = "navigate";
                    headers["Sec-Fetch-Site"] = "none";
                    headers["Sec-Fetch-User"] = "?1";
                    headers["Upgrade-Insecure-Requests"] = "1";
                    headers["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:120.0) Gecko/20100101 Firefox/120.0";
                    headers["Te"] = "trailers";
                    break;

                case TlsClientProfile.Safari:
                    headers["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
                    headers["Accept-Language"] = "en-US,en;q=0.9";
                    headers["Accept-Encoding"] = "gzip, deflate, br";
                    headers["User-Agent"] = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15";
                    headers["Sec-Fetch-Dest"] = "document";
                    headers["Sec-Fetch-Mode"] = "navigate";
                    headers["Sec-Fetch-Site"] = "none";
                    break;
            }

            return headers;
        }

        /// <summary>
        /// Gets Client Hints headers for the browser profile.
        /// These are critical for bypassing Cloudflare and other anti-bot systems.
        /// </summary>
        private static Dictionary<string, string> GetClientHints(TlsClientProfile profile)
        {
            var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            switch (profile)
            {
                case TlsClientProfile.Chrome133:
                case TlsClientProfile.Chrome120:
                case TlsClientProfile.Edge:
                    // Chrome 133 client hints
                    hints["Sec-CH-UA"] = "\"Not A(Brand\";v=\"8\", \"Chromium\";v=\"133\", \"Google Chrome\";v=\"133\"";
                    hints["Sec-CH-UA-Mobile"] = "?0";
                    hints["Sec-CH-UA-Platform"] = "\"Windows\"";
                    hints["Sec-CH-UA-Platform-Version"] = "\"15.0.0\"";
                    hints["Sec-CH-UA-Arch"] = "\"x86\"";
                    hints["Sec-CH-UA-Bitness"] = "\"64\"";
                    hints["Sec-CH-UA-Full-Version"] = "\"133.0.6943.127\"";
                    hints["Sec-CH-UA-Full-Version-List"] = "\"Not A(Brand\";v=\"8.0.0.0\", \"Chromium\";v=\"133.0.6943.127\", \"Google Chrome\";v=\"133.0.6943.127\"";
                    hints["Sec-CH-UA-Model"] = "\"\"";
                    break;

                case TlsClientProfile.Firefox:
                    // Firefox doesn't send Client Hints by default
                    break;

                case TlsClientProfile.Safari:
                    // Safari has limited Client Hints support
                    hints["Sec-CH-UA-Mobile"] = "?0";
                    hints["Sec-CH-UA-Platform"] = "\"macOS\"";
                    break;
            }

            return hints;
        }

        private static List<string> GetHeaderOrder(TlsClientProfile profile)
        {
            // Header order for HTTP/2
            return profile switch
            {
                TlsClientProfile.Chrome133 or TlsClientProfile.Chrome120 or TlsClientProfile.Edge => new List<string>
                {
                    "sec-ch-ua",
                    "sec-ch-ua-mobile",
                    "sec-ch-ua-platform",
                    "upgrade-insecure-requests",
                    "user-agent",
                    "accept",
                    "sec-fetch-site",
                    "sec-fetch-mode",
                    "sec-fetch-user",
                    "sec-fetch-dest",
                    "accept-encoding",
                    "accept-language",
                    "priority",
                    "cookie"
                },
                TlsClientProfile.Firefox => new List<string>
                {
                    "user-agent",
                    "accept",
                    "accept-language",
                    "accept-encoding",
                    "upgrade-insecure-requests",
                    "sec-fetch-dest",
                    "sec-fetch-mode",
                    "sec-fetch-site",
                    "sec-fetch-user",
                    "te",
                    "cookie"
                },
                TlsClientProfile.Safari => new List<string>
                {
                    "accept",
                    "accept-language",
                    "accept-encoding",
                    "user-agent",
                    "sec-fetch-dest",
                    "sec-fetch-mode",
                    "sec-fetch-site",
                    "sec-fetch-user",
                    "cookie"
                },
                _ => new List<string>()
            };
        }

        private static List<string> GetHttp2SettingsOrder(TlsClientProfile profile)
        {
            return profile switch
            {
                TlsClientProfile.Chrome133 or TlsClientProfile.Chrome120 or TlsClientProfile.Edge => new List<string>
                {
                    "HEADER_TABLE_SIZE",
                    "ENABLE_PUSH",
                    "MAX_CONCURRENT_STREAMS",
                    "INITIAL_WINDOW_SIZE",
                    "MAX_FRAME_SIZE",
                    "MAX_HEADER_LIST_SIZE"
                },
                TlsClientProfile.Firefox => new List<string>
                {
                    "HEADER_TABLE_SIZE",
                    "INITIAL_WINDOW_SIZE",
                    "MAX_FRAME_SIZE"
                },
                _ => new List<string>
                {
                    "HEADER_TABLE_SIZE",
                    "ENABLE_PUSH",
                    "MAX_CONCURRENT_STREAMS",
                    "INITIAL_WINDOW_SIZE",
                    "MAX_FRAME_SIZE",
                    "MAX_HEADER_LIST_SIZE"
                }
            };
        }

        private static List<string> GetPseudoHeaderOrder(TlsClientProfile profile)
        {
            return profile switch
            {
                TlsClientProfile.Chrome133 or TlsClientProfile.Chrome120 or TlsClientProfile.Edge => new List<string>
                {
                    ":method",
                    ":authority",
                    ":scheme",
                    ":path"
                },
                TlsClientProfile.Firefox => new List<string>
                {
                    ":method",
                    ":path",
                    ":authority",
                    ":scheme"
                },
                _ => new List<string>
                {
                    ":method",
                    ":authority",
                    ":scheme",
                    ":path"
                }
            };
        }
    }
}
