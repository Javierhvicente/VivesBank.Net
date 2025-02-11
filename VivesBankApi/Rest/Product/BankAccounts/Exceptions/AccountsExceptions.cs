namespace VivesBankApi.Rest.Products.BankAccounts.Exceptions;

using System;

public class AccountsExceptions : Exception
{
    public AccountsExceptions(string message) : base(message) { }

    /// <summary>
    /// Exception thrown when an account is not found by its ID.
    /// </summary>
    public class AccountNotFoundException : AccountsExceptions
    {
        public AccountNotFoundException(string id)
            : base($"Account not found by id {id}") { }
    }

    /// <summary>
    /// Exception thrown when an account is not found by its IBAN.
    /// </summary>
    public class AccountNotFoundByIban : AccountsExceptions
    {
        public AccountNotFoundByIban(string iban)
            : base($"Account not found by IBAN {iban}") { }
    }

    /// <summary>
    /// Exception thrown when an account couldn't be created due to missing client or product.
    /// </summary>
    public class AccountNotCreatedException : AccountsExceptions
    {
        public AccountNotCreatedException()
            : base("Account couldn't be created, check that the client and the product exist") { }
    }

    /// <summary>
    /// Exception thrown when an IBAN couldn't be generated after several attempts.
    /// </summary>
    public class AccountIbanNotGeneratedException : AccountsExceptions
    {
        public AccountIbanNotGeneratedException()
            : base("IBAN couldn't be created after 1000 tries") { }
    }

    /// <summary>
    /// Exception thrown when an account cannot be deleted because the user doesn't own it.
    /// </summary>
    public class AccountNotDeletedException : AccountsExceptions
    {
        public AccountNotDeletedException(string iban)
            : base($"Account with IBAN {iban} could not be deleted because you don't own it") { }
    }

    /// <summary>
    /// Exception thrown when an account cannot be deleted because it has a balance.
    /// </summary>
    public class AccountWithBalanceException : AccountsExceptions
    {
        public AccountWithBalanceException(string iban)
            : base($"Account with IBAN {iban} cannot be deleted because it has money in it") { }
    }

    /// <summary>
    /// Exception thrown when the IBAN provided is unknown.
    /// </summary>
    public class AccountUnknownIban : AccountsExceptions
    {
        public AccountUnknownIban(string iban)
            : base($"Unknown IBAN {iban}") { }
    }

    /// <summary>
    /// Exception thrown when an account couldn't be updated due to missing client or product.
    /// </summary>
    public class AccountNotUpdatedException : AccountsExceptions
    {
        public AccountNotUpdatedException(string id)
            : base($"Account couldn't be updated, ID = {id}, check that the client and the product exist") { }
    }

    /// <summary>
    /// Exception thrown when an IBAN provided is not valid.
    /// </summary>
    public class AccountIbanNotValid : AccountsExceptions
    {
        public AccountIbanNotValid(string iban)
            : base($"Account IBAN number is not valid: {iban}") { }
    }
    
}
