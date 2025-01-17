using ApiFunkosCS.Utils.GenericRepository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using VivesBankApi.Database;
using VivesBankApi.Rest.Product.Base.Models;

namespace Tests.Utils.GenericRepository;

[TestFixture]
public class GenericRepositoryTests
{
    private PostgreSqlContainer _postgreSqlContainer;
    private BancoDbContext _dbContext;
    private GenericRepository<BancoDbContext, Product> _repository;

    [OneTimeSetUp]
    public async Task Setup()
    {
        _postgreSqlContainer = new PostgreSqlBuilder()
            .WithImage("postgres:15-alpine")
            .WithDatabase("testdb")
            .WithUsername("testuser")
            .WithPassword("testpassword")
            .WithPortBinding(5432, true)
            .Build();

        await _postgreSqlContainer.StartAsync();

        var options = new DbContextOptionsBuilder<BancoDbContext>()
            .UseNpgsql(_postgreSqlContainer.GetConnectionString())
            .Options;

        _dbContext = new BancoDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _repository = new GenericRepository<BancoDbContext, Product>(_dbContext, NullLogger<GenericRepository<BancoDbContext, Product>>.Instance);
    }

    [OneTimeTearDown]
    public async Task Teardown()
    {
        if (_dbContext != null) await _dbContext.DisposeAsync();
        if (_postgreSqlContainer != null)
        {
            await _postgreSqlContainer.StopAsync();
            await _postgreSqlContainer.DisposeAsync();
        }
    }
    
    [Test]
    public async Task GetAllAsync()
    {
        var products = new List<Product>
        {
            new Product("Producto 1", Product.Type.BankAccount),
            new Product("Producto 2", Product.Type.CreditCard)
        };

        foreach (var product in products)
        {
            await _repository.AddAsync(product);
        }

        var result = await _repository.GetAllAsync();

        Assert.That(result.Count, Is.EqualTo(products.Count));
        Assert.That(result.Select(p => p.Id), Is.EquivalentTo(products.Select(p => p.Id)));
    }

    [Test]
    public async Task GetByIdAsync()
    {
        var product = new Product("Producto Test", Product.Type.BankAccount);
        await _repository.AddAsync(product);

        var result = await _repository.GetByIdAsync(product.Id);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo(product.Id));
    }

    [Test]
    public async Task GetByIdNotFound()
    {
        var nonExistingProductId = "ProductoNoExiste";

        var result = await _repository.GetByIdAsync(nonExistingProductId);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task AddAsync()
    {
        var product = new Product("Producto Nuevo", Product.Type.BankAccount);
        await _repository.AddAsync(product);

        var result = await _repository.GetByIdAsync(product.Id);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo(product.Id));
    }

    [Test]
    public async Task UpdateAsync()
    {
        var product = new Product("Producto Test", Product.Type.BankAccount);
        await _repository.AddAsync(product);

        product.Name = "Producto Actualizado";
        await _repository.UpdateAsync(product);

        var result = await _repository.GetByIdAsync(product.Id);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("Producto Actualizado"));
    }

    [Test]
    public async Task DeleteAsync()
    {
        var product = new Product("Producto Test", Product.Type.BankAccount);
        await _repository.AddAsync(product);

        await _repository.DeleteAsync(product.Id);

        var result = await _repository.GetByIdAsync(product.Id);

        Assert.That(result, Is.Null);
    }
    
    [Test]
    public async Task DeleteByIdNotFound()
    {
        var nonExistingProductId = "ProductoNoExiste";

        await _repository.DeleteAsync(nonExistingProductId);

        var result = await _repository.GetByIdAsync(nonExistingProductId);

        Assert.That(result, Is.Null);
    }
    
    [Test]
    public async Task DeleteAllAsync()
    {
        var products = new List<Product>
        {
            new Product("Producto 1", Product.Type.BankAccount),
            new Product("Producto 2", Product.Type.CreditCard)
        };

        foreach (var product in products)
        {
            await _repository.AddAsync(product);
        }

        await _repository.DeleteAllAsync();

        var result = await _repository.GetAllAsync();

        Assert.That(result.Count, Is.Zero);
    }
    
    
}