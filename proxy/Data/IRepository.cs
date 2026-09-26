using System.Data;

namespace Tvnoms.Proxy.Data;

/// <summary>
/// Base repository interface for common database operations
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
public interface IRepository<T> where T : class {
  Task<IEnumerable<T>> GetAllAsync();
  Task<T?> GetByIdAsync(Guid id);
  Task<int> InsertAsync(T entity);
  Task<bool> UpdateAsync(T entity);
  Task<bool> DeleteAsync(Guid id);
}

/// <summary>
/// Unit of work pattern for managing database transactions
/// </summary>
public interface IUnitOfWork : IDisposable {
  IDbConnection Connection { get; }
  IDbTransaction? Transaction { get; }

  void BeginTransaction();
  Task CommitAsync();
  Task RollbackAsync();
}
