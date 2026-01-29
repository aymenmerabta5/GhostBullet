using RuriLib.Http;
using RuriLib.Http.Models;
using RuriLib.Models.Proxies;
using RuriLib.Proxies;
using RuriLib.Proxies.Clients;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Functions.Http
{
    /// <summary>
    /// A wrapper around HTTP client that mimics browser TLS fingerprints.
    /// </summary>
    public class TlsClientWrapper : IDisposable
    {
        private readonly TlsClientOptions _options;
        private readonly ProxyClient _proxyClient;
        private HttpClient _httpClient;
        private SocketsHttpHandler _handler;
        private CookieContainer _cookieContainer;

        /// <summary>
        /// Gets the raw bytes of all requests that were sent.
        /// </summary>
        public List<byte[]> RawRequests { get; } = new();

        /// <summary>
        /// Creates a new instance of TlsClientWrapper.
        /// </summary>
        public TlsClientWrapper(TlsClientOptions options, ProxyClient proxyClient = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _proxyClient = proxyClient ?? new NoProxyClient();
            _cookieContainer = new CookieContainer();

            InitializeClient();
        }

        private void InitializeClient()
        {
            // Create the handler with TLS configuration
            _handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = _options.AutoRedirect,
                MaxAutomaticRedirections = _options.MaxNumberOfRedirects,
                CookieContainer = _cookieContainer,
                UseCookies = true,
                ConnectTimeout = _options.ConnectTimeout,
                ResponseDrainTimeout = _options.ReadWriteTimeout,
                SslOptions = CreateSslOptions()
            };

            // Configure proxy
            ConfigureProxy();

            // Configure HTTP/2 or HTTP/1.1
            // Note: DefaultRequestVersion and DefaultVersionPolicy are set on HttpClient, not SocketsHttpHandler

            // Create the HTTP client
            _httpClient = new HttpClient(_handler)
            {
                Timeout = _options.ReadWriteTimeout,
                DefaultRequestVersion = _options.ForceHttp1 ? HttpVersion.Version11 : HttpVersion.Version20,
                DefaultVersionPolicy = _options.ForceHttp1 ? HttpVersionPolicy.RequestVersionExact : HttpVersionPolicy.RequestVersionOrLower
            };

            // Apply browser-specific headers and settings
            ApplyBrowserProfile();
        }

        private SslClientAuthenticationOptions CreateSslOptions()
        {
            var sslOptions = new SslClientAuthenticationOptions
            {
                CertificateRevocationCheckMode = _options.InsecureSkipVerify
                    ? X509RevocationMode.NoCheck
                    : _options.CertRevocationMode,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CipherSuitesPolicy = GetCipherSuitesPolicy()
            };

            if (_options.InsecureSkipVerify)
            {
                sslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            }

            // Add client certificates if provided
            if (_options.ClientCertificates?.Count > 0)
            {
                var clientCerts = new X509CertificateCollection();
                foreach (var certPem in _options.ClientCertificates)
                {
                    try
                    {
                        var cert = X509Certificate2.CreateFromPem(certPem);
                        clientCerts.Add(cert);
                    }
                    catch { /* Ignore invalid certificates */ }
                }
                sslOptions.ClientCertificates = clientCerts;
            }

            return sslOptions;
        }

        private CipherSuitesPolicy GetCipherSuitesPolicy()
        {
            TlsCipherSuite[] cipherSuites;

            if (_options.CustomCipherSuites?.Count > 0)
            {
                // Use custom cipher suites
                cipherSuites = _options.CustomCipherSuites
                    .Select(ParseCipherSuite)
                    .Where(c => c != null)
                    .Cast<TlsCipherSuite>()
                    .ToArray();
            }
            else
            {
                // Use profile defaults
                cipherSuites = TlsClientProfileDefaults.GetCipherSuites(_options.BrowserProfile);
            }

            if (cipherSuites.Length == 0 || RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // CipherSuitesPolicy is not supported on Windows
                return null;
            }

            return new CipherSuitesPolicy(cipherSuites);
        }

        private TlsCipherSuite? ParseCipherSuite(string suiteName)
        {
            if (Enum.TryParse<TlsCipherSuite>(suiteName, out var suite))
            {
                return suite;
            }
            return null;
        }

        private void ConfigureProxy()
        {
            if (_proxyClient is NoProxyClient)
            {
                _handler.UseProxy = false;
                return;
            }

            // Get proxy settings
            var proxyHost = GetProxyHost();
            var proxyPort = GetProxyPort();

            if (string.IsNullOrEmpty(proxyHost))
            {
                _handler.UseProxy = false;
                return;
            }

            var proxyType = _proxyClient.GetType().Name;
            var proxyAddress = proxyType switch
            {
                "HttpProxyClient" => $"http://{proxyHost}:{proxyPort}",
                "Socks4ProxyClient" => $"socks4://{proxyHost}:{proxyPort}",
                "Socks4aProxyClient" => $"socks4a://{proxyHost}:{proxyPort}",
                "Socks5ProxyClient" => $"socks5://{proxyHost}:{proxyPort}",
                _ => null
            };

            if (proxyAddress != null)
            {
                var proxyCredentials = GetProxyCredentials();
                _handler.Proxy = new WebProxy(proxyAddress, true, null, proxyCredentials);
                _handler.UseProxy = true;
            }
        }

        private string GetProxyHost()
        {
            // Use reflection to get host from ProxyClient
            var type = _proxyClient.GetType();
            var hostProperty = type.GetProperty("Host");
            if (hostProperty != null)
                return hostProperty.GetValue(_proxyClient)?.ToString();
            var hostField = type.GetField("Host");
            return hostField?.GetValue(_proxyClient)?.ToString();
        }

        private int GetProxyPort()
        {
            var type = _proxyClient.GetType();
            var portProperty = type.GetProperty("Port");
            if (portProperty != null)
                return (int)(portProperty.GetValue(_proxyClient) ?? 0);
            var portField = type.GetField("Port");
            return (int)(portField?.GetValue(_proxyClient) ?? 0);
        }

        private ICredentials GetProxyCredentials()
        {
            var type = _proxyClient.GetType();
            var settingsProperty = type.GetProperty("Settings");
            object settings = null;
            if (settingsProperty != null)
                settings = settingsProperty.GetValue(_proxyClient);
            else
            {
                var settingsField = type.GetField("Settings");
                settings = settingsField?.GetValue(_proxyClient);
            }

            if (settings != null)
            {
                var credsProperty = settings.GetType().GetProperty("Credentials");
                return credsProperty?.GetValue(settings) as ICredentials;
            }

            return null;
        }

        private void ApplyBrowserProfile()
        {
            // Set default headers based on browser profile
            var headers = GetDefaultHeaders(_options.BrowserProfile);
            foreach (var header in headers)
            {
                if (!_httpClient.DefaultRequestHeaders.Contains(header.Key))
                {
                    _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        private Dictionary<string, string> GetDefaultHeaders(TlsClientProfile profile)
        {
            var headers = profile switch
            {
                TlsClientProfile.Chrome133 or TlsClientProfile.Chrome120 or TlsClientProfile.Edge => new Dictionary<string, string>
                {
                    ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36",
                    ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7",
                    ["Accept-Language"] = "en-US,en;q=0.9",
                    ["Accept-Encoding"] = "gzip, deflate, br, zstd",
                    ["Upgrade-Insecure-Requests"] = "1",
                    ["Sec-Fetch-Dest"] = "document",
                    ["Sec-Fetch-Mode"] = "navigate",
                    ["Sec-Fetch-Site"] = "none",
                    ["Sec-Fetch-User"] = "?1",
                    ["Cache-Control"] = "max-age=0",
                    ["Priority"] = "u=0, i"
                },
                TlsClientProfile.Firefox => new Dictionary<string, string>
                {
                    ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:120.0) Gecko/20100101 Firefox/120.0",
                    ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8",
                    ["Accept-Language"] = "en-US,en;q=0.5",
                    ["Accept-Encoding"] = "gzip, deflate, br",
                    ["Upgrade-Insecure-Requests"] = "1",
                    ["Sec-Fetch-Dest"] = "document",
                    ["Sec-Fetch-Mode"] = "navigate",
                    ["Sec-Fetch-Site"] = "none",
                    ["Sec-Fetch-User"] = "?1",
                    ["Te"] = "trailers"
                },
                TlsClientProfile.Safari => new Dictionary<string, string>
                {
                    ["User-Agent"] = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15",
                    ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                    ["Accept-Language"] = "en-US,en;q=0.9",
                    ["Accept-Encoding"] = "gzip, deflate, br",
                    ["Sec-Fetch-Dest"] = "document",
                    ["Sec-Fetch-Mode"] = "navigate",
                    ["Sec-Fetch-Site"] = "none",
                    ["Sec-Fetch-User"] = "?1"
                },
                _ => new Dictionary<string, string>()
            };

            // Add Client Hints for Chrome-based browsers (critical for Cloudflare bypass)
            if (_options.IncludeClientHints)
            {
                var clientHints = GetClientHints(profile);
                foreach (var hint in clientHints)
                {
                    headers[hint.Key] = hint.Value;
                }
            }

            return headers;
        }

        /// <summary>
        /// Gets Client Hints headers for the browser profile.
        /// These are critical for bypassing Cloudflare and other anti-bot systems.
        /// </summary>
        private static Dictionary<string, string> GetClientHints(TlsClientProfile profile)
        {
            return profile switch
            {
                TlsClientProfile.Chrome133 or TlsClientProfile.Chrome120 or TlsClientProfile.Edge => new Dictionary<string, string>
                {
                    ["Sec-CH-UA"] = "\"Not A(Brand\";v=\"8\", \"Chromium\";v=\"133\", \"Google Chrome\";v=\"133\"",
                    ["Sec-CH-UA-Mobile"] = "?0",
                    ["Sec-CH-UA-Platform"] = "\"Windows\"",
                    ["Sec-CH-UA-Platform-Version"] = "\"15.0.0\"",
                    ["Sec-CH-UA-Arch"] = "\"x86\"",
                    ["Sec-CH-UA-Bitness"] = "\"64\"",
                    ["Sec-CH-UA-Full-Version"] = "\"133.0.6943.127\"",
                    ["Sec-CH-UA-Full-Version-List"] = "\"Not A(Brand\";v=\"8.0.0.0\", \"Chromium\";v=\"133.0.6943.127\", \"Google Chrome\";v=\"133.0.6943.127\"",
                    ["Sec-CH-UA-Model"] = "\"\""
                },
                TlsClientProfile.Safari => new Dictionary<string, string>
                {
                    // Safari has limited Client Hints support
                    ["Sec-CH-UA-Mobile"] = "?0",
                    ["Sec-CH-UA-Platform"] = "\"macOS\""
                },
                // Firefox doesn't send Client Hints by default
                _ => new Dictionary<string, string>()
            };
        }

        /// <summary>
        /// Sends an HTTP request and returns the response.
        /// </summary>
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            // Store raw request for logging
            var rawRequest = await SerializeRequestAsync(request);
            RawRequests.Add(rawRequest);

            // Apply custom extensions as headers
            if (_options.CustomExtensions?.Count > 0)
            {
                foreach (var ext in _options.CustomExtensions)
                {
                    request.Headers.TryAddWithoutValidation($"X-TLS-{ext.Key}", ext.Value);
                }
            }

            return await _httpClient.SendAsync(request, cancellationToken);
        }

        private async Task<byte[]> SerializeRequestAsync(HttpRequestMessage request)
        {
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms, Encoding.UTF8);

            // Write request line
            await writer.WriteLineAsync($"{request.Method} {request.RequestUri.PathAndQuery} HTTP/{request.Version}");

            // Write headers
            await writer.WriteLineAsync($"Host: {request.RequestUri.Host}");
            foreach (var header in request.Headers)
            {
                await writer.WriteLineAsync($"{header.Key}: {string.Join(", ", header.Value)}");
            }

            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                {
                    await writer.WriteLineAsync($"{header.Key}: {string.Join(", ", header.Value)}");
                }

                // Write content
                var content = await request.Content.ReadAsByteArrayAsync();
                await writer.WriteLineAsync();
                await writer.FlushAsync();
                await ms.WriteAsync(content);
            }

            await writer.FlushAsync();
            return ms.ToArray();
        }

        /// <summary>
        /// Gets the JA3 fingerprint that will be used for this client.
        /// </summary>
        public string GetEffectiveJa3Fingerprint()
        {
            if (!string.IsNullOrEmpty(_options.Ja3Fingerprint))
            {
                return _options.Ja3Fingerprint;
            }
            return TlsClientProfileDefaults.GetJa3Fingerprint(_options.BrowserProfile);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _httpClient?.Dispose();
            _handler?.Dispose();
        }
    }
}
