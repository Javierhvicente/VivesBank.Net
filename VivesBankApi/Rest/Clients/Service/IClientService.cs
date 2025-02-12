using VivesBankApi.Rest.Clients.Dto;
using VivesBankApi.Rest.Clients.Models;
using VivesBankApi.Utils.GenericStorage.JSON;

namespace VivesBankApi.Rest.Clients.Service
{
    public interface IClientService : IGenericStorageJson<Client>
    {
        /// <summary>
        /// Obtiene todos los clientes en formato paginado.
        /// </summary>
        /// <param name="pageNumber">Número de página.</param>
        /// <param name="pageSize">Número de elementos por página.</param>
        /// <param name="fullName">Nombre del cliente para realizar búsqueda.</param>
        /// <param name="isDeleted">Indica si se deben incluir clientes eliminados o no.</param>
        /// <param name="direction">Dirección de la ordenación (ascendente o descendente).</param>
        /// <returns>Una lista paginada de respuestas de clientes.</returns>
        public Task<PagedList<ClientResponse>> GetAllClientsAsync(
            int pageNumber,
            int pageSize,
            string fullName,
            bool? isDeleted,
            string direction);

        /// <summary>
        /// Obtiene todos los clientes sin paginación.
        /// </summary>
        /// <returns>Una lista de todos los clientes.</returns>
        Task<List<Client>> GetAll();

        /// <summary>
        /// Obtiene un cliente por su ID.
        /// </summary>
        /// <param name="id">El ID del cliente.</param>
        /// <returns>Una respuesta de cliente correspondiente al ID.</returns>
        Task<ClientResponse> GetClientByIdAsync(string id);

        /// <summary>
        /// Obtiene un cliente por su UserId.
        /// </summary>
        /// <param name="userId">El UserId del cliente.</param>
        /// <returns>Una respuesta de cliente correspondiente al UserId.</returns>
        Task<ClientResponse> GetClientByUserIdAsync(string userId);

        /// <summary>
        /// Obtiene los datos de un cliente autenticado (mis datos).
        /// </summary>
        /// <returns>Una respuesta con los datos del cliente autenticado.</returns>
        Task<ClientResponse> GettingMyClientData();

        /// <summary>
        /// Crea un nuevo cliente.
        /// </summary>
        /// <param name="request">La solicitud para crear el cliente.</param>
        /// <returns>El ID del cliente creado.</returns>
        Task<String> CreateClientAsync(ClientRequest request);

        /// <summary>
        /// Actualiza los datos de un cliente por su ID.
        /// </summary>
        /// <param name="id">El ID del cliente a actualizar.</param>
        /// <param name="request">La solicitud con los nuevos datos.</param>
        /// <returns>Una respuesta con los nuevos datos del cliente.</returns>
        Task<ClientResponse> UpdateClientAsync(string id, ClientUpdateRequest request);

        /// <summary>
        /// Actualiza los datos del cliente autenticado.
        /// </summary>
        /// <param name="request">La solicitud con los nuevos datos.</param>
        /// <returns>Una respuesta con los nuevos datos del cliente.</returns>
        Task<ClientResponse> UpdateMeAsync(ClientUpdateRequest request);

        /// <summary>
        /// Realiza un borrado lógico de un cliente.
        /// </summary>
        /// <param name="id">El ID del cliente a eliminar.</param>
        Task LogicDeleteClientAsync(string id);
        
        /// <summary>
        /// Elimina los datos del cliente autenticado.
        /// </summary>
        Task DeleteMe();

        // Funciones para almacenamiento local de archivos

        /// <summary>
        /// Guarda un archivo en el almacenamiento local.
        /// </summary>
        /// <param name="file">El archivo que se va a guardar.</param>
        /// <param name="baseFileName">El nombre base del archivo.</param>
        /// <returns>El nombre del archivo guardado.</returns>
        Task<String> SaveFileAsync(IFormFile file, string baseFileName);

        /// <summary>
        /// Actualiza la foto de perfil de un cliente.
        /// </summary>
        /// <param name="clientId">El ID del cliente cuya foto se va a actualizar.</param>
        /// <param name="file">El nuevo archivo de foto.</param>
        /// <returns>La URL de la foto actualizada.</returns>
        Task<string> UpdateClientPhotoAsync(string clientId, IFormFile file);

        /// <summary>
        /// Obtiene un archivo desde el almacenamiento local.
        /// </summary>
        /// <param name="fileName">El nombre del archivo que se va a obtener.</param>
        /// <returns>El archivo solicitado.</returns>
        Task<FileStream> GetFileAsync(string fileName);

        /// <summary>
        /// Obtiene la foto de perfil del cliente autenticado.
        /// </summary>
        /// <returns>El archivo de la foto de perfil.</returns>
        Task<FileStream> GettingMyProfilePhotoAsync();

        /// <summary>
        /// Actualiza la foto de perfil del cliente autenticado.
        /// </summary>
        /// <param name="file">El nuevo archivo de foto.</param>
        /// <returns>La URL de la foto actualizada.</returns>
        Task<string> UpdateMyProfilePhotoAsync(IFormFile file);
        
        // Funciones para exportar datos en formato JSON

        /// <summary>
        /// Exporta los datos del cliente autenticado en formato JSON.
        /// </summary>
        /// <param name="user">El cliente cuyos datos serán exportados.</param>
        /// <returns>El flujo de archivo con los datos exportados.</returns>
        Task<FileStream> ExportOnlyMeData(Client user);

        // Funciones para almacenamiento remoto FTP

        /// <summary>
        /// Guarda un archivo en un servidor FTP.
        /// </summary>
        /// <param name="file">El archivo que se va a guardar.</param>
        /// <param name="dni">El DNI asociado al archivo.</param>
        /// <returns>El nombre del archivo guardado en el FTP.</returns>
        Task<string> SaveFileToFtpAsync(IFormFile file, string dni);

        /// <summary>
        /// Obtiene un archivo desde un servidor FTP.
        /// </summary>
        /// <param name="fileName">El nombre del archivo que se va a obtener.</param>
        /// <returns>El archivo solicitado desde el servidor FTP.</returns>
        Task<FileStream> GetFileFromFtpAsync(string fileName);

        /// <summary>
        /// Actualiza la foto de DNI de un cliente en el servidor FTP.
        /// </summary>
        /// <param name="userId">El ID del usuario cuya foto de DNI se va a actualizar.</param>
        /// <param name="file">El nuevo archivo de foto de DNI.</param>
        /// <returns>La URL de la foto de DNI actualizada.</returns>
        Task<string> UpdateClientPhotoDniAsync(string userId, IFormFile file);

        /// <summary>
        /// Actualiza la foto de DNI del cliente autenticado en el servidor FTP.
        /// </summary>
        /// <param name="file">El nuevo archivo de foto de DNI.</param>
        /// <returns>La URL de la foto de DNI actualizada.</returns>
        Task<string> UpdateMyPhotoDniAsync(IFormFile file);

        /// <summary>
        /// Obtiene la foto de DNI del cliente autenticado desde el servidor FTP.
        /// </summary>
        /// <returns>El archivo de la foto de DNI.</returns>
        Task<FileStream> GettingMyDniPhotoFromFtpAsync();

        Task<ClientResponse> DeleteMeData();
    }
}
