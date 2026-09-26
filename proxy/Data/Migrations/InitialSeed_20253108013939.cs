using FluentMigrator;
using Microsoft.Extensions.Options;
using Tvnoms.Proxy.Configuration;

namespace Tvnoms.Proxy.Data.Migrations;

[Migration(20253108013939)]
public class InitialSeed : Migration{
  private readonly AppConfiguration  _config;

  public InitialSeed(IOptions<AppConfiguration> config) {
    _config = config.Value;
  }
  public override void Up() {
    Insert.IntoTable("settings").Row(new {
      Id = Guid.NewGuid().ToString(),
      Key = "MediaPlayerPath",
      Value = _config.VideoPlayer.MediaPlayerPath
    });
    
    Insert.IntoTable("settings").Row(new {
      Id = Guid.NewGuid().ToString(),
      Key = "MediaPlayerArguments",
      Value = _config.VideoPlayer.MediaPlayerArguments
    });
    
    Insert.IntoTable("settings").Row(new {
      Id = Guid.NewGuid().ToString(),
      Key = "RecordingsPath",
      Value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Recordings")
    });
        
    Insert.IntoTable("settings").Row(new {
      Id = Guid.NewGuid().ToString(),
      Key = "Port",
      Value = _config.Networking.Port
    });
    
  }

  public override void Down() {
    Delete.FromTable("settings").AllRows();
  }
}
