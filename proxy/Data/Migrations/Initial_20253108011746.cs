using FluentMigrator;
using Tvnoms.Proxy.Configuration;

namespace Tvnoms.Proxy.Data.Migrations;

[Migration(20253108011746)]
public class InitialTables : Migration {
  public override void Up() {
    Create.Table("settings")
      .WithColumn("Id").AsGuid().PrimaryKey()
      .WithColumn("Key").AsString().NotNullable().Unique()
      .WithColumn("Value").AsString().NotNullable();

    Create.Table("recordings")
      .WithColumn("Id").AsGuid().PrimaryKey()
      .WithColumn("JobId").AsString().NotNullable()
      .WithColumn("Url").AsString().NotNullable()
      .WithColumn("Title").AsString().NotNullable()
      .WithColumn("StartTime").AsDateTime().NotNullable()
      .WithColumn("EndTime").AsDateTime().NotNullable()
      .WithColumn("IsRecorded").AsBoolean().NotNullable().WithDefaultValue(false)
      .WithColumn("FilePath").AsString().Nullable()
      .WithColumn("Status").AsString().NotNullable().WithDefaultValue("pending");
  }

  public override void Down() {
    Delete.Table("Settings");
    Delete.Table("Recordings");
  }
}
