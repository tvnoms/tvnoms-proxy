using System.Web;
using Microsoft.AspNetCore.Mvc;
using Tvnoms.Proxy.Services;

namespace Tvnoms.Proxy.Endpoints;

public static class PlayerEndpoints {
  public static void RegisterPlayerEndpoints(this IEndpointRouteBuilder app) {
    var endpoints = app.MapGroup("/play");
    endpoints.MapPost(
        "{url}", async ([FromServices] IVideoPlayerService player, string url) => {
          var result = await player.PlayFromUrl(HttpUtility.UrlDecode(url));
          if (result.Success) return Results.Ok();
          return result.IsClientError
            ? Results.BadRequest(new { error = result.ErrorMessage })
            : Results.InternalServerError(new { error = result.ErrorMessage });
        })
      .RequireCors("WebFrontend");
  }
}
