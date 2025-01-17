using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Testcontainers.PostgreSql;
using VivesBankApi.Database;
using VivesBankApi.Rest.Product.Base.Controller;
using VivesBankApi.Rest.Product.Base.Dto;
using VivesBankApi.Rest.Product.Base.Models;
using VivesBankApi.Rest.Product.Base.Repository;
using VivesBankApi.Rest.Product.Service;


[TestFixture]
[TestOf(typeof(ProductController))]
public class ProductControllerTests
{
    private PostgreSqlContainer _postgreSqlContainer;
    private BancoDbContext _dbContext;
    private ProductRepository _repository;
    private ProductService _service;
    private ProductController _controller;

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
        _service = new ProductService(NullLogger<ProductService>.Instance, _repository);
        _controller = new ProductController(_service, NullLogger<ProductController>.Instance);
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
    public async Task GetAllProducts()
    {
        var product = new Product("Product 1", Product.Type.BankAccount);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.GetAllProductsAsync();

        Assert.That(result, Is.InstanceOf<ActionResult<List<ProductResponse>>>());

        var okResult = result.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult.Value, Is.InstanceOf<List<ProductResponse>>());
        Assert.That(okResult.Value, Is.Not.Null);
    }
    
    [Test]
    public async Task GetProductById()
    {
        var product = new Product("Product 3", Product.Type.BankAccount);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var result = await _controller.GetProductByIdAsync(product.Id);

        Assert.That(result, Is.InstanceOf<ActionResult<ProductResponse>>());

        var okResult = result.Result as OkObjectResult;

        Assert.That(okResult, Is.Not.Null);
        Assert.That(okResult.Value, Is.InstanceOf<ProductResponse>());
    }

    [Test]
    public async Task GetProductByIdNotFound()
    {
        var noExistentId = "noExistentId";   
        var result = await _controller.GetProductByIdAsync(noExistentId);

        var notFoundResult = result.Result as NotFoundResult;

        Assert.That(notFoundResult, Is.Not.Null);
    }

    
}
