using Dapper;
using Tvnoms.Proxy.Data.Models;

namespace Tvnoms.Proxy.Data.Repositories;

public interface IRecordingRepository : IRepository<Recording> {
  Task<Recording?> GetByJobIdAsync(string jobId);
  Task<IEnumerable<Recording>> GetScheduledRecordingsAsync();
  Task<IEnumerable<Recording>> GetCompletedRecordingsAsync();
  Task<IEnumerable<Recording>> GetByStatusAsync(string status);
}

public class RecordingRepository : Repository<Recording>, IRecordingRepository {
  public RecordingRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) {
  }

  public override async Task<IEnumerable<Recording>> GetAllAsync() {
    using var connection = await _connectionFactory.CreateConnectionAsync();
    const string sql = "SELECT * FROM recordings ORDER BY StartTime DESC";
    return await connection.QueryAsync<Recording>(sql);
  }

  public override async Task<Recording?> GetByIdAsync(Guid id) {
    using var connection = await _connectionFactory.CreateConnectionAsync();
    const string sql = "SELECT * FROM recordings WHERE Id = @Id";
    return await connection.QueryFirstOrDefaultAsync<Recording>(sql, new { Id = id });
  }

  public override async Task<bool> DeleteAsync(Guid id) {
    using var connection = await _connectionFactory.CreateConnectionAsync();
    const string sql = "DELETE FROM recordings WHERE Id = @Id";
    var result = await connection.ExecuteAsync(sql, new { Id = id });
    return result > 0;
  }

  public async Task<Recording?> GetByJobIdAsync(string jobId) {
    using var connection = await _connectionFactory.CreateConnectionAsync();
    const string sql = "SELECT * FROM recordings WHERE JobId = @JobId";
    return await connection.QueryFirstOrDefaultAsync<Recording>(sql, new { JobId = jobId });
  }

  public async Task<IEnumerable<Recording>> GetScheduledRecordingsAsync() {
    using var connection = await _connectionFactory.CreateConnectionAsync();
    const string sql = "SELECT * FROM recordings WHERE IsRecorded = 0 ORDER BY StartTime DESC";
    return await connection.QueryAsync<Recording>(sql);
  }

  public async Task<IEnumerable<Recording>> GetCompletedRecordingsAsync() {
    using var connection = await _connectionFactory.CreateConnectionAsync();
    const string sql = "SELECT * FROM recordings WHERE IsRecorded = 1 ORDER BY StartTime DESC";
    return await connection.QueryAsync<Recording>(sql);
  }

  public async Task<IEnumerable<Recording>> GetByStatusAsync(string status) {
    using var connection = await _connectionFactory.CreateConnectionAsync();
    const string sql = "SELECT * FROM recordings WHERE Status = @Status ORDER BY StartTime DESC";
    return await connection.QueryAsync<Recording>(sql, new { Status = status });
  }
}
