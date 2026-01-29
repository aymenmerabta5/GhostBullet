using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RuriLib.Functions.Http.NativeTlsClient
{
    /// <summary>
    /// Native tls-client wrapper that calls the Go-based tls-client library via P/Invoke.
    /// This enables proper JA3 fingerprinting, HTTP/2 SETTINGS, and TLS extensions.
    /// </summary>
    public static class NativeTlsClient
    {
        private const string DllName = "tls-client-windows-64.dll";

        // P/Invoke declarations for the tls-client library
        // Note: tls-client uses UTF-8 encoded byte arrays, not null-terminated strings
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "request")]
        private static extern IntPtr Request(byte[] requestPayload);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "freeMemory")]
        private static extern void FreeMemory(IntPtr ptr);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "destroySession")]
        private static extern void DestroySession(byte[] sessionId);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "getCookiesFromSession")]
        private static extern IntPtr GetCookiesFromSession(byte[] sessionId, byte[] url);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "addCookiesToSession")]
        private static extern IntPtr AddCookiesToSession(byte[] sessionId, byte[] cookiesJson);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        private static bool? _isAvailable;
        private static string _lastError;

        /// <summary>
        /// Gets the last error message if IsAvailable is false.
        /// </summary>
        public static string LastError => _lastError;

        /// <summary>
        /// Checks if the native tls-client library is available.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue)
                    return _isAvailable.Value;

                try
                {
                    // Check if DLL exists first
                    var dllPath = FindDllPath();
                    if (string.IsNullOrEmpty(dllPath))
                    {
                        var searchedStr = string.Join(", ", _searchedPaths?.Take(2) ?? Array.Empty<string>());
                        _lastError = $"DLL not found: {DllName}. Searched: {searchedStr}. Download from https://github.com/bogdanfinn/tls-client/releases";
                        _isAvailable = false;
                        return false;
                    }

                    // Try to make a simple request to test if the library is loaded
                    var testRequest = new TlsClientRequest
                    {
                        RequestUrl = "https://example.com",
                        RequestMethod = "HEAD",
                        TlsClientIdentifier = "chrome_120",
                        TimeoutMilliseconds = 5000,
                        IsByteRequest = false,
                        FollowRedirects = false
                    };

                    var json = JsonSerializer.Serialize(testRequest, JsonOptions);
                    var jsonBytes = ToNullTerminatedUtf8(json);
                    var resultPtr = Request(jsonBytes);
                    
                    if (resultPtr != IntPtr.Zero)
                    {
                        var responseJson = PtrToUtf8String(resultPtr);
                        FreeMemory(resultPtr);
                        
                        if (!string.IsNullOrEmpty(responseJson))
                        {
                            _isAvailable = true;
                            _lastError = null;
                        }
                        else
                        {
                            _lastError = "DLL loaded but returned empty response";
                            _isAvailable = false;
                        }
                    }
                    else
                    {
                        _lastError = "DLL loaded but Request returned null";
                        _isAvailable = false;
                    }
                }
                catch (DllNotFoundException ex)
                {
                    _lastError = $"DLL not found: {ex.Message}";
                    _isAvailable = false;
                }
                catch (BadImageFormatException ex)
                {
                    _lastError = $"DLL architecture mismatch (need 64-bit): {ex.Message}";
                    _isAvailable = false;
                }
                catch (Exception ex)
                {
                    _lastError = $"Failed to load DLL: {ex.Message}";
                    _isAvailable = false;
                }

                return _isAvailable.Value;
            }
        }

        /// <summary>
        /// Resets the availability check to retry loading the DLL.
        /// </summary>
        public static void ResetAvailability()
        {
            _isAvailable = null;
            _lastError = null;
        }

        private static string[] _searchedPaths;

        /// <summary>
        /// Gets the paths that were searched for the DLL.
        /// </summary>
        public static string[] SearchedPaths => _searchedPaths ?? Array.Empty<string>();

        private static string FindDllPath()
        {
            // Check common locations
            _searchedPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DllName),
                Path.Combine(Environment.CurrentDirectory, DllName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", DllName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "native", DllName),
                DllName, // Current directory
            };

            foreach (var path in _searchedPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        /// <summary>
        /// Sends an HTTP request using the native tls-client library.
        /// </summary>
        public static TlsClientResponse SendRequest(TlsClientRequest request)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException("Native tls-client is only supported on Windows");
            }

            if (!IsAvailable)
            {
                throw new TlsClientException($"Native tls-client is not available: {_lastError}");
            }

            var requestJson = JsonSerializer.Serialize(request, JsonOptions);
            var requestBytes = ToNullTerminatedUtf8(requestJson);
            
            IntPtr resultPtr;
            try
            {
                resultPtr = Request(requestBytes);
            }
            catch (Exception ex)
            {
                throw new TlsClientException($"P/Invoke call failed: {ex.Message}", ex);
            }

            if (resultPtr == IntPtr.Zero)
            {
                throw new TlsClientException("tls-client returned null pointer");
            }

            try
            {
                var responseJson = PtrToUtf8String(resultPtr);
                if (string.IsNullOrEmpty(responseJson))
                {
                    throw new TlsClientException("Empty response from tls-client");
                }

                var response = JsonSerializer.Deserialize<TlsClientResponse>(responseJson, JsonOptions);
                if (response == null)
                {
                    throw new TlsClientException($"Failed to deserialize response: {responseJson.Substring(0, Math.Min(500, responseJson.Length))}");
                }

                // Check for error response (status 0 with error body)
                if (response.Status == 0 && !string.IsNullOrEmpty(response.Body))
                {
                    throw new TlsClientException($"tls-client error: {response.Body}");
                }

                return response;
            }
            finally
            {
                FreeMemory(resultPtr);
            }
        }

        /// <summary>
        /// Destroys a session to free resources.
        /// </summary>
        public static void DestroyClientSession(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && IsAvailable)
            {
                try
                {
                    DestroySession(ToNullTerminatedUtf8(sessionId));
                }
                catch
                {
                    // Ignore errors when destroying session
                }
            }
        }

        /// <summary>
        /// Gets cookies from a session.
        /// </summary>
        public static string GetSessionCookies(string sessionId, string url)
        {
            if (string.IsNullOrEmpty(sessionId) || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !IsAvailable)
                return "[]";

            try
            {
                var resultPtr = GetCookiesFromSession(ToNullTerminatedUtf8(sessionId), ToNullTerminatedUtf8(url));
                if (resultPtr == IntPtr.Zero)
                    return "[]";

                var result = PtrToUtf8String(resultPtr);
                FreeMemory(resultPtr);
                return result ?? "[]";
            }
            catch
            {
                return "[]";
            }
        }

        #region UTF-8 Helpers

        /// <summary>
        /// Converts a string to a null-terminated UTF-8 byte array for P/Invoke.
        /// </summary>
        private static byte[] ToNullTerminatedUtf8(string str)
        {
            if (string.IsNullOrEmpty(str))
                return new byte[] { 0 };

            var utf8Bytes = Encoding.UTF8.GetBytes(str);
            var result = new byte[utf8Bytes.Length + 1];
            Array.Copy(utf8Bytes, result, utf8Bytes.Length);
            result[utf8Bytes.Length] = 0; // Null terminator
            return result;
        }

        /// <summary>
        /// Reads a UTF-8 string from a native pointer.
        /// </summary>
        private static string PtrToUtf8String(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return null;

            // Find the length by looking for null terminator
            int length = 0;
            while (Marshal.ReadByte(ptr, length) != 0)
            {
                length++;
                // Safety limit
                if (length > 100_000_000)
                    throw new TlsClientException("Response too large");
            }

            if (length == 0)
                return string.Empty;

            var bytes = new byte[length];
            Marshal.Copy(ptr, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        #endregion
    }

    /// <summary>
    /// Request payload for the native tls-client library.
    /// </summary>
    public class TlsClientRequest
    {
        [JsonPropertyName("tlsClientIdentifier")]
        public string TlsClientIdentifier { get; set; } = "chrome_120";

        [JsonPropertyName("customTlsClient")]
        public CustomTlsClientConfig CustomTlsClient { get; set; }

        [JsonPropertyName("followRedirects")]
        public bool FollowRedirects { get; set; } = true;

        [JsonPropertyName("insecureSkipVerify")]
        public bool InsecureSkipVerify { get; set; } = false;

        [JsonPropertyName("withoutCookieJar")]
        public bool WithoutCookieJar { get; set; } = false;

        [JsonPropertyName("withDefaultCookieJar")]
        public bool WithDefaultCookieJar { get; set; } = true;

        [JsonPropertyName("isByteRequest")]
        public bool IsByteRequest { get; set; } = false;

        [JsonPropertyName("isByteResponse")]
        public bool IsByteResponse { get; set; } = false;

        [JsonPropertyName("forceHttp1")]
        public bool ForceHttp1 { get; set; } = false;

        [JsonPropertyName("catchPanics")]
        public bool CatchPanics { get; set; } = true;

        [JsonPropertyName("withDebug")]
        public bool WithDebug { get; set; } = false;

        [JsonPropertyName("withRandomTLSExtensionOrder")]
        public bool WithRandomTlsExtensionOrder { get; set; } = false;

        [JsonPropertyName("timeoutSeconds")]
        public int? TimeoutSeconds { get; set; }

        [JsonPropertyName("timeoutMilliseconds")]
        public int? TimeoutMilliseconds { get; set; }

        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; }

        [JsonPropertyName("proxyUrl")]
        public string ProxyUrl { get; set; }

        [JsonPropertyName("isRotatingProxy")]
        public bool IsRotatingProxy { get; set; } = false;

        [JsonPropertyName("certificatePinningHosts")]
        public Dictionary<string, string[]> CertificatePinningHosts { get; set; }

        [JsonPropertyName("headers")]
        public Dictionary<string, string> Headers { get; set; }

        [JsonPropertyName("headerOrder")]
        public List<string> HeaderOrder { get; set; }

        [JsonPropertyName("requestUrl")]
        public string RequestUrl { get; set; }

        [JsonPropertyName("requestMethod")]
        public string RequestMethod { get; set; } = "GET";

        [JsonPropertyName("requestBody")]
        public string RequestBody { get; set; }

        [JsonPropertyName("requestCookies")]
        public List<TlsClientCookie> RequestCookies { get; set; }
    }

    /// <summary>
    /// Custom TLS client configuration for advanced fingerprinting.
    /// </summary>
    public class CustomTlsClientConfig
    {
        [JsonPropertyName("ja3String")]
        public string Ja3String { get; set; }

        [JsonPropertyName("h2Settings")]
        public Http2Settings H2Settings { get; set; }

        [JsonPropertyName("h2SettingsOrder")]
        public List<string> H2SettingsOrder { get; set; }

        [JsonPropertyName("pseudoHeaderOrder")]
        public List<string> PseudoHeaderOrder { get; set; }

        [JsonPropertyName("connectionFlow")]
        public int ConnectionFlow { get; set; } = 15663105;

        [JsonPropertyName("priorityFrames")]
        public List<PriorityFrame> PriorityFrames { get; set; }

        [JsonPropertyName("headerPriority")]
        public PriorityParam HeaderPriority { get; set; }

        [JsonPropertyName("certCompressionAlgo")]
        public string CertCompressionAlgo { get; set; }

        [JsonPropertyName("supportedVersions")]
        public List<string> SupportedVersions { get; set; }

        [JsonPropertyName("supportedSignatureAlgorithms")]
        public List<string> SupportedSignatureAlgorithms { get; set; }

        [JsonPropertyName("supportedDelegatedCredentialsAlgorithms")]
        public List<string> SupportedDelegatedCredentialsAlgorithms { get; set; }

        [JsonPropertyName("keyShareCurves")]
        public List<string> KeyShareCurves { get; set; }

        [JsonPropertyName("alpnProtocols")]
        public List<string> AlpnProtocols { get; set; }

        [JsonPropertyName("alpsProtocols")]
        public List<string> AlpsProtocols { get; set; }
    }

    /// <summary>
    /// HTTP/2 settings configuration.
    /// </summary>
    public class Http2Settings
    {
        [JsonPropertyName("HEADER_TABLE_SIZE")]
        public int HeaderTableSize { get; set; } = 65536;

        [JsonPropertyName("ENABLE_PUSH")]
        public int EnablePush { get; set; } = 0;

        [JsonPropertyName("MAX_CONCURRENT_STREAMS")]
        public int MaxConcurrentStreams { get; set; } = 1000;

        [JsonPropertyName("INITIAL_WINDOW_SIZE")]
        public int InitialWindowSize { get; set; } = 6291456;

        [JsonPropertyName("MAX_FRAME_SIZE")]
        public int MaxFrameSize { get; set; } = 16384;

        [JsonPropertyName("MAX_HEADER_LIST_SIZE")]
        public int MaxHeaderListSize { get; set; } = 262144;
    }

    /// <summary>
    /// HTTP/2 priority frame configuration.
    /// </summary>
    public class PriorityFrame
    {
        [JsonPropertyName("streamID")]
        public int StreamId { get; set; }

        [JsonPropertyName("priorityParam")]
        public PriorityParam PriorityParam { get; set; }
    }

    /// <summary>
    /// HTTP/2 priority parameter.
    /// </summary>
    public class PriorityParam
    {
        [JsonPropertyName("weight")]
        public int Weight { get; set; } = 256;

        [JsonPropertyName("streamDep")]
        public int StreamDep { get; set; } = 0;

        [JsonPropertyName("exclusive")]
        public bool Exclusive { get; set; } = true;
    }

    /// <summary>
    /// Cookie for tls-client requests.
    /// </summary>
    public class TlsClientCookie
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; }

        [JsonPropertyName("path")]
        public string Path { get; set; } = "/";

        [JsonPropertyName("domain")]
        public string Domain { get; set; }

        [JsonPropertyName("expires")]
        public string Expires { get; set; }
    }

    /// <summary>
    /// Response from the native tls-client library.
    /// </summary>
    public class TlsClientResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; }

        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("target")]
        public string Target { get; set; }

        [JsonPropertyName("body")]
        public string Body { get; set; }

        [JsonPropertyName("headers")]
        public Dictionary<string, List<string>> Headers { get; set; }

        [JsonPropertyName("cookies")]
        public Dictionary<string, string> Cookies { get; set; }

        [JsonPropertyName("usedProtocol")]
        public string UsedProtocol { get; set; }
    }

    /// <summary>
    /// Exception thrown when tls-client operations fail.
    /// </summary>
    public class TlsClientException : Exception
    {
        public TlsClientException(string message) : base(message) { }
        public TlsClientException(string message, Exception inner) : base(message, inner) { }
    }
}
