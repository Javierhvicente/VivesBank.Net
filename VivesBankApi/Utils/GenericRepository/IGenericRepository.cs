namespace ApiFunkosCS.Utils.GenericRepository;

public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(String id);
    Task<List<T>> GetAllAsync();
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(String id);
    
    Task DeleteAllAsync();
}
