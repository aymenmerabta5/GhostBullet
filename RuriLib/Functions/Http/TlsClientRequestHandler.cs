using RuriLib.Extensions;
using RuriLib.Functions.Files;
using RuriLib.Functions.Http.NativeTlsClient;
using RuriLib.Functions.Http.Options;
using RuriLib.Helpers;
using RuriLib.Logging;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Bots;
using RuriLib.Models.Proxies;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Functions.Http
{
    /// <summary>
    /// HTTP request handler that uses the TLS client wrapper for browser fingerprint mimicking.
    /// Supports both native tls-client engine (for proper JA3 fingerprinting) and .NET HttpClient fallback.
    /// </summary>
    internal class TlsClientRequestHandler : HttpRequestHandler
    {
        private readonly TlsClientOptions _tlsOptions;
        private TlsClientRequestBuilder _nativeRequestBuilder;

        public TlsClientRequestHandler(TlsClientOptions tlsOptions)
        {
            _tlsOptions = tlsOptions;
        }

        public override async Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
        {
            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            // Try native engine first (reset check in case DLL was added)
            if (_tlsOptions.UseNativeEngine && !NativeTlsClient.NativeTlsClient.IsAvailable)
                NativeTlsClient.NativeTlsClient.ResetAvailability();

            if (_tlsOptions.ShouldUseNativeEngine())
            {
                await HttpRequestStandardNative(data, options).ConfigureAwait(false);
                return;
            }

            // Log why native wasn't used
            if (_tlsOptions.UseNativeEngine)
            {
                var reason = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "Not Windows"
                    : !NativeTlsClient.NativeTlsClient.IsAvailable
                        ? $"DLL unavailable: {NativeTlsClient.NativeTlsClient.LastError ?? "unknown"}"
                        : "Unknown";
                data.Logger.Log($"[TLS Client] Native engine requested but unavailable: {reason}", LogColors.Tomato);
            }

            // Fallback to .NET HttpClient
            var client = CreateTlsClient(data);

            using var request = new HttpRequestMessage(
                new System.Net.Http.HttpMethod(options.Method.ToString()),
                new Uri(options.Url));

            // Set HTTP version
            request.Version = Version.Parse(options.HttpVersion);

            // Add headers
            AddHeaders(request, options.CustomHeaders, data);

            // Add cookies
            AddCookies(request, data.COOKIES, options.Url);

            // Add content
            if (!string.IsNullOrEmpty(options.Content) || options.AlwaysSendContent)
            {
                var content = options.Content;

                if (options.UrlEncodeContent)
                {
                    content = string.Join("", content.SplitInChunks(2080)
                        .Select(Uri.EscapeDataString))
                        .Replace($"%26", "&").Replace($"%3D", "=");
                }

                request.Content = new StringContent(content.Unescape());
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(options.ContentType);
            }

            data.Logger.LogHeader();

            try
            {
                Activity.Current = null;
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);
                using var response = await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);

                LogHttpRequestData(data, client, request);
                await LogHttpResponseData(data, response, request, options).ConfigureAwait(false);
            }
            catch
            {
                LogHttpRequestData(data, client, request);
                throw;
            }
        }

        private async Task HttpRequestStandardNative(BotData data, StandardHttpRequestOptions options)
        {
            _nativeRequestBuilder ??= new TlsClientRequestBuilder(_tlsOptions);

            var content = options.Content;
            if (!string.IsNullOrEmpty(content) && options.UrlEncodeContent)
            {
                content = string.Join("", content.SplitInChunks(2080)
                    .Select(Uri.EscapeDataString))
                    .Replace("%26", "&").Replace("%3D", "=");
            }

            var proxy = data.UseProxy ? data.Proxy : null;
            var nativeRequest = _nativeRequestBuilder.Build(
                options.Url,
                options.Method.ToString(),
                options.CustomHeaders,
                data.COOKIES,
                content?.Unescape(),
                proxy,
                _tlsOptions.SessionId,
                options.TimeoutMilliseconds
            );

            data.Logger.LogHeader();
            LogNativeRequestData(data, nativeRequest);

            try
            {
                var response = await Task.Run(() => NativeTlsClient.NativeTlsClient.SendRequest(nativeRequest), data.CancellationToken).ConfigureAwait(false);
                ProcessNativeResponse(data, response, options);
            }
            catch (TlsClientException ex)
            {
                LogNativeError(data, ex);
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                data.Logger.Log($"Unexpected error: {ex.GetType().Name}: {ex.Message}", LogColors.Tomato);
                throw new TlsClientException($"Native request failed: {ex.Message}", ex);
            }
        }

        public override async Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
        {
            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            // Try native engine first
            if (_tlsOptions.ShouldUseNativeEngine())
            {
                await HttpRequestRawNative(data, options).ConfigureAwait(false);
                return;
            }

            // Fallback to .NET HttpClient
            var client = CreateTlsClient(data);

            using var request = new HttpRequestMessage(
                new System.Net.Http.HttpMethod(options.Method.ToString()),
                new Uri(options.Url))
            {
                Content = new ByteArrayContent(options.Content)
            };

            request.Version = Version.Parse(options.HttpVersion);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(options.ContentType);

            AddHeaders(request, options.CustomHeaders, data);
            AddCookies(request, data.COOKIES, options.Url);

            data.Logger.LogHeader();

            try
            {
                Activity.Current = null;
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);
                using var response = await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);

                LogHttpRequestData(data, client, request);
                await LogHttpResponseData(data, response, request, options).ConfigureAwait(false);
            }
            catch
            {
                LogHttpRequestData(data, client, request);
                throw;
            }
        }

        private async Task HttpRequestRawNative(BotData data, RawHttpRequestOptions options)
        {
            _nativeRequestBuilder ??= new TlsClientRequestBuilder(_tlsOptions);

            var proxy = data.UseProxy ? data.Proxy : null;
            var bodyBase64 = Convert.ToBase64String(options.Content);

            var nativeRequest = _nativeRequestBuilder.Build(
                options.Url,
                options.Method.ToString(),
                options.CustomHeaders,
                data.COOKIES,
                bodyBase64,
                proxy,
                _tlsOptions.SessionId,
                options.TimeoutMilliseconds
            );

            // Mark as byte request
            nativeRequest.IsByteRequest = true;
            nativeRequest.Headers["Content-Type"] = options.ContentType;

            data.Logger.LogHeader();
            LogNativeRequestData(data, nativeRequest);

            try
            {
                var response = await Task.Run(() => NativeTlsClient.NativeTlsClient.SendRequest(nativeRequest), data.CancellationToken).ConfigureAwait(false);
                ProcessNativeResponse(data, response, options);
            }
            catch (TlsClientException ex)
            {
                LogNativeError(data, ex);
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                data.Logger.Log($"Unexpected error: {ex.GetType().Name}: {ex.Message}", LogColors.Tomato);
                throw new TlsClientException($"Native request failed: {ex.Message}", ex);
            }
        }

        public override async Task HttpRequestBasicAuth(BotData data, BasicAuthHttpRequestOptions options)
        {
            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            // Try native engine first
            if (_tlsOptions.ShouldUseNativeEngine())
            {
                await HttpRequestBasicAuthNative(data, options).ConfigureAwait(false);
                return;
            }

            // Fallback to .NET HttpClient
            var client = CreateTlsClient(data);

            using var request = new HttpRequestMessage(
                new System.Net.Http.HttpMethod(options.Method.ToString()),
                new Uri(options.Url));

            request.Version = Version.Parse(options.HttpVersion);

            // Add basic auth header
            var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);

            AddHeaders(request, options.CustomHeaders, data);
            AddCookies(request, data.COOKIES, options.Url);

            data.Logger.LogHeader();

            try
            {
                Activity.Current = null;
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);
                using var response = await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);

                LogHttpRequestData(data, client, request);
                await LogHttpResponseData(data, response, request, options).ConfigureAwait(false);
            }
            catch
            {
                LogHttpRequestData(data, client, request);
                throw;
            }
        }

        private async Task HttpRequestBasicAuthNative(BotData data, BasicAuthHttpRequestOptions options)
        {
            _nativeRequestBuilder ??= new TlsClientRequestBuilder(_tlsOptions);

            // Add Authorization header
            var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"));
            var headers = new Dictionary<string, string>(options.CustomHeaders)
            {
                ["Authorization"] = $"Basic {authValue}"
            };

            var proxy = data.UseProxy ? data.Proxy : null;
            var nativeRequest = _nativeRequestBuilder.Build(
                options.Url,
                options.Method.ToString(),
                headers,
                data.COOKIES,
                null,
                proxy,
                _tlsOptions.SessionId,
                options.TimeoutMilliseconds
            );

            data.Logger.LogHeader();
            LogNativeRequestData(data, nativeRequest);

            try
            {
                var response = await Task.Run(() => NativeTlsClient.NativeTlsClient.SendRequest(nativeRequest), data.CancellationToken).ConfigureAwait(false);
                ProcessNativeResponse(data, response, options);
            }
            catch (TlsClientException ex)
            {
                LogNativeError(data, ex);
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                data.Logger.Log($"Unexpected error: {ex.GetType().Name}: {ex.Message}", LogColors.Tomato);
                throw new TlsClientException($"Native request failed: {ex.Message}", ex);
            }
        }

        public override async Task HttpRequestMultipart(BotData data, MultipartHttpRequestOptions options)
        {
            foreach (var cookie in options.CustomCookies)
                data.COOKIES[cookie.Key] = cookie.Value;

            // Try native engine first
            if (_tlsOptions.ShouldUseNativeEngine())
            {
                await HttpRequestMultipartNative(data, options).ConfigureAwait(false);
                return;
            }

            // Fallback to .NET HttpClient
            var client = CreateTlsClient(data);

            using var request = new HttpRequestMessage(
                new System.Net.Http.HttpMethod(options.Method.ToString()),
                new Uri(options.Url));

            request.Version = Version.Parse(options.HttpVersion);

            // Build multipart content
            var boundary = string.IsNullOrEmpty(options.Boundary) ? GenerateMultipartBoundary() : options.Boundary;
            var multipartContent = new MultipartFormDataContent(boundary);

            foreach (var content in options.Contents)
            {
                switch (content)
                {
                    case StringHttpContent stringContent:
                        multipartContent.Add(new StringContent(stringContent.Data), stringContent.Name);
                        break;

                    case RawHttpContent rawContent:
                        var byteContent = new ByteArrayContent(rawContent.Data);
                        byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse(rawContent.ContentType);
                        multipartContent.Add(byteContent, rawContent.Name);
                        break;

                    case FileHttpContent fileContent:
                        lock (FileLocker.GetHandle(fileContent.FileName))
                        {
                            if (data.Providers.Security.RestrictBlocksToCWD)
                                FileUtils.ThrowIfNotInCWD(fileContent.FileName);

                            var fileStream = new FileStream(fileContent.FileName, FileMode.Open);
                            var fileStreamContent = CreateFileContent(fileStream, fileContent.Name, Path.GetFileName(fileContent.FileName), fileContent.ContentType);
                            multipartContent.Add(fileStreamContent, fileContent.Name);
                        }
                        break;
                }
            }

            request.Content = multipartContent;

            AddHeaders(request, options.CustomHeaders, data);
            AddCookies(request, data.COOKIES, options.Url);

            data.Logger.LogHeader();

            try
            {
                Activity.Current = null;
                using var timeoutCts = new CancellationTokenSource(options.TimeoutMilliseconds);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(data.CancellationToken, timeoutCts.Token);
                using var response = await client.SendAsync(request, linkedCts.Token).ConfigureAwait(false);

                LogHttpRequestData(data, client, request);
                await LogHttpResponseData(data, response, request, options).ConfigureAwait(false);
            }
            catch
            {
                LogHttpRequestData(data, client, request);
                throw;
            }
        }

        private async Task HttpRequestMultipartNative(BotData data, MultipartHttpRequestOptions options)
        {
            _nativeRequestBuilder ??= new TlsClientRequestBuilder(_tlsOptions);

            // Build multipart body manually for native engine
            var boundary = string.IsNullOrEmpty(options.Boundary) ? GenerateMultipartBoundary() : options.Boundary;
            var multipartBody = BuildMultipartBody(options.Contents, boundary, data);

            var headers = new Dictionary<string, string>(options.CustomHeaders)
            {
                ["Content-Type"] = $"multipart/form-data; boundary={boundary}"
            };

            var proxy = data.UseProxy ? data.Proxy : null;
            var nativeRequest = _nativeRequestBuilder.Build(
                options.Url,
                options.Method.ToString(),
                headers,
                data.COOKIES,
                Convert.ToBase64String(multipartBody),
                proxy,
                _tlsOptions.SessionId,
                options.TimeoutMilliseconds
            );

            nativeRequest.IsByteRequest = true;

            data.Logger.LogHeader();
            LogNativeRequestData(data, nativeRequest);

            try
            {
                var response = await Task.Run(() => NativeTlsClient.NativeTlsClient.SendRequest(nativeRequest), data.CancellationToken).ConfigureAwait(false);
                ProcessNativeResponse(data, response, options);
            }
            catch (TlsClientException ex)
            {
                LogNativeError(data, ex);
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                data.Logger.Log($"Unexpected error: {ex.GetType().Name}: {ex.Message}", LogColors.Tomato);
                throw new TlsClientException($"Native request failed: {ex.Message}", ex);
            }
        }

        private byte[] BuildMultipartBody(List<MyHttpContent> contents, string boundary, BotData data)
        {
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true);

            foreach (var content in contents)
            {
                switch (content)
                {
                    case StringHttpContent stringContent:
                        writer.Write($"--{boundary}\r\n");
                        writer.Write($"Content-Disposition: form-data; name=\"{stringContent.Name}\"\r\n");
                        if (!string.IsNullOrEmpty(stringContent.ContentType))
                            writer.Write($"Content-Type: {stringContent.ContentType}\r\n");
                        writer.Write("\r\n");
                        writer.Write(stringContent.Data);
                        writer.Write("\r\n");
                        break;

                    case RawHttpContent rawContent:
                        writer.Write($"--{boundary}\r\n");
                        writer.Write($"Content-Disposition: form-data; name=\"{rawContent.Name}\"\r\n");
                        writer.Write($"Content-Type: {rawContent.ContentType}\r\n");
                        writer.Write("\r\n");
                        writer.Flush();
                        ms.Write(rawContent.Data, 0, rawContent.Data.Length);
                        writer.Write("\r\n");
                        break;

                    case FileHttpContent fileContent:
                        lock (FileLocker.GetHandle(fileContent.FileName))
                        {
                            if (data.Providers.Security.RestrictBlocksToCWD)
                                FileUtils.ThrowIfNotInCWD(fileContent.FileName);

                            var fileBytes = File.ReadAllBytes(fileContent.FileName);
                            var fileName = Path.GetFileName(fileContent.FileName);

                            writer.Write($"--{boundary}\r\n");
                            writer.Write($"Content-Disposition: form-data; name=\"{fileContent.Name}\"; filename=\"{fileName}\"\r\n");
                            writer.Write($"Content-Type: {fileContent.ContentType}\r\n");
                            writer.Write("\r\n");
                            writer.Flush();
                            ms.Write(fileBytes, 0, fileBytes.Length);
                            writer.Write("\r\n");
                        }
                        break;
                }
            }

            writer.Write($"--{boundary}--\r\n");
            writer.Flush();

            return ms.ToArray();
        }

        #region Native Engine Helpers

        private void LogNativeError(BotData data, TlsClientException ex)
        {
            data.Logger.Log($"Native TLS client error: {ex.Message}", LogColors.Tomato);
            
            // Check if DLL is available and show helpful message
            if (!NativeTlsClient.NativeTlsClient.IsAvailable)
            {
                var lastError = NativeTlsClient.NativeTlsClient.LastError ?? "Unknown error";
                data.Logger.Log($"DLL Status: {lastError}", LogColors.Tomato);
                data.Logger.Log("Download tls-client-windows-64.dll from: https://github.com/bogdanfinn/tls-client/releases", LogColors.Tomato);
            }
        }

        private void LogNativeRequestData(BotData data, TlsClientRequest request)
        {
            // Format like standard HTTP request logging
            var uri = new Uri(request.RequestUrl);
            
            data.Logger.Log($"{request.RequestMethod} {uri.PathAndQuery} HTTP/2", LogColors.Gold);
            data.Logger.Log($"Host: {uri.Host}", LogColors.Gold);
            data.Logger.Log($"TLS Profile: {request.TlsClientIdentifier}", LogColors.MediumPurple);
            data.Logger.Log("", LogColors.White);
            data.Logger.Log($"Address: {request.RequestUrl}", LogColors.Aqua);

            // Log proxy if used
            if (!string.IsNullOrEmpty(request.ProxyUrl))
            {
                var proxyLogUrl = request.ProxyUrl;
                if (proxyLogUrl.Contains("@") && proxyLogUrl.Contains(":"))
                {
                    var atIndex = proxyLogUrl.IndexOf('@');
                    var schemeEnd = proxyLogUrl.IndexOf("://") + 3;
                    if (atIndex > schemeEnd)
                    {
                        proxyLogUrl = proxyLogUrl.Substring(0, schemeEnd) + "***:***@" + proxyLogUrl.Substring(atIndex + 1);
                    }
                }
                data.Logger.Log($"Proxy: {proxyLogUrl}", LogColors.Aqua);
            }
        }

        private void ProcessNativeResponse(BotData data, TlsClientResponse response, Options.HttpRequestOptions options)
        {
            // Set response code
            data.RESPONSECODE = response.Status;

            // Log response code (like standard format)
            data.Logger.Log($"Response code: {response.Status}", LogColors.Aqua);
            data.Logger.Log("", LogColors.White);

            // Process and log headers
            data.Logger.Log("Received Headers:", LogColors.DarkOrange);
            if (response.Headers != null)
            {
                foreach (var header in response.Headers)
                {
                    var headerValue = string.Join(", ", header.Value);
                    data.HEADERS[header.Key] = headerValue;

                    data.Logger.Log($"{header.Key}: {headerValue}", LogColors.MediumPurple);

                    // Handle Set-Cookie headers
                    if (header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var cookie in header.Value)
                        {
                            var cookieParts = cookie.Split('=');
                            if (cookieParts.Length >= 2)
                            {
                                var cookieName = cookieParts[0].Trim();
                                var cookieValuePart = cookie.Substring(cookie.IndexOf('=') + 1);
                                var cookieValue = cookieValuePart.Split(';')[0].Trim();
                                data.COOKIES[cookieName] = cookieValue;
                            }
                        }
                    }
                }
            }

            // Also process cookies from response object
            if (response.Cookies != null)
            {
                foreach (var cookie in response.Cookies)
                {
                    data.COOKIES[cookie.Key] = cookie.Value;
                }
            }

            // Log cookies
            data.Logger.Log("", LogColors.White);
            data.Logger.Log("Received Cookies:", LogColors.DarkOrange);
            foreach (var cookie in data.COOKIES)
            {
                data.Logger.Log($"{cookie.Key}: {cookie.Value}", LogColors.MediumPurple);
            }

            // Process body
            if (options.ReadResponseContent && !string.IsNullOrEmpty(response.Body))
            {
                var encoding = GetEncoding(null, options.CodePagesEncoding);
                
                // The body from tls-client is already decoded (not base64)
                data.SOURCE = response.Body;
                data.RAWSOURCE = encoding.GetBytes(response.Body);

                if (options.DecodeHtml)
                {
                    data.SOURCE = WebUtility.HtmlDecode(data.SOURCE);
                }

                // Log payload (like standard format)
                data.Logger.Log("", LogColors.White);
                data.Logger.Log("Received Payload:", LogColors.DarkOrange);
                data.Logger.Log(data.SOURCE, LogColors.GreenYellow);
            }
            else
            {
                data.SOURCE = string.Empty;
                data.RAWSOURCE = Array.Empty<byte>();
            }
        }

        #endregion

        #region .NET HttpClient Fallback

        private TlsClientWrapper CreateTlsClient(BotData data)
        {
            var options = new TlsClientOptions
            {
                UseNativeEngine = false, // Force .NET path since we're in fallback
                BrowserProfile = _tlsOptions.BrowserProfile,
                Ja3Fingerprint = _tlsOptions.Ja3Fingerprint,
                ForceHttp1 = _tlsOptions.ForceHttp1,
                InsecureSkipVerify = _tlsOptions.InsecureSkipVerify,
                CustomExtensions = _tlsOptions.CustomExtensions,
                CustomCipherSuites = _tlsOptions.CustomCipherSuites,
                Http2Settings = _tlsOptions.Http2Settings,
                ClientCertificates = _tlsOptions.ClientCertificates,
                DisableSessionResumption = _tlsOptions.DisableSessionResumption,
                ConnectTimeout = data.Providers.ProxySettings.ConnectTimeout,
                ReadWriteTimeout = TimeSpan.FromMilliseconds(data.Providers.ProxySettings.ReadWriteTimeout.TotalMilliseconds),
                AutoRedirect = _tlsOptions.AutoRedirect,
                MaxNumberOfRedirects = _tlsOptions.MaxNumberOfRedirects,
                ReadResponseContent = _tlsOptions.ReadResponseContent,
                CertRevocationMode = _tlsOptions.CertRevocationMode
            };

            var proxyClient = HttpFactory.GetProxyClient(data.UseProxy ? data.Proxy : null, new HttpOptions
            {
                ConnectTimeout = options.ConnectTimeout,
                ReadWriteTimeout = options.ReadWriteTimeout
            });
            return new TlsClientWrapper(options, proxyClient);
        }

        #endregion

        private void AddHeaders(HttpRequestMessage request, Dictionary<string, string> customHeaders, BotData data)
        {
            foreach (var header in customHeaders)
            {
                var value = header.Value;

                if (commaHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                {
                    // Handle comma-separated headers
                    var values = value.Split(',').Select(v => v.Trim());
                    request.Headers.TryAddWithoutValidation(header.Key, values);
                }
                else
                {
                    request.Headers.TryAddWithoutValidation(header.Key, value);
                }
            }
        }

        private void AddCookies(HttpRequestMessage request, Dictionary<string, string> cookies, string url)
        {
            if (cookies?.Count > 0)
            {
                var cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
            }
        }

        private void LogHttpRequestData(BotData data, TlsClientWrapper client, HttpRequestMessage request)
        {
            data.Logger.Log($"[{request.Method}] {request.RequestUri}", LogColors.Gold);

            // Log JA3 fingerprint being used
            var ja3 = client.GetEffectiveJa3Fingerprint();
            if (!string.IsNullOrEmpty(ja3))
            {
                data.Logger.Log($"JA3: {ja3.TruncatePretty(80)}", LogColors.MediumPurple);
            }

            // Log headers
            foreach (var header in request.Headers)
            {
                data.Logger.Log($"{header.Key}: {string.Join(", ", header.Value)}", LogColors.DarkCyan);
            }

            if (request.Content != null)
            {
                foreach (var header in request.Content.Headers)
                {
                    data.Logger.Log($"{header.Key}: {string.Join(", ", header.Value)}", LogColors.DarkCyan);
                }
            }
        }

        private async Task LogHttpResponseData(BotData data, HttpResponseMessage response, HttpRequestMessage request, Options.HttpRequestOptions options)
        {
            // Set response info
            data.RESPONSECODE = (int)response.StatusCode;

            // Process headers
            foreach (var header in response.Headers)
            {
                var headerValue = string.Join(", ", header.Value);
                data.HEADERS[header.Key] = headerValue;

                // Handle cookies
                if (header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var cookie in header.Value)
                    {
                        var cookieName = cookie.Split('=')[0];
                        var cookieValue = cookie.Contains('=') ? cookie.Substring(cookie.IndexOf('=') + 1).Split(';')[0] : "";
                        data.COOKIES[cookieName] = cookieValue;
                    }
                }
            }

            // Read content
            if (options.ReadResponseContent && response.Content != null)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                data.RAWSOURCE = bytes;

                // Decode content
                var encoding = GetEncoding(response.Content.Headers.ContentType?.CharSet, options.CodePagesEncoding);
                data.SOURCE = encoding.GetString(bytes);

                if (options.DecodeHtml)
                {
                    data.SOURCE = WebUtility.HtmlDecode(data.SOURCE);
                }
            }
            else
            {
                data.RAWSOURCE = Array.Empty<byte>();
                data.SOURCE = string.Empty;
            }

            // Log response
            data.Logger.Log($"Response: {(int)response.StatusCode} {response.ReasonPhrase}", LogColors.Gold);
            foreach (var header in response.Headers)
            {
                data.Logger.Log($"{header.Key}: {string.Join(", ", header.Value)}", LogColors.DarkCyan);
            }
        }

        private static System.Text.Encoding GetEncoding(string charSet, string codePagesEncoding)
        {
            if (!string.IsNullOrEmpty(codePagesEncoding))
            {
                try
                {
                    return System.Text.Encoding.GetEncoding(codePagesEncoding);
                }
                catch { /* Fallback to default */ }
            }

            if (!string.IsNullOrEmpty(charSet))
            {
                try
                {
                    return System.Text.Encoding.GetEncoding(charSet);
                }
                catch { /* Fallback to default */ }
            }

            return System.Text.Encoding.UTF8;
        }
    }
}
