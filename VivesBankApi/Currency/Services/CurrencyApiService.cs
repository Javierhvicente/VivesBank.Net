using ApiFranfurkt.Properties.Currency.Exceptions;
using ApiFrankfurt.Configuration;

namespace ApiFranfurkt.Properties.Currency.Services;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Refit;

public class CurrencyApiService : ICurrencyApiService
{
    private readonly ICurrencyApiService _apiClient;
    private readonly ILogger<CurrencyApiService> _logger;

    // Constructor que inicializa el cliente API usando un HttpClient y un logger.
    public CurrencyApiService(ICurrencyApiService apiClient, ILogger<CurrencyApiService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // Método para obtener las tasas de cambio más recientes desde la API.
    public async Task<ApiResponse<ExchangeRateResponse>> GetLatestRatesAsync(string baseCurrency, string symbols)
    {
        try
        {
            _logger.LogInformation($"Fetching exchange rates for BaseCurrency: {baseCurrency}, Symbols: {symbols}");

            // Hacer la llamada a la API externa
            var response = await _apiClient.GetLatestRatesAsync(baseCurrency, symbols);

            // Verificar si la llamada fue exitosa
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"API returned non-success status code: {response.StatusCode}");
                if (response.Content != null)
                {
                    // Convertimos el contenido de la respuesta a String si es necesario para el log
                    var errorContent = response.Content.ToString();
                    _logger.LogError($"API Response Content: {errorContent}");
                }
                throw new CurrencyConnectionException(
                    $"Error connecting to API. Status code: {(int)response.StatusCode} ({response.StatusCode})"
                );
            }

            // Validación del contenido de la respuesta.
            if (response.Content == null)
            {
                _logger.LogError("API response content is null.");
                throw new CurrencyEmptyResponseException();
            }

            _logger.LogInformation("Successfully fetched exchange rates.");
            return response;
        }
        catch (CurrencyConnectionException ex)
        {
            _logger.LogError(ex, "Connection exception occurred while fetching exchange rates.");
            throw;
        }
        catch (CurrencyEmptyResponseException ex)
        {
            _logger.LogError(ex, "Empty response exception occurred.");
            throw;
        }
        catch (Exception ex)
        {
            // Capturamos más detalles del error para entender mejor el problema
            _logger.LogError(ex, "Unexpected exception occurred while fetching exchange rates.");
            throw new CurrencyUnexpectedException("Error getting exchange rates.", ex);
        }
    }


    // Método para obtener las tasas de cambio más recientes con la cantidad especificada.
    public async Task<ExchangeRateResponse> GetLatestRatesAsync(string baseCurrency, string targetCurrencies, string amount)
    {
        try
        {
            _logger.LogInformation($"Processing exchange rates for Amount: {amount}, BaseCurrency: {baseCurrency}, TargetCurrencies: {targetCurrencies}");

            // Llamada para obtener las tasas más recientes.
            var response = await GetLatestRatesAsync(baseCurrency, targetCurrencies);

            // Validamos la respuesta y su contenido.
            if (!response.IsSuccessStatusCode || response.Content == null)
            { 
                _logger.LogError("API response is either unsuccessful or content is null."); 
                throw new CurrencyEmptyResponseException();
            }

            // Convertimos las tasas de cambio según la cantidad.
            ConvertExchangeRates(response.Content, amount);

            _logger.LogInformation("Exchange rates successfully processed.");
            return response.Content;
        }
        catch (CurrencyEmptyResponseException ex)
        {
            _logger.LogError(ex, "Empty response exception occurred during processing.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception occurred while processing exchange rates.");
            throw new CurrencyUnexpectedException("Error processing exchange rates.", ex);
        }
    }

    // Método para convertir las tasas de cambio según la cantidad especificada.
    public void ConvertExchangeRates(ExchangeRateResponse response, string amount)
    {
        _logger.LogInformation($"Converting exchange rates with Amount: {amount}");

        if (!double.TryParse(amount, out var parsedAmount))
        {
            _logger.LogError($"Invalid amount provided: {amount}");
            throw new CurrencyUnexpectedException("The amount provided is not valid.");
        }

        if (response.Rates == null || response.Rates.Count == 0)
        {
            _logger.LogError("Exchange rates are empty or null.");
            throw new CurrencyEmptyResponseException();
        }

        var convertedRates = new Dictionary<string, double>();

        foreach (var rate in response.Rates)
        {
            convertedRates[rate.Key] = rate.Value * parsedAmount;
        }
        
        response.Rates = convertedRates;
        _logger.LogInformation("Exchange rates successfully converted.");
    }
}