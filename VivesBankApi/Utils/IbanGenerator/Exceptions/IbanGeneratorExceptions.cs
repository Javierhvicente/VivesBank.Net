namespace VivesBankApi.Utils.IbanGenerator.Exceptions;

public class IbanGeneratorExceptions:Exception
{
    public IbanGeneratorExceptions(string message) : base(message) { }
    
    /// <summary>
    /// Exception thrown when IbanGenerator fail.
    /// </summary>
    public class IbanGeneratorFail : IbanGeneratorExceptions
    {
        public IbanGeneratorFail()
            : base("IBAN generation failed") { }
    }
}