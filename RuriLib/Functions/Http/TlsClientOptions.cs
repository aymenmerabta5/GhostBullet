using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace RuriLib.Functions.Http
{
    /// <summary>
    /// Options for configuring the TLS client behavior.
    /// </summary>
    public class TlsClientOptions
    {
        /// <summary>
        /// Whether to use the native tls-client engine (Go-based) for proper JA3 fingerprinting.
        /// When false, falls back to .NET HttpClient which has limited fingerprint control.
        /// Only supported on Windows.
        /// </summary>
        public bool UseNativeEngine { get; set; } = true;

        /// <summary>
        /// The browser profile to mimic (determines JA3 fingerprint, cipher suites, extensions).
        /// </summary>
        public TlsClientProfile BrowserProfile { get; set; } = TlsClientProfile.Chrome120;

        /// <summary>
        /// Custom JA3 fingerprint string. If empty, the profile's default JA3 is used.
        /// Only applied when UseNativeEngine is true.
        /// </summary>
        public string Ja3Fingerprint { get; set; } = string.Empty;

        /// <summary>
        /// Force HTTP/1.1 instead of HTTP/2.
        /// </summary>
        public bool ForceHttp1 { get; set; } = false;

        /// <summary>
        /// Skip TLS certificate verification (insecure).
        /// </summary>
        public bool InsecureSkipVerify { get; set; } = false;

        /// <summary>
        /// Custom TLS extensions to add (extension_name -> extension_value).
        /// </summary>
        public Dictionary<string, string> CustomExtensions { get; set; } = new();

        /// <summary>
        /// Custom cipher suites to use (overrides profile defaults if specified).
        /// </summary>
        public List<string> CustomCipherSuites { get; set; } = new();

        /// <summary>
        /// HTTP/2 SETTINGS frame parameters.
        /// Only applied when UseNativeEngine is true.
        /// </summary>
        public Dictionary<string, int> Http2Settings { get; set; } = new();

        /// <summary>
        /// Client certificates for mutual TLS authentication (PEM format).
        /// </summary>
        public List<string> ClientCertificates { get; set; } = new();

        /// <summary>
        /// Disable TLS session resumption.
        /// </summary>
        public bool DisableSessionResumption { get; set; } = false;

        /// <summary>
        /// The timeout for connection establishment.
        /// </summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// The timeout for read/write operations.
        /// </summary>
        public TimeSpan ReadWriteTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Whether to allow automatic redirection.
        /// </summary>
        public bool AutoRedirect { get; set; } = true;

        /// <summary>
        /// Maximum number of redirects to follow.
        /// </summary>
        public int MaxNumberOfRedirects { get; set; } = 8;

        /// <summary>
        /// Whether to read response content.
        /// </summary>
        public bool ReadResponseContent { get; set; } = true;

        /// <summary>
        /// Certificate revocation check mode.
        /// </summary>
        public X509RevocationMode CertRevocationMode { get; set; } = X509RevocationMode.NoCheck;

        /// <summary>
        /// Session ID for persistent sessions across requests (native engine only).
        /// If empty, a new session is created per request.
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// Whether to include Client Hints headers automatically based on the browser profile.
        /// These are critical for bypassing Cloudflare and other anti-bot systems.
        /// </summary>
        public bool IncludeClientHints { get; set; } = true;

        /// <summary>
        /// Whether to randomize TLS extension order (can help avoid detection).
        /// Recommended to keep enabled for anti-bot bypass.
        /// </summary>
        public bool RandomizeTlsExtensionOrder { get; set; } = true;

        /// <summary>
        /// Checks if the native engine should be used based on platform and availability.
        /// </summary>
        public bool ShouldUseNativeEngine()
        {
            if (!UseNativeEngine)
                return false;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            try
            {
                return NativeTlsClient.NativeTlsClient.IsAvailable;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Predefined browser profiles for TLS fingerprint mimicking.
    /// </summary>
    public enum TlsClientProfile
    {
        Chrome133,
        Chrome120,
        Firefox,
        Safari,
        Edge,
        Custom
    }

    /// <summary>
    /// Provides default configurations for browser profiles.
    /// </summary>
    public static class TlsClientProfileDefaults
    {
        /// <summary>
        /// Gets the default JA3 fingerprint for a browser profile.
        /// </summary>
        public static string GetJa3Fingerprint(TlsClientProfile profile)
        {
            return profile switch
            {
                TlsClientProfile.Chrome133 => "771,4865-4866-4867-49195-49199-49196-49200-52393-52392-49171-49172-156-157-47-53,0-23-65281-10-11-35-16-5-13-18-51-45-43-27-17513-21,29-23-24,0",
                TlsClientProfile.Chrome120 => "771,4865-4866-4867-49195-49199-49196-49200-52393-52392-49171-49172-156-157-47-53,0-23-65281-10-11-35-16-5-13-18-51-45-43-27-17513-21,29-23-24,0",
                TlsClientProfile.Firefox => "771,4865-4867-49195-49199-49196-49200-52393-52392-49171-49172-156-157-47-53,0-23-65281-10-11-35-16-5-51-43-13-45-28-65037,29-23-24-25-256-257,0",
                TlsClientProfile.Safari => "771,4865-4866-4867-49196-49195-52393-49200-49199-52392-49162-49161-49172-49171-157-156-53-47-49160-49170-10,0-23-65281-10-11-35-16-5-13-18-51-45-43-27-17513,29-23-24-256-257,0",
                TlsClientProfile.Edge => "771,4865-4866-4867-49195-49199-49196-49200-52393-52392-49171-49172-156-157-47-53,0-23-65281-10-11-35-16-5-13-18-51-45-43-27-17513-21,29-23-24,0",
                TlsClientProfile.Custom => string.Empty,
                _ => string.Empty
            };
        }

        /// <summary>
        /// Gets the default cipher suites for a browser profile.
        /// </summary>
        public static TlsCipherSuite[] GetCipherSuites(TlsClientProfile profile)
        {
            var chromeSuites = new[]
            {
                TlsCipherSuite.TLS_AES_128_GCM_SHA256,
                TlsCipherSuite.TLS_AES_256_GCM_SHA384,
                TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,
                TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
                TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
                TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
                TlsCipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
                TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA,
                TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA,
                TlsCipherSuite.TLS_RSA_WITH_AES_128_GCM_SHA256,
                TlsCipherSuite.TLS_RSA_WITH_AES_256_GCM_SHA384,
                TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA,
                TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA
            };

            var firefoxSuites = new[]
            {
                TlsCipherSuite.TLS_AES_128_GCM_SHA256,
                TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,
                TlsCipherSuite.TLS_AES_256_GCM_SHA384,
                TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,
                TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
                TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
                TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
                TlsCipherSuite.TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256,
                TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA,
                TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA,
                TlsCipherSuite.TLS_RSA_WITH_AES_128_GCM_SHA256,
                TlsCipherSuite.TLS_RSA_WITH_AES_256_GCM_SHA384,
                TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA,
                TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA
            };

            return profile switch
            {
                TlsClientProfile.Chrome133 or TlsClientProfile.Chrome120 or TlsClientProfile.Edge => chromeSuites,
                TlsClientProfile.Firefox => firefoxSuites,
                TlsClientProfile.Safari => chromeSuites,
                _ => chromeSuites
            };
        }

        /// <summary>
        /// Gets the default HTTP/2 settings for a browser profile.
        /// </summary>
        public static Dictionary<string, int> GetHttp2Settings(TlsClientProfile profile)
        {
            var chromeSettings = new Dictionary<string, int>
            {
                ["HEADER_TABLE_SIZE"] = 65536,
                ["ENABLE_PUSH"] = 0,
                ["MAX_CONCURRENT_STREAMS"] = 1000,
                ["INITIAL_WINDOW_SIZE"] = 6291456,
                ["MAX_FRAME_SIZE"] = 16384,
                ["MAX_HEADER_LIST_SIZE"] = 262144
            };

            var firefoxSettings = new Dictionary<string, int>
            {
                ["HEADER_TABLE_SIZE"] = 65536,
                ["ENABLE_PUSH"] = 0,
                ["MAX_CONCURRENT_STREAMS"] = 100,
                ["INITIAL_WINDOW_SIZE"] = 131072,
                ["MAX_FRAME_SIZE"] = 16384,
                ["MAX_HEADER_LIST_SIZE"] = 262144
            };

            return profile switch
            {
                TlsClientProfile.Chrome133 or TlsClientProfile.Chrome120 or TlsClientProfile.Edge => chromeSettings,
                TlsClientProfile.Firefox => firefoxSettings,
                TlsClientProfile.Safari => chromeSettings,
                _ => chromeSettings
            };
        }
    }
}
