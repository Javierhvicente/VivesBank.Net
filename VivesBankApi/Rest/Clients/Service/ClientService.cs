using System.Security.Claims;
using FluentFTP;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Newtonsoft.Json;
using StackExchange.Redis;
using VivesBankApi.Middleware.Jwt;
using VivesBankApi.Rest.Clients.Dto;
using VivesBankApi.Rest.Clients.Exceptions;
using VivesBankApi.Rest.Clients.Mappers;
using VivesBankApi.Rest.Clients.Models;
using VivesBankApi.Rest.Clients.Repositories;
using VivesBankApi.Rest.Clients.storage.Config;
using VivesBankApi.Rest.Clients.Storage.Service;
using VivesBankApi.Rest.Users.Dtos;
using VivesBankApi.Rest.Users.Exceptions;
using VivesBankApi.Rest.Users.Mapper;
using VivesBankApi.Rest.Users.Repository;
using VivesBankApi.Rest.Users.Service;
using VivesBankApi.Utils.GenericStorage.JSON;
using VivesBankApi.WebSocket.Model;
using VivesBankApi.WebSocket.Service;
using Path = System.IO.Path;
using Role = VivesBankApi.Rest.Users.Models.Role;
/*
 * Cambiar find by id que salte la excepcion directamente, mejorar el filtrado,
 * que no devuelva 500 el controlador, usar mapper para actualizar clientes
 * update cliente no borra la foto anterior
 */



/// <summary>
/// Service for managing client-related operations such as fetching, creating, updating, and deleting clients.
/// It interacts with repositories and external services such as user services, file storage, and JWT generation.
/// </summary>
/// <author>Raul Fernandez, Javier Hernandez, Samuel Cortes, Alvaro Herrero, German, Tomas</author>
/// <version>1.0</version>
namespace VivesBankApi.Rest.Clients.Service;

public class ClientService : GenericStorageJson<Client>, IClientService
    {
        private readonly ILogger _logger;
        private readonly IClientRepository _clientRepository;
        private readonly IUserService _userService;
        private readonly IUserRepository _userRepository;
        private readonly IDatabase _cache;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly FileStorageConfig _fileStorageConfig;
        private readonly IJwtGenerator _jwtGenerator;
        private readonly IWebsocketHandler _websocketHandler;
        private readonly FileStorageRemoteConfig _fileStorageRemoteConfig;
        private readonly IConfiguration _configuration;
        private readonly IFileStorageService _ftpService; 

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientService"/> class.
        /// </summary>
        /// <param name="logger">The logger service used for logging information.</param>
        /// <param name="userService">The user service used for user management operations.</param>
        /// <param name="clientRepository">The client repository for managing client data in the database.</param>
        /// <param name="connection">The Redis connection for caching client data.</param>
        /// <param name="httpContextAccessor">The HTTP context accessor to fetch user data from the context.</param>
        /// <param name="fileStorageConfig">The configuration for file storage operations.</param>
        /// <param name="websocketHandler">The WebSocket handler used for real-time communication.</param>
        /// <param name="jwtGenerator">The JWT generator for generating authentication tokens.</param>
        /// <param name="configuration">The application configuration for loading settings.</param>
        /// <param name="ftpService">The file storage service for handling FTP-based file storage operations.</param>
        public ClientService(
            ILogger<ClientService> logger,
            IUserService userService,
            IClientRepository clientRepository,
            IConnectionMultiplexer connection,
            IHttpContextAccessor httpContextAccessor,
            FileStorageConfig fileStorageConfig,
            IWebsocketHandler websocketHandler,
            IJwtGenerator jwtGenerator,
            IConfiguration configuration,
            IFileStorageService ftpService 
        ) : base(logger)
        {
            _jwtGenerator = jwtGenerator;
            _userService = userService; 
            _logger = logger;
            _clientRepository = clientRepository;
            _cache = connection.GetDatabase();
            _httpContextAccessor = httpContextAccessor;
            _fileStorageConfig = fileStorageConfig;
            _websocketHandler = websocketHandler;
            _fileStorageRemoteConfig = configuration.GetSection("FileStorageRemoteConfig").Get<FileStorageRemoteConfig>();
            _ftpService = ftpService; 
        }

        /// <summary>
        /// Retrieves all clients from the repository.
        /// </summary>
        /// <returns>A list of clients.</returns>
        public async Task<List<Client>> GetAll()
        {
            return await _clientRepository.GetAllAsync();
        }

        /// <summary>
        /// Retrieves a paginated list of clients with optional filters for name, deleted status, and sorting direction.
        /// </summary>
        /// <param name="pageNumber">The page number to retrieve.</param>
        /// <param name="pageSize">The number of clients per page.</param>
        /// <param name="fullName">The full name to filter the clients by.</param>
        /// <param name="isDeleted">The deleted status to filter clients by.</param>
        /// <param name="direction">The sorting direction (ascending or descending).</param>
        /// <returns>A paginated list of client responses.</returns>
        public async Task<PagedList<ClientResponse>> GetAllClientsAsync(
            int pageNumber, 
            int pageSize,
            string fullName,
            bool? isDeleted,
            string direction)
        {
            var clients = await _clientRepository.GetAllClientsPagedAsync(pageNumber, pageSize, fullName, isDeleted, direction);
            var mappedClients = new PagedList<ClientResponse>(
                clients.Select(u => u.ToResponse()),
                clients.TotalCount,
                clients.PageNumber,
                clients.PageSize
            );
            return mappedClients;
        }

        /// <summary>
        /// Retrieves the client data associated with the currently authenticated user.
        /// </summary>
        /// <returns>The client response for the authenticated user.</returns>
        public async Task<ClientResponse> GettingMyClientData()
        {
            var user = _httpContextAccessor.HttpContext!.User;
            var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userForFound = await _userService.GetUserByIdAsync(id);
            if (userForFound == null)
                throw new UserNotFoundException(id);
            var client = await _clientRepository.getByUserIdAsync(userForFound.Id);
            if (client == null)
                throw new ClientExceptions.ClientNotFoundException(userForFound.Id);
            return client.ToResponse();
        }

        /// <summary>
        /// Retrieves a client by their unique ID.
        /// </summary>
        /// <param name="id">The client ID to search for.</param>
        /// <returns>The client response matching the ID.</returns>
        public async Task<ClientResponse> GetClientByIdAsync(string id)
        {
            _logger.LogInformation($"Getting Client by id {id}");
            var res = await GetByIdAsync(id) ?? throw new ClientExceptions.ClientNotFoundException(id);
            return res.ToResponse();
        }

        /// <summary>
        /// Retrieves a client by their associated user ID.
        /// </summary>
        /// <param name="userId">The user ID associated with the client.</param>
        /// <returns>The client response matching the user ID.</returns>
        public async Task<ClientResponse> GetClientByUserIdAsync(string userId)
        {
            _logger.LogInformation($"Getting client by user id {userId}");
            var res = await _clientRepository.getByUserIdAsync(userId) ?? throw new ClientExceptions.ClientNotFoundException(userId);
            return res.ToResponse();
        }

        /// <summary>
        /// Creates a new client associated with the authenticated user.
        /// </summary>
        /// <param name="request">The client creation request.</param>
        /// <returns>A JWT token for the created user.</returns>
        public async Task<String> CreateClientAsync(ClientRequest request)
        {
            var user = _httpContextAccessor.HttpContext!.User;
            var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userForFound = await _userService.GetUserByIdAsync(id);
        
            if (userForFound == null)
                throw new UserNotFoundException(id);
        
            var existingClient = await _clientRepository.getByUserIdAsync(id);
            if (existingClient != null)
                throw new ClientExceptions.ClientAlreadyExistsException(id);
            
            var userUpdate = new UserUpdateRequest
            {
                Role = Role.Client.ToString(),
            };
        
            var client = request.FromDtoRequest();
            client.UserId = id;
            client.Photo = "defaultProfile.png";
            client.PhotoDni = "defaultDni.png";

            
            await _userService.UpdateUserAsync(id, userUpdate);
            
            await _clientRepository.AddAsync(client);
            
            var updatedUser = await _userService.GetUserByIdAsync(id);
            _logger.LogDebug($"Updating user for client role: {updatedUser.Role}");
            return _jwtGenerator.GenerateJwtToken(updatedUser.ToUser());
        }

        /// <summary>
        /// Updates an existing client with the provided information.
        /// </summary>
        /// <param name="id">The ID of the client to update.</param>
        /// <param name="request">The client update request.</param>
        /// <returns>The updated client response.</returns>
        public async Task<ClientResponse> UpdateClientAsync(string id, ClientUpdateRequest request)
        {
            _logger.LogInformation($"Updating client with id {id}");
            var clientToUpdate = await GetByIdAsync(id) ??  throw new ClientExceptions.ClientNotFoundException(id);
            clientToUpdate.Adress = request.Address;
            clientToUpdate.FullName = request.FullName;
            clientToUpdate.UpdatedAt = DateTime.UtcNow;
            await _clientRepository.UpdateAsync(clientToUpdate);
            await _cache.KeyDeleteAsync(id); // Invalidating the cached client
            return clientToUpdate.ToResponse();
        }

        /// <summary>
        /// Updates the current client's data.
        /// </summary>
        /// <param name="request">The client update request.</param>
        /// <returns>The updated client response.</returns>
        public async Task<ClientResponse> UpdateMeAsync(ClientUpdateRequest request)
        {
            var user = _httpContextAccessor.HttpContext!.User;
            var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userForFound = await _userService.GetUserByIdAsync(id);
            _logger.LogInformation($"Updating client with id {id}");
            var clientToUpdate = await _clientRepository.getByUserIdAsync(id) ??  throw new ClientExceptions.ClientNotFoundException(id);
            clientToUpdate.Adress = request.Address;
            clientToUpdate.FullName = request.FullName;
            clientToUpdate.UpdatedAt = DateTime.UtcNow;
            await _clientRepository.UpdateAsync(clientToUpdate);
            await _cache.KeyDeleteAsync(id);
            await EnviarNotificacionUpdateAsync(clientToUpdate.ToResponse());
            return clientToUpdate.ToResponse();
        }

        /// <summary>
        /// Marks a client as deleted in the system.
        /// </summary>
        /// <param name="id">The ID of the client to mark as deleted.</param>
        public async Task LogicDeleteClientAsync(string id)
        {
            _logger.LogInformation($"Setting Client with id {id} to deleted");
            var clientToDelete = await _clientRepository.GetByIdAsync(id)?? throw new ClientExceptions.ClientNotFoundException(id);
            clientToDelete.IsDeleted = true;
            var userToDelete = await _userService.GetUserByIdAsync(clientToDelete.UserId);
            var userUpdate = new UserUpdateRequest
            {
                Role = Role.Revoked.ToString(),
                IsDeleted = false
            };
            await _userService.UpdateUserAsync(clientToDelete.UserId, userUpdate);
            await _clientRepository.UpdateAsync(clientToDelete);
        }

        /// <summary>
        /// Deletes the currently authenticated user and their associated client data.
        /// </summary>
        public async Task DeleteMe()
        {
            var user = _httpContextAccessor.HttpContext!.User;
            var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userForFound = await _userService.GetUserByIdAsync(id);
        
            if (userForFound == null)
                throw new UserNotFoundException(id);
        
            var existingClient = await _clientRepository.getByUserIdAsync(id);
            if (existingClient == null)
                throw new ClientExceptions.ClientNotFoundException(id);
            
            existingClient.IsDeleted = true;
            await _clientRepository.UpdateAsync(existingClient);
            await _userService.DeleteUserAsync(id, logically: true);
        }

        /// <summary>
        /// Retrieves a client from the cache or database by their ID.
        /// </summary>
        /// <param name="id">The client ID.</param>
        /// <returns>The client object.</returns>
        private async Task<Client?> GetByIdAsync(string id)
        {
            // Try to get from cache first
            var cachedClient = await _cache.StringGetAsync(id);
            if (!cachedClient.IsNullOrEmpty)
            {
                return JsonConvert.DeserializeObject<Client>(cachedClient);
            }

            Client? client = await _clientRepository.GetByIdAsync(id);
            if (client != null)
            {
                await _cache.StringSetAsync(id, JsonConvert.SerializeObject(client), TimeSpan.FromMinutes(10));
                return client;
            }
            return null;
        }

        /// <summary>
        /// Saves a file to the file system.
        /// </summary>
        /// <param name="file">The file to save.</param>
        /// <param name="baseFileName">The base file name.</param>
        /// <returns>The file name of the saved file.</returns>
        public async Task<string> SaveFileAsync(IFormFile file, string baseFileName)
        {
            _logger.LogInformation($"Saving file for user with base name: {baseFileName}");

            if (file.Length > _fileStorageConfig.MaxFileSize)
            {
                throw new FileStorageExceptions("El tamaño del fichero excede del máximo permitido");
            }

            var fileExtension = Path.GetExtension(file.FileName);
            if (!_fileStorageConfig.AllowedFileTypes.Contains(fileExtension))
            {
                throw new FileStorageExceptions("Tipo de fichero no permitido");
            }

            var uploadPath = Path.Combine(_fileStorageConfig.UploadDirectory);
            if (!Directory.Exists(uploadPath))
            {
                Directory.CreateDirectory(uploadPath);
            }

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
            var fullFileName = $"{baseFileName}-{timestamp}{fileExtension}";
            var filePath = Path.Combine(uploadPath, fullFileName);

            await using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            _logger.LogInformation($"File saved: {fullFileName}");
            return fullFileName;
        }
        
        /// <summary>
        /// Updates the client's DNI photo with the provided file.
        /// </summary>
        /// <param name="clientId">The client ID.</param>
        /// <param name="file">The file to save.</param>
        /// <returns>The file name of the updated DNI photo.</returns>
        public async Task<string> UpdateClientDniPhotoAsync(string clientId, IFormFile file)
        {
            _logger.LogInformation($"Updating DNI photo for client with ID: {clientId}");

            if (file == null || file.Length == 0)
            {
                throw new FileNotFoundException("No file was provided or the file is empty.");
            }

            var client = await _clientRepository.GetByIdAsync(clientId);
            if (client == null)
            {
                throw new ClientExceptions.ClientNotFoundException($"Client with ID {clientId} not found.");
            }

            var user = await _userService.GetUserByIdAsync(client.UserId);
            if (user == null)
            {
                throw new UserNotFoundException(client.UserId);
            }

            var newFileName = await SaveFileAsync(file, $"DNI-{user.Dni}");

            if (client.PhotoDni != "default.png")
            {
                await DeleteFileAsync(client.PhotoDni);
            }

            client.PhotoDni = newFileName;
            client.UpdatedAt = DateTime.UtcNow;

            await _clientRepository.UpdateAsync(client);

            _logger.LogInformation($"DNI photo updated successfully for client with ID: {clientId}");
            return newFileName;
        }

    
        /// <summary>
        /// Updates the profile photo of a client.
        /// </summary>
        /// <param name="clientId">The ID of the client whose profile photo is being updated.</param>
        /// <param name="file">The file (photo) to be uploaded.</param>
        /// <returns>A string representing the new file name of the updated profile photo.</returns>
        /// <exception cref="FileNotFoundException">Thrown if no file is provided or the file is empty.</exception>
        /// <exception cref="ClientExceptions.ClientNotFoundException">Thrown if the client with the provided ID is not found.</exception>
        /// <exception cref="UserNotFoundException">Thrown if the user associated with the client is not found.</exception>
        /// <remarks>
        /// This method updates the profile photo for a specific client and saves the photo with a unique name based on their DNI.
        /// If the client already has a photo, the old one is deleted.
        /// </remarks>
        public async Task<string> UpdateClientPhotoAsync(string clientId, IFormFile file)
        {
            _logger.LogInformation($"Updating profile photo for client with ID: {clientId}");

            if (file == null || file.Length == 0)
            {
                throw new FileNotFoundException("No file was provided or the file is empty.");
            }

            var client = await _clientRepository.GetByIdAsync(clientId);
            if (client == null)
            {
                throw new ClientExceptions.ClientNotFoundException($"Client with ID {clientId} not found.");
            }

            var user = await _userService.GetUserByIdAsync(client.UserId);
            if (user == null)
            {
                throw new UserNotFoundException(client.UserId);
            }

            var newFileName = await SaveFileAsync(file, $"PROFILE-{user.Dni}");

            if (client.Photo != "defaultId.png")
            {
                await DeleteFileAsync(client.Photo);
            }

            client.Photo = newFileName;
            client.UpdatedAt = DateTime.UtcNow;

            await _clientRepository.UpdateAsync(client);

            _logger.LogInformation($"Profile photo updated successfully for client with ID: {clientId}");
            return newFileName;
        }

        /// <summary>
        /// Deletes a file from the file storage system.
        /// </summary>
        /// <param name="fileName">The name of the file to delete.</param>
        /// <returns>A boolean indicating whether the file was successfully deleted.</returns>
        /// <exception cref="FileNotFoundException">Thrown if the file does not exist in the storage.</exception>
        /// <remarks>
        /// This method deletes the file from the local storage system. If the file is not found, it returns false.
        /// </remarks>
        public async Task<bool> DeleteFileAsync(string fileName)
        {
            _logger.LogInformation($"Deleting file: {fileName}");
            try
            {
                var filePath = Path.Combine(_fileStorageConfig.UploadDirectory, fileName);
                
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning($"File not found: {filePath}");
                    return false;
                }
                
                File.Delete(filePath);
                _logger.LogInformation($"File deleted: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file");
                throw;
            }
        }

        /// <summary>
        /// Retrieves a file from the local file storage system.
        /// </summary>
        /// <param name="fileName">The name of the file to retrieve.</param>
        /// <returns>A FileStream for the requested file.</returns>
        /// <exception cref="FileNotFoundException">Thrown if the file does not exist in the storage.</exception>
        /// <remarks>
        /// This method returns a FileStream for the requested file, allowing it to be streamed to the client.
        /// </remarks>
        public async Task<FileStream> GetFileAsync(string fileName)
        {
            _logger.LogInformation($"Getting file: {fileName}");
            try
            {
                var filePath = Path.Combine(_fileStorageConfig.UploadDirectory, fileName);
                
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning($"File not found: {filePath}");
                    throw new FileNotFoundException($"File not found: {fileName}");
                }
                
                _logger.LogInformation($"File found: {filePath}");
                return new FileStream(filePath, FileMode.Open, FileAccess.Read);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting file");
                throw;
            }
        }

        /// <summary>
        /// Retrieves the profile photo for the current user.
        /// </summary>
        /// <returns>A FileStream representing the current user's profile photo.</returns>
        /// <exception cref="FileNotFoundException">Thrown if the profile photo does not exist in the storage.</exception>
        /// <exception cref="ClientExceptions.ClientNotFoundException">Thrown if the client associated with the current user is not found.</exception>
        /// <remarks>
        /// This method retrieves the profile photo for the current user. If the file does not exist, it will throw a FileNotFoundException.
        /// </remarks>
        public async Task<FileStream> GettingMyProfilePhotoAsync()
        {
            _logger.LogInformation("Getting my profile photo.");

            var user = _httpContextAccessor.HttpContext!.User;
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var client = await _clientRepository.getByUserIdAsync(userId);
            if (client == null)
            {
                throw new ClientExceptions.ClientNotFoundException(userId);
            }

            string filePath = Path.Combine(_fileStorageConfig.UploadDirectory, client.Photo);

            if (!File.Exists(filePath))
            {
                _logger.LogWarning($"Profile photo not found: {filePath}");
                throw new FileNotFoundException($"Profile photo file not found: {client.Photo}");
            }

            _logger.LogInformation($"Profile photo found: {filePath}");
            return new FileStream(filePath, FileMode.Open, FileAccess.Read);
        }

        /// <summary>
        /// Updates the profile photo for the current user.
        /// </summary>
        /// <param name="file">The new profile photo to upload.</param>
        /// <returns>A string representing the new file name of the updated profile photo.</returns>
        /// <exception cref="ClientExceptions.ClientNotFoundException">Thrown if the client associated with the current user is not found.</exception>
        /// <exception cref="UserNotFoundException">Thrown if the user associated with the current client is not found.</exception>
        /// <remarks>
        /// This method allows the current user to update their profile photo. The new photo is uploaded and the file name is updated in the client's data.
        /// </remarks>
        public async Task<string> UpdateMyProfilePhotoAsync(IFormFile file)
        {
            _logger.LogInformation("Updating profile photo for current user.");

            var user = _httpContextAccessor.HttpContext!.User;
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var client = await _clientRepository.getByUserIdAsync(userId);
            if (client == null)
            {
                throw new ClientExceptions.ClientNotFoundException($"Client with user ID {userId} not found.");
            }

            var userData = await _userService.GetUserByIdAsync(client.UserId);
            if (userData == null)
            {
                throw new UserNotFoundException(client.UserId);
            }

            var newFileName = await SaveFileAsync(file, $"PROFILE-{userData.Dni}");

            client.Photo = newFileName;
            client.UpdatedAt = DateTime.UtcNow;

            await _clientRepository.UpdateAsync(client);

            _logger.LogInformation($"Profile photo updated successfully for user ID: {userId} (DNI: {userData.Dni})");
            return newFileName;
        }

        /// <summary>
        /// Saves a file to an FTP server.
        /// </summary>
        /// <param name="file">The file to be uploaded.</param>
        /// <param name="fileName">The desired name of the file on the FTP server.</param>
        /// <returns>The file name of the uploaded file.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the FTP configuration is invalid or incomplete.</exception>
        /// <exception cref="FileStorageExceptions">Thrown if there is an error uploading the file to the FTP server.</exception>
        /// <remarks>
        /// This method uploads a file to a configured FTP server. The file is uploaded with a specific file name.
        /// If the FTP configuration is incomplete or invalid, an exception is thrown.
        /// </remarks>
        public async Task<string> SaveFileToFtpAsync(IFormFile file, string fileName)
        {
            try
            {
                if (_fileStorageRemoteConfig == null ||
                    string.IsNullOrEmpty(_fileStorageRemoteConfig.FtpHost) ||
                    string.IsNullOrEmpty(_fileStorageRemoteConfig.FtpUsername) ||
                    string.IsNullOrEmpty(_fileStorageRemoteConfig.FtpPassword) ||
                    string.IsNullOrEmpty(_fileStorageRemoteConfig.FtpDirectory))
                {
                    throw new InvalidOperationException("Configuración de almacenamiento remoto inválida o incompleta.");
                }

                if (!_fileStorageRemoteConfig.AllowedFileTypes.Contains(Path.GetExtension(file.FileName).ToLower()) ||
                    file.Length > _fileStorageRemoteConfig.MaxFileSize)
                {
                    throw new InvalidOperationException("Archivo no permitido por tipo o tamaño.");
                }

                using (var client = new AsyncFtpClient(
                           _fileStorageRemoteConfig.FtpHost,
                           _fileStorageRemoteConfig.FtpUsername,
                           _fileStorageRemoteConfig.FtpPassword))
                {
                    client.Config.ConnectTimeout = 30000;
                    client.Config.ReadTimeout = 30000;
                    client.Config.DataConnectionConnectTimeout = 30000;
                    client.Config.DataConnectionReadTimeout = 30000;

                    await client.Connect();

                    if (!await client.DirectoryExists(_fileStorageRemoteConfig.FtpDirectory))
                    {
                        await client.CreateDirectory(_fileStorageRemoteConfig.FtpDirectory);
                    }

                    string fullPath = $"{_fileStorageRemoteConfig.FtpDirectory}/{fileName}";

                    _logger.LogInformation($"Esta es la ruta donde se guarda el ftp: {fullPath}");

                    using (var stream = file.OpenReadStream())
                    {
                        if (await client.UploadStream(stream, fullPath, FtpRemoteExists.Overwrite, true) != FtpStatus.Success)
                        {
                            throw new Exception("Error al subir el archivo al servidor FTP.");
                        }
                    }

                    await client.Disconnect();
                    return fileName;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar el archivo en el servidor FTP.");
                throw;
            }
        }


        /// <summary>
        /// Actualiza la foto del DNI para un cliente específico.
        /// </summary>
        /// <remarks>
        /// Este método permite que un administrador o usuario con permisos actualice la foto del DNI
        /// de un cliente específico, subiendo el archivo al servidor FTP y actualizando la base de datos.
        /// </remarks>
        /// <param name="clientId">El identificador único del cliente cuyo DNI se actualizará.</param>
        /// <param name="file">El archivo de imagen del DNI que se va a cargar.</param>
        /// <returns>El nombre del archivo guardado en el servidor FTP.</returns>
        /// <response code="200">La foto del DNI fue actualizada correctamente.</response>
        /// <response code="400">Archivo no válido o error al procesar la carga del archivo.</response>
        /// <response code="404">El cliente o el usuario no se encuentran en el sistema.</response>
        public async Task<string> UpdateClientPhotoDniAsync(string clientId, IFormFile file)
        {
            try
            {
                using (var client = new AsyncFtpClient(_fileStorageRemoteConfig.FtpHost,
                           _fileStorageRemoteConfig.FtpUsername, _fileStorageRemoteConfig.FtpPassword))
                {
                    client.Config.ConnectTimeout = 30000;
                    client.Config.ReadTimeout = 30000;
                    client.Config.DataConnectionConnectTimeout = 30000;
                    client.Config.DataConnectionReadTimeout = 30000;
                    await client.Connect();
                }
               

                // Buscar usuario asociado al cliente
                var clientToFound = await _clientRepository.GetByIdAsync(clientId)?? throw new ClientExceptions.ClientNotFoundException(clientId);
                var user = await _userRepository.GetByIdAsync(clientToFound.UserId);
                if (user == null)
                {
                    _logger.LogError($"Usuario con UserId {clientToFound.UserId} no encontrado.");
                    throw new UserNotFoundException($"El usuario con UserId {clientToFound.UserId} no existe.");
                }

                // Generación del nombre del archivo con DNI, extensión y timestamp
                string dni = user.Dni;
                string extension = Path.GetExtension(file.FileName).ToLower();
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
                string fileName = $"DNI-{dni}-{timestamp}{extension}";

                // Guardar el archivo en el servidor FTP y obtener el nombre del archivo guardado
                string savedFileName = await SaveFileToFtpAsync(file, fileName);

                // Actualizar la propiedad PhotoDni del cliente con la ruta del archivo guardado
                clientToFound.PhotoDni = savedFileName;

                // Actualizar el cliente en la base de datos
                await _clientRepository.UpdateAsync(clientToFound);

                // Retornar el nombre del archivo guardado
                return savedFileName;
            }
            catch (Exception ex)
            {
                // Log error si ocurre alguna excepción
                _logger.LogError(ex, "Error al actualizar la foto del cliente");
                throw;
            }
        }


        /// <summary>
        /// Actualiza la foto del DNI para el usuario actual.
        /// </summary>
        /// <remarks>
        /// Este método permite que el usuario suba una nueva foto del DNI,
        /// la cual se almacenará en un servidor FTP y se actualizará en la base de datos del cliente.
        /// </remarks>
        /// <param name="file">El archivo de imagen del DNI que se va a cargar.</param>
        /// <returns>El nombre del archivo guardado en el servidor FTP.</returns>
        /// <response code="200">La foto del DNI se actualizó correctamente.</response>
        /// <response code="400">Archivo no válido o error al procesar la carga del archivo.</response>
        /// <response code="404">El cliente o el usuario no se encuentran en el sistema.</response>
        public async Task<string> UpdateMyPhotoDniAsync(IFormFile file)
        {
            using (var client = new AsyncFtpClient(_fileStorageRemoteConfig.FtpHost,
                       _fileStorageRemoteConfig.FtpUsername, _fileStorageRemoteConfig.FtpPassword))
            {
                client.Config.ConnectTimeout = 30000;
                client.Config.ReadTimeout = 30000;
                client.Config.DataConnectionConnectTimeout = 30000;
                client.Config.DataConnectionReadTimeout = 30000;
            
                await client.Connect();
            }
            

            var user = _httpContextAccessor.HttpContext!.User;
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var clientToFound = await _clientRepository.getByUserIdAsync(userId);
            if (clientToFound == null)
            {
                _logger.LogError($"Client with user ID {userId} not found.");
                throw new ClientExceptions.ClientNotFoundException($"Client with user ID {userId} not found.");
            }

            var userData = await _userService.GetUserByIdAsync(clientToFound.UserId);
            if (userData == null)
            {
                _logger.LogError($"User with ID {clientToFound.UserId} not found.");
                throw new UserNotFoundException(clientToFound.UserId);
            }

            string dni = userData.Dni;
            string extension = Path.GetExtension(file.FileName).ToLower();
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd");
            string fileName = $"DNI-{dni}-{timestamp}{extension}";

            _logger.LogInformation($"Generated file name for DNI: {fileName}");

            string savedFileName = await SaveFileToFtpAsync(file, fileName);

            clientToFound.PhotoDni = savedFileName;
            clientToFound.UpdatedAt = DateTime.UtcNow;

            await _clientRepository.UpdateAsync(clientToFound);

            _logger.LogInformation($"DNI photo updated successfully for user ID: {userId} (DNI: {dni})");

            return savedFileName;
        }

        /// <summary>
        /// Obtiene la foto del DNI para el usuario actual desde el servidor FTP.
        /// </summary>
        /// <remarks>
        /// Este método permite al usuario obtener su foto del DNI almacenada en el servidor FTP.
        /// Si no existe, se retornará la foto predeterminada.
        /// </remarks>
        /// <returns>El archivo de la foto del DNI del usuario.</returns>
        /// <response code="200">La foto del DNI fue obtenida con éxito.</response>
        /// <response code="404">No se pudo encontrar la foto del DNI para el usuario.</response>
        public async Task<FileStream> GettingMyDniPhotoFromFtpAsync()
        {
            _logger.LogInformation("Getting my DNI photo from FTP.");

            var user = _httpContextAccessor.HttpContext!.User;
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var client = await _clientRepository.getByUserIdAsync(userId);
            if (client == null)
            {
                throw new ClientExceptions.ClientNotFoundException(userId);
            }

            string fileName = client.PhotoDni;

            if (string.IsNullOrEmpty(fileName) || fileName == "defaultDni.png")
            {
                fileName = "defaultDni.png";
            }

            try
            {
                _logger.LogInformation($"Resolving file path for DNI on FTP: {fileName}");
                var fileStream = await GetFileFromFtpAsync(fileName);

                _logger.LogInformation($"Returning DNI photo from FTP: {fileName}");
                return fileStream;
            }
            catch (FileNotFoundException)
            {
                _logger.LogWarning($"DNI photo not found on FTP for user: {userId}");
                throw new FileNotFoundException("DNI photo not found on FTP.");
            }
        }
        
        public async Task<bool> DeleteFileFromFtpAsync(string fileName)
        {
            _logger.LogInformation("Deleting file from FTP: {fileName}", fileName);

            using (var client = new AsyncFtpClient(_fileStorageRemoteConfig.FtpHost, _fileStorageRemoteConfig.FtpUsername, _fileStorageRemoteConfig.FtpPassword))
            {
                client.Config.ConnectTimeout = 30000;
                client.Config.ReadTimeout = 30000;
                client.Config.DataConnectionConnectTimeout = 30000;
                client.Config.DataConnectionReadTimeout = 30000;
                await client.Connect();

                string remotePath = $"{_fileStorageRemoteConfig.FtpDirectory}/{fileName}";
                _logger.LogInformation($"FTP file path resolved to: {remotePath}");

                if (!await client.FileExists(remotePath))
                {
                    _logger.LogWarning($"File not found on FTP server: {remotePath}");
                    return false;
                }

                _logger.LogInformation($"File found on FTP server. Deleting: {remotePath}");
                await client.DeleteFile(remotePath);
                _logger.LogInformation($"File successfully deleted: {remotePath}");

                await client.Disconnect();
                return true;
            }
        }

        public async Task<FileStream> GetFileFromFtpAsync(string fileName)
        {
            _logger.LogInformation($"Getting file from FTP: {fileName}");

            try
            {
                using (var client = new AsyncFtpClient(_fileStorageRemoteConfig.FtpHost,
                           _fileStorageRemoteConfig.FtpUsername, _fileStorageRemoteConfig.FtpPassword))
                {
                    client.Config.ConnectTimeout = 30000;
                    client.Config.ReadTimeout = 30000;
                    client.Config.DataConnectionConnectTimeout = 30000;
                    client.Config.DataConnectionReadTimeout = 30000;
                    await client.Connect();

                    string remotePath = $"{_fileStorageRemoteConfig.FtpDirectory}/{fileName}";
                    _logger.LogInformation($"FTP file path resolved to: {remotePath}");

                    if (!await client.FileExists(remotePath))
                    {
                        _logger.LogWarning($"File not found on FTP server: {remotePath}");
                        throw new FileStorageExceptions($"File not found: {fileName}");
                    }

                    _logger.LogInformation($"File found on FTP server: {remotePath}");

                    string tempFilePath = Path.GetTempFileName();

                    await client.DownloadFile(tempFilePath, remotePath, FtpLocalExists.Overwrite);

                    _logger.LogInformation($"File downloaded to temporary path: {tempFilePath}");

                    return new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Delete);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting file from FTP");
                throw;
            }
        }


        /// <summary>
        /// Enviar una notificación de actualización a un usuario.
        /// </summary>
        /// <param name="t">El objeto que contiene los datos de la notificación.</param>
        /// <typeparam name="T">El tipo de los datos de la notificación.</typeparam>
        /// <returns>Un Task que indica la ejecución asíncrona de la operación.</returns>
        /// <response code="200">La notificación fue enviada correctamente.</response>
        /// <response code="404">El usuario no se encuentra en el sistema.</response>
        public async Task EnviarNotificacionUpdateAsync<T>(T t)
        {
            var user = _httpContextAccessor.HttpContext!.User;
            var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userForFound = await _userService.GetUserByIdAsync(id);
            if (userForFound == null)
                throw new UserNotFoundException(id);
            var notificacion = new Notification<T>
            {
                Type = Notification<T>.NotificationType.Update.ToString(),
                CreatedAt = DateTime.Now,
                Data = t
            };
            await _websocketHandler.NotifyUserAsync(userForFound.Id, notificacion);
        }

        /// <summary>
        /// Exporta los datos de un cliente a un archivo JSON.
        /// </summary>
        /// <param name="client">El cliente cuyos datos se exportarán.</param>
        /// <returns>Un archivo JSON que contiene los datos del cliente.</returns>
        /// <response code="200">Los datos fueron exportados correctamente a un archivo JSON.</response>
        /// <response code="500">Hubo un error al crear el archivo.</response>
        public async Task<FileStream> ExportOnlyMeData(Client client)
        {
            try
            {
                _logger.LogInformation($"Exporting Client to a JSON file");
                var json = JsonConvert.SerializeObject(client, Formatting.Indented);
                var directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads", "Json");

                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                var fileName = "Client_WithId_" + client.Id + "_" + "InSystem-" +
                               DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".json";
                var filePath = Path.Combine(directoryPath, fileName);

                await File.WriteAllTextAsync(filePath, json);

                _logger.LogInformation($"File written to: {filePath}");

                return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception ex)
            {
                //Cambiar a BadRequest Error de serializacion
                throw new ClientExceptions.ClientAlreadyExistsException("Error al transformar el cliente en PDF");
            }
        }

        public async Task<ClientResponse> DeleteMeData()
        {
            _logger.LogInformation("Deleting data of the current user.");
            var user = _httpContextAccessor.HttpContext!.User;
            var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userForFound = await _userService.GetUserByIdAsync(id);
            if (userForFound == null)
                throw new UserNotFoundException(id);
            var client = await _clientRepository.getByUserIdAsync(userForFound.Id);
            if (client == null)
                throw new ClientExceptions.ClientNotFoundException(userForFound.Id);
            client = DeleteData(client);
            return client.ToResponse();
        }

        private Client DeleteData(Client cliente)
        {
            cliente.FullName = "null";
            cliente.Adress = "null";

            if (cliente.Photo != "defaultProfile.png")
            { 
                DeleteFileAsync(cliente.Photo);
                cliente.Photo = "defaultProfile.png";
            }

            if (cliente.PhotoDni != "defaultDni.png")
            {
                DeleteFileFromFtpAsync(cliente.PhotoDni);
                cliente.PhotoDni = "defaultDni.png";
            }
            cliente.UpdatedAt = DateTime.UtcNow;
            cliente.IsDeleted = true;

            return cliente;
        }
    }
    