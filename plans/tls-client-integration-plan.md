# OpenBullet2 TLS Client Integration Plan

## Overview
This document outlines the architecture and implementation plan for integrating a TLS client library into OpenBullet2 Native to enable advanced browser fingerprint mimicking capabilities similar to Python's `tls-client` library.

## Current Architecture Analysis

### HTTP Library Structure
OpenBullet2 currently supports two HTTP library options:
1. **RuriLibHttp** - Custom HTTP client with manual TLS/cipher suite control
2. **SystemNet** - Standard .NET HttpClient

### Key Components

```
RuriLib/
├── Functions/Http/
│   ├── HttpLibrary.cs              # Enum: SystemNet, RuriLibHttp
│   ├── HttpRequestOptions.cs       # Base options class
│   ├── HttpRequestHandler.cs       # Abstract handler
│   ├── RLHttpClientRequestHandler.cs  # RuriLibHttp implementation
│   ├── HttpClientRequestHandler.cs    # SystemNet implementation
│   └── HttpFactory.cs              # Client factory
├── Models/Blocks/Custom/
│   ├── HttpRequestBlockDescriptor.cs  # Block metadata
│   └── HttpRequestBlockInstance.cs    # Block logic
└── Blocks/Requests/Http/
    └── Methods.cs                  # Block methods

OpenBullet2.Native/
└── Controls/
    ├── HttpRequestBlockSettingsViewer.xaml    # UI
    └── HttpRequestBlockSettingsViewer.xaml.cs # Code-behind
```

## Proposed Architecture

### New Component: TlsClient

```
RuriLib/
├── Functions/Http/
│   ├── HttpLibrary.cs              # ADD: TlsClient to enum
│   ├── TlsClientRequestHandler.cs  # NEW: Handler implementation
│   ├── TlsClientOptions.cs         # NEW: TLS-specific options
│   └── HttpFactory.cs              # MODIFY: Add TlsClient factory method
└── Models/Blocks/Custom/
    └── HttpRequestBlockDescriptor.cs  # MODIFY: Add TLS settings

OpenBullet2.Native/
└── Controls/
    └── HttpRequestBlockSettingsViewer.xaml  # MODIFY: Add TLS UI
```

## Implementation Plan

### Phase 1: Core TLS Client Integration

#### 1.1 Update HttpLibrary Enum
**File:** `RuriLib/Functions/Http/Options/HttpRequestOptions.cs`

```csharp
public enum HttpLibrary
{
    SystemNet,
    RuriLibHttp,
    TlsClient  // NEW
}
```

#### 1.2 Create TlsClientOptions
**File:** `RuriLib/Functions/Http/TlsClientOptions.cs` (NEW)

```csharp
public class TlsClientOptions
{
    // JA3 Fingerprint
    public string Ja3Fingerprint { get; set; } = string.Empty;
    
    // HTTP/2 Settings
    public bool ForceHttp1 { get; set; } = false;
    public Dictionary<string, int> Http2Settings { get; set; } = new();
    public List<string> Http2WindowUpdate { get; set; } = new();
    
    // TLS Extensions
    public List<string> SupportedExtensions { get; set; } = new();
    public Dictionary<string, string> ExtensionSettings { get; set; } = new();
    
    // Browser Profiles
    public TlsClientProfile BrowserProfile { get; set; } = TlsClientProfile.Chrome120;
    
    // Certificate Settings
    public bool InsecureSkipVerify { get; set; } = false;
    public List<string> ClientCertificates { get; set; } = new();
    
    // Session Settings
    public bool DisableSessionResumption { get; set; } = false;
    public string SessionId { get; set; } = string.Empty;
}

public enum TlsClientProfile
{
    Chrome120,
    Chrome117,
    Firefox120,
    Firefox117,
    Safari17,
    Safari16,
    Edge120,
    Opera104,
    Custom
}
```

#### 1.3 Create TlsClientRequestHandler
**File:** `RuriLib/Functions/Http/TlsClientRequestHandler.cs` (NEW)

```csharp
internal class TlsClientRequestHandler : HttpRequestHandler
{
    private readonly TlsClientWrapper _tlsClient;
    
    public TlsClientRequestHandler(TlsClientOptions options)
    {
        _tlsClient = new TlsClientWrapper(options);
    }
    
    public override async Task HttpRequestStandard(BotData data, StandardHttpRequestOptions options)
    {
        // Implementation using TLS client library
    }
    
    public override async Task HttpRequestRaw(BotData data, RawHttpRequestOptions options)
    {
        // Implementation using TLS client library
    }
    
    // ... other methods
}
```

#### 1.4 Update HttpFactory
**File:** `RuriLib/Functions/Http/HttpFactory.cs`

Add factory method:
```csharp
public static TlsClientWrapper GetTlsClient(Proxy proxy, TlsClientOptions options)
{
    // Initialize TLS client with proxy and options
}
```

#### 1.5 Update Methods.cs
**File:** `RuriLib/Blocks/Requests/Http/Methods.cs`

```csharp
private static HttpRequestHandler GetHandler(HttpRequestOptions options)
    => options.HttpLibrary switch
    {
        HttpLibrary.RuriLibHttp => new RLHttpClientRequestHandler(),
        HttpLibrary.SystemNet => new HttpClientRequestHandler(),
        HttpLibrary.TlsClient => new TlsClientRequestHandler(options.TlsClientOptions),  // NEW
        _ => throw new System.NotImplementedException()
    };
```

### Phase 2: Block Settings Extension

#### 2.1 Update HttpRequestBlockDescriptor
**File:** `RuriLib/Models/Blocks/Custom/HttpRequestBlockDescriptor.cs`

Add new parameters:
```csharp
Parameters = new Dictionary<string, BlockParameter>
{
    // Existing parameters...
    
    // TLS Client Settings
    { "tlsClientProfile", new EnumParameter("tlsClientProfile", typeof(TlsClientProfile), TlsClientProfile.Chrome120.ToString()) },
    { "tlsClientJa3", new StringParameter("tlsClientJa3", string.Empty) },
    { "tlsClientForceHttp1", new BoolParameter("tlsClientForceHttp1", false) },
    { "tlsClientInsecureSkipVerify", new BoolParameter("tlsClientInsecureSkipVerify", false) },
    { "tlsClientCustomExtensions", new DictionaryOfStringsParameter("tlsClientCustomExtensions", null, SettingInputMode.Interpolated) },
};
```

#### 2.2 Update HttpRequestOptions
**File:** `RuriLib/Functions/Http/Options/HttpRequestOptions.cs`

```csharp
public class HttpRequestOptions
{
    // Existing properties...
    
    // TLS Client Options
    public TlsClientProfile TlsClientProfile { get; set; } = TlsClientProfile.Chrome120;
    public string TlsClientJa3 { get; set; } = string.Empty;
    public bool TlsClientForceHttp1 { get; set; } = false;
    public bool TlsClientInsecureSkipVerify { get; set; } = false;
    public Dictionary<string, string> TlsClientCustomExtensions { get; set; } = new();
}
```

### Phase 3: UI Integration

#### 3.1 Update HttpRequestBlockSettingsViewer.xaml
**File:** `OpenBullet2.Native/Controls/HttpRequestBlockSettingsViewer.xaml`

Add TLS Client settings section:
```xml
<!-- TLS Client Settings (visible when httpLibrary == TlsClient) -->
<StackPanel Visibility="{Binding IsTlsClientSelected, Converter={StaticResource BoolToVis}}">
    <local:EnumSettingViewer x:Name="tlsClientProfileSetting" />
    <local:StringSettingViewer x:Name="tlsClientJa3Setting" />
    <local:BoolSettingViewer x:Name="tlsClientForceHttp1Setting" />
    <local:BoolSettingViewer x:Name="tlsClientInsecureSkipVerifySetting" />
    <local:DictionaryOfStringsSettingViewer x:Name="tlsClientCustomExtensionsSetting" />
</StackPanel>
```

#### 3.2 Update HttpRequestBlockSettingsViewer.xaml.cs
**File:** `OpenBullet2.Native/Controls/HttpRequestBlockSettingsViewer.xaml.cs`

Add binding for TLS settings:
```csharp
private void BindSettings()
{
    // Existing bindings...
    
    // TLS Client bindings
    tlsClientProfileSetting.Setting = vm.HttpRequestBlock.Settings["tlsClientProfile"];
    tlsClientJa3Setting.Setting = vm.HttpRequestBlock.Settings["tlsClientJa3"];
    tlsClientForceHttp1Setting.Setting = vm.HttpRequestBlock.Settings["tlsClientForceHttp1"];
    tlsClientInsecureSkipVerifySetting.Setting = vm.HttpRequestBlock.Settings["tlsClientInsecureSkipVerify"];
    tlsClientCustomExtensionsSetting.Setting = vm.HttpRequestBlock.Settings["tlsClientCustomExtensions"];
}
```

### Phase 4: TLS Client Library Wrapper

#### 4.1 Create TlsClientWrapper
**File:** `RuriLib/Functions/Http/TlsClientWrapper.cs` (NEW)

Options for implementation:

**Option A: Use existing C# TLS library**
- Use `HttpClient` with custom `SocketsHttpHandler` and `SslClientAuthenticationOptions`
- Implement JA3 fingerprinting via custom cipher suite ordering
- Limited by .NET's TLS implementation

**Option B: P/Invoke to native tls-client**
- Use the Go-based `tls-client` library via C bindings
- Full feature parity with Python's tls-client
- Requires platform-specific native libraries

**Option C: Custom TLS implementation**
- Implement TLS 1.2/1.3 from scratch
- Full control over all parameters
- Significant development effort

**Recommended: Option B with fallback to Option A**

```csharp
public class TlsClientWrapper : IDisposable
{
    private readonly TlsClientOptions _options;
    private readonly ProxyClient _proxyClient;
    private IntPtr _clientHandle;
    
    public TlsClientWrapper(TlsClientOptions options, ProxyClient proxyClient = null)
    {
        _options = options;
        _proxyClient = proxyClient ?? new NoProxyClient();
        InitializeClient();
    }
    
    private void InitializeClient()
    {
        // P/Invoke to native tls-client library
        // or initialize managed HTTP client with custom options
    }
    
    public async Task<HttpResponse> SendAsync(HttpRequest request, CancellationToken ct = default)
    {
        // Send request using TLS client
    }
    
    public void Dispose()
    {
        // Cleanup
    }
}
```

## External Dependencies

### Option 1: tls-client (Go-based)
- Repository: https://github.com/bogdanfinn/tls-client
- Requires: Go build for native libraries
- Platforms: Windows, Linux, macOS
- Architecture: x64, ARM64

### Option 2: Managed Alternative
- Use `BouncyCastle` or custom implementation
- Limited JA3 support
- Pure C#, no native dependencies

## Files to Modify

| File | Action | Description |
|------|--------|-------------|
| `RuriLib/Functions/Http/Options/HttpRequestOptions.cs` | Modify | Add TlsClient to HttpLibrary enum |
| `RuriLib/Functions/Http/HttpLibrary.cs` | Create | New file with extended enum |
| `RuriLib/Functions/Http/TlsClientOptions.cs` | Create | TLS-specific options |
| `RuriLib/Functions/Http/TlsClientRequestHandler.cs` | Create | Request handler implementation |
| `RuriLib/Functions/Http/TlsClientWrapper.cs` | Create | TLS client wrapper |
| `RuriLib/Functions/Http/HttpFactory.cs` | Modify | Add TlsClient factory method |
| `RuriLib/Blocks/Requests/Http/Methods.cs` | Modify | Add TlsClient handler case |
| `RuriLib/Models/Blocks/Custom/HttpRequestBlockDescriptor.cs` | Modify | Add TLS settings parameters |
| `RuriLib/Models/Blocks/Custom/HttpRequestBlockInstance.cs` | Modify | Add TLS settings to C# generation |
| `OpenBullet2.Native/Controls/HttpRequestBlockSettingsViewer.xaml` | Modify | Add TLS UI controls |
| `OpenBullet2.Native/Controls/HttpRequestBlockSettingsViewer.xaml.cs` | Modify | Add TLS bindings |
| `RuriLib/RuriLib.csproj` | Modify | Add TLS client NuGet package |

## Implementation Checklist

- [ ] Add TlsClient to HttpLibrary enum
- [ ] Create TlsClientOptions class
- [ ] Create TlsClientRequestHandler
- [ ] Create TlsClientWrapper (choose implementation approach)
- [ ] Update HttpFactory with TlsClient support
- [ ] Update Methods.cs handler selection
- [ ] Add TLS settings to HttpRequestBlockDescriptor
- [ ] Update HttpRequestBlockInstance C# generation
- [ ] Add TLS UI to HttpRequestBlockSettingsViewer.xaml
- [ ] Add TLS bindings in code-behind
- [ ] Add TLS client NuGet package reference
- [ ] Test with various TLS configurations
- [ ] Document new features

## Security Considerations

1. **Certificate Validation**: Allow disabling for testing, but warn users
2. **Proxy Support**: Ensure TLS client works with all proxy types
3. **Session Resumption**: Handle session tickets securely
4. **Cipher Suites**: Don't allow weak ciphers in production

## Testing Strategy

1. Unit tests for TlsClientOptions parsing
2. Integration tests with various TLS configurations
3. Test against Cloudflare-protected sites
4. Test with different proxy types
5. Performance benchmarks vs existing handlers

## Migration Path

Existing configurations will default to RuriLibHttp. Users can opt-in to TlsClient by:
1. Opening config in editor
2. Changing HttpLibrary from RuriLibHttp to TlsClient
3. Configuring TLS-specific options
4. Saving config
