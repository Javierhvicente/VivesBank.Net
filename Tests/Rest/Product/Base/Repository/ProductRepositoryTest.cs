using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;
using VivesBankApi.Database;
using VivesBankApi.Rest.Product.Base.Models;
using VivesBankApi.Rest.Product.Base.Repository;


[TestFixture]
[TestOf(typeof(ProductRepository))]
public class ProductRepositoryTest
{
    private PostgreSqlContainer _postgreSqlContainer;
    private BancoDbContext _dbContext;
    private ProductRepository _repository;

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

        _repository = new ProductRepository(_dbContext, NullLogger<ProductRepository>.Instance);
    }
    
    [OneTimeTearDown]
    public async Task Teardown()
    {
        if (_dbContext != null)
        {
            await _dbContext.DisposeAsync();
        }
        
        if (_postgreSqlContainer != null)
        {
            await _postgreSqlContainer.StopAsync();
            await _postgreSqlContainer.DisposeAsync();
        }
    }

    [Test]
    public async Task GetByName()
    {
        var product = new Product("ProductoTest",Product.Type.BankAccount);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var result = await _repository.GetByNameAsync("ProductoTest");

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("ProductoTest"));
    }

}