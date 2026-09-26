using System.Web;

namespace Tvnoms.Proxy.Services;

public static class UrlHelpers {
  /// <summary>
  /// Safely decode a URL with validation
  /// </summary>
  public static string DecodeUrl(this string? url) {
    if (string.IsNullOrWhiteSpace(url)) {
      throw new ArgumentException("URL cannot be null or empty", nameof(url));
    }

    var decoded = HttpUtility.UrlDecode(url);
    if (!decoded.IsValidUrl()) {
      throw new ArgumentException("Decoded URL is not valid", nameof(url));
    }

    return decoded;
  }

  /// <summary>
  /// Validate URL with security considerations
  /// </summary>
  public static bool IsValidUrl(this string? url) {
    if (string.IsNullOrWhiteSpace(url)) {
      return false;
    }

    if (!Uri.TryCreate(url, UriKind.Absolute, out var uriResult)) {
      return false;
    }

    // Only allow HTTP and HTTPS
    if (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps) {
      return false;
    }

    // Block localhost, private IPs, and loopback addresses for security
    if (IsLocalOrPrivateAddress(uriResult.Host)) {
      return false;
    }

    return true;
  }

  /// <summary>
  /// Check if host is localhost, private network, or loopback
  /// </summary>
  private static bool IsLocalOrPrivateAddress(string host) {
    if (string.IsNullOrWhiteSpace(host)) {
      return true;
    }

    // Check for localhost variations
    if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("127.0.0.1") ||
        host.StartsWith("::1")) {
      return true;
    }

    // Check for private IP ranges (10.x.x.x, 172.16-31.x.x, 192.168.x.x)
    if (System.Net.IPAddress.TryParse(host, out var ipAddress)) {
      var bytes = ipAddress.GetAddressBytes();

      // IPv4 private ranges
      if (bytes.Length == 4) {
        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
      }

      // IPv6 private ranges (simplified check)
      if (bytes.Length == 16) {
        return ipAddress.IsIPv6LinkLocal || ipAddress.IsIPv6SiteLocal;
      }
    }

    return false;
  }
}
