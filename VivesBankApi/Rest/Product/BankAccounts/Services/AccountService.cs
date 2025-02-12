using System.Reactive.Linq;
using System.Security.Claims;
using Newtonsoft.Json;
using StackExchange.Redis;
using VivesBankApi.Rest.Clients.Exceptions;
using VivesBankApi.Rest.Clients.Repositories;
using VivesBankApi.Rest.Movimientos.Validators;
using VivesBankApi.Rest.Product.BankAccounts.AccountTypeExtensions;
using VivesBankApi.Rest.Product.BankAccounts.Dto;
using VivesBankApi.Rest.Product.BankAccounts.Mappers;
using VivesBankApi.Rest.Product.BankAccounts.Models;
using VivesBankApi.Rest.Product.BankAccounts.Repositories;
using VivesBankApi.Rest.Products.BankAccounts.Exceptions;
using VivesBankApi.Rest.Users.Dtos;
using VivesBankApi.Rest.Users.Exceptions;
using VivesBankApi.Rest.Users.Models;
using VivesBankApi.Rest.Users.Service;
using VivesBankApi.Utils.GenericStorage.JSON;
using VivesBankApi.Utils.IbanGenerator;
using VivesBankApi.WebSocket.Model;
using VivesBankApi.WebSocket.Service;

namespace VivesBankApi.Rest.Product.BankAccounts.Services;

/// <summary>
/// Implementación del servicio para gestionar cuentas bancarias.
/// Proporciona métodos para crear, obtener, actualizar y eliminar cuentas bancarias, así como operaciones de paginación y notificaciones.
/// </summary>
/// <remarks>
/// Autor: Raúl Fernández, Javier Hernández, Samuel Cortés, Germán, Álvaro Herrero, Tomás
/// Versión: 1.0
/// </remarks>
public class AccountService : GenericStorageJson<Account>, IAccountsService
{
    private readonly IAccountsRepository _accountsRepository;
    private readonly IClientRepository _clientRepository;
    private readonly IProductRepository _productRepository;
    private readonly IIbanGenerator _ibanGenerator;
    private readonly IDatabase _cache;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserService _userService;
    private readonly IWebsocketHandler _websocketHandler;

    
    /// <summary>
    /// Constructor de la clase <see cref="AccountService"/>.
    /// </summary>
    /// <param name="logger">Instancia del logger.</param>
    /// <param name="ibanGenerator">Generador de IBAN.</param>
    /// <param name="clientRepository">Repositorio de clientes.</param>
    /// <param name="productRepository">Repositorio de productos.</param>
    /// <param name="accountsRepository">Repositorio de cuentas.</param>
    /// <param name="connection">Conexión a la base de datos.</param>
    /// <param name="httpContextAccessor">Accesor para el contexto HTTP.</param>
    /// <param name="userService">Servicio de usuario.</param>
    /// <param name="websocketHandler">Manejador de WebSockets para notificaciones.</param>
    public AccountService(
        ILogger<AccountService> logger, 
        IIbanGenerator ibanGenerator,
        IClientRepository clientRepository,
        IProductRepository productRepository,
        IAccountsRepository accountsRepository, 
        IConnectionMultiplexer connection, 
        IHttpContextAccessor httpContextAccessor, 
        IUserService userService, 
        IWebsocketHandler websocketHandler)
        : base(logger)
    {
        _ibanGenerator = ibanGenerator;
        _accountsRepository = accountsRepository;
        _clientRepository = clientRepository;
        _productRepository = productRepository;
        _cache = connection.GetDatabase();
        _httpContextAccessor = httpContextAccessor;
        _userService = userService;
        _websocketHandler = websocketHandler;
    }
    
    /// <summary>
    /// Obtiene todas las cuentas bancarias.
    /// </summary>
    /// <returns>Lista de todas las cuentas bancarias.</returns>
    public async Task<List<Account>> GetAll()
    {
        return await _accountsRepository.GetAllAsync();
    }
    
    /// <summary>
    /// Obtiene todas las cuentas bancarias con paginación y ordenación.
    /// </summary>
    /// <param name="pageNumber">Número de página (por defecto 0).</param>
    /// <param name="pageSize">Número de elementos por página (por defecto 10).</param>
    /// <param name="sortBy">Campo por el cual ordenar (por defecto "id").</param>
    /// <param name="direction">Dirección de ordenación: "asc" o "desc" (por defecto "asc").</param>
    /// <returns>Respuesta con las cuentas bancarias paginadas.</returns>
    public async Task<PageResponse<AccountResponse>> GetAccountsAsync(int pageNumber = 0, int pageSize = 10, string sortBy = "id", string direction = "asc")
    {
        _logger.LogInformation("Getting all accounts with pagination");
        
        var pagedAccounts = await _accountsRepository.GetAllPagedAsync(pageNumber, pageSize);
        
        var accountResponses = pagedAccounts.Select(a => a.toResponse()).ToList();
        
        var response = new PageResponse<AccountResponse>
        {
            Content = accountResponses,
            TotalPages = pagedAccounts.PageCount,
            TotalElements = pagedAccounts.TotalCount,
            PageSize = pagedAccounts.PageSize,
            PageNumber = pagedAccounts.PageNumber,
            TotalPageElements = pagedAccounts.Count,
            Empty = pagedAccounts.Count == 0,
            First = pagedAccounts.IsFirstPage,
            Last = pagedAccounts.IsLastPage,
            SortBy = sortBy,
            Direction = direction
        };

        return response;
    }

    /// <summary>
    /// Obtiene una cuenta bancaria por su ID.
    /// </summary>
    /// <param name="id">ID de la cuenta bancaria.</param>
    /// <returns>Detalles de la cuenta.</returns>
    /// <exception cref="AccountsExceptions.AccountNotFoundException">Lanzado cuando la cuenta no se encuentra.</exception>
    public async Task<AccountResponse> GetAccountByIdAsync(string id)
    {
        _logger.LogInformation($"Getting account by id: {id}");
        var result = await GetByIdAsync(id);
        if (result == null) throw new AccountsExceptions.AccountNotFoundException(id);
        return result.toResponse();
    }

    /// <summary>
    /// Obtiene todas las cuentas bancarias asociadas a un cliente por su ID.
    /// </summary>
    /// <param name="clientId">ID del cliente.</param>
    /// <returns>Lista de cuentas bancarias asociadas al cliente.</returns>
    /// <exception cref="AccountsExceptions.AccountNotFoundException">Lanzado cuando no se encuentran cuentas para el cliente.</exception>
    public async Task<List<AccountResponse>> GetAccountByClientIdAsync(string clientId)
    {
        _logger.LogInformation($"Getting account by client id: {clientId}");
        var res = await _accountsRepository.getAccountByClientIdAsync(clientId);
        if (res == null) throw new AccountsExceptions.AccountNotFoundException(clientId);
        return res.Select(a => a.toResponse()).ToList();
    }

    public async Task<List<AccountResponse>> GetMyAccountsAsClientAsync()
    {
        _logger.LogInformation("Getting my accounts as client");
        var user = _httpContextAccessor.HttpContext!.User;
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userForFound = await _userService.GetUserByIdAsync(id) ?? throw new UserNotFoundException(id);
        var client = await _clientRepository.getByUserIdAsync(id) ?? throw new ClientExceptions.ClientNotFoundException(id);
        var accounts = await _accountsRepository.getAccountByClientIdAsync(client.Id);
        return accounts.Select(a => a.toResponse()).ToList();
    }
    
    /// <summary>
    /// Obtiene todas las cuentas completas asociadas a un cliente por su ID.
    /// </summary>
    /// <param name="clientId">ID del cliente.</param>
    /// <returns>Lista de cuentas completas asociadas al cliente.</returns>
    /// <exception cref="AccountsExceptions.AccountNotFoundException">Lanzado cuando no se encuentran cuentas completas para el cliente.</exception>
    public async Task<List<AccountCompleteResponse>> GetCompleteAccountByClientIdAsync(string clientId)
    {
        _logger.LogInformation($"Getting complete accounts by client id: {clientId}");
        var res = await _accountsRepository.getAccountByClientIdAsync(clientId);
        if (res == null) throw new AccountsExceptions.AccountNotFoundException(clientId);
        return res.Select(a => a.toCompleteResponse()).ToList();
    }

    /// <summary>
    /// Obtiene una cuenta bancaria por su IBAN.
    /// </summary>
    /// <param name="iban">IBAN de la cuenta.</param>
    /// <returns>Detalles de la cuenta asociada al IBAN.</returns>
    /// <exception cref="AccountsExceptions.AccountNotFoundByIban">Lanzado cuando no se encuentra la cuenta por el IBAN proporcionado.</exception>
    public async Task<AccountResponse> GetAccountByIbanAsync(string iban)
    {
        _logger.LogInformation($"Getting account by IBAN {iban}");
        var result = await GetByIbanAsync(iban);
        if (result == null) throw new AccountsExceptions.AccountNotFoundByIban(iban);
        return result.toResponse();
    }
    public async Task<AccountCompleteResponse> GetCompleteAccountByIbanAsync(string iban)
    {
        _logger.LogInformation($"Getting complete account by IBAN {iban}");
        var result = await _accountsRepository.getAccountByIbanAsync(iban);
        if (result == null) throw new AccountsExceptions.AccountNotFoundByIban(iban);
        return result.toCompleteResponse();
    }

    /// <summary>
    /// Crea una nueva cuenta bancaria para un cliente registrado.
    /// </summary>
    /// <param name="request">Detalles de la cuenta a crear.</param>
    /// <returns>Detalles de la cuenta creada.</returns>
    /// <exception cref="AccountsExceptions.AccountNotCreatedException">Lanzado cuando no se puede crear la cuenta.</exception>
    public async Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request)
    {
        _logger.LogInformation("Creating account for Client registered on the system");

        if (_httpContextAccessor.HttpContext == null)
            throw new Exception("HttpContext is null");

        var user = _httpContextAccessor.HttpContext.User;

        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(id))
            throw new Exception("User ID claim is missing");

        var userForFound = await _userService.GetUserByIdAsync(id)
                           ?? throw new UserNotFoundException(id);
        var client = await _clientRepository.getByUserIdAsync(id)
                     ?? throw new ClientExceptions.ClientNotFoundException(id);

        var product = await _productRepository.GetByNameAsync(request.ProductName);
        if (product == null)
            throw new AccountsExceptions.AccountNotCreatedException();

        var iban = await _ibanGenerator.GenerateUniqueIbanAsync();
        if (string.IsNullOrWhiteSpace(iban))
            throw new Exception("IBAN generation failed");

        var account = request.fromDtoRequest();
        account.ClientId = client.Id;
        account.ProductId = product.Id;
        account.IBAN = iban;
        account.Balance = 0;

        await _accountsRepository.AddAsync(account);
        await EnviarNotificacionCreateAsync(userForFound, account.toResponse());

        return account.toResponse();
    }

    /// <summary>
    /// Elimina una cuenta bancaria asociada al IBAN proporcionado.
    /// </summary>
    /// <param name="iban">IBAN de la cuenta a eliminar.</param>
    public async Task DeleteMyAccountAsync(String iban)
    {
        _logger.LogInformation("Deleting my account with iban: " +iban);
        var user = _httpContextAccessor.HttpContext!.User;
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userForFound = await _userService.GetUserByIdAsync(id) ?? throw new UserNotFoundException(id);
        var client = await _clientRepository.getByUserIdAsync(id) ?? throw new ClientExceptions.ClientNotFoundException(id);
        var accountToDelete = await _accountsRepository.getAccountByIbanAsync(iban) ?? throw new AccountsExceptions.AccountNotFoundException(iban);
        if(accountToDelete.ClientId!= client.Id)
            throw new AccountsExceptions.AccountNotDeletedException(iban);
        if (accountToDelete.Balance > 0) throw new AccountsExceptions(iban);
        accountToDelete.IsDeleted = true;
        await _accountsRepository.UpdateAsync(accountToDelete);
        await EnviarNotificacionDeleteAsync(userForFound, accountToDelete.toResponse());
        await _cache.KeyDeleteAsync(id);
        await _cache.KeyDeleteAsync("account:" + accountToDelete.IBAN);
    }

    /// <summary>
    /// Actualiza una cuenta bancaria con nuevos detalles.
    /// </summary>
    /// <param name="id">ID de la cuenta a actualizar.</param>
    /// <param name="request">Detalles de la actualización de la cuenta.</param>
    /// <returns>Detalles de la cuenta actualizada.</returns>
    /// <exception cref="AccountsExceptions.AccountNotFoundException">Lanzado cuando no se encuentra la cuenta.</exception>
    /// <exception cref="AccountsExceptions.AccountNotUpdatedException">Lanzado cuando no se puede actualizar la cuenta.</exception>
    /// <exception cref="AccountsExceptions.AccountNotCreatedException">Lanzado cuando no se puede crear la cuenta asociada.</exception>
    /// <exception cref="AccountsExceptions.AccountIbanNotValid">Lanzado cuando el IBAN proporcionado no es válido.</exception>
    public async Task<AccountCompleteResponse> UpdateAccountAsync(string id, UpdateAccountRequest request)
    {
        _logger.LogInformation($"Actualizando cuenta con ID {id}");

        var account = await GetByIdAsync(id) ?? throw new AccountsExceptions.AccountNotFoundException(id);
        
        // Validaciones de producto, cliente y formato del IBAN
        if (await _productRepository.GetByIdAsync(request.ProductID) == null)
            throw new AccountsExceptions.AccountNotUpdatedException(id);
        
        if (await _clientRepository.GetByIdAsync(request.ClientID) == null)
            throw new AccountsExceptions.AccountNotCreatedException();

        if (!IbanValidator.ValidateIban(request.IBAN)) 
            throw new AccountsExceptions.AccountIbanNotValid(request.IBAN);

        if (!Enum.IsDefined(typeof(AccountType), request.AccountType)) 
            throw new AccountsExceptions.AccountNotUpdatedException(id);

        // Actualización de la cuenta
        account.Balance = request.Balance;
        account.UpdatedAt = DateTime.UtcNow;
        
        await _accountsRepository.UpdateAsync(account);
        return account.toCompleteResponse();
    }

    /// <summary>
    /// Elimina una cuenta bancaria de la base de datos.
    /// </summary>
    /// <param name="id">ID de la cuenta a eliminar.</param>
    /// <exception cref="AccountsExceptions.AccountNotFoundException">Lanzado cuando no se encuentra la cuenta.</exception>
    public async Task DeleteAccountAsync(string id)
    {
        _logger.LogInformation($"Eliminando cuenta con ID {id}");
        var result = await GetByIdAsync(id);
        if (result == null) throw new AccountsExceptions.AccountNotFoundException(id);
        result.IsDeleted = true;
        await _accountsRepository.UpdateAsync(result);
        await _cache.KeyDeleteAsync(id);
        await _cache.KeyDeleteAsync("account:" + result.IBAN);
    }

    /// <summary>
    /// Obtiene una cuenta bancaria por su ID, primero verificando la caché.
    /// </summary>
    /// <param name="id">ID de la cuenta.</param>
    /// <returns>Cuenta bancaria correspondiente al ID, si existe.</returns>
    private async Task<Account?> GetByIdAsync(string id)
    {
        return await GetAccountFromCacheAsync(id)
               ?? await GetAccountFromRepositoryAndCacheAsync(id);
    }

    private async Task<Account?> GetAccountFromCacheAsync(string id)
    {
        var cachedAccount = await _cache.StringGetAsync(id);
        return !cachedAccount.IsNullOrEmpty ? JsonConvert.DeserializeObject<Account>(cachedAccount) : null;
    }

    private async Task<Account?> GetAccountFromRepositoryAndCacheAsync(string id)
    {
        var account = await _accountsRepository.GetByIdAsync(id);
        if (account != null)
        {
            await CacheAccountAsync(id, account);
        }
        return account;
    }

    private async Task CacheAccountAsync(string id, Account account)
    {
        await _cache.StringSetAsync(id, JsonConvert.SerializeObject(account), TimeSpan.FromMinutes(10));
    }

    /// <summary>
    /// Obtiene una cuenta bancaria por su IBAN, verificando la caché primero.
    /// </summary>
    /// <param name="iban">IBAN de la cuenta.</param>
    /// <returns>Cuenta bancaria correspondiente al IBAN, si existe.</returns>
    private async Task<Account?> GetByIbanAsync(string iban)
    {
        // Intentamos obtener la cuenta de la caché primero
        var cachedAccount = await _cache.StringGetAsync("account:" + iban);
        if (!cachedAccount.IsNullOrEmpty)
        {
            return JsonConvert.DeserializeObject<Account>(cachedAccount);
        }

        // Si no está en caché, la obtenemos de la base de datos y la almacenamos en caché
        Account? account = await _accountsRepository.getAccountByIbanAsync(iban);
        if (account != null)
        {
            await _cache.StringSetAsync("account:" + iban, JsonConvert.SerializeObject(account), TimeSpan.FromMinutes(10));
            return account;
        }
        return null;
    }

    /// <summary>
    /// Envia una notificación de creación de una cuenta bancaria a través de WebSocket.
    /// </summary>
    /// <typeparam name="T">Tipo de dato de la cuenta.</typeparam>
    /// <param name="user">Usuario al que se enviará la notificación.</param>
    /// <param name="t">Datos de la cuenta que se han creado.</param>
    public async Task EnviarNotificacionCreateAsync<T>(UserResponse user, T t)
    {
        var notificacion = new Notification<T>
        {
            Type = Notification<T>.NotificationType.Create.ToString(),
            CreatedAt = DateTime.Now,
            Data = t
        };
        await _websocketHandler.NotifyUserAsync(user.Id, notificacion);
    }

    /// <summary>
    /// Envia una notificación de eliminación de una cuenta bancaria a través de WebSocket.
    /// </summary>
    /// <typeparam name="T">Tipo de dato de la cuenta.</typeparam>
    /// <param name="user">Usuario al que se enviará la notificación.</param>
    /// <param name="t">Datos de la cuenta que se han eliminado.</param>
    public async Task EnviarNotificacionDeleteAsync<T>(UserResponse user, T t)
    {
        var notificacion = new Notification<T>
        {
            Type = Notification<T>.NotificationType.Delete.ToString(),
            CreatedAt = DateTime.Now,
            Data = t
        };
        await _websocketHandler.NotifyUserAsync(user.Id, notificacion);
    }

    /// <summary>
    /// Importa cuentas bancarias desde un archivo JSON.
    /// </summary>
    /// <param name="fileStream">Archivo JSON que contiene las cuentas a importar.</param>
    /// <returns>Un flujo de observables que emite las cuentas importadas.</returns>
    public IObservable<Account> Import(IFormFile fileStream)
    {
        _logger.LogInformation("Iniciando importación de cuentas desde archivo JSON.");

        return Observable.Create<Account>(async (observer, cancellationToken) =>
        {
            try
            {
                using var stream = fileStream.OpenReadStream();
                using var streamReader = new StreamReader(stream);
                using var jsonReader = new JsonTextReader(streamReader) { SupportMultipleContent = true };

                var serializer = new JsonSerializer
                {
                    MissingMemberHandling = MissingMemberHandling.Error
                };

                while (await jsonReader.ReadAsync(cancellationToken))
                {
                    if (jsonReader.TokenType == JsonToken.StartObject)
                    {
                        var account = serializer.Deserialize<Account>(jsonReader);
                    
                        if (account != null)
                        {
                            _logger.LogInformation($"Cuenta deserializada: {account.Id}"); 
                            observer.OnNext(account);
                        }
                        else
                        {
                            _logger.LogWarning("Error al deserializar una cuenta.");
                        }
                    }
                }

                observer.OnCompleted();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al procesar el archivo JSON: {ex.Message}");
                observer.OnError(ex);
            }
        });
    }

    /// <summary>
    /// Exporta las cuentas bancarias a un archivo JSON.
    /// </summary>
    /// <param name="entities">Lista de cuentas bancarias a exportar.</param>
    /// <returns>Flujo de archivo JSON que contiene las cuentas exportadas.</returns>
    public async Task<FileStream> Export(List<Account> entities)
    {
        _logger.LogInformation("Exportando cuentas a archivo JSON...");

        var json = JsonConvert.SerializeObject(entities, Formatting.Indented);

        var directoryPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "uploads", "Json");

        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var fileName = "BankAccountInSystem-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".json";
        var filePath = System.IO.Path.Combine(directoryPath, fileName);

        await File.WriteAllTextAsync(filePath, json);

        _logger.LogInformation($"Archivo escrito en: {filePath}");

        return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}