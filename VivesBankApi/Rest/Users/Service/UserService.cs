using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HotChocolate.Authorization;
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
    private readonly AuthJwtConfig _authConfig;
    private readonly ILogger _logger;
    private readonly IWebsocketHandler _webSocketHandler;
    private readonly IHttpContextAccessor _httpContextAccessor;

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
        IHttpContextAccessor httpContextAccessor
    ) : base(logger)
    {
        _logger = logger;
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
        ValidateUserRequest(userRequest);

        User newUser = userRequest.toUser();
        await EnsureUserDoesNotExist(userRequest.Dni);

        await _userRepository.AddAsync(newUser);
        await NotifyUserCreation(newUser);

        return newUser.ToUserResponse();
    }
    
    private void ValidateUserRequest(CreateUserRequest userRequest)
    {
        if (!UserValidator.ValidateDni(userRequest.Dni))
        {
            throw new InvalidDniException(userRequest.Dni);
        }
    }
    
    private async Task EnsureUserDoesNotExist(string dni)
    {
        User? userWithTheSameUsername = await GetByUsernameAsync(dni);
        if (userWithTheSameUsername != null)
        {
            throw new UserAlreadyExistsException(dni);
        }
    }

    private async Task NotifyUserCreation(User newUser)
    {
        var notification = new Notification<UserResponse>
        {
            Type = Notification<UserResponse>.NotificationType.Create.ToString(),
            CreatedAt = DateTime.Now,
            Data = newUser.ToUserResponse()
        };

        var user = _httpContextAccessor.HttpContext!.User;
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        await _webSocketHandler.NotifyUserAsync(id, notification);
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
    public async Task<UserResponse> UpdateUserAsync(String id, UserUpdateRequest user)
    {
        if (user.Dni != null && !UserValidator.ValidateDni(user.Dni))
        {
             throw new InvalidDniException(user.Dni);
        }

        User? userToUpdate = await GetByIdAsync(id) ?? throw new UserNotFoundException(id);
        
        if (user.Dni != null)
        {
            User? userWithTheSameUsername = await GetByUsernameAsync(userToUpdate.Dni);
            if (userWithTheSameUsername != null && userWithTheSameUsername.Id != id)
            {
                throw new UserAlreadyExistsException(user.Dni);
            }
        }
        
        User updatedUser = user.UpdateUserFromInput(userToUpdate);
        await _userRepository.UpdateAsync(updatedUser);
        // Eliminar la entrada antigua de la caché
        await _cache.KeyDeleteAsync(id);
        await _cache.KeyDeleteAsync("users:" + userToUpdate.Dni.Trim().ToUpper());
        // Agregar la nueva entrada a la caché
        await _cache.StringSetAsync(id, JsonConvert.SerializeObject(updatedUser), TimeSpan.FromMinutes(10));
        return updatedUser.ToUserResponse();
    }

    /// <summary>
    /// Elimina un usuario del sistema. Puede ser una eliminación lógica o física.
    /// </summary>
    /// <param name="id">El ID del usuario a eliminar.</param>
    /// <param name="logically">Indica si la eliminación es lógica (true) o física (false).</param>
    public async Task DeleteUserAsync(string id, bool logically)
    {
        var userToUpdate = await GetUserByIdOrThrowAsync(id);
        await DeleteUserAsync(userToUpdate, logically);
    }

    private async Task<User> GetUserByIdOrThrowAsync(string id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        return user ?? throw new UserNotFoundException(id);
    }

    private async Task DeleteUserAsync(User user, bool logically)
    {
        if (logically)
        {
            await LogicallyDeleteUserAsync(user);
        }
        else
        {
            await PhysicallyDeleteUserAsync(user);
        }
    }

    private async Task LogicallyDeleteUserAsync(User user)
    {
        user.IsDeleted = true;
        user.Role = Role.Revoked;
        await _userRepository.UpdateAsync(user);
        await RemoveUserFromCacheAsync(user.Id, user.Dni);
    }

    private async Task PhysicallyDeleteUserAsync(User user)
    {
        await RemoveUserFromCacheAsync(user.Id, user.Dni);
        await _userRepository.DeleteAsync(user.Id);
    }

    private async Task RemoveUserFromCacheAsync(string id, string dni)
    {
        await _cache.KeyDeleteAsync(id);
        await _cache.KeyDeleteAsync("users:" + dni.Trim().ToUpper());
    }
    
    // Métodos privados de acceso a caché y base de datos...

    /// <summary>
    /// Intenta obtener un usuario por su ID desde la caché o la base de datos.
    /// </summary>
    /// <param name="id">El ID del usuario.</param>
    /// <returns>El usuario si se encuentra, de lo contrario null.</returns>
    private async Task<User?> GetByIdAsync(string id)
    {
        var cachedUser = await _cache.StringGetAsync(id);
        if (cachedUser.HasValue)
        {
            return JsonConvert.DeserializeObject<User>(cachedUser);
        }

        User? user = await _userRepository.GetByIdAsync(id);
        if (user != null)
        {
            await _cache.StringSetAsync(id, JsonConvert.SerializeObject(user), TimeSpan.FromMinutes(10));
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
        var cachedUser = await _cache.StringGetAsync("users:" + username.Trim().ToUpper());
        if (!cachedUser.IsNullOrEmpty)
        {
            return JsonConvert.DeserializeObject<User>(cachedUser);
        }
        User? user = await _userRepository.GetByUsernameAsync(username);
        if (user != null)
        {
            await _cache.StringSetAsync("users:" + username.Trim().ToUpper(), JsonConvert.SerializeObject(user), TimeSpan.FromMinutes(10));
            return user;
        }
        return null;
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
        userToUpdate.Password = BCrypt.Net.BCrypt.HashPassword(request.Password);
        await _userRepository.UpdateAsync(userToUpdate);
        await _cache.KeyDeleteAsync(id);
        await _cache.KeyDeleteAsync("users:" + userToUpdate.Dni.Trim().ToUpper());
        await _cache.StringSetAsync(id, JsonConvert.SerializeObject(userToUpdate), TimeSpan.FromMinutes(10));
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