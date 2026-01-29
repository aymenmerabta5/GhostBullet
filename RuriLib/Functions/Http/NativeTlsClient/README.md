# Native TLS Client Integration

This directory contains the native tls-client integration for proper JA3 fingerprinting and anti-bot bypass.

## Requirements

To use the native TLS client engine, you need to download the `tls-client` library:

1. Go to [bogdanfinn/tls-client releases](https://github.com/bogdanfinn/tls-client/releases)
2. Download the latest release (look for `tls-client-windows-64-x.x.x.zip`)
3. Extract `tls-client-windows-64.dll` to **one of these locations**:
   - Same folder as OpenBullet2.exe (recommended)
   - `lib/` subfolder
   - `native/` subfolder

**Important**: Make sure you download the **64-bit Windows** version of the DLL.

## How It Works

When `HttpLibrary = TlsClient` is selected and `Use Native Engine` is enabled:

1. Requests are routed through the native Go-based tls-client library
2. JA3 fingerprints are properly applied at the TLS handshake level
3. HTTP/2 SETTINGS frames match real browser profiles
4. Client Hints headers (Sec-CH-UA-*) are automatically included

## Verification

To verify the fix works:

1. Create a new config in OpenBullet2
2. Add an HTTP Request block with:
   - URL: `https://www.udemy.com/`
   - Method: GET
   - Http Library: **TlsClient**
   - Use Native Engine: **true** (default)
   - Browser Profile: Chrome120 (default)
   - Include Client Hints: **true** (default)

3. Run the request
4. Check response:
   - **Before fix**: 403 Forbidden with `cf-mitigated: challenge`
   - **After fix**: 200 OK (or redirect to actual page)

## Troubleshooting

If you still get 403:

1. Check that `tls-client-windows-64.dll` is in the app directory
2. Enable logging to see which headers are being sent
3. Try different browser profiles (Chrome120, Firefox120, etc.)
4. Some sites may require additional techniques (cookies, JS challenge solving)

## Fallback Behavior

If the native DLL is not found or fails to load:
- Requests fall back to .NET HttpClient
- JA3 fingerprinting will NOT work
- Client Hints are still added as HTTP headers
- Anti-bot bypass will be limited
