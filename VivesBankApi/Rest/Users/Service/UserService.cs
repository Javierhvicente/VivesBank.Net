using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HotChocolate.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using StackExchange.Redis;
using VivesBankApi.Database;
using VivesBankApi.Rest.Users.Dtos;
using VivesBankApi.Rest.Users.Exceptions;
using VivesBankApi.Rest.Users.Mapper;
using VivesBankApi.Rest.Users.Models;
using VivesBankApi.Rest.Users.Repository;
using VivesBankApi.Rest.Users.Validator;
using VivesBankApi.Utils.GenericStorage.JSON;
using VivesBankApi.WebSocket.Model;
using VivesBankApi.WebSocket.Service;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Role = VivesBankApi.Rest.Users.Models.Role;

namespace VivesBankApi.Rest.Users.Service;

/// <summary>
/// Servicio que maneja las operaciones relacionadas con los usuarios, incluyendo creación, actualización, obtención y eliminación.
/// </summary>
public class UserService : GenericStorageJson<User>, IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IDatabase _cache;
    private readonly IMemoryCache _memoryCache;
    private readonly AuthJwtConfig _authConfig;
    private readonly ILogger _logger;
    private readonly IWebsocketHandler _webSocketHandler;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string CacheKeyPrefix = "User:";

    /// <summary>
    /// Constructor para inyectar las dependencias necesarias.
    /// </summary>
    /// <param name="logger">El logger para registrar eventos.</param>
    /// <param name="userRepository">El repositorio de usuarios para acceder a la base de datos.</param>
    /// <param name="authConfig">La configuración de autenticación JWT.</param>
    /// <param name="connectionMultiplexer">El multiplexer de conexiones de Redis para la caché.</param>
    /// <param name="webSocketHandler">El manejador de WebSockets para notificar eventos.</param>
    /// <param name="httpContextAccessor">El accesorio para obtener el contexto HTTP.</param>
    public UserService(
        ILogger<UserService> logger,
        IUserRepository userRepository,
        AuthJwtConfig authConfig,
        IConnectionMultiplexer connectionMultiplexer,
        IWebsocketHandler webSocketHandler,
        IHttpContextAccessor httpContextAccessor, IMemoryCache memoryCache
    ) : base(logger)
    {
        _logger = logger;
        _memoryCache = memoryCache;
        _authConfig = authConfig;
        _userRepository = userRepository;
        _cache = connectionMultiplexer.GetDatabase();
        _webSocketHandler = webSocketHandler;
        _httpContextAccessor = httpContextAccessor;
    }
    
    /// <summary>
    /// Obtiene todos los usuarios sin paginación.
    /// </summary>
    /// <returns>Una lista de usuarios.</returns>
    public async Task<List<User>> GetAll()
    {
        return await _userRepository.GetAllAsync();
    }
    
    /// <summary>
    /// Obtiene una lista de usuarios de manera paginada, con filtros opcionales por rol y estado de eliminación.
    /// </summary>
    /// <param name="pageNumber">Número de la página para la paginación.</param>
    /// <param name="pageSize">Número de elementos por página.</param>
    /// <param name="role">Rol de los usuarios a filtrar.</param>
    /// <param name="isDeleted">Filtra los usuarios según si están eliminados lógicamente.</param>
    /// <param name="direction">Dirección de ordenamiento, puede ser 'asc' o 'desc'.</param>
    /// <returns>Una lista paginada de usuarios.</returns>
    public async Task<PagedList<UserResponse>> GetAllUsersAsync(
        int pageNumber, 
        int pageSize,
        string role,
        bool? isDeleted,
        string direction = "asc")
    {
        var users = await _userRepository.GetAllUsersPagedAsync(pageNumber, pageSize, role, isDeleted, direction);
        var mappedUsers = new PagedList<UserResponse>(
            users.Select(u => u.ToUserResponse()),
            users.TotalCount,
            users.PageNumber,
            users.PageSize
        );
        return mappedUsers;
    }
    
    /// <summary>
    /// Obtiene un usuario por su ID.
    /// </summary>
    /// <param name="id">El ID del usuario.</param>
    /// <returns>El usuario encontrado.</returns>
    /// <exception cref="UserNotFoundException">Si no se encuentra el usuario.</exception>
    public async Task<UserResponse> GetUserByIdAsync(string id)
    {
       var user = await GetByIdAsync(id) ?? throw new UserNotFoundException(id);
       return user.ToUserResponse();
    }

    /// <summary>
    /// Añade un nuevo usuario al sistema.
    /// </summary>
    /// <param name="userRequest">Los detalles del usuario a crear.</param>
    /// <returns>El usuario recién creado.</returns>
    /// <exception cref="InvalidDniException">Si el DNI no es válido.</exception>
    /// <exception cref="UserAlreadyExistsException">Si ya existe un usuario con el mismo DNI.</exception>
    public async Task<UserResponse> AddUserAsync(CreateUserRequest userRequest)
    {
        if (!UserValidator.ValidateDni(userRequest.Dni))
        {
            throw new  InvalidDniException(userRequest.Dni);
        }
        User newUser = userRequest.toUser();
        User? userWithTheSameUsername = await GetByUsernameAsync(userRequest.Dni);
        if (userWithTheSameUsername != null)
        {
            throw new UserAlreadyExistsException(userRequest.Dni);
        }
        await _userRepository.AddAsync(newUser);
        var notificacion = new Notification<UserResponse>
        {
            Type = Notification<UserResponse>.NotificationType.Create.ToString(),
            CreatedAt = DateTime.Now,
            Data = newUser.ToUserResponse()
        };
        var user = _httpContextAccessor.HttpContext!.User;
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await _webSocketHandler.NotifyUserAsync(id, notificacion);
        return newUser.ToUserResponse();
    }
    

    /// <summary>
    /// Obtiene un usuario por su nombre de usuario (DNI).
    /// </summary>
    /// <param name="username">El DNI del usuario.</param>
    /// <returns>El usuario encontrado.</returns>
    /// <exception cref="UserNotFoundException">Si no se encuentra el usuario.</exception>
    public async Task<UserResponse> GetUserByUsernameAsync(string username)
    {
        var user = await GetByUsernameAsync(username) ?? throw new UserNotFoundException(username);
        return user.ToUserResponse();
    }

    /// <summary>
    /// Obtiene los detalles del usuario autenticado.
    /// </summary>
    /// <returns>Los datos del usuario autenticado.</returns>
    public async Task<UserResponse> GettingMyUserData()
    {
        var user = _httpContextAccessor.HttpContext!.User;
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var res = await GetByIdAsync(id);
        return res.ToUserResponse();
    }

    /// <summary>
    /// Actualiza los datos de un usuario existente.
    /// </summary>
    /// <param name="id">El ID del usuario a actualizar.</param>
    /// <param name="user">Los nuevos datos del usuario.</param>
    /// <returns>El usuario actualizado.</returns>
    /// <exception cref="InvalidDniException">Si el DNI proporcionado no es válido.</exception>
    /// <exception cref="UserNotFoundException">Si no se encuentra el usuario.</exception>
    /// <exception cref="UserAlreadyExistsException">Si el DNI ya está en uso por otro usuario.</exception>
    public async Task<UserResponse> UpdateUserAsync(string id, UserUpdateRequest user)
    {
        if (user.Dni != null && !UserValidator.ValidateDni(user.Dni))
        {
            throw new InvalidDniException(user.Dni);
        }

        // Obtener el usuario a actualizar
        User? userToUpdate = await GetByIdAsync(id) ?? throw new UserNotFoundException(id);
    
        if (user.Dni != null)
        {
            // Verificar si hay otro usuario con el mismo DNI
            User? userWithTheSameUsername = await GetByUsernameAsync(userToUpdate.Dni);
            if (userWithTheSameUsername != null && userWithTheSameUsername.Id != id)
            {
                throw new UserAlreadyExistsException(user.Dni);
            }
        }

        // Actualizar los detalles del usuario
        User updatedUser = user.UpdateUserFromInput(userToUpdate);
        await _userRepository.UpdateAsync(updatedUser);

        // Eliminar la entrada antigua de la caché (tanto en Redis como en la memoria)
        await _cache.KeyDeleteAsync(id); 
        await _cache.KeyDeleteAsync("users:" + userToUpdate.Dni.Trim().ToUpper()); 
    
        _memoryCache.Remove(id); 
        _memoryCache.Remove("users:" + userToUpdate.Dni.Trim().ToUpper()); 
        
        await _cache.StringSetAsync(id, JsonConvert.SerializeObject(updatedUser), TimeSpan.FromMinutes(10)); 
        _memoryCache.Set(id, updatedUser, TimeSpan.FromMinutes(10)); 
        _memoryCache.Set("users:" + updatedUser.Dni.Trim().ToUpper(), updatedUser, TimeSpan.FromMinutes(10)); 

        return updatedUser.ToUserResponse();
    }


    /// <summary>
    /// Elimina un usuario del sistema. Puede ser una eliminación lógica o física.
    /// </summary>
    /// <param name="id">El ID del usuario a eliminar.</param>
    /// <param name="logically">Indica si la eliminación es lógica (true) o física (false).</param>
    public async Task DeleteUserAsync(string id, bool logically)
    {
        // Obtener el usuario a eliminar
        User? userToUpdate = await _userRepository.GetByIdAsync(id);
        if (userToUpdate == null)
        {
            throw new UserNotFoundException(id);
        }

        if (logically)
        {
            // Realizar eliminación lógica
            userToUpdate.IsDeleted = true;
            userToUpdate.Role = Role.Revoked;
            await _userRepository.UpdateAsync(userToUpdate);

            // Eliminar de la caché (tanto en Redis como en memoria)
            await _cache.KeyDeleteAsync(id); 
            await _cache.KeyDeleteAsync("users:" + userToUpdate.Dni.Trim().ToUpper()); 
            _memoryCache.Remove(id); 
            _memoryCache.Remove("users:" + userToUpdate.Dni.Trim().ToUpper()); 
        }
        else
        {
            // Realizar eliminación física
            await _cache.KeyDeleteAsync(id);
            await _cache.KeyDeleteAsync("users:" + userToUpdate.Dni.Trim().ToUpper()); 
            _memoryCache.Remove(id); 
            _memoryCache.Remove("users:" + userToUpdate.Dni.Trim().ToUpper()); 
            await _userRepository.DeleteAsync(id); 
        }
    }

    
    // Métodos privados de acceso a caché y base de datos...

    /// <summary>
    /// Intenta obtener un usuario por su ID desde la caché o la base de datos.
    /// </summary>
    /// <param name="id">El ID del usuario.</param>
    /// <returns>El usuario si se encuentra, de lo contrario null.</returns>
    private async Task<User?> GetByIdAsync(string id)
    {
        var cacheKey = CacheKeyPrefix + id;

        // Intentar obtener desde la memoria caché
        if (_memoryCache.TryGetValue(cacheKey, out User? memoryCacheUser))
        {
            _logger.LogInformation("Usuario obtenido desde la memoria caché");
            return memoryCacheUser;
        }

        // Intentar obtener desde Redis
        var cachedUser = await _cache.StringGetAsync(cacheKey);
        if (cachedUser.HasValue)
        {
            var userFromRedis = JsonConvert.DeserializeObject<User>(cachedUser);
            _memoryCache.Set(cacheKey, userFromRedis, TimeSpan.FromMinutes(5)); 
            return userFromRedis;
        }

        // Si no está en la caché, obtener desde la base de datos
        User? user = await _userRepository.GetByIdAsync(id);
        if (user != null)
        {
            // Guardar en Redis
            await _cache.StringSetAsync(cacheKey, JsonConvert.SerializeObject(user), TimeSpan.FromMinutes(10));
            // Guardar en memoria
            _memoryCache.Set(cacheKey, user, TimeSpan.FromMinutes(5));
        }
        return user;
    }
    
   /// <summary>
    /// Obtiene un usuario por su nombre de usuario (DNI) desde la caché o la base de datos.
    /// </summary>
    /// <param name="username">El DNI del usuario.</param>
    /// <returns>El usuario encontrado si existe, de lo contrario null.</returns>
    private async Task<User?> GetByUsernameAsync(string username)
    {
        var cacheKey = CacheKeyPrefix + username;

        // Intentar obtener desde la memoria caché
        if (_memoryCache.TryGetValue(cacheKey, out User? memoryCacheUser))
        {
            _logger.LogInformation("Usuario obtenido desde la memoria caché");
            return memoryCacheUser;
        }

        // Intentar obtener desde Redis
        var cachedUser = await _cache.StringGetAsync(cacheKey);
        if (!cachedUser.IsNullOrEmpty)
        {
            var userFromRedis = JsonConvert.DeserializeObject<User>(cachedUser);
            _memoryCache.Set(cacheKey, userFromRedis, TimeSpan.FromMinutes(5));
            return userFromRedis;
        }

        // Si no está en Redis, obtener de la base de datos
        User? user = await _userRepository.GetByUsernameAsync(username);
        if (user != null)
        {
            // Guardar en Redis
            await _cache.StringSetAsync(cacheKey, JsonConvert.SerializeObject(user), TimeSpan.FromMinutes(10));
            // Guardar en caché de memoria
            _memoryCache.Set(cacheKey, user, TimeSpan.FromMinutes(5));
        }
        return user;
    }

    /// <summary>
    /// Inicia sesión con un usuario proporcionando su DNI y contraseña.
    /// </summary>
    /// <param name="request">El detalle de la solicitud de inicio de sesión con DNI y contraseña.</param>
    /// <returns>El usuario autenticado si las credenciales son correctas, de lo contrario null.</returns>
    public async Task<User?> LoginUser(LoginRequest request)
    {
        var user = await _userRepository.GetByUsernameAsync(request.Dni);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.Password))
            return null;
        return user;
    }

    /// <summary>
    /// Actualiza la contraseña del usuario autenticado.
    /// </summary>
    /// <param name="request">Los detalles de la solicitud de actualización de contraseña.</param>
    /// <returns>El usuario con la contraseña actualizada.</returns>
    /// <exception cref="UserNotFoundException">Si no se encuentra el usuario.</exception>
    public async Task<User?> UpdateMyPassword(UpdatePasswordRequest request)
    {
        _logger.LogInformation("Updating my user profile");
        var user = _httpContextAccessor.HttpContext!.User;
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        User? userToUpdate = await GetByIdAsync(id) ?? throw new UserNotFoundException(id);

        // Actualizar la contraseña
        userToUpdate.Password = BCrypt.Net.BCrypt.HashPassword(request.Password);
        await _userRepository.UpdateAsync(userToUpdate);

        // Eliminar usuario de la caché
        var cacheKey = CacheKeyPrefix + userToUpdate.Dni;
        await _cache.KeyDeleteAsync(id);
        await _cache.KeyDeleteAsync("users:" + userToUpdate.Dni.Trim().ToUpper());
        _memoryCache.Remove(cacheKey);

        // Guardar la nueva versión del usuario en caché
        await _cache.StringSetAsync(id, JsonConvert.SerializeObject(userToUpdate), TimeSpan.FromMinutes(10));
        _memoryCache.Set(cacheKey, userToUpdate, TimeSpan.FromMinutes(5));

        return userToUpdate;
    }


    /// <summary>
    /// Elimina la cuenta del usuario autenticado de forma lógica.
    /// </summary>
    /// <returns>Una tarea asíncrona que elimina la cuenta del usuario.</returns>
    /// <exception cref="UserNotFoundException">Si no se encuentra el usuario.</exception>
    public async Task DeleteMeAsync()
    {
        _logger.LogInformation("Deleting my account");
        var user = _httpContextAccessor.HttpContext!.User;
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        User? userToDelete = await GetByIdAsync(id) ?? throw new UserNotFoundException(id);

        // Eliminar de la caché antes de la eliminación
        var cacheKey = CacheKeyPrefix + userToDelete.Dni;
        await _cache.KeyDeleteAsync(id);
        await _cache.KeyDeleteAsync("users:" + userToDelete.Dni.Trim().ToUpper());
        _memoryCache.Remove(cacheKey);

        await DeleteUserAsync(id, logically: true);
    }


    /// <summary>
    /// Registra un nuevo usuario con su DNI y contraseña.
    /// </summary>
    /// <param name="request">Los detalles de la solicitud de registro con DNI y contraseña.</param>
    /// <returns>El usuario recién registrado.</returns>
    /// <exception cref="UserAlreadyExistsException">Si ya existe un usuario con el mismo DNI.</exception>
    public async Task<User?> RegisterUser(LoginRequest request)
    {
        var user = await _userRepository.GetByUsernameAsync(request.Dni);
        if (user != null)
            throw new UserAlreadyExistsException(request.Dni);

        var newUser = request.ToUser();
        _logger.LogInformation("RegisterUser new id " + newUser.Id);
        newUser.Password = BCrypt.Net.BCrypt.HashPassword(request.Password);
        await _userRepository.AddAsync(newUser);
        return newUser;
    }
}