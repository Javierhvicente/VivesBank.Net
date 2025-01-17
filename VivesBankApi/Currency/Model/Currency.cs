using Newtonsoft.Json;
using Swashbuckle.AspNetCore.Annotations;

public class Currency
{
    [SwaggerSchema(Description = "Cantidad para la que se calculó la tasa de cambio")]
    public required string Amount { get; set; } = "0"; // Valor predeterminado

    [SwaggerSchema(Description = "Moneda base utilizada en la conversión")]
    public required string Base { get; set; } = "USD"; // Moneda base por defecto

    [SwaggerSchema(Description = "Fecha en la que se consultaron las tasas de cambio")]
    public required string Date { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd"); // Fecha actual por defecto

    [JsonProperty("rates")]
    [SwaggerSchema(Description = "Mapa de tasas de cambio por moneda")]
    public required Dictionary<string, double> ExchangeRates { get; set; } = new(); // Diccionario vacío como valor predeterminado
}