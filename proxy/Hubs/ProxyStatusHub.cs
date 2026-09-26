using Microsoft.AspNetCore.SignalR;

namespace Tvnoms.Proxy.Hubs;

public class ProxyStatusHub : Hub {
  public async Task SendMessage(string user, string message) {
    await Clients.All.SendAsync("ServerMessage", user, message);
  }
}
