using FluentMigrator;
using Microsoft.Extensions.Options;
using Tvnoms.Proxy.Configuration;

namespace Tvnoms.Proxy.Data.Migrations;

/// <summary>
/// Promotes the recording duration bounds from appsettings-only values to real settings rows, so
/// they can be changed from the settings page instead of by editing a file and restarting.
/// </summary>
[Migration(20260709120000)]
public class AddDurationSettings : Migration {
  private readonly AppConfiguration _config;

  public AddDurationSettings(IOptions<AppConfiguration> config) {
    _config = config.Value;
  }

  public override void Up() {
    Insert.IntoTable("settings").Row(new {
      Id = Guid.NewGuid().ToString(),
      Key = "MinDurationMinutes",
      Value = _config.Recordings.MinDurationMinutes.ToString()
    });

    Insert.IntoTable("settings").Row(new {
      Id = Guid.NewGuid().ToString(),
      Key = "MaxDurationMinutes",
      Value = _config.Recordings.MaxDurationMinutes.ToString()
    });
  }

  /// <summary>
  /// Only the two rows this migration added. Emptying the table would take the media player and
  /// recordings path with it.
  /// </summary>
  public override void Down() {
    Delete.FromTable("settings").Row(new { Key = "MinDurationMinutes" });
    Delete.FromTable("settings").Row(new { Key = "MaxDurationMinutes" });
  }
}
