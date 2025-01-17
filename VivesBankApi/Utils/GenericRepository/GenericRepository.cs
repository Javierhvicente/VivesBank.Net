using ApiFunkosCS.Utils.GenericRepository;
using Microsoft.EntityFrameworkCore;


public class GenericRepository<C, T> : IGenericRepository<T> where T : class where C : DbContext
{
    protected readonly C _context;
    protected readonly DbSet<T> _dbSet;
    protected readonly ILogger _logger;

    public GenericRepository(C context, ILogger logger)
    {
        _context = context;
        _dbSet = _context.Set<T>();
        _logger = logger;
    }

    public async Task<List<T>> GetAllAsync()
    {
        _logger.LogInformation($"Getting all {typeof(T).Name}s");
        return await _dbSet.ToListAsync();
    }
    public async Task<PagedList<T>> GetAllPagedAsync(int pageNumber, int pageSize)
    {
        var query = _dbSet.AsQueryable();
        
        var totalCount = await query.CountAsync();

        var items = await query
            .Skip(pageNumber * pageSize) 
            .Take(pageSize)             
            .ToListAsync();

        return new PagedList<T>(items, totalCount, pageNumber, pageSize);
    }

    public async Task<T?> GetByIdAsync(String id)
    {
        _logger.LogInformation($"Getting {typeof(T).Name} with id {id}");
        return await _dbSet.FindAsync(id);
    }

    public async Task AddAsync(T entity)
    {
        _logger.LogInformation($"Adding {typeof(T).Name}");
        await _dbSet.AddAsync(entity);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(T entity)
    {
        _logger.LogInformation($"Updating {typeof(T).Name} entity: {entity}");
        _context.Entry(entity).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        
    }


    public async Task DeleteAsync(String id)
    {
        _logger.LogInformation($"Deleting {typeof(T).Name} with id {id}");
        var entity = await GetByIdAsync(id);
        if (entity == null)
        {
            _logger.LogWarning($"No {typeof(T).Name} found with id {id}");
            return;
        }
        _dbSet.Remove(entity);
        await _context.SaveChangesAsync();
    }
    
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task DeleteAllAsync()
    {
        await _semaphore.WaitAsync(); // 🔒 Bloquea la sección crítica de forma asíncrona
        try
        {
            await _dbSet.ExecuteDeleteAsync();
        }
        finally
        {
            _semaphore.Release(); // 🔓 Libera el acceso
        }
    }



}
