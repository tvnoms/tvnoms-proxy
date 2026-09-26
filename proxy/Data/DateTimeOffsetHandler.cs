using System.Data;
using Dapper;

namespace Tvnoms.Proxy.Data;

/// <summary>
/// Custom Dapper type handler for DateTimeOffset to handle SQLite string conversions
/// </summary>
public class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset> {
  public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) {
    parameter.Value = value.ToString("o"); // ISO 8601 format
  }

  public override DateTimeOffset Parse(object value) {
    return value switch {
      DateTimeOffset dto => dto,
      DateTime dt => new DateTimeOffset(dt),
      string str => DateTimeOffset.Parse(str),
      _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to DateTimeOffset")
    };
  }
}

