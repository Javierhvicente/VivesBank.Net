using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework.Legacy;
using VivesBankApi.Backup;
using VivesBankApi.Backup.Exceptions;
using VivesBankApi.Rest.Clients.Models;
using VivesBankApi.Rest.Clients.Repositories;
using VivesBankApi.Rest.Movimientos.Models;
using VivesBankApi.Rest.Movimientos.Repositories.Movimientos;
using VivesBankApi.Rest.Product.BankAccounts.AccountTypeExtensions;
using VivesBankApi.Rest.Product.BankAccounts.Models;
using VivesBankApi.Rest.Product.BankAccounts.Repositories;
using VivesBankApi.Rest.Product.Base.Models;
using VivesBankApi.Rest.Product.CreditCard.Models;
using VivesBankApi.Rest.Users.Models;
using VivesBankApi.Rest.Users.Repository;
using VivesBankApi.Utils.Backup;
using Path = System.IO.Path;

[TestFixture]
[TestOf(typeof(BackupService))]
public class BackupServiceTest
{
    private Mock<IClientRepository> _mockClientRepository;
    private Mock<IUserRepository> _mockUserRepository;
    private Mock<IProductRepository> _mockProductRepository;
    private Mock<ICreditCardRepository> _mockCreditCardRepository;
    private Mock<IAccountsRepository> _mockBankAccountRepository;
    private Mock<IMovimientoRepository> _mockMovimientoRepository;
    private BackupService _backupService;

    private List<Client> _clients;
    private List<User> _users;
    private List<Product> _products;
    private List<Account> _accounts;
    private List<CreditCard> _creditCards;
    private List<Movimiento> _movements;

    [SetUp]
    public void Setup()
    {
        _clients = new List<Client>
        {
            new Client
            {
                Id = "client1",
                UserId = "user1",
                FullName = "John Doe",
                Adress = "123 Main St",
                Photo = "john.png",
                PhotoDni = "john_dni.png",
                AccountsIds = new List<string> { "account1" },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new Client
            {
                Id = "client2",
                UserId = "user2",
                FullName = "Jane Smith",
                Adress = "456 Oak Ave",
                Photo = "jane.png",
                PhotoDni = "jane_dni.png",
                AccountsIds = new List<string> { "account2" },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsDeleted = false
            }
        };

        _users = new List<User>
        {
            new User
            {
                Id = "user1",
                Dni = "12345678A",
                Password = "password123",
                Role = Role.Client,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new User
            {
                Id = "user2",
                Dni = "87654321B",
                Password = "password456",
                Role = Role.Client,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsDeleted = false
            }
        };

        _products = new List<Product>
        {
            new Product("Basic Account", Product.Type.BankAccount),
            new Product("Premium Credit Card", Product.Type.CreditCard)
        };

        _accounts = new List<Account>
        {
            new Account
            {
                Id = "account1",
                ProductId = "product1",
                ClientId = "client1",
                IBAN = "IBAN1234",
                Balance = 1000.0M,
                AccountType = AccountType.SAVING,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new Account
            {
                Id = "account2",
                ProductId = "product2",
                ClientId = "client2",
                IBAN = "IBAN5678",
                Balance = 5000.0M,
                AccountType = AccountType.STANDARD,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsDeleted = false
            }
        };

        _creditCards = new List<CreditCard>
        {
            new CreditCard
            {
                Id = "card1",
                AccountId = "account1",
                CardNumber = "1234-5678-9876-5432",
                Pin = "1234",
                Cvc = "123",
                ExpirationDate = new DateOnly(2025, 12, 31),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new CreditCard
            {
                Id = "card2",
                AccountId = "account2",
                CardNumber = "4321-8765-6789-1234",
                Pin = "4321",
                Cvc = "321",
                ExpirationDate = new DateOnly(2026, 6, 30),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsDeleted = false
            }
        };

        _movements = new List<Movimiento>
        {
            new Movimiento
            {
                Guid = "movement1",
                ClienteGuid = "client1",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsDeleted = false
            },
            new Movimiento
            {
                Guid = "movement2",
                ClienteGuid = "client2",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsDeleted = false
            }
        };

        _mockClientRepository = new Mock<IClientRepository>();
        _mockUserRepository = new Mock<IUserRepository>();
        _mockProductRepository = new Mock<IProductRepository>();
        _mockCreditCardRepository = new Mock<ICreditCardRepository>();
        _mockBankAccountRepository = new Mock<IAccountsRepository>();
        _mockMovimientoRepository = new Mock<IMovimientoRepository>();

        _mockClientRepository.Setup(x => x.GetAllAsync()).ReturnsAsync(_clients);
        _mockUserRepository.Setup(x => x.GetAllAsync()).ReturnsAsync(_users);
        _mockProductRepository.Setup(x => x.GetAllAsync()).ReturnsAsync(_products);
        _mockCreditCardRepository.Setup(x => x.GetAllAsync()).ReturnsAsync(_creditCards);
        _mockBankAccountRepository.Setup(x => x.GetAllAsync()).ReturnsAsync(_accounts);
        _mockMovimientoRepository.Setup(x => x.GetAllMovimientosAsync()).ReturnsAsync(_movements);

        _backupService = new BackupService(
            Mock.Of<ILogger<BackupService>>(),
            _mockClientRepository.Object,
            _mockUserRepository.Object,
            _mockProductRepository.Object,
            _mockCreditCardRepository.Object,
            _mockBankAccountRepository.Object,
            _mockMovimientoRepository.Object
        );
    }

    [Test]
    public async Task ExportToZipOk()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var zipRequest = new BackUpRequest { FilePath = Path.Combine(tempDir, "testBackup.zip") };

        var result = await _backupService.ExportToZip(zipRequest);

        ClassicAssert.True(File.Exists(result));
        using (var zip = ZipFile.OpenRead(result))
        {
            ClassicAssert.True(zip.Entries.Any(e => e.FullName == "clients.json"));
            ClassicAssert.True(zip.Entries.Any(e => e.FullName == "users.json"));
            ClassicAssert.True(zip.Entries.Any(e => e.FullName == "products.json"));
            ClassicAssert.True(zip.Entries.Any(e => e.FullName == "creditCards.json"));
            ClassicAssert.True(zip.Entries.Any(e => e.FullName == "bankAccounts.json"));
            ClassicAssert.True(zip.Entries.Any(e => e.FullName == "movements.json"));
        }

        File.Delete(result);
    }

    [Test]
    public async Task ExportToZipDirectoryNotExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "nonexistent");
        var zipRequest = new BackUpRequest { FilePath = Path.Combine(tempDir, "testBackup.zip") };

        var result = await _backupService.ExportToZip(zipRequest);

        ClassicAssert.True(Directory.Exists(Path.GetDirectoryName(result)));
        ClassicAssert.True(File.Exists(result));

        File.Delete(result);
        Directory.Delete(Path.GetDirectoryName(result), true);
    }

    [Test]
    public void ExportToZipPathIsNull()
    {
        var zipRequest = new BackUpRequest { FilePath = null };

        Assert.ThrowsAsync<ArgumentException>(async () => await _backupService.ExportToZip(zipRequest));
    }

    [Test]
    public void ImportFromZipFileNotFound()
    {
        var zipRequest = new BackUpRequest { FilePath = "nonexistent.zip" };

        var caughtException = Assert.ThrowsAsync<BackupException.BackupFileNotFoundException>(async () =>
        {
            await _backupService.ImportFromZip(zipRequest);
        });

        Assert.That(caughtException.Message, Is.EqualTo("El archivo nonexistent.zip no fue encontrado."));
    }

    [Test]
    public async Task ImportFromZip_Success()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var zipFilePath = Path.Combine(tempDir, "testImport.zip");
        var jsonFiles = new Dictionary<string, string>
        {
            { "clients.json", JsonSerializer.Serialize(_clients) },
            { "users.json", JsonSerializer.Serialize(_users) },
            { "products.json", JsonSerializer.Serialize(_products) },
            { "creditCards.json", JsonSerializer.Serialize(_creditCards) },
            { "bankAccounts.json", JsonSerializer.Serialize(_accounts) },
            { "movements.json", JsonSerializer.Serialize(_movements) }
        };

        using (var zip = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
        {
            foreach (var file in jsonFiles)
            {
                var entry = zip.CreateEntry(file.Key);
                using (var entryStream = entry.Open())
                using (var writer = new StreamWriter(entryStream))
                {
                    writer.Write(file.Value);
                }
            }
        }

        var zipRequest = new BackUpRequest { FilePath = zipFilePath };

        await _backupService.ImportFromZip(zipRequest);

        _mockClientRepository.Verify(x => x.GetAllAsync(), Times.Never);
        _mockUserRepository.Verify(x => x.GetAllAsync(), Times.Never);
        _mockProductRepository.Verify(x => x.GetAllAsync(), Times.Never);
        _mockCreditCardRepository.Verify(x => x.GetAllAsync(), Times.Never);
        _mockBankAccountRepository.Verify(x => x.GetAllAsync(), Times.Never);
        _mockMovimientoRepository.Verify(x => x.GetAllMovimientosAsync(), Times.Never);

        File.Delete(zipFilePath);
        Directory.Delete(tempDir, true);
    }

    [Test]
    public async Task ImportFromZip_CorruptJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var zipFilePath = Path.Combine(tempDir, "corruptImport.zip");

        using (var zip = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("clients.json");
            using (var entryStream = entry.Open())
            using (var writer = new StreamWriter(entryStream))
            {
                writer.Write("{invalid_json}");
            }
        }

        var zipRequest = new BackUpRequest { FilePath = zipFilePath };

        Assert.ThrowsAsync<BackupException.BackupPermissionException>(async () =>
        {
            await _backupService.ImportFromZip(zipRequest);
        });

        File.Delete(zipFilePath);
        Directory.Delete(tempDir, true);
    }
}