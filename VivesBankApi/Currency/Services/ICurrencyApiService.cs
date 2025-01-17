using ApiFrankfurt.Configuration;
using Refit;

namespace ApiFranfurkt.Properties.Currency.Services;

/*
 * Interfaz para definir el cliente Refit utilizado para interactuar con la API de tasas de cambio de Frankfurter.
 */

public interface ICurrencyApiService    
{
    [Get("/latest")]
    Task<ApiResponse<ExchangeRateResponse>> GetLatestRatesAsync(
        [Refit.Query] string baseCurrency,
        [Refit.Query] string symbols
    );
}