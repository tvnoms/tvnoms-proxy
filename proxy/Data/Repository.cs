using System.Data;
using Dapper;
using Dapper.Contrib.Extensions;

namespace Tvnoms.Proxy.Data;

/// <summary>
/// Base repository implementation using Dapper
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
public class Repository<T> : IRepository<T> where T : class {
  protected readonly IDbConnectionFactory _connectionFactory;

  public Repository(IDbConnectionFactory connectionFactory) {
    _connectionFactory = connectionFactory;
  }

  public virtual async Task<IEnumerable<T>> GetAllAsync() {
    using var connection = await _connectionFactory.CreateConnectionAsync();
    return await connection.GetAllAsync<T>();
  }

  public virtual async Task<T?> GetByIdAsync(Guid id) {
    using var connection = await _connectionFactory.CreateConnectionAsync();
    return await connection.GetAsync<T>(id);
  }

  public virtual async Task<int> InsertAsync(T entity) {
    using var connection = await _connectionFactory.CreateConnectionAsync();
    return await connection.InsertAsync(entity);
  }

  public virtual async Task<bool> UpdateAsync(T entity) {
    using var connection = await _connectionFactory.CreateConnectionAsync();
    return await connection.UpdateAsync(entity);
  }

  public virtual async Task<bool> DeleteAsync(Guid id) {
    using var connection = await _connectionFactory.CreateConnectionAsync();
    var entity = await connection.GetAsync<T>(id);
    if (entity == null) return false;

    return await connection.DeleteAsync(entity);
  }
}

/// <summary>
/// Unit of work implementation for transaction management
/// </summary>
public class UnitOfWork : IUnitOfWork {
  private readonly IDbConnectionFactory _connectionFactory;
  private IDbConnection? _connection;
  private IDbTransaction? _transaction;
  private bool _disposed;

  public UnitOfWork(IDbConnectionFactory connectionFactory) {
    _connectionFactory = connectionFactory;
  }

  public IDbConnection Connection => _connection ??= _connectionFactory.CreateConnectionAsync().Result;
  public IDbTransaction? Transaction => _transaction;

  public void BeginTransaction() {
    if (_transaction != null) {
      throw new InvalidOperationException("Transaction already started");
    }
    _transaction = Connection.BeginTransaction();
  }

  public Task CommitAsync() {
    if (_transaction == null) {
      throw new InvalidOperationException("No transaction to commit");
    }

    _transaction.Commit();
    _transaction.Dispose();
    _transaction = null;
    return Task.CompletedTask;
  }

  public Task RollbackAsync() {
    if (_transaction == null) {
      throw new InvalidOperationException("No transaction to rollback");
    }

    _transaction.Rollback();
    _transaction.Dispose();
    _transaction = null;
    return Task.CompletedTask;
  }

  public void Dispose() {
    if (!_disposed) {
      _transaction?.Dispose();
      _connection?.Dispose();
      _disposed = true;
    }
  }
}
