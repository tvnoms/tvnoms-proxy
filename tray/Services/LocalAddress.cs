using System.Net;
using System.Net.Sockets;

namespace Tvnoms.Tray.Services;

/// <summary>Best guess at the address other devices on the LAN would use to reach this machine.
/// The proxy binds 0.0.0.0, which is meaningless to show a user, so the tooltip shows this instead.</summary>
public static class LocalAddress {
  public static string Resolve() {
    try {
      // Connecting a UDP socket sends nothing on the wire — it just asks the OS which local
      // interface it would route through, which is the primary LAN address on a normal machine.
      using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
      socket.Connect("8.8.8.8", 53);
      if (socket.LocalEndPoint is IPEndPoint endPoint) {
        return endPoint.Address.ToString();
      }
    } catch (SocketException) {
      // No route (offline, no network) — fall through to loopback, which is still correct locally.
    }
    return IPAddress.Loopback.ToString();
  }
}
