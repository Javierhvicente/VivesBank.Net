namespace VivesBankApi.Rest.Context.Exceptions;

public class ContextExceptions:Exception
{
    public ContextExceptions(string message) : base(message) { }
    
    /// <summary>
    /// Exception thrown when Http context is null.
    /// </summary>
    public class HttContextNull : ContextExceptions
    {
        public HttContextNull()
            : base("HttpContext is null") { }
    }
    
    /// <summary>
    /// Exception thrown when Http context user id is missing.
    /// </summary>
    public class UserIdMissing : ContextExceptions
    {
        public UserIdMissing()
            : base("User ID claim is missing") { }
    }
    
}