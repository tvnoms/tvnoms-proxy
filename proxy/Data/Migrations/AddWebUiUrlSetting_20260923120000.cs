using FluentMigrator;
using Microsoft.Extensions.Options;
using Tvnoms.Proxy.Configuration;

namespace Tvnoms.Proxy.Data.Migrations;

/// <summary>
/// Backfills the "open web UI" target for existing installs. Previously this was derived from
/// Cors:AllowedOrigins[0] (an unrelated setting that always resolved to the dev URL); now it's a
/// real setting seeded from the environment-aware default computed in Program.cs.
/// </summary>
[Migration(20260923120000)]
public class AddWebUiUrlSetting : Migration {
  private readonly AppConfiguration _config;

  public AddWebUiUrlSetting(IOptions<AppConfiguration> config) {
    _config = config.Value;
  }

  public override void Up() {
    Insert.IntoTable("settings").Row(new {
      Id = Guid.NewGuid().ToString(),
      Key = "WebUiUrl",
      Value = _config.WebUiUrl ?? AppConfiguration.DefaultProductionWebUiUrl
    });
  }

  /// <summary>Only the row this migration added, matching AddDurationSettings' Down().</summary>
  public override void Down() {
    Delete.FromTable("settings").Row(new { Key = "WebUiUrl" });
  }
}
