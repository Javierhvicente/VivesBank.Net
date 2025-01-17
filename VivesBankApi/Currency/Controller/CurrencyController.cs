using ApiFranfurkt.Properties.Currency.Services;
using Microsoft.AspNetCore.Mvc;

namespace ApiFranfurkt.Properties.Currency.Controller;

[ApiController]
[Route("api/v1/currency")]
public class CurrencyController : ControllerBase
{
    private readonly ICurrencyApiService _currencyApiService;

    public CurrencyController(ICurrencyApiService currencyApiService)
    {
        _currencyApiService = currencyApiService;
    }

    [HttpGet("latest")]
    public async Task<IActionResult> GetLatestRates(
        [FromQuery] string amount = "1",
        [FromQuery] string baseCurrency = "EUR",
        [FromQuery] string? symbols = null)
    {
        var targetCurrencies = symbols ?? string.Empty;
        
        var apiResponse = await _currencyApiService.GetLatestRatesAsync(baseCurrency, amount);
        
        var exchangeRateResponse = apiResponse.Content;
        
        return Ok(exchangeRateResponse);
    }

}