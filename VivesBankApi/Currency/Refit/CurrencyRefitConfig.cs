
using Refit;

namespace ApiFrankfurt.Configuration;

public static class CurrencyRefitConfig
{
    public static void AddFrankFurterServices(this IServiceCollection services, IConfiguration configuration)
    {
        string baseUrl = configuration["Frankfurter:BaseUrl"] ?? "https://api.frankfurter.app";
        
        if (string.IsNullOrEmpty(baseUrl))
        {
            throw new InvalidOperationException("Frankfurter BaseUrl is not configured properly.");
        }
        
        Console.WriteLine($"Base URL from configuration: {baseUrl}");
        
        services.AddRefitClient<ICurrencyApiService>()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(baseUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
            });
    }
}
    
public interface ICurrencyApiService
{
    [Get("/api/v1/currency/latest")]
    Task<ApiResponse<ExchangeRateResponse>> GetLatestRatesAsync(
        [Refit.Query] string baseCurrency,
        [Refit.Query] string symbols,
        [Refit.Query] string amount
    );
}   

public class ExchangeRateResponse
{
    public string Base { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public Dictionary<string, double> Rates { get; set; } = new Dictionary<string, double>();
}
