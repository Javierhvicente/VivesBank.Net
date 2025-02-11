using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework.Legacy;
using StackExchange.Redis;
using VivesBankApi.Rest.Clients.Exceptions;
using VivesBankApi.Rest.Clients.Models;
using VivesBankApi.Rest.Clients.Repositories;
using VivesBankApi.Rest.Product.BankAccounts.AccountTypeExtensions;
using VivesBankApi.Rest.Product.BankAccounts.Dto;
using VivesBankApi.Rest.Product.BankAccounts.Models;
using VivesBankApi.Rest.Product.BankAccounts.Repositories;
using VivesBankApi.Rest.Product.BankAccounts.Services;
using VivesBankApi.Rest.Products.BankAccounts.Exceptions;
using VivesBankApi.Rest.Users.Dtos;
using VivesBankApi.Rest.Users.Exceptions;
using VivesBankApi.Rest.Users.Service;
using VivesBankApi.Utils.IbanGenerator;
using VivesBankApi.WebSocket.Service;
using Microsoft.Extensions.Caching.Memory;
using VivesBankApi.Utils.IbanGenerator.Exceptions;
using Role = VivesBankApi.Rest.Users.Models.Role;

namespace Tests.Rest.Product.BankAccounts.Service;

using VivesBankApi.Rest.Product.Base.Models;

[TestFixture]
[TestOf(typeof(AccountService))]
public class AccountServiceTest
{
    private Mock<IAccountsRepository> _accountRepository;
    private Mock<IClientRepository> _clientRepository;
    private Mock<IProductRepository> _productRepository;
    private Mock<IIbanGenerator> _ibanGenerator;
    private Mock<ILogger<AccountService>> _logger;
    private Mock<IDatabase> _cache;
    private Mock<IConnectionMultiplexer> _connectionMultiplexer;
    private Mock<IHttpContextAccessor> _httpContextAccessor;
    private Mock<IUserService> _userService;
    private Mock<IWebsocketHandler> _websocketHandler;
    private AccountService _accountService;

    [SetUp]
    public void Setup()
    {
        _connectionMultiplexer = new Mock<IConnectionMultiplexer>();
        _cache = new Mock<IDatabase>();
        _connectionMultiplexer
            .Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<string>()))
            .Returns(_cache.Object);
        _accountRepository = new Mock<IAccountsRepository>();
        _clientRepository = new Mock<IClientRepository>();
        _productRepository = new Mock<IProductRepository>();
        _ibanGenerator = new Mock<IIbanGenerator>();
        _websocketHandler = new Mock<IWebsocketHandler>();
        _logger = new Mock<ILogger<AccountService>>();
        _httpContextAccessor = new Mock<IHttpContextAccessor>();
        _userService = new Mock<IUserService>();

        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "TestUserId")
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims));
        _httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        _accountService = new AccountService(
            _logger.Object,
            _ibanGenerator.Object,
            _clientRepository.Object,
            _productRepository.Object,
            _accountRepository.Object,
            _connectionMultiplexer.Object,
            _httpContextAccessor.Object,
            _userService.Object,
            _websocketHandler.Object);
    }

    
    private readonly Account account = new Account
    {
        Id = "TkjPO5u_2w",
        ClientId = "Q5hsVJ2-oQ",
        ProductId = "xFtC3Mv_oA",
        AccountType = AccountType.STANDARD,
        IBAN = "ES9121000418450200051332",
        Balance = 1000
    };

    private readonly AccountResponse _response = new AccountResponse
    {
        AccountType = AccountType.STANDARD,
        IBAN = "ES9121000418450200051332",
    };
    [Test]
    public async Task GetAccountsAsync_ReturnsPagedAccounts()
    {
        var accounts = new List<Account>
        {
            new Account { Id = "1", ClientId = "C1", ProductId = "P1", AccountType = AccountType.STANDARD, IBAN = "IBAN1", Balance = 1000 },
            new Account { Id = "2", ClientId = "C2", ProductId = "P2", AccountType = AccountType.STANDARD, IBAN = "IBAN2", Balance = 2000 },
            new Account { Id = "3", ClientId = "C3", ProductId = "P3", AccountType = AccountType.STANDARD, IBAN = "IBAN3", Balance = 3000 }
        };
        var pagedList = PagedList<Account>.Create(accounts, pageNumber: 0, pageSize: 2);

        _accountRepository
            .Setup(repo => repo.GetAllPagedAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(pagedList);

        var result = await _accountService.GetAccountsAsync(pageNumber: 0, pageSize: 2, sortBy: "id", direction: "asc");
        
        ClassicAssert.IsNotNull(result);
        ClassicAssert.AreEqual(2, result.Content.Count);
        ClassicAssert.AreEqual("1", result.Content[0].Id);
        ClassicAssert.AreEqual("2", result.Content[1].Id);
        ClassicAssert.AreEqual(2, result.PageSize);
        ClassicAssert.AreEqual(0, result.PageNumber);
        ClassicAssert.AreEqual(2, result.TotalPageElements);
        ClassicAssert.AreEqual(2, result.TotalPages);
        ClassicAssert.IsFalse(result.Empty);
        ClassicAssert.IsTrue(result.First);
        ClassicAssert.IsFalse(result.Last);
        
        _accountRepository.Verify(repo => repo.GetAllPagedAsync(0, 2), Times.Once);
    }

    [Test]
    public async Task GetAccountsAsync_ReutrnsEmpty()
    {
        _accountRepository
            .Setup(repo => repo.GetAllPagedAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(PagedList<Account>.Create(new List<Account>(), pageNumber: 0, pageSize: 2));

        var result = await _accountService.GetAccountsAsync(pageNumber: 0, pageSize: 2, sortBy: "id", direction: "asc");
        
        ClassicAssert.IsTrue(result.Empty);
    }


    [Test]
    public async Task GetAccountByIdAsync_ShouldReturnAccount()
    {
        _accountRepository.Setup(r => r.GetByIdAsync(It.Is<string>(id => id == account.Id)))
            .ReturnsAsync(account);
        var result = await _accountService.GetAccountByIdAsync(account.Id);
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IBAN, Is.EqualTo(_response.IBAN));
        
        _accountRepository.Verify(repo => repo.GetByIdAsync(account.Id),Times.Once);
    }

    [Test]
    public async Task GetAccountByIdAsync_ShouldReturnAccount_WhenFoundInCache()
    {
        _cache.Setup(r => r.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>() ))
            .ReturnsAsync((RedisValue)JsonSerializer.Serialize(account));
        var result = await _accountService.GetAccountByIdAsync(account.Id);
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IBAN, Is.EqualTo(_response.IBAN));
        
        _accountRepository.Verify(repo => repo.GetByIdAsync(account.Id),Times.Never);
        _cache.Verify(r => r.StringGetAsync(account.Id, CommandFlags.None), Times.Once);
    }

    [Test]
    public async Task GetAccountByIdAsync_ShouldReturnNotFound()
    {
        var id = "notFound";
        _accountRepository.Setup(r => r.GetByIdAsync(It.Is<string>(id => id == account.Id)))
            .ReturnsAsync((Account)null);
        var result =Assert.ThrowsAsync<AccountsExceptions.AccountNotFoundException>(async () =>
            await _accountService.GetAccountByIdAsync(id));
        Assert.That(result.Message, Is.EqualTo($"Account not found by id {id}"));
        
        _accountRepository.Verify(repo => repo.GetByIdAsync(id), Times.Once);
    }
    
    [Test]
    public async Task GetAccountByIdAsync_ShouldReturnNotFound_WhenNotFoundOnDBorCache()
    {
        var id = "notFound";
        _cache.Setup(r => r.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>() ))
            .ReturnsAsync(String.Empty);
        _accountRepository.Setup(r => r.GetByIdAsync(It.Is<string>(id => id == account.Id)))
            .ReturnsAsync((Account)null);
        var result =Assert.ThrowsAsync<AccountsExceptions.AccountNotFoundException>(async () =>
            await _accountService.GetAccountByIdAsync(id));
        Assert.That(result.Message, Is.EqualTo($"Account not found by id {id}"));
        
        _cache.Verify(r => r.StringGetAsync(id, CommandFlags.None), Times.Once);
        _accountRepository.Verify(repo => repo.GetByIdAsync(id), Times.Once);
    }

    [Test]
    public async Task getAccountByClientIdAsync_ShouldReturnAccount()
    {
        _accountRepository.Setup(r => r.getAccountByClientIdAsync(It.IsAny<String>()))
           .ReturnsAsync(new List<Account> { account });
        var result = await _accountService.GetAccountByClientIdAsync(account.ClientId);
        Assert.That(result, Is.Not.Null);
        Assert.That(result.First().IBAN, Is.EqualTo(_response.IBAN));
        
        _accountRepository.Verify(repo => repo.getAccountByClientIdAsync(account.ClientId), Times.Once);
    }
    
    [Test]
    public void GetAccountByClientIdAsync_ShouldThrowAccountNotFoundException_WhenNoAccountsFound()
    {
        var id = "NotFound";
        _accountRepository.Setup(r => r.getAccountByClientIdAsync(It.IsAny<string>()))
            .ReturnsAsync((List<Account>)null);
        
        var exception = Assert.ThrowsAsync<AccountsExceptions.AccountNotFoundException>(async () =>
            await _accountService.GetAccountByClientIdAsync(id));

        Assert.That(exception.Message, Is.EqualTo($"Account not found by id {id}"));
        _accountRepository.Verify(repo => repo.getAccountByClientIdAsync(id), Times.Once);
    }

    [Test]
    public async Task GetAccountByIbanAsync_ShouldReturnAccount()
    {
        _accountRepository.Setup(r => r.getAccountByIbanAsync(It.IsAny<string>()))
           .ReturnsAsync(account);
        var result = await _accountService.GetAccountByIbanAsync(account.IBAN);
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IBAN, Is.EqualTo(_response.IBAN));
        
        _accountRepository.Verify(repo => repo.getAccountByIbanAsync(account.IBAN), Times.Once);
    }
    
    [Test]
    public async Task GetAccountByIbanAsync_ShouldReturnAccount_WhenFoundInCache()
    {
        _cache.Setup(r => r.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>() ))
            .ReturnsAsync((RedisValue)JsonSerializer.Serialize(account));
        var result = await _accountService.GetAccountByIbanAsync(account.IBAN);
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IBAN, Is.EqualTo(_response.IBAN));
        
        _accountRepository.Verify(repo => repo.GetByIdAsync(account.IBAN), Times.Never);
    }

    [Test]
    public async Task GetMyAccountsAsClientAsync_ShouldReturnAccounts_WhenClientExists()
    {
        var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        var mockUserService = new Mock<IUserService>();
        var mockClientRepository = new Mock<IClientRepository>();
        var mockAccountsRepository = new Mock<IAccountsRepository>();
        var mockLogger = new Mock<ILogger<AccountService>>();
        var mockIbanGenerator = new Mock<IIbanGenerator>(); 
        var mockProductRepository = new Mock<IProductRepository>();
        var mockConnectionMultiplexer = new Mock<IConnectionMultiplexer>(); 
        var mockWebsocketHandler = new Mock<IWebsocketHandler>();


        var userId = "test-user-id";
        var mockUser = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        }));

        mockHttpContextAccessor.Setup(x => x.HttpContext.User).Returns(mockUser);

        mockUserService.Setup(service => service.GetUserByIdAsync(userId))
            .ReturnsAsync(new UserResponse { Id = userId });

        var mockClient = new Client { Id = "client-id" };
        mockClientRepository.Setup(repo => repo.getByUserIdAsync(userId))
            .ReturnsAsync(mockClient);

        mockAccountsRepository.Setup(repo => repo.getAccountByClientIdAsync(mockClient.Id))
            .ReturnsAsync(new List<Account>
            {
                new Account { Id = "1", ClientId = "client-id", IBAN = "IBAN123" }
            });

        var accountService = new AccountService(
            mockLogger.Object,
            mockIbanGenerator.Object,
            mockClientRepository.Object,
            mockProductRepository.Object,
            mockAccountsRepository.Object,
            mockConnectionMultiplexer.Object,
            mockHttpContextAccessor.Object,
            mockUserService.Object,
            mockWebsocketHandler.Object
        );

        var result = await accountService.GetMyAccountsAsClientAsync();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Id, Is.EqualTo("1"));
        Assert.That(result[0].IBAN, Is.EqualTo("IBAN123"));
    }
    
    [Test]
    public async Task UpdateAccount_UpdatesTheAccountCorrectly()
    {
        var testAccount = new Account
        {
            Id = "1",
            ClientId = "C1",
            ProductId = "P1",
            AccountType = AccountType.STANDARD,
            IBAN = "ES4704879765177458621788",
            Balance = 1000
        };

        var testClient = new Client(); // ✅ Cliente válido
        var testProduct = new Product("Premium", Product.Type.BankAccount); // ✅ Producto válido
        var updateRequest = new UpdateAccountRequest
        {
            ProductID = "P1",
            ClientID = "C1",
            IBAN = "ES4704879765177458621788",
            Balance = 2000,
            AccountType = AccountType.SAVING
        };

        _accountRepository.Setup(r => r.GetByIdAsync("1")).ReturnsAsync(testAccount);
        _productRepository.Setup(r => r.GetByIdAsync("P1")).ReturnsAsync(testProduct); // 🔹 Corregido
        _clientRepository.Setup(r => r.GetByIdAsync("C1")).ReturnsAsync(testClient);
        _accountRepository.Setup(r => r.UpdateAsync(It.IsAny<Account>())).Returns(Task.CompletedTask);

        // Act
        var result = await _accountService.UpdateAccountAsync("1", updateRequest);

        // Assert
        ClassicAssert.IsNotNull(result);
    }

    [Test]
    public void UpdateAccount_ThrowsException_WhenAccountNotFound()
    {
        // Arrange
        _accountRepository.Setup(r => r.GetByIdAsync("1")).ReturnsAsync((Account)null);
        var updateRequest = new UpdateAccountRequest { ProductID = "P1", ClientID = "C1", IBAN = "ES4704879765177458621788", Balance = 2000, AccountType = AccountType.SAVING };

        // Act & Assert
        ClassicAssert.ThrowsAsync<AccountsExceptions.AccountNotFoundException>(async () => await _accountService.UpdateAccountAsync("1", updateRequest));
    }

    [Test]
    public void UpdateAccount_ThrowsException_WhenProductNotFound()
    {
        // Arrange
        var testAccount = new Account { Id = "1", ProductId = "P1", ClientId = "C1" };
        _accountRepository.Setup(r => r.GetByIdAsync("1")).ReturnsAsync(testAccount);
        _productRepository.Setup(r => r.GetByNameAsync("P1")).ReturnsAsync((Product)null);
        var updateRequest = new UpdateAccountRequest { ProductID = "P1", ClientID = "C1", IBAN = "ES4704879765177458621788", Balance = 2000, AccountType = AccountType.STANDARD };

        // Act & Assert
        ClassicAssert.ThrowsAsync<AccountsExceptions.AccountNotUpdatedException>(async () => await _accountService.UpdateAccountAsync("1", updateRequest));
    }

    [Test]
    public void UpdateAccount_ThrowsException_WhenClientNotFound()
    {
        var testAccount = new Account { Id = "1", ProductId = "P1", ClientId = "C1" };
        _accountRepository.Setup(r => r.GetByIdAsync("1")).ReturnsAsync(testAccount);
        _productRepository.Setup(r => r.GetByIdAsync("P1")) 
            .ReturnsAsync(new Product("Premium", Product.Type.BankAccount));
        _clientRepository.Setup(r => r.GetByIdAsync("C1")).ReturnsAsync((Client)null);

        var updateRequest = new UpdateAccountRequest
        {
            ProductID = "P1",
            ClientID = "C1",
            IBAN = "ES4704879765177458621788",
            Balance = 2000,
            AccountType = AccountType.STANDARD
        };

        // Act & Assert
        ClassicAssert.ThrowsAsync<AccountsExceptions.AccountNotCreatedException>(
            async () => await _accountService.UpdateAccountAsync("1", updateRequest));
    }

    [Test]
    public async Task UpdateAccount_FoundInCache_ReturnsCachedAccount()
    {
        var testAccount = new Account 
        { 
            Id = "1", 
            ProductId = "P1", 
            ClientId = "C1" 
        };

        // Simula que el producto existe en el repositorio
        var mockProduct = new Product("Premium", Product.Type.BankAccount);
        _productRepository.Setup(r => r.GetByIdAsync("P1"))
            .ReturnsAsync(mockProduct);
    
        // Simula que el cliente existe en el repositorio
        var mockClient = new Client();
        _clientRepository.Setup(r => r.GetByIdAsync("C1"))
            .ReturnsAsync(mockClient);

        // Simula que la cuenta existe en la caché
        _cache.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(JsonSerializer.Serialize(testAccount));

        var updateRequest = new UpdateAccountRequest
        {
            ProductID = "P1",
            ClientID = "C1",
            IBAN = "ES4704879765177458621788",
            Balance = 2000,
            AccountType = AccountType.SAVING
        };

        // Act
        var result = await _accountService.UpdateAccountAsync("1", updateRequest);

        // Assert
        ClassicAssert.IsNotNull(result);
        ClassicAssert.AreEqual(testAccount.Id, result.Id);
    }

    [Test]
    public async Task UpdateAccount_NotFoundInCache_FetchesFromDatabase()
    {
        // Arrange
        var testAccount = new Account { Id = "1", ProductId = "P1", ClientId = "C1" };
        _cache.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        
        _accountRepository.Setup(r => r.GetByIdAsync("1")).ReturnsAsync(testAccount);
        _productRepository.Setup(r => r.GetByIdAsync("P1"))
            .ReturnsAsync(new Product("Premium", Product.Type.BankAccount));
        _clientRepository.Setup(r => r.GetByIdAsync("C1")).ReturnsAsync(new Client());
        _accountRepository.Setup(r => r.UpdateAsync(It.IsAny<Account>())).Returns(Task.CompletedTask);

        var updateRequest = new UpdateAccountRequest
        {
            ProductID = "P1",
            ClientID = "C1",
            IBAN = "ES4704879765177458621788",
            Balance = 2000,
            AccountType = AccountType.STANDARD
        };
        
        // Act
        var result = await _accountService.UpdateAccountAsync("1", updateRequest);

        // Assert
        ClassicAssert.IsNotNull(result);
        ClassicAssert.AreEqual(updateRequest.Balance, result.Balance);
    }
    
    [Test]
    public void UpdateAccount_ThrowsException_WhenIBANIsNotValid()
    {
        var testAccount = new Account { Id = "1", ProductId = "P1", ClientId = "C1" };
        _accountRepository.Setup(r => r.GetByIdAsync("1")).ReturnsAsync(testAccount);
        _productRepository.Setup(r => r.GetByIdAsync("P1")) // ⚠️ Corregido
            .ReturnsAsync(new Product("Premium", Product.Type.BankAccount));
        _clientRepository.Setup(r => r.GetByIdAsync("C1")).ReturnsAsync(new Client());

        var updateRequest = new UpdateAccountRequest
        {
            ProductID = "P1",
            ClientID = "C1",
            IBAN = "invalid", // IBAN inválido
            Balance = 2000,
            AccountType = AccountType.STANDARD
        };

        // Act & Assert
        ClassicAssert.ThrowsAsync<AccountsExceptions.AccountIbanNotValid>(
            async () => await _accountService.UpdateAccountAsync("1", updateRequest));
    }
    
    [Test]
    public async Task GetMyAccountsAsClientAsync_ShouldThrowUserNotFoundException_WhenUserNotFound()
    {
        var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        var mockUserService = new Mock<IUserService>();
        var mockClientRepository = new Mock<IClientRepository>();
        var mockAccountsRepository = new Mock<IAccountsRepository>();
        var mockLogger = new Mock<ILogger<AccountService>>();
        var mockIbanGenerator = new Mock<IIbanGenerator>();
        var mockProductRepository = new Mock<IProductRepository>();
        var mockConnectionMultiplexer = new Mock<IConnectionMultiplexer>();
        var mockWebsocketHandler = new Mock<IWebsocketHandler>();

        var userId = "test-user-id";
        var mockUser = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        }));

        mockHttpContextAccessor.Setup(x => x.HttpContext.User).Returns(mockUser);

        mockUserService.Setup(service => service.GetUserByIdAsync(userId))
            .ReturnsAsync((UserResponse)null);

        var accountService = new AccountService(
            mockLogger.Object,
            mockIbanGenerator.Object,
            mockClientRepository.Object,
            mockProductRepository.Object,
            mockAccountsRepository.Object,
            mockConnectionMultiplexer.Object,
            mockHttpContextAccessor.Object,
            mockUserService.Object,
            mockWebsocketHandler.Object
        );

        var exception = Assert.ThrowsAsync<UserNotFoundException>(async () => 
            await accountService.GetMyAccountsAsClientAsync()
        );
    
        Assert.That(exception.Message, Is.EqualTo($"The user with id: {userId} was not found"));
    }
    
    [Test]
    public async Task GetCompleteAccountByClientIdAsync_ShouldReturnAccounts_WhenAccountsFound()
    {
        var mockLogger = new Mock<ILogger<AccountService>>();
        var mockIbanGenerator = new Mock<IIbanGenerator>();
        var mockClientRepository = new Mock<IClientRepository>();
        var mockProductRepository = new Mock<IProductRepository>();
        var mockAccountsRepository = new Mock<IAccountsRepository>();
        var mockConnection = new Mock<IConnectionMultiplexer>();
        var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        var mockUserService = new Mock<IUserService>();
        var mockWebsocketHandler = new Mock<IWebsocketHandler>();

        var clientId = "client-id";

        var accounts = new List<Account>
        {
            new Account { Id = "1", IBAN = "IBAN123" },
            new Account { Id = "2", IBAN = "IBAN456" }
        };

        mockAccountsRepository.Setup(repo => repo.getAccountByClientIdAsync(clientId)).ReturnsAsync(accounts);

        var accountService = new AccountService(
            mockLogger.Object,
            mockIbanGenerator.Object,
            mockClientRepository.Object,
            mockProductRepository.Object,
            mockAccountsRepository.Object,
            mockConnection.Object,
            mockHttpContextAccessor.Object,
            mockUserService.Object,
            mockWebsocketHandler.Object
        );

        var result = await accountService.GetCompleteAccountByClientIdAsync(clientId);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0].Id, Is.EqualTo("1"));
        Assert.That(result[0].IBAN, Is.EqualTo("IBAN123"));
        Assert.That(result[1].Id, Is.EqualTo("2"));
        Assert.That(result[1].IBAN, Is.EqualTo("IBAN456"));
    }
    
    [Test]
    public async Task GetCompleteAccountByClientIdAsync_ShouldThrowAccountNotFoundException_WhenAccountsNotFound()
    {
        var mockLogger = new Mock<ILogger<AccountService>>();
        var mockIbanGenerator = new Mock<IIbanGenerator>();
        var mockClientRepository = new Mock<IClientRepository>();
        var mockProductRepository = new Mock<IProductRepository>();
        var mockAccountsRepository = new Mock<IAccountsRepository>();
        var mockConnection = new Mock<IConnectionMultiplexer>();
        var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        var mockUserService = new Mock<IUserService>();
        var mockWebsocketHandler = new Mock<IWebsocketHandler>();

        var clientId = "client-id";

        mockAccountsRepository.Setup(repo => repo.getAccountByClientIdAsync(clientId)).ReturnsAsync((List<Account>)null);

        var accountService = new AccountService(
            mockLogger.Object,
            mockIbanGenerator.Object,
            mockClientRepository.Object,
            mockProductRepository.Object,
            mockAccountsRepository.Object,
            mockConnection.Object,
            mockHttpContextAccessor.Object,
            mockUserService.Object,
            mockWebsocketHandler.Object
        );

        var exception = Assert.ThrowsAsync<AccountsExceptions.AccountNotFoundException>(async () =>
            await accountService.GetCompleteAccountByClientIdAsync(clientId)
        );

        Assert.That(exception.Message, Is.EqualTo($"Account not found by id {clientId}"));
    }


    
    
    [Test]
    public void GetAccountByIbanAsync_ShouldThrowAccountNotFoundException_WhenNoAccountsFound()
    {
        var iban = "notFound";
        _accountRepository.Setup(r => r.getAccountByIbanAsync(It.Is<string>(id => id == iban)))
           .ReturnsAsync((Account)null);

        var exception = Assert.ThrowsAsync<AccountsExceptions.AccountNotFoundByIban>(async () =>
            await _accountService.GetAccountByIbanAsync(iban));

        Assert.That(exception.Message, Is.EqualTo($"Account not found by IBAN {iban}"));
        
        _accountRepository.Verify(repo => repo.getAccountByIbanAsync(iban), Times.Once);
    }
    

    
    
    [Test]
    public async Task GetCompleteAccountByIbanAsync_ShouldThrowAccountNotFoundByIban_WhenAccountNotFound()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<AccountService>>();
        var mockIbanGenerator = new Mock<IIbanGenerator>();
        var mockClientRepository = new Mock<IClientRepository>();
        var mockProductRepository = new Mock<IProductRepository>();
        var mockAccountsRepository = new Mock<IAccountsRepository>();
        var mockConnection = new Mock<IConnectionMultiplexer>();
        var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        var mockUserService = new Mock<IUserService>();
        var mockWebsocketHandler = new Mock<IWebsocketHandler>();

        var iban = "IBAN123";

        // Simulamos que no se encuentra la cuenta por IBAN
        mockAccountsRepository.Setup(repo => repo.getAccountByIbanAsync(iban)).ReturnsAsync((Account)null);

        var accountService = new AccountService(
            mockLogger.Object,
            mockIbanGenerator.Object,
            mockClientRepository.Object,
            mockProductRepository.Object,
            mockAccountsRepository.Object,
            mockConnection.Object,
            mockHttpContextAccessor.Object,
            mockUserService.Object,
            mockWebsocketHandler.Object
        );

        // Act & Assert
        var exception = Assert.ThrowsAsync<AccountsExceptions.AccountNotFoundByIban>(async () => 
            await accountService.GetCompleteAccountByIbanAsync(iban)
        );
        Assert.That(exception.Message, Is.EqualTo($"Account not found by IBAN {iban}"));
    }
    
    [Test]
    public async Task GetCompleteAccountByIbanAsync_ShouldReturnAccountCompleteResponse_WhenAccountFound()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<AccountService>>();
        var mockIbanGenerator = new Mock<IIbanGenerator>();
        var mockClientRepository = new Mock<IClientRepository>();
        var mockProductRepository = new Mock<IProductRepository>();
        var mockAccountsRepository = new Mock<IAccountsRepository>();
        var mockConnection = new Mock<IConnectionMultiplexer>();
        var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        var mockUserService = new Mock<IUserService>();
        var mockWebsocketHandler = new Mock<IWebsocketHandler>();

        var iban = "IBAN123";

        var account = new Account { Id = "1", IBAN = iban };
        mockAccountsRepository.Setup(repo => repo.getAccountByIbanAsync(iban)).ReturnsAsync(account);

        var accountService = new AccountService(
            mockLogger.Object,
            mockIbanGenerator.Object,
            mockClientRepository.Object,
            mockProductRepository.Object,
            mockAccountsRepository.Object,
            mockConnection.Object,
            mockHttpContextAccessor.Object,
            mockUserService.Object,
            mockWebsocketHandler.Object
        );

        // Act
        var result = await accountService.GetCompleteAccountByIbanAsync(iban);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo(account.Id));
        Assert.That(result.IBAN, Is.EqualTo(account.IBAN));
    }


    
     [Test]
    public async Task CreateAccountAsync_ShouldCreateSuccessfully()
    {
        var testUserId = "TestUserId";
        var testClient = new Client { Id = "ClientId", UserId = testUserId };
        var testProduct = new VivesBankApi.Rest.Product.Base.Models.Product("TestProduct", VivesBankApi.Rest.Product.Base.Models.Product.Type.BankAccount);
        var testIban = "ES123456789";
        var testRequest = new CreateAccountRequest { ProductName = "TestProduct" };

        _userService.Setup(u => u.GetUserByIdAsync(testUserId))
            .ReturnsAsync(new UserResponse
            {
                Id = testUserId,
                Dni = "12345678X",
                Role = Role.Client.ToString(),
                IsDeleted = false
            });

        _clientRepository.Setup(c => c.getByUserIdAsync(testUserId))
            .ReturnsAsync(testClient);

        _productRepository.Setup(p => p.GetByNameAsync(testRequest.ProductName))
            .ReturnsAsync(testProduct);

        _ibanGenerator.Setup(i => i.GenerateUniqueIbanAsync())
            .ReturnsAsync(testIban);

        _accountRepository.Setup(a => a.AddAsync(It.IsAny<Account>()))
            .Returns(Task.CompletedTask);

        var result = await _accountService.CreateAccountAsync(testRequest);

        ClassicAssert.IsNotNull(result);
        ClassicAssert.AreEqual(testIban, result.IBAN);
        ClassicAssert.AreEqual(testProduct.Id, result.productID);
    }

    [Test]
    public void CreateAccountAsync_ShouldThrow_UserNotFoundException()
    {
        _userService.Setup(u => u.GetUserByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((UserResponse)null);

        var request = new CreateAccountRequest { ProductName = "TestProduct" };

        Assert.ThrowsAsync<UserNotFoundException>(async () => 
            await _accountService.CreateAccountAsync(request));
    }

    [Test]
    public void CreateAccountAsync_ShouldThrow_ClientNotFoundException()
    {
        var testUserId = "TestUserId";
        
        _userService.Setup(u => u.GetUserByIdAsync(testUserId))
            .ReturnsAsync(new UserResponse { Id = testUserId });

        _clientRepository.Setup(c => c.getByUserIdAsync(testUserId))
            .ReturnsAsync((Client)null);

        var request = new CreateAccountRequest { ProductName = "TestProduct" };

        Assert.ThrowsAsync<ClientExceptions.ClientNotFoundException>(async () => 
            await _accountService.CreateAccountAsync(request));
    }

    [Test]
    public void CreateAccountAsync_ShouldThrow_AccountNotCreatedException_WhenProductNotFound()
    {
        // Arrange
        var testUserId = "TestUserId";
        var testClient = new Client { Id = "ClientId", UserId = testUserId };

        _userService.Setup(u => u.GetUserByIdAsync(testUserId))
            .ReturnsAsync(new UserResponse { Id = testUserId });

        _clientRepository.Setup(c => c.getByUserIdAsync(testUserId))
            .ReturnsAsync(testClient);

        _productRepository.Setup(p => p.GetByNameAsync(It.IsAny<string>()))
            .ReturnsAsync((VivesBankApi.Rest.Product.Base.Models.Product)null);

        var request = new CreateAccountRequest { ProductName = "TestProduct" };

        // Act & Assert
        Assert.ThrowsAsync<AccountsExceptions.AccountNotCreatedException>(async () => 
            await _accountService.CreateAccountAsync(request));
    }

    [Test]
    public void CreateAccountAsync_ShouldThrow_Exception_WhenIbanGenerationFails()
    {
        var testUserId = "TestUserId";
        var testClient = new Client { Id = "ClientId", UserId = testUserId };
        var testProduct = new VivesBankApi.Rest.Product.Base.Models.Product("TestProduct", VivesBankApi.Rest.Product.Base.Models.Product.Type.BankAccount);

        _userService.Setup(u => u.GetUserByIdAsync(testUserId))
            .ReturnsAsync(new UserResponse { Id = testUserId });

        _clientRepository.Setup(c => c.getByUserIdAsync(testUserId))
            .ReturnsAsync(testClient);

        _productRepository.Setup(p => p.GetByNameAsync("TestProduct"))
            .ReturnsAsync(testProduct);

        _ibanGenerator.Setup(i => i.GenerateUniqueIbanAsync())
            .ReturnsAsync(string.Empty);

        var request = new CreateAccountRequest { ProductName = "TestProduct" };

        Assert.ThrowsAsync<IbanGeneratorExceptions.IbanGeneratorFail>(async () => 
            await _accountService.CreateAccountAsync(request), "IBAN generation failed");
    }

    [Test]
    public async Task DeleteAccountAsync_ShouldDeleteLogically()
    {
        var accountId = "TkjPO5u_2w";
        _accountRepository.Setup(r => r.GetByIdAsync(account.Id)).
            ReturnsAsync(account);
        _accountRepository.Setup(r => r.UpdateAsync(It.IsAny<Account>())).Callback<Account>(updatedAccount =>
        {
            Assert.That(updatedAccount.IsDeleted, Is.True);
            Assert.That(updatedAccount.Id, Is.EqualTo(account.Id));
        });
        await _accountService.DeleteAccountAsync(accountId);
        _accountRepository.Verify(r => r.GetByIdAsync(accountId), Times.Once);
        _accountRepository.Verify(r => r.UpdateAsync(It.Is<Account>(a => a.IsDeleted == true)), Times.Once);
    }
    
    [Test]
    public async Task DeleteMyAccountAsync_ShouldDeleteAccount_WhenAccountFoundAndBalanceIsZero()
    {
        var mockLogger = new Mock<ILogger<AccountService>>();
        var mockIbanGenerator = new Mock<IIbanGenerator>();
        var mockClientRepository = new Mock<IClientRepository>();
        var mockProductRepository = new Mock<IProductRepository>();
        var mockAccountsRepository = new Mock<IAccountsRepository>();
        var mockConnection = new Mock<IConnectionMultiplexer>();
        var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        var mockUserService = new Mock<IUserService>();
        var mockWebsocketHandler = new Mock<IWebsocketHandler>();
        var mockCache = new Mock<IDatabase>();

        var iban = "IBAN123";
        var userId = "user-id";
        var clientId = "client-id";
        
        var userClaims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        };
        
        var mockUser = new ClaimsPrincipal(new ClaimsIdentity(userClaims));

        var userForFound = new UserResponse { Id = userId }; 
        var client = new Client { Id = clientId };
        var accountToDelete = new Account { Id = "1", IBAN = iban, ClientId = clientId, Balance = 0, IsDeleted = false };

        mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(new DefaultHttpContext());
        mockHttpContextAccessor.Setup(x => x.HttpContext.User).Returns(mockUser);
        mockUserService.Setup(x => x.GetUserByIdAsync(It.IsAny<string>())).ReturnsAsync(new UserResponse { Id = userId });
        mockClientRepository.Setup(x => x.getByUserIdAsync(It.IsAny<string>())).ReturnsAsync(new Client { Id = clientId });
        mockAccountsRepository.Setup(x => x.getAccountByIbanAsync(It.IsAny<string>())).ReturnsAsync(new Account { Id = "1", IBAN = iban, ClientId = clientId, Balance = 0, IsDeleted = false });
        mockConnection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockCache.Object);
        mockCache.Setup(x => x.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);

        var accountService = new AccountService(
            mockLogger.Object,
            mockIbanGenerator.Object,
            mockClientRepository.Object,
            mockProductRepository.Object,
            mockAccountsRepository.Object,
            mockConnection.Object,
            mockHttpContextAccessor.Object,
            mockUserService.Object,
            mockWebsocketHandler.Object
        );

        await accountService.DeleteMyAccountAsync(iban);

        mockAccountsRepository.Verify(x => x.UpdateAsync(It.Is<Account>(a => a.IsDeleted == true)), Times.Once);
        mockCache.Verify(
            x => x.KeyDeleteAsync(It.Is<RedisKey>(rk => rk == (RedisKey)$"account:{iban}"), It.IsAny<CommandFlags>()),
            Times.Once
        );

    }
    
    [Test]
    public async Task DeleteMyAccountAsync_ShouldThrowUserNotFoundException_WhenUserNotFound()
    {
        var mockLogger = new Mock<ILogger<AccountService>>();
        var mockIbanGenerator = new Mock<IIbanGenerator>();
        var mockClientRepository = new Mock<IClientRepository>();
        var mockProductRepository = new Mock<IProductRepository>();
        var mockAccountsRepository = new Mock<IAccountsRepository>();
        var mockConnection = new Mock<IConnectionMultiplexer>();
        var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        var mockUserService = new Mock<IUserService>();
        var mockWebsocketHandler = new Mock<IWebsocketHandler>();
        var mockCache = new Mock<IDatabase>();

        var iban = "IBAN123";
        var userId = "user-id";

        var userClaims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        };

        var mockUser = new ClaimsPrincipal(new ClaimsIdentity(userClaims));

        mockHttpContextAccessor.Setup(x => x.HttpContext.User).Returns(mockUser);
    
        mockUserService.Setup(x => x.GetUserByIdAsync(userId)).ReturnsAsync((UserResponse)null);

        var accountService = new AccountService(
            mockLogger.Object,
            mockIbanGenerator.Object,
            mockClientRepository.Object,
            mockProductRepository.Object,
            mockAccountsRepository.Object,
            mockConnection.Object,
            mockHttpContextAccessor.Object,
            mockUserService.Object,
            mockWebsocketHandler.Object
        );

        var exception = Assert.ThrowsAsync<UserNotFoundException>(() => accountService.DeleteMyAccountAsync(iban));
        Assert.That(exception.Message, Is.EqualTo("The user with id: user-id was not found"));

    }
    

    [Test]
    public async Task ImportJson()
    {
        var fileContent = "[{\"Id\": 1}, {\"Id\": 2}]";
        var mockFile = new Mock<IFormFile>();
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent));
        mockFile.Setup(f => f.OpenReadStream()).Returns(stream);

        var mockLogger = new Mock<ILogger<AccountService>>();
        var mockIbanGenerator = new Mock<IIbanGenerator>();
        var mockClientRepository = new Mock<IClientRepository>();
        var mockProductRepository = new Mock<IProductRepository>();
        var mockAccountsRepository = new Mock<IAccountsRepository>();
        var mockConnectionMultiplexer = new Mock<IConnectionMultiplexer>();
        var mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        var mockUserService = new Mock<IUserService>();
        var mockWebsocketHandler = new Mock<IWebsocketHandler>();

        var importClass = new AccountService(
            mockLogger.Object,
            mockIbanGenerator.Object,
            mockClientRepository.Object,
            mockProductRepository.Object,
            mockAccountsRepository.Object,
            mockConnectionMultiplexer.Object,
            mockHttpContextAccessor.Object,
            mockUserService.Object,
            mockWebsocketHandler.Object
        );

        var result = importClass.Import(mockFile.Object);
        var accounts = new List<Account>();
        result.Subscribe(account => accounts.Add(account));

        await Task.Delay(100);

        ClassicAssert.AreEqual("1", accounts[0].Id);
        ClassicAssert.AreEqual("2", accounts[1].Id);
    }

    
    [Test]
    public async Task ExportJson()
    {
        var accounts = new List<Account>
        {
            new Account { Id = "1", ClientId = "C1", ProductId = "P1", AccountType = AccountType.STANDARD, IBAN = "IBAN1", Balance = 1000 },
            new Account { Id = "2", ClientId = "C2", ProductId = "P2", AccountType = AccountType.SAVING, IBAN = "IBAN2", Balance = 2000 }
        };

        var loggerMock = new Mock<ILogger<AccountService>>();
        var ibanGeneratorMock = new Mock<IIbanGenerator>();
        var clientRepositoryMock = new Mock<IClientRepository>();
        var productRepositoryMock = new Mock<IProductRepository>();
        var accountRepositoryMock = new Mock<IAccountsRepository>();
        var connectionMultiplexerMock = new Mock<IConnectionMultiplexer>();
        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        var userServiceMock = new Mock<IUserService>();
        var websocketHandlerMock = new Mock<IWebsocketHandler>();

        var accountService = new AccountService(
            loggerMock.Object, 
            ibanGeneratorMock.Object, 
            clientRepositoryMock.Object, 
            productRepositoryMock.Object, 
            accountRepositoryMock.Object, 
            connectionMultiplexerMock.Object, 
            httpContextAccessorMock.Object, 
            userServiceMock.Object, 
            websocketHandlerMock.Object
        );

        var directoryPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "uploads", "Json");
        var fileName = "BankAccountInSystem-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".json";
        var filePath = System.IO.Path.Combine(directoryPath, fileName);

        var fileStream = await accountService.Export(accounts);

        Assert.That(fileStream, Is.InstanceOf<FileStream>());
        Assert.That(File.Exists(filePath), Is.True);
        Assert.That(filePath, Does.Contain(fileName));

        fileStream.Close();
        File.Delete(filePath); 
    }

}
