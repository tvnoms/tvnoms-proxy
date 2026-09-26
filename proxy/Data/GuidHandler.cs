using System.Data;
using Dapper;

namespace Tvnoms.Proxy.Data;

/// <summary>
/// Custom Dapper type handler for Guid to handle SQLite string conversions
/// </summary>
public class GuidHandler : SqlMapper.TypeHandler<Guid> {
  public override void SetValue(IDbDataParameter parameter, Guid value) {
    parameter.Value = value.ToString();
  }

  public override Guid Parse(object value) {
    return value switch {
      Guid g => g,
      string s => Guid.Parse(s),
      byte[] b => new Guid(b),
      _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to Guid")
    };
  }
}

