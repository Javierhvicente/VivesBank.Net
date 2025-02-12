using System.Security.Claims;
using System.Reactive.Linq;
using Newtonsoft.Json;
using StackExchange.Redis;
using VivesBankApi.Rest.Clients.Dto;
using VivesBankApi.Rest.Clients.Exceptions;
using VivesBankApi.Rest.Clients.Repositories;
using VivesBankApi.Rest.Clients.Service;
using VivesBankApi.Rest.Product.BankAccounts.Repositories;
using VivesBankApi.Rest.Product.CreditCard.Dto;
using VivesBankApi.Rest.Product.CreditCard.Exceptions;
using VivesBankApi.Rest.Product.CreditCard.Generators;
using VivesBankApi.Rest.Product.CreditCard.Mappers;
using VivesBankApi.Rest.Products.BankAccounts.Exceptions;
using VivesBankApi.Rest.Users.Exceptions;
using VivesBankApi.Rest.Users.Service;
using VivesBankApi.Utils.GenericStorage.JSON;


namespace VivesBankApi.Rest.Product.CreditCard.Service;

/// <summary>
/// Implementa los métodos definidos en la interfaz `ICreditCardService` para gestionar tarjetas de crédito.
/// Proporciona funcionalidades para crear, actualizar, eliminar y obtener tarjetas de crédito, tanto para clientes como para administradores.
/// </summary>
public class CreditCardService
    : GenericStorageJson<Models.CreditCard>, ICreditCardService
{
    private readonly ICreditCardRepository _creditCardRepository;
    private readonly ILogger<CreditCardService> _logger;
    private readonly ICvcGenerator _cvcGenerator;
    private readonly IExpirationDateGenerator _expirationDateGenerator;
    private readonly INumberGenerator _numberGenerator;
    private readonly IAccountsRepository _accountsRepository;
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserService _userService;
    private readonly IClientRepository _clientRepository;
    private readonly IDatabase _cache;
    
    /// <summary>
    /// Constructor de `CreditCardService`.
    /// </summary>
    /// <param name="creditCardRepository">Repositorio para las tarjetas de crédito.</param>
    /// <param name="logger">Instancia de Logger para registrar eventos.</param>
    /// <param name="cvcGenerator">Generador de CVC.</param>
    /// <param name="expirationDateGenerator">Generador de fechas de expiración.</param>
    /// <param name="numberGenerator">Generador de números de tarjetas de crédito.</param>
    /// <param name="accountsRepository">Repositorio para las cuentas.</param>
    /// <param name="connectionMultiplexer">Multiplexor para la conexión a Redis.</param>
    /// <param name="httpContextAccessor">Accesor para el contexto HTTP.</param>
    /// <param name="userService">Servicio para gestionar usuarios.</param>
    /// <param name="clientRepository">Repositorio para los clientes.</param>
    public CreditCardService(ICreditCardRepository creditCardRepository, ILogger<CreditCardService> logger, ICvcGenerator cvcGenerator, IExpirationDateGenerator expirationDateGenerator, INumberGenerator numberGenerator, IAccountsRepository accountsRepository, IConnectionMultiplexer connectionMultiplexer, IHttpContextAccessor httpContextAccessor, IUserService userService, IClientRepository clientRepository) : base(logger)
    {
        _creditCardRepository = creditCardRepository;
        _logger = logger;
        _cvcGenerator = cvcGenerator;
        _expirationDateGenerator = expirationDateGenerator;
        _numberGenerator = numberGenerator;
        _accountsRepository = accountsRepository;
        _cache = connectionMultiplexer.GetDatabase();
        _httpContextAccessor = httpContextAccessor;
        _userService = userService;
        _clientRepository = clientRepository;
    }

    /// <summary>
    /// Obtiene todas las tarjetas de crédito disponibles.
    /// </summary>
    /// <returns>Una lista de tarjetas de crédito.</returns>
    public async Task<List<Models.CreditCard>> GetAll()
    {
        return await _creditCardRepository.GetAllAsync();
    }
    
    /// <summary>
    /// Obtiene las tarjetas de crédito disponibles para administradores con soporte para paginación, filtrado y ordenamiento.
    /// </summary>
    /// <param name="pageNumber">Número de página.</param>
    /// <param name="pageSize">Tamaño de página.</param>
    /// <param name="fullName">Nombre completo para filtrar las tarjetas.</param>
    /// <param name="isDeleted">Estado de eliminación.</param>
    /// <param name="direction">Dirección de ordenamiento ("asc" o "desc").</param>
    /// <returns>Una lista de respuestas para administradores.</returns>
    public async Task<List<CreditCardAdminResponse>> GetAllCreditCardAdminAsync(
        int pageNumber, int pageSize, string fullName, bool? isDeleted, string direction)
    {
        _logger.LogInformation("Getting all credit cards");
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Max(1, pageSize);
    
        var cards = await _creditCardRepository.GetAllCrediCardsPaginated(pageNumber, pageSize, fullName, isDeleted, direction);

        _logger.LogInformation($"Total cards retrieved: {cards.Count}");

        var mappedCards = new PagedList<CreditCardAdminResponse>(
            cards.Select(u => u.ToAdminResponse()),
            cards.TotalCount,
            cards.PageNumber,
            cards.PageSize
        );

        return mappedCards;
    }

    /// <summary>
    /// Obtiene las tarjetas de crédito asociadas al cliente autenticado.
    /// </summary>
    /// <returns>Una lista de respuestas para el cliente.</returns>
    public async Task<List<CreditCardClientResponse>> GetMyCreditCardsAsync()
    {
        var user = _httpContextAccessor.HttpContext!.User;
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userForFound = await _userService.GetUserByIdAsync(id);
        if (userForFound == null)
            throw new UserNotFoundException(id);
        var client = await _clientRepository.getByUserIdAsync(userForFound.Id);
        if (client == null)
            throw new ClientExceptions.ClientNotFoundException(userForFound.Id);
        var accounts = await _accountsRepository.getAccountByClientIdAsync(client.Id);
        if (accounts == null || !accounts.Any())
            return new List<CreditCardClientResponse>();
        var creditCards = new List<CreditCardClientResponse>();
        foreach (var account in accounts)
        {
            var creditCard = await _creditCardRepository.GetCardsByAccountId(account.Id);
            if (creditCard != null)
            {
                var creditCardResponse = creditCard.ToClientResponse();
                creditCards.Add(creditCardResponse);
            }
        }
        return creditCards;
    }
    
    /// <summary>
    /// Obtiene una tarjeta de crédito por su ID para administradores.
    /// </summary>
    /// <param name="id">ID de la tarjeta.</param>
    /// <returns>La tarjeta de crédito solicitada.</returns>
    public async Task<CreditCardAdminResponse?> GetCreditCardByIdAdminAsync(string id)
    {
        _logger.LogInformation($"Getting card with id {id}");
        var creditCard = await GetByIdAsync(id) ?? throw new CreditCardException.CreditCardNotFoundException(id);
        return creditCard.ToAdminResponse();
    }

    /// <summary>
    /// Obtiene una tarjeta de crédito por su número de tarjeta.
    /// </summary>
    /// <param name="cardNumber">Número de tarjeta de crédito.</param>
    /// <returns>La tarjeta de crédito solicitada.</returns>
    public async Task<CreditCardAdminResponse?> GetCreditCardByCardNumber(string cardNumber)
    {
        _logger.LogInformation($"Getting card with card number {cardNumber}");
        var creditCard = await _creditCardRepository.GetByCardNumber(cardNumber) 
                         ?? throw new CreditCardException.CreditCardNotFoundException(cardNumber);
        return creditCard.ToAdminResponse();
    }

    private async Task<Models.CreditCard?> GetByIdAsync(string id)
    {
        return await GetCreditCardFromCacheAsync(id)
               ?? await GetCreditCardFromRepositoryAndCacheAsync(id);
    }

    private async Task<Models.CreditCard?> GetCreditCardFromCacheAsync(string id)
    {
        var cacheCreditCard = await _cache.StringGetAsync(id);
        return !cacheCreditCard.IsNullOrEmpty ? JsonConvert.DeserializeObject<Models.CreditCard>(cacheCreditCard) : null;
    }

    private async Task<Models.CreditCard?> GetCreditCardFromRepositoryAndCacheAsync(string id)
    {
        var creditCard = await _creditCardRepository.GetByIdAsync(id);
        if (creditCard != null)
        {
            await CacheCreditCardAsync(id, creditCard);
        }
        return creditCard;
    }

    private async Task CacheCreditCardAsync(string id, Models.CreditCard creditCard)
    {
        await _cache.StringSetAsync(id, JsonConvert.SerializeObject(creditCard), TimeSpan.FromMinutes(10));
    }

    /// <summary>
    /// Crea una nueva tarjeta de crédito asociada a una cuenta.
    /// </summary>
    /// <param name="createRequest">Los datos necesarios para crear la tarjeta de crédito.</param>
    /// <returns>Una tarjeta de crédito creada.</returns>
    public async Task<CreditCardClientResponse> CreateCreditCardAsync(CreditCardRequest createRequest)
    {
        var user = _httpContextAccessor.HttpContext!.User;
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userForFound = await _userService.GetUserByIdAsync(id);
        if (userForFound == null)
            throw new UserNotFoundException(id);
        var client = await _clientRepository.getByUserIdAsync(userForFound.Id);
        if (client == null)
            throw new ClientExceptions.ClientNotFoundException(userForFound.Id);
        _logger.LogInformation($"Creating card: {createRequest}");
            
        var creditCardModel = CreditCardMapper.FromDtoRequest(createRequest);

        creditCardModel.CardNumber = _numberGenerator.GenerateCreditCardNumber();
        creditCardModel.ExpirationDate = _expirationDateGenerator.GenerateRandomDate();
        creditCardModel.Cvc = _cvcGenerator.Generate();
        
        var account = await _accountsRepository.getAccountByIbanAsync(createRequest.AccountIban);
        
        if (account == null)
        {
            _logger.LogError($"Account not found with iban {createRequest.AccountIban}");
            throw new AccountsExceptions.AccountNotFoundByIban(createRequest.AccountIban);
        }

        if (account.ClientId != client.Id)
        {
            _logger.LogError($"Client does not have access to account with iban {createRequest.AccountIban}");
            throw new ClientExceptions.ClientNotAllowedToAccessAccount(userForFound.Id, createRequest.AccountIban);
        }
        
        creditCardModel.AccountId = account.Id;
        _logger.LogInformation($"{creditCardModel}");
        await _creditCardRepository.AddAsync(creditCardModel);
            
        return creditCardModel.ToClientResponse();
    }

    
    /// <summary>
    /// Actualiza una tarjeta de crédito existente.
    /// </summary>
    /// <param name="cardNumber">Número de tarjeta de crédito a actualizar.</param>
    /// <param name="updateRequest">Los datos necesarios para la actualización.</param>
    /// <returns>La tarjeta de crédito actualizada.</returns>
    public async Task<CreditCardClientResponse> UpdateCreditCardAsync(string cardNumber, CreditCardUpdateRequest updateRequest)
    {
        _logger.LogInformation($"Updating card: {updateRequest} by Id: {cardNumber}");
        
        var myCreditCards = await GetMyCreditCardsAsync();
        var creditCard = await _creditCardRepository.GetByCardNumber(cardNumber);

        if (creditCard == null)
        {
            _logger.LogError($"Card not found with id {cardNumber}");
            throw new CreditCardException.CreditCardNotFoundByCardNumberException(cardNumber);
        }
        
        var cardToUpdate = myCreditCards.FirstOrDefault(card => card.AccountId == creditCard.AccountId);
        if (cardToUpdate == null)
        {
            _logger.LogError($"Card not found with account id {creditCard.AccountId}");
            throw new CreditCardException.CreditCardNotFoundByCardNumberException(cardNumber);
        }
        
        creditCard.Pin = updateRequest.Pin;
        creditCard.UpdatedAt = DateTime.UtcNow;
    
        await _creditCardRepository.UpdateAsync(creditCard);
        
        await _cache.KeyDeleteAsync(cardNumber);
        await _cache.StringSetAsync(cardNumber, JsonConvert.SerializeObject(updateRequest), TimeSpan.FromMinutes(10));

        return creditCard.ToClientResponse();
    }


     /// <summary>
    /// Elimina una tarjeta de crédito por su número.
    /// </summary>
    /// <param name="number">Número de tarjeta de crédito a eliminar.</param>
    /// <returns>Una tarea asincrónica.</returns>
    public async Task DeleteCreditCardAsync(string number)
    {
        _logger.LogInformation($"Removing card by number: {number} ");
        var myCreditCards = await GetMyCreditCardsAsync();
        var myCardToDelete = myCreditCards.FirstOrDefault(card => card.CardNumber == number)?? 
                             throw new CreditCardException.CreditCardNotFoundByCardNumberException(number);
        _cache.KeyDeleteAsync(myCardToDelete.Id);
        var deletedCard = await _creditCardRepository.GetByCardNumber(number) ?? throw new CreditCardException.CreditCardNotFoundByCardNumberException(number);
        deletedCard.IsDeleted = true;
        await _creditCardRepository.UpdateAsync(deletedCard);
    }

    /// <summary>
    /// Importa tarjetas de crédito desde un archivo JSON.
    /// </summary>
    /// <param name="fileStream">El archivo JSON con las tarjetas de crédito.</param>
    /// <returns>Un observable de las tarjetas de crédito importadas.</returns>
    public IObservable<Models.CreditCard> Import(IFormFile fileStream)
    {
        _logger.LogInformation("Starting to import credit cards from JSON file.");

        return Observable.Create<Models.CreditCard>(async (observer, cancellationToken) =>
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
                        var creditCard = serializer.Deserialize<Models.CreditCard>(jsonReader);
                        observer.OnNext(creditCard); 
                    }
                }

                observer.OnCompleted();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error while processing the JSON file: {ex.Message}");
                observer.OnError(ex);
            }
        });
    }

    /// <summary>
    /// Exporta una lista de tarjetas de crédito a un archivo JSON.
    /// </summary>
    /// <param name="entities">Lista de tarjetas de crédito a exportar.</param>
    /// <returns>Un `FileStream` con el archivo exportado.</returns>
    public async Task<FileStream> Export(List<Models.CreditCard> entities)
    {
        if (entities == null || !entities.Any())
        {
            throw new ArgumentException("Cannot export an empty list of credit cards.");
        }

        _logger.LogInformation("Exporting Credit Cards to JSON file...");

        var json = JsonConvert.SerializeObject(entities, Formatting.Indented); 
        var directoryPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "uploads", "Json");

        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var fileName = "CreditCardInSystem-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".json";
        var filePath = System.IO.Path.Combine(directoryPath, fileName);

        await File.WriteAllTextAsync(filePath, json);

        // Usamos "using" para asegurar que el archivo se cierre correctamente
        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return fileStream;
    }


}