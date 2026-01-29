using RuriLib.Functions.Http;
using RuriLib.Functions.Http.Options;
using RuriLib.Models.Blocks.Parameters;
using RuriLib.Models.Blocks.Settings;
using System.Collections.Generic;

namespace RuriLib.Models.Blocks.Custom
{
    public class HttpRequestBlockDescriptor : BlockDescriptor
    {
        public HttpRequestBlockDescriptor()
        {
            Id = "HttpRequest";
            Name = "Http Request";
            Description = "Performs an Http request and reads the response";
            Category = new BlockCategory
            {
                Name = "Http",
                BackgroundColor = "#32cd32",
                ForegroundColor = "#000",
                Path = "RuriLib.Blocks.Requests.Http",
                Namespace = "RuriLib.Blocks.Requests.Http.Methods",
                Description = "Blocks for performing Http requests"
            };
            Parameters = new Dictionary<string, BlockParameter>
            {
                { "url", new StringParameter("url", "https://google.com") },
                { "method", new EnumParameter("method", typeof(HttpMethod), HttpMethod.GET.ToString()) },
                { "autoRedirect", new BoolParameter("autoRedirect", true) },
                { "maxNumberOfRedirects", new IntParameter("maxNumberOfRedirects", 8) },
                { "readResponseContent", new BoolParameter("readResponseContent", true) },
                { "urlEncodeContent", new BoolParameter("urlEncodeContent", false) },
                { "absoluteUriInFirstLine", new BoolParameter("absoluteUriInFirstLine", false) },
                { "httpLibrary", new EnumParameter("httpLibrary", typeof(HttpLibrary), HttpLibrary.TlsClient.ToString()) },
                { "securityProtocol", new EnumParameter("securityProtocol", typeof(SecurityProtocol), SecurityProtocol.SystemDefault.ToString()) },
                { "useCustomCipherSuites", new BoolParameter("useCustomCipherSuites", false) },
                { "alwaysSendContent", new BoolParameter("alwaysSendContent", false) },
                { "decodeHtml", new BoolParameter("decodeHtml", false) },
                { "codePagesEncoding", new StringParameter("codePagesEncoding", string.Empty) },
                { "customCipherSuites", new ListOfStringsParameter("customCipherSuites",
                    new List<string>
                    {
                        "TLS_AES_128_GCM_SHA256",
                        "TLS_CHACHA20_POLY1305_SHA256",
                        "TLS_AES_256_GCM_SHA384",
                        "TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256",
                        "TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256",
                        "TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256",
                        "TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256",
                        "TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384",
                        "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384",
                        "TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA",
                        "TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA",
                        "TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA",
                        "TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA",
                        "TLS_RSA_WITH_AES_128_GCM_SHA256",
                        "TLS_RSA_WITH_AES_256_GCM_SHA384",
                        "TLS_RSA_WITH_AES_128_CBC_SHA",
                        "TLS_RSA_WITH_AES_256_CBC_SHA",
                        "TLS_RSA_WITH_3DES_EDE_CBC_SHA"
                    },
                    SettingInputMode.Fixed) },
                { "customCookies", new DictionaryOfStringsParameter("customCookies", null, SettingInputMode.Interpolated) },
                { "customHeaders", new DictionaryOfStringsParameter("customHeaders",
                    new Dictionary<string, string>
                    {
                        { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36" },
                        { "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7" },
                        { "Accept-Language", "en-US,en;q=0.9" },
                        { "Accept-Encoding", "gzip, deflate, br, zstd" },
                        { "Upgrade-Insecure-Requests", "1" },
                        { "Sec-Fetch-Dest", "document" },
                        { "Sec-Fetch-Mode", "navigate" },
                        { "Sec-Fetch-Site", "none" },
                        { "Sec-Fetch-User", "?1" },
                        { "Cache-Control", "max-age=0" },
                        { "Sec-CH-UA", "\"Not A(Brand\";v=\"8\", \"Chromium\";v=\"133\", \"Google Chrome\";v=\"133\"" },
                        { "Sec-CH-UA-Mobile", "?0" },
                        { "Sec-CH-UA-Platform", "\"Windows\"" },
                        { "Priority", "u=0, i" }
                    },
                    SettingInputMode.Interpolated) },
                { "timeoutMilliseconds", new IntParameter("timeoutMilliseconds", 15000) },
                { "httpVersion", new StringParameter("httpVersion", "1.1") },
                // TLS Client Settings
                { "tlsClientUseNativeEngine", new BoolParameter("tlsClientUseNativeEngine", true) },
                { "tlsClientProfile", new EnumParameter("tlsClientProfile", typeof(TlsClientProfile), TlsClientProfile.Chrome133.ToString()) },
                { "tlsClientJa3", new StringParameter("tlsClientJa3", string.Empty) },
                { "tlsClientForceHttp1", new BoolParameter("tlsClientForceHttp1", false) },
                { "tlsClientInsecureSkipVerify", new BoolParameter("tlsClientInsecureSkipVerify", false) },
                { "tlsClientIncludeClientHints", new BoolParameter("tlsClientIncludeClientHints", true) },
                { "tlsClientCustomExtensions", new DictionaryOfStringsParameter("tlsClientCustomExtensions", null, SettingInputMode.Interpolated) }
            };
        }
    }
}
