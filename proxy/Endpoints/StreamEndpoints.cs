using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Tvnoms.Proxy.Endpoints;

public static class StreamEndpoints {
  private const int MaxRedirects = 10;
  // Small buffer so each chunk is flushed to the browser quickly, reducing stall/buffering.
  private const int CopyBufferSize = 32 * 1024;
  // Timeout for receiving response headers only. Does NOT apply to body streaming so that
  // long-running streams are not killed mid-play.
  private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(25);

  public static void RegisterStreamEndpoints(this IEndpointRouteBuilder app) {
    app.MapGet("/stream", async (
        [FromQuery] string url,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] ILogger<Program> logger,
        HttpContext context) => {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var upstream)
            || (upstream.Scheme != Uri.UriSchemeHttp && upstream.Scheme != Uri.UriSchemeHttps)) {
          return Results.BadRequest(new {error = "Invalid or missing url parameter."});
        }

        var client = httpClientFactory.CreateClient("StreamPassthrough");
        HttpResponseMessage? upstreamResponse = null;

        try {
          upstreamResponse = await FetchWithRedirectsAsync(client, upstream, context, logger, context.RequestAborted);

          if (upstreamResponse is null) {
            logger.LogWarning("Stream passthrough: too many redirects for {Url}", upstream);
            return Results.StatusCode(StatusCodes.Status502BadGateway);
          }

          if (!upstreamResponse.IsSuccessStatusCode) {
            var status = (int)upstreamResponse.StatusCode;
            logger.LogWarning("Upstream stream returned {Status} for {Url}", status, upstream);
            upstreamResponse.Dispose();
            return Results.StatusCode(status);
          }

          try {
            await ForwardResponseAsync(upstreamResponse, context, logger, context.RequestAborted);
          } finally {
            upstreamResponse.Dispose();
          }

          return Results.Empty;
        } catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) {
          logger.LogInformation("Stream passthrough: client disconnected from {Url}", upstream);
          upstreamResponse?.Dispose();
          return Results.Empty;
        } catch (OperationCanceledException) {
          // Header timeout (manual CTS) fired and all retries were exhausted.
          logger.LogWarning("Stream passthrough: upstream timed out waiting for headers for {Url}", upstream);
          upstreamResponse?.Dispose();
          return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        } catch (HttpRequestException ex) {
          logger.LogError(ex, "Failed to fetch upstream stream {Url}", upstream);
          upstreamResponse?.Dispose();
          return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
      })
      .RequireCors("WebFrontend");
  }

  /// <summary>
  /// Sends a GET request to <paramref name="url"/>, manually following redirects up to
  /// <see cref="MaxRedirects"/> hops. Returns <c>null</c> if the redirect limit is exceeded.
  /// Retry/timeout resilience is handled by the Polly pipeline configured on the named HttpClient.
  /// </summary>
  private static async Task<HttpResponseMessage?> FetchWithRedirectsAsync(
    HttpClient client,
    Uri url,
    HttpContext context,
    ILogger logger,
    CancellationToken ct) {
    var currentUrl = url;

    for (var hop = 0; hop <= MaxRedirects; hop++) {
      using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);

      if (context.Request.Headers.TryGetValue("Range", out var clientRange))
        request.Headers.TryAddWithoutValidation("Range", (string?)clientRange);

      // Timeout only covers waiting for response headers — NOT body streaming.
      // Using a separate CTS here ensures the main `ct` (client disconnect) is unaffected.
      using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      headerCts.CancelAfter(HeaderTimeout);

      var sw = System.Diagnostics.Stopwatch.StartNew();
      var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerCts.Token);

      logger.LogInformation(
        "Stream passthrough: hop {Hop} — {Status} in {ElapsedMs}ms for {Url}",
        hop, response.StatusCode, sw.ElapsedMilliseconds, currentUrl);

      if ((int)response.StatusCode is >= 300 and < 400
          && response.Headers.Location is { } location) {
        currentUrl = location.IsAbsoluteUri ? location : new Uri(currentUrl, location);
        logger.LogInformation("Stream passthrough: hop {Hop} → redirect to {Next}", hop, currentUrl);
        response.Dispose();
        continue;
      }

      return response;
    }

    return null; // too many redirects
  }

  /// <summary>
  /// Copies relevant upstream response headers and streams the body to the client,
  /// flushing after every chunk to minimise browser buffering.
  /// </summary>
  private static async Task ForwardResponseAsync(
    HttpResponseMessage upstream,
    HttpContext context,
    ILogger logger,
    CancellationToken ct) {
    context.Response.StatusCode = (int)upstream.StatusCode;
    context.Response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "video/mp2t";

    // Always advertise range support so browsers can seek without re-requesting from byte 0.
    context.Response.Headers["Accept-Ranges"] = upstream.Headers.AcceptRanges.Any()
      ? string.Join(", ", upstream.Headers.AcceptRanges)
      : "bytes";

    if (upstream.Content.Headers.ContentLength is { } contentLength)
      context.Response.ContentLength = contentLength;

    // Forward Content-Range for 206 Partial Content responses (required for browser seeking).
    if (upstream.Content.Headers.ContentRange is { } contentRange)
      context.Response.Headers["Content-Range"] = contentRange.ToString();

    if (upstream.Headers.CacheControl is { } cacheControl)
      context.Response.Headers.CacheControl = cacheControl.ToString();

    // Disable ASP.NET Core's response buffering so bytes reach the player immediately.
    context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
    await context.Response.StartAsync(ct);

    await using var upstreamStream = await upstream.Content.ReadAsStreamAsync(ct);
    var buffer = new byte[CopyBufferSize];
    int bytesRead;

    logger.LogInformation("Stream passthrough: starting body copy");
    while ((bytesRead = await upstreamStream.ReadAsync(buffer, ct)) > 0) {
      await context.Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
      await context.Response.Body.FlushAsync(ct);
    }

    logger.LogInformation("Stream passthrough: body copy finished");
  }
}
