namespace ApiFranfurkt.Properties.Currency.Exceptions;

public abstract class CurrencyException : System.Exception
{
    protected CurrencyException(string message) : base(message) { }

    protected CurrencyException(string message, Exception innerException)
        : base(message, innerException) { }

    public override string ToString()
    {
        return Message ?? "ERROR";
    }
}

/**
 * Clase de excepción para manejar respuestas vacías.
 */
public class CurrencyEmptyResponseException : CurrencyException
{
    public CurrencyEmptyResponseException() : base("API response is empty") { }
}

/**
 * Clase de excepción para manejar los errores de conexión.
 */
public class CurrencyConnectionException : CurrencyException
{
    public CurrencyConnectionException() : base("API connection failed") { }

    public CurrencyConnectionException(string message) : base(message) { }
}

/**
 * Clase de excepción para manejar los errores inesperados.
 */
public class CurrencyUnexpectedException : CurrencyException
{
    public CurrencyUnexpectedException() : base("API unexpected error") { }

    public CurrencyUnexpectedException(string message) : base(message) { }

    public CurrencyUnexpectedException(string message, Exception innerException)
        : base(message, innerException) { }
}