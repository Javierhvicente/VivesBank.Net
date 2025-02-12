namespace VivesBankApi.Rest.Clients.Exceptions
{
    /// <summary>
    /// Clase base abstracta para manejar las excepciones relacionadas con los clientes.
    /// </summary>
    /// <author>Raul Fernandez, Javier Hernandez, Samuel Cortes, Alvaro Herrero, German, Tomas</author>
    /// <version>1.0</version>
    public abstract class ClientExceptions : Exception
    {
        /// <summary>
        /// Constructor para inicializar la excepción con un mensaje personalizado.
        /// </summary>
        /// <param name="message">El mensaje de error que describe la excepción.</param>
        protected ClientExceptions(string message) : base(message)
        {
        }

        /// <summary>
        /// Excepción que indica que no se ha encontrado un cliente por su ID.
        /// </summary>
        public class ClientNotFoundException : ClientExceptions
        {
            /// <summary>
            /// Constructor que crea una instancia de la excepción con el ID del cliente.
            /// </summary>
            /// <param name="id">El ID del cliente que no se ha encontrado.</param>
            public ClientNotFoundException(string id) 
                : base($"Client not found by id {id}")
            {
            }
        }

        /// <summary>
        /// Excepción que indica que ya existe un cliente con el mismo ID de usuario.
        /// </summary>
        public class ClientAlreadyExistsException : ClientExceptions
        {
            /// <summary>
            /// Constructor que crea una instancia de la excepción con el ID de usuario.
            /// </summary>
            /// <param name="id">El ID de usuario con el que ya existe un cliente.</param>
            public ClientAlreadyExistsException(string id)
                : base($"A client already exists with this user id {id}")
            {
            }
        }

        /// <summary>
        /// Excepción que indica que un cliente no está autorizado a acceder a una cuenta.
        /// </summary>
        public class ClientNotAllowedToAccessAccount : ClientExceptions
        {
            /// <summary>
            /// Constructor que crea una instancia de la excepción con el ID del cliente y el IBAN de la cuenta.
            /// </summary>
            /// <param name="id">El ID del cliente que no tiene acceso.</param>
            /// <param name="Iban">El IBAN de la cuenta a la que no se puede acceder.</param>
            public ClientNotAllowedToAccessAccount(string id, string Iban)
                : base($"The client with id {id} is not allowed to access the account with iban {Iban}")
            {
            }
        }
        
        public class ClientUnprocessable : ClientExceptions
        {
            public ClientUnprocessable(string id)
                : base($"It has occurred an error while exporting the client's data with id {id}")
            {
            }
        }
        
        public class ClientStorageException : ClientExceptions
        {
            public ClientStorageException(string id)
                : base($"It has occurred an error while working the client with id {id}, try later")
            {
            }
        }
    }
}
