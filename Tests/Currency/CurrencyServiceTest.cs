using System.Net;
using ApiFranfurkt.Properties.Currency.Exceptions;
using ApiFranfurkt.Properties.Currency.Services;
using ApiFrankfurt.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework.Legacy;
using Refit;
using ICurrencyApiService = ApiFranfurkt.Properties.Currency.Services.ICurrencyApiService;

namespace Test.Currency;

[TestFixture]
public class CurrencyApiServiceTests
{
    private readonly Mock<ICurrencyApiService> _mockApiClient;
    private readonly Mock<ILogger<CurrencyApiService>> _mockLogger;
    private readonly CurrencyApiService _service;
    
    public CurrencyApiServiceTests()
    {
        _mockApiClient = new Mock<ICurrencyApiService>();
        _mockLogger = new Mock<ILogger<CurrencyApiService>>();
        
        _service = new CurrencyApiService(_mockApiClient.Object, _mockLogger.Object);
    }
    
    // Caso ok.
    
    [Test]
    public async Task GetLatestRatesAsyncOk()
    {
        var expectedResponse = new ApiResponse<ExchangeRateResponse>(
            new HttpResponseMessage(HttpStatusCode.OK),
            new ExchangeRateResponse
            {
                Base = "USD",
                Rates = new Dictionary<string, double>
                {
                    { "EUR", 0.85 },
                    { "GBP", 0.75 }
                },
                Date = "2025-01-01"
            },
            new RefitSettings()
        );

        _mockApiClient
            .Setup(client => client.GetLatestRatesAsync("USD", "EUR,GBP"))
            .ReturnsAsync(expectedResponse);
        
        var response = await _service.GetLatestRatesAsync("USD", "EUR,GBP");

        ClassicAssert.True(response.IsSuccessStatusCode);
        ClassicAssert.NotNull(response.Content);
        ClassicAssert.AreEqual("USD", response.Content.Base); 
        ClassicAssert.AreEqual(0.85, response.Content.Rates["EUR"]);
        ClassicAssert.AreEqual(0.75, response.Content.Rates["GBP"]); 
    }
    
    // Respuesta vacia.
    
    [Test]
    public async Task GetLatestRatesAsyncApiEmpty()
    {
        var emptyRatesResponse = new ApiResponse<ExchangeRateResponse>(
            new HttpResponseMessage(HttpStatusCode.OK),
            new ExchangeRateResponse
            {
                Base = "USD",
                Rates = new Dictionary<string, double>(), 
                Date = "2025-01-01"
            },
            new RefitSettings()
        );

        _mockApiClient
            .Setup(client => client.GetLatestRatesAsync("USD", "EUR,GBP"))
            .ReturnsAsync(emptyRatesResponse);
        
        var response = await _service.GetLatestRatesAsync("USD", "EUR,GBP");

        ClassicAssert.NotNull(response.Content);
        ClassicAssert.AreEqual("USD", response.Content.Base);
        ClassicAssert.IsEmpty(response.Content.Rates); 
    }
    
    // Excepcion al llamar a la Api.
    
    [Test]
    public async Task GetLatestRatesAsyncIsUnsuccessful()
    {
        var errorResponse = new ApiResponse<ExchangeRateResponse>(
            new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError),
            null,
            new RefitSettings()
        );

        _mockApiClient
            .Setup(client => client.GetLatestRatesAsync("USD", "EUR"))
            .ReturnsAsync(errorResponse);
    
        var exception = Assert.ThrowsAsync<CurrencyConnectionException>(async () =>
        {
            await _service.GetLatestRatesAsync("USD", "EUR");
        });

        StringAssert.Contains("Error connecting to API", exception.Message);
        StringAssert.Contains("500", exception.Message);
    }
    
    // Excepcion error inesperado.
    
    [Test]
    public async Task GetLatestRatesAsyncUnexpectedException()
    {
        var baseCurrency = "USD";
        var symbols = "EUR";
        
        _mockApiClient
            .Setup(client => client.GetLatestRatesAsync(baseCurrency, symbols))
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        var exception = Assert.ThrowsAsync<CurrencyUnexpectedException>(async () =>
        {
            await _service.GetLatestRatesAsync(baseCurrency, symbols);
        });

        StringAssert.Contains("Error getting exchange rates.", exception.Message);

        Assert.That(exception.InnerException, Is.Not.Null);
        Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
        StringAssert.Contains("Unexpected error", exception.InnerException.Message);
    }
    
    // Obtener las ultimas tarifas
    
    [Test]
    public async Task GetLatestRatesAsyncSuccessOk()
    {
        var baseCurrency = "USD";
        var targetCurrencies = "EUR";
        var amount = "100";
        
        var exchangeRateResponse = new ExchangeRateResponse
        {
            Base = baseCurrency,
            Rates = new Dictionary<string, double>
            {
                { "EUR", 0.85 }
            },
            Date = "2025-01-01"
        };

        var apiResponse = new ApiResponse<ExchangeRateResponse>(
            new HttpResponseMessage(HttpStatusCode.OK),
            exchangeRateResponse,
            new RefitSettings()
        );

        _mockApiClient
            .Setup(client => client.GetLatestRatesAsync(baseCurrency, targetCurrencies))
            .ReturnsAsync(apiResponse);
        
        var result = await _service.GetLatestRatesAsync(baseCurrency, targetCurrencies, amount);
        
        ClassicAssert.NotNull(result);
        ClassicAssert.AreEqual(baseCurrency, result.Base);
        ClassicAssert.IsTrue(result.Rates.ContainsKey("EUR"));
        ClassicAssert.AreEqual(85, result.Rates["EUR"]); 
    }

    // Ultimas tarifas Api Vacia.
    
    [Test]
    public void GetLatestRatesAsyncEmptyApiResponse()
    {
        var baseCurrency = "USD";
        var targetCurrencies = "EUR";
        var amount = "100";

        var apiResponse = new ApiResponse<ExchangeRateResponse>(
            new HttpResponseMessage(HttpStatusCode.OK),
            null,
            new RefitSettings()
        );

        _mockApiClient
            .Setup(client => client.GetLatestRatesAsync(baseCurrency, targetCurrencies))
            .ReturnsAsync(apiResponse);

        
        var exception = Assert.ThrowsAsync<CurrencyEmptyResponseException>(async () =>
        {
            await _service.GetLatestRatesAsync(baseCurrency, targetCurrencies, amount);
        });

        ClassicAssert.NotNull(exception);
        Assert.That(exception, Is.TypeOf<CurrencyEmptyResponseException>());
    }

    // Obtener las ultimas tarifas con tarifas vacias.
    
    [Test]
    public void GetLatestRatesAsyncApiResponseWithEmptyRates()
    {
        var baseCurrency = "USD";
        var targetCurrencies = "EUR";
        var amount = "100";

        var exchangeRateResponse = new ExchangeRateResponse
        {
            Base = baseCurrency,
            Rates = new Dictionary<string, double>(),
            Date = "2025-01-01"
        };

        var apiResponse = new ApiResponse<ExchangeRateResponse>(
            new HttpResponseMessage(HttpStatusCode.OK),
            exchangeRateResponse,
            new RefitSettings()
        );

        _mockApiClient
            .Setup(client => client.GetLatestRatesAsync(baseCurrency, targetCurrencies))
            .ReturnsAsync(apiResponse);
        
        var exception = Assert.ThrowsAsync<CurrencyEmptyResponseException>(async () =>
        {
            await _service.GetLatestRatesAsync(baseCurrency, targetCurrencies, amount);
        });
        
        Assert.That(exception.Message, Is.EqualTo("API response is empty"));
    }
    
    // Convertir tipo de cambio error.
    
    [Test]
    public void ConvertExchangeRatesEmptyRatesError()
    {
        // Arrange
        var response = new ExchangeRateResponse
        {
            Rates = new Dictionary<string, double>(),
            Base = "USD",
            Date = "2030-01-01"
        };
        var amount = "100";

        List<string> loggedMessages = new List<string>();
        _mockLogger.Setup(m =>
            m.Log(
                It.Is<LogLevel>(l => l == LogLevel.Error), 
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()
            )
        ).Callback<LogLevel, EventId, object, Exception, Delegate>((level, eventId, state, exception, formatter) =>
        {
            loggedMessages.Add(state.ToString());
        });

        Assert.Throws<CurrencyEmptyResponseException>(() =>
            _service.ConvertExchangeRates(response, amount));

        Assert.That(loggedMessages.Count, Is.EqualTo(1));
        Assert.That(loggedMessages[0], Does.Contain("Exchange rates are empty or null."));
    }
}