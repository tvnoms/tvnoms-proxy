using Dapper.Contrib.Extensions;

namespace Tvnoms.Proxy.Data.Models;

[Table("settings")]
public record Setting {
  [Key]
  public Guid Id { get; set; }

  public required string Key { get; set; }
  public required string Value { get; set; }
}
