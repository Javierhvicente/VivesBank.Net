using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ICSharpCode.SharpZipLib.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using NUnit.Framework.Legacy;
using StackExchange.Redis;
using VivesBankApi.Database;
using VivesBankApi.Rest.Users.Dtos;
using VivesBankApi.Rest.Users.Exceptions;
using VivesBankApi.Rest.Users.Mapper;
using VivesBankApi.Rest.Users.Models;
using VivesBankApi.Rest.Users.Repository;
using VivesBankApi.Rest.Users.Service;
using VivesBankApi.WebSocket.Model;
using VivesBankApi.WebSocket.Service;
using Role = VivesBankApi.Rest.Users.Models.Role;

namespace Tests.Rest.Users.Service;

[TestFixture]
public class UserServiceTest
{
    private Mock<IConnectionMultiplexer> _connection;
    private Mock<IDatabase> _cache;
    private Mock<IUserRepository> userRepositoryMock;
    private Mock<AuthJwtConfig> _authConfig;
    private Mock<ILogger<UserService>> _logger;
    private Mock<IMemoryCache> _memoryCacheMock;
    private UserService userService;
    private User _user1;
    private User _user2;
    private Mock<IWebsocketHandler> _webSocketHandler;
    private Mock<IHttpContextAccessor> _httpContextAccessor;
    
    
    [SetUp]
    public void SetUp()
    {
        _connection = new Mock<IConnectionMultiplexer>();
        _cache = new Mock<IDatabase>();
        _connection.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_cache.Object);
    
        // Configuración real de AuthJwtConfig
        var authConfig = new AuthJwtConfig
        {
            Key = "UnaClaveDe256BitsQueDebeSerSeguraParaLaFirmaJWT", // Usa una clave de prueba
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            ExpiresInMinutes = "60"
        };

        // Mock de otras dependencias
        _logger = new Mock<ILogger<UserService>>();
        userRepositoryMock = new Mock<IUserRepository>();
        _webSocketHandler = new Mock<IWebsocketHandler>();
        _httpContextAccessor = new Mock<IHttpContextAccessor>();
        _memoryCacheMock = new Mock<IMemoryCache>();
        
        _memoryCacheMock.Setup(m => m.CreateEntry(It.IsAny<object>()))
            .Returns(Mock.Of<ICacheEntry>());
        
        // Creación del servicio con las dependencias
        userService = new UserService(
            _logger.Object,
            userRepositoryMock.Object,
            authConfig, // Pasar la instancia real aquí
            _connection.Object,
            _webSocketHandler.Object,
            _httpContextAccessor.Object,
            _memoryCacheMock.Object
        );

        // Datos de prueba
        _user1 = new User
        {
            Id = "1",
            Dni = "43080644B",
            Password = "Password123",
            Role = Role.Admin,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            IsDeleted = false
        };

        _user2 = new User
        {
            Id = "2",
            Dni = "86896998P",
            Password = "SecurePass456",
            Role = Role.User,
            CreatedAt = DateTime.Now.AddDays(-1),
            UpdatedAt = DateTime.Now,
            IsDeleted = false
        };
    }


    [Test]
    public async Task GetAllUsersAsync()
    {
        // Arrange
        int pageNumber = 1;
        int pageSize = 10;
        string role = "Admin";
        bool? isDeleted = false;
        string direction = "asc";

        var usersFromRepo = new PagedList<User>(
            new List<User> { _user1, _user2 },
            2,
            pageNumber,
            pageSize
        );

        userRepositoryMock
            .Setup(repo => repo.GetAllUsersPagedAsync(pageNumber, pageSize, role, isDeleted, direction))
            .ReturnsAsync(usersFromRepo);

        // Act
        var result = await userService.GetAllUsersAsync(pageNumber, pageSize, role, isDeleted, direction);

        // Assert
        ClassicAssert.NotNull(result);
        ClassicAssert.AreEqual(2, result.TotalCount);
        ClassicAssert.AreEqual(pageNumber, result.PageNumber);
        ClassicAssert.AreEqual(pageSize, result.PageSize);
        ClassicAssert.AreEqual(2, result.Count);
        ClassicAssert.AreEqual("43080644B", result.First().Dni);
    }

    [Test]
    public async Task GetAllUsersAsync_ReturnsEmptyList_WhenNoUsersFound()
    {
        // Arrange
        int pageNumber = 1;
        int pageSize = 10;
        string role = "Admin";
        bool? isDeleted = false;
        string direction = "asc";

        var usersFromRepo = new PagedList<User>(
            new List<User>(),
            0, // TotalCount
            pageNumber, // PageNumber
            pageSize // PageSize
        );

        userRepositoryMock
            .Setup(repo => repo.GetAllUsersPagedAsync(pageNumber, pageSize, role, isDeleted, direction))
            .ReturnsAsync(usersFromRepo);

        // Act
        var result = await userService.GetAllUsersAsync(pageNumber, pageSize, role, isDeleted, direction);

        // Assert
        ClassicAssert.NotNull(result);
        ClassicAssert.AreEqual(0, result.TotalCount);
        ClassicAssert.AreEqual(result.TotalCount, 0);
    }

    [Test]
    public async Task GetAllUsersAsync_ReturnsMappedUserResponse()
    {
        // Arrange
        int pageNumber = 1;
        int pageSize = 10;
        string role = "User";
        bool? isDeleted = false;
        string direction = "asc";

        var usersFromRepo = new PagedList<User>(
            new List<User> { _user1 },
            1,
            pageNumber,
            pageSize
        );
        
        userRepositoryMock
            .Setup(repo => repo.GetAllUsersPagedAsync(pageNumber, pageSize, role, isDeleted, direction))
            .ReturnsAsync(usersFromRepo);

        // Act
        var result = await userService.GetAllUsersAsync(pageNumber, pageSize, role, isDeleted, direction);

        // Assert
        var userResponse = result.First();
        ClassicAssert.AreEqual("43080644B", userResponse.Dni);
        ClassicAssert.AreEqual(Role.Admin.ToString(), userResponse.Role);
        ClassicAssert.False(userResponse.IsDeleted);
    }
    
    [Test]
    public async Task GetUserByIdAsync_WhenInCache()
    {
        // Arrange
        _cache.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)JsonSerializer.Serialize(_user1));

        // Act
        var result = await userService.GetUserByIdAsync(_user1.Id);

        // Assert
        Assert.Multiple(() =>
        {
            ClassicAssert.IsNotNull(result);
            ClassicAssert.AreEqual(_user1.Dni, result.Dni);
        });

        // Verify
        userRepositoryMock.Verify(repo => repo.GetByIdAsync(_user1.Id), Times.Never);
    }
    
    [Test]
    public async Task GetUserByIdAsync_WhenNotInCache()
    {
        // Arrange
        userRepositoryMock.Setup(repo => repo.GetByIdAsync(_user1.Id)).ReturnsAsync(_user1);

        // Act
        var result = await userService.GetUserByIdAsync(_user1.Id);

        // Assert
        Assert.Multiple(() =>
        {
            ClassicAssert.IsNotNull(result);
            ClassicAssert.AreEqual(_user1.Dni, result.Dni);
        });

        // Verify
        userRepositoryMock.Verify(repo => repo.GetByIdAsync(_user1.Id), Times.Once);
    }
    
    [Test]
    public void GetUserByIdAsync_NotExist()
    {
        // Arrange
        userRepositoryMock.Setup(repo => repo.GetByIdAsync("3")).ReturnsAsync((User)null);

        // Act
        Assert.ThrowsAsync<UserNotFoundException>((() => userService.GetUserByIdAsync("3")));

        // Verify
        userRepositoryMock.Verify(repo => repo.GetByIdAsync("3"), Times.Once);
    }
    
    [Test]
    public async Task AddUserAsync_ShouldCreateUserAndNotify_WhenValidRequest()
    {
        // Arrange
        var userRequest = new CreateUserRequest
        {
            Dni = "43080644B",
            Password = "Password123",
            Role = "User"
        };
        var createdUser = userRequest.toUser(); // Simula el mapeo de forma correcta
        var mockUserResponse = createdUser.ToUserResponse();

        userRepositoryMock.Setup(repo => repo.GetByUsernameAsync(userRequest.Dni)).ReturnsAsync((User)null);
        userRepositoryMock.Setup(repo => repo.AddAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, "12345") };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var mockHttpContext = new DefaultHttpContext { User = principal };

        _httpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext);

        _webSocketHandler.Setup(ws => ws.NotifyUserAsync(It.IsAny<string>(), It.IsAny<Notification<UserResponse>>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await userService.AddUserAsync(userRequest);

        // Assert
        ClassicAssert.IsNotNull(result);
        ClassicAssert.AreEqual(createdUser.Dni, result.Dni);
        userRepositoryMock.Verify(repo => repo.AddAsync(It.IsAny<User>()), Times.Once);
        _webSocketHandler.Verify(ws => ws.NotifyUserAsync("12345", It.IsAny<Notification<UserResponse>>()), Times.Once);
    }

    [Test]
    public void AddUserAsync_ShouldThrowInvalidNameException()
    {
        var userRequest = new CreateUserRequest
        {
            Dni = "",
            Password = "Password123",
            Role = Role.User.ToString()
        };
        
        var ex = Assert.ThrowsAsync<InvalidDniException>(async () =>
            await userService.AddUserAsync(userRequest)
        );
        Assert.That(ex.Message, Is.EqualTo($"The dni {userRequest.Dni} is not a valid DNI"));
        
        userRepositoryMock.Verify(repo => repo.GetByUsernameAsync(userRequest.Dni), Times.Never);
        userRepositoryMock.Verify(repo => repo.AddAsync(It.IsAny<User>()), Times.Never);
    }

    
    [Test]
    public void AddUserAsync_ShouldThrowAlreadyExistsException()
    {
        var userRequest = new CreateUserRequest
        {
            Dni = "43080644B",
            Password = "Password123",
            Role = Role.Admin.ToString()
        };

        var existingUser = new User
        {
            Id = "1",
            Dni = "43080644B",
            Password = "Password123",
            Role = Role.Admin
        };

        userRepositoryMock.Setup(repo => repo.GetByUsernameAsync(userRequest.Dni)).ReturnsAsync(existingUser);
        
        var ex = Assert.ThrowsAsync<UserAlreadyExistsException>(async () =>
            await userService.AddUserAsync(userRequest)
        );
        Assert.That(ex.Message, Is.EqualTo($"A user with the username '{userRequest.Dni}' already exists."));
        
        userRepositoryMock.Verify(repo => repo.GetByUsernameAsync(userRequest.Dni), Times.Once);
        userRepositoryMock.Verify(repo => repo.AddAsync(It.IsAny<User>()), Times.Never);
    }

    [Test]
    public async Task GetUserByUsernameAsync()
    {
        // Arrange
        string username = "User1"; 
        
        userRepositoryMock.Setup(repo => repo.GetByUsernameAsync(username)).ReturnsAsync(_user1);

        // Act
        var result = await userService.GetUserByUsernameAsync(username);

        // Assert
        Assert.Multiple(() =>
        {
            ClassicAssert.IsNotNull(result); 
            ClassicAssert.AreEqual(_user1.Dni, result.Dni); 
            ClassicAssert.AreEqual("Admin", result.Role);
        });

        // Verify
        userRepositoryMock.Verify(repo => repo.GetByUsernameAsync(username), Times.Once);
    }
    
    [Test]
    public async Task GetUserByUsernameAsync_NotExists()
    {
        // Arrange
        string username = "NonExistentUser"; 
        
        userRepositoryMock.Setup(repo => repo.GetByUsernameAsync(username)).ReturnsAsync((User)null);

        // Act
        Assert.ThrowsAsync<UserNotFoundException>(
            () => userService.GetUserByUsernameAsync(username)
        );

        // Verify
        userRepositoryMock.Verify(repo => repo.GetByUsernameAsync(username), Times.Once);
    }

    
    [Test]
    public async Task UpdateUserAsync()
    {
        // Arrange
        var userId = "1";
        var userUpdateRequest = new UserUpdateRequest
        {
            Dni = "43080644B",
            Password = "UpdatedPassword123",
            Role = "User"
        };

        var existingUser = new User
        {
            Id = userId,
            Dni = "43080644B",
            Password = "Password123",
            Role = Role.Admin
        };

        var updatedUser = new User
        {
            Id = userId,
            Dni = "43080644B",
            Password = "UpdatedPassword123",
            Role = Role.User
        };

        userRepositoryMock.Setup(repo => repo.GetByIdAsync(userId)).ReturnsAsync(existingUser);
        userRepositoryMock.Setup(repo => repo.GetByUsernameAsync(userUpdateRequest.Dni)).ReturnsAsync((User)null);
        userRepositoryMock.Setup(repo => repo.UpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        // Act
        var result = await userService.UpdateUserAsync(userId, userUpdateRequest);

        // Assert
        Assert.Multiple(() =>
        {
            ClassicAssert.IsNotNull(result);
            ClassicAssert.AreEqual(updatedUser.Dni, result.Dni);
            ClassicAssert.AreEqual("User", result.Role);
        });

        // Verify
        userRepositoryMock.Verify(repo => repo.GetByIdAsync(userId), Times.Once);
        userRepositoryMock.Verify(repo => repo.GetByUsernameAsync(userUpdateRequest.Dni), Times.Once);
        userRepositoryMock.Verify(repo => repo.UpdateAsync(It.IsAny<User>()), Times.Once);
    }
    
    [Test]
    public void UpdateUserAsync_WhenUsernameIsInvalid(){
        // Arrange
        var userId = "1";
        var userUpdateRequest = new UserUpdateRequest
        {
            Dni = "",
            Password = "UpdatedPassword123",
            Role = "User"
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidDniException>(async () =>
            await userService.UpdateUserAsync(userId, userUpdateRequest)
        );

        // Verify
        userRepositoryMock.Verify(repo => repo.GetByIdAsync(userId), Times.Never);
        userRepositoryMock.Verify(repo => repo.GetByUsernameAsync(userUpdateRequest.Dni), Times.Never);
        userRepositoryMock.Verify(repo => repo.UpdateAsync(It.IsAny<User>()), Times.Never);
    }
    
    [Test]
    public async Task UpdateUserAsync_WhenFoundInCache()
    {
        // Arrange
        var userId = "1";
        var userUpdateRequest = new UserUpdateRequest
        {
            Dni = "43080644B",
            Password = "UpdatedPassword123",
            Role = "User"
        };

        var existingUser = new User
        {
            Id = userId,
            Dni = "43080644B",
            Password = "Password123",
            Role = Role.Admin
        };

        var updatedUser = new User
        {
            Id = userId,
            Dni = "43080644B",
            Password = "UpdatedPassword123",
            Role = Role.User
        };
        
        _cache.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
           .ReturnsAsync((RedisValue)JsonSerializer.Serialize(existingUser));
        userRepositoryMock.Setup(repo => repo.UpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        // Act
        var result = await userService.UpdateUserAsync(userId, userUpdateRequest);

        // Assert
        Assert.Multiple(() =>
        {
            ClassicAssert.IsNotNull(result);
            ClassicAssert.AreEqual("43080644B", result.Dni);
            ClassicAssert.AreEqual("User", result.Role);
        });

        // Verify
        userRepositoryMock.Verify(repo => repo.GetByIdAsync(userId), Times.Never);
        userRepositoryMock.Verify(repo => repo.UpdateAsync(It.IsAny<User>()), Times.Once);
    }
    
    [Test]
    public async Task UpdateUserAsync_NotExist()
    {
        // Arrange
        var userId = "999"; 
        var userUpdateRequest = new UserUpdateRequest { Dni = "43080644B" };
    
        userRepositoryMock.Setup(repo => repo.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((User)null); 
    
        // Act & Assert
        var ex = Assert.ThrowsAsync<UserNotFoundException>(async () =>
            await userService.UpdateUserAsync(userId, userUpdateRequest)
        );
        Assert.That(ex.Message, Is.EqualTo($"The user with id: {userId} was not found"));
    }
    
    [Test]
    public async Task UpdateUserAsync_UserNameTaken()
    {
        // Arrange
        var userId = "1"; 
        var userToUpdate = new User { Id = userId, Dni = "43080644B" }; 
        var userUpdateRequest = new UserUpdateRequest { Dni = "43080644B" };
        var existingUserSameName = new User { Id = "2", Dni = "43080644B" };

        userRepositoryMock.Setup(repo => repo.GetByIdAsync(It.IsAny<string>())).ReturnsAsync(userToUpdate);
        userRepositoryMock.Setup(repo => repo.GetByUsernameAsync(It.IsAny<string>())).ReturnsAsync(existingUserSameName);
    
        // Act & Assert
        var ex = Assert.ThrowsAsync<UserAlreadyExistsException>(async () =>
            await userService.UpdateUserAsync(userId, userUpdateRequest)
        );
        Assert.That(ex.Message, Is.EqualTo($"A user with the username '{userUpdateRequest.Dni}' already exists."));
    }


    
    [Test]
    public async Task DeleteUserAsync()
    {
        // Arrange
        var userId = "1"; 
        var userToUpdate = new User { Id = userId, Dni = "TestUser", IsDeleted = false };
        
        userRepositoryMock.Setup(repo => repo.GetByIdAsync(It.IsAny<string>())).ReturnsAsync(userToUpdate);
        userRepositoryMock.Setup(repo => repo.UpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);
    
        // Act
        await userService.DeleteUserAsync(userId, true); 
    
        // Assert
        ClassicAssert.IsTrue(userToUpdate.IsDeleted);
    
        // Verify
        userRepositoryMock.Verify(repo => repo.UpdateAsync(It.IsAny<User>()), Times.Once);
    }
    
    [Test]
    public async Task DeleteUserAsync_SoftDeletion()
    {
        // Arrange
        var userId = "1";
        var userToUpdate = new User { Id = userId, Dni = "TestUser", IsDeleted = false };
        
        userRepositoryMock.Setup(repo => repo.GetByIdAsync(It.IsAny<string>())).ReturnsAsync(userToUpdate);
        userRepositoryMock.Setup(repo => repo.UpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);
        
        // Act
        await userService.DeleteUserAsync(userId, false); // Eliminación física
        
        // Assert
        ClassicAssert.IsFalse(userToUpdate.IsDeleted);
        
        // Verify
        userRepositoryMock.Verify(repo => repo.UpdateAsync(It.IsAny<User>()), Times.Never);
    }
    
    [Test]
    public async Task DeleteUserAsync_SoftDeletion_WhenFoundInCache()
    {
        // Arrange
        var userId = "1";
        var userToUpdate = new User { Id = userId, Dni = "TestUser", IsDeleted = false };
        
        _cache.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
           .ReturnsAsync((RedisValue)JsonSerializer.Serialize(userToUpdate));
        userRepositoryMock.Setup(repo => repo.GetByIdAsync(It.IsAny<string>())).ReturnsAsync(userToUpdate);
        userRepositoryMock.Setup(repo => repo.DeleteAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        
        // Act
        await userService.DeleteUserAsync(userId, false); // Eliminación física
        
        // Assert
        userRepositoryMock.Verify(repo => repo.DeleteAsync(It.IsAny<string>()), Times.Once);
    }
    
    [Test]
    public async Task DeleteUserAsync_NotExist()
    {
        // Arrange
        var userId = "999";

        userRepositoryMock.Setup(repo => repo.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((User)null); 
        
        // Act & Assert
        var ex = Assert.ThrowsAsync<UserNotFoundException>(async () =>
            await userService.DeleteUserAsync(userId, true)
        );
        Assert.That(ex.Message, Is.EqualTo($"The user with id: {userId} was not found"));
    }
    
    [Test]
    public async Task DeleteUserAsync_PhysicalDeletion()
    {
        // Arrange
        var userId = "1";
        var userToUpdate = new User { Id = userId, Dni = "TestUser", IsDeleted = false }; 
        
        userRepositoryMock.Setup(repo => repo.GetByIdAsync(It.IsAny<string>())).ReturnsAsync(userToUpdate);
        userRepositoryMock.Setup(repo => repo.DeleteAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
    
        // Act
        await userService.DeleteUserAsync(userId, false); // Eliminación física
    
        // Assert
        userRepositoryMock.Verify(repo => repo.DeleteAsync(It.IsAny<string>()), Times.Once);
    }

    [Test]
    public async Task GettingMyUserDataAsync_ShouldReturnResponse()
    {
        var userId = "12345";
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        var mockHttpContext = new DefaultHttpContext
        {
            User = principal
        };

        _httpContextAccessor
            .Setup(x => x.HttpContext)
            .Returns(mockHttpContext);

        var user = new User
        {
            Id = userId,
            Dni = "43080644B",
            Password = "HashedPassword123",
            Role = Role.User
        };

        userRepositoryMock
            .Setup(repo => repo.GetByIdAsync(userId))
            .ReturnsAsync(user);
        
        var result = await userService.GettingMyUserData();
        
        ClassicAssert.IsNotNull(result);
        ClassicAssert.AreEqual(user.Id, result.Id);
        ClassicAssert.AreEqual(user.Dni, result.Dni);
    }
    
    [Test]
    public void GettingMyUserData_ShouldThrowExceptionHttpContextIsNull()
    {
        _httpContextAccessor
            .Setup(x => x.HttpContext)
            .Returns((HttpContext)null);
        
        Assert.ThrowsAsync<NullReferenceException>(async () => 
            await userService.GettingMyUserData());
    }
    
    [Test]
    public void GettingMyUserData_ShouldThrowException_IdentifierIsMissing()
    {
        var claims = new List<Claim>();
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        var mockHttpContext = new DefaultHttpContext
        {
            User = principal
        };

        _httpContextAccessor
            .Setup(x => x.HttpContext)
            .Returns(mockHttpContext);
        
        Assert.ThrowsAsync<NullReferenceException>(async () =>
            await userService.GettingMyUserData());
    }

    [Test]
    public async Task LoginUser_ShouldReturnUser()
    {
        var request = new LoginRequest
        {
            Dni = "43080644B",
            Password = "Password123"
        };

        var user = new User
        {
            Id = "1",
            Dni = "43080644B",
            Password = BCrypt.Net.BCrypt.HashPassword("Password123")
        };

        userRepositoryMock
            .Setup(repo => repo.GetByUsernameAsync(request.Dni))
            .ReturnsAsync(user);
        
        var result = await userService.LoginUser(request);
        
        ClassicAssert.IsNotNull(result);
        ClassicAssert.AreEqual(user.Id, result.Id);
        ClassicAssert.AreEqual(user.Dni, result.Dni);
    }
    
    [Test]
    public async Task LoginUser_ShouldReturnNull_WhenUserNotFound()
    {
        var request = new LoginRequest
        {
            Dni = "43080644B",
            Password = "Password123"
        };

        userRepositoryMock
            .Setup(repo => repo.GetByUsernameAsync(request.Dni))
            .ReturnsAsync((User?)null);
        
        var result = await userService.LoginUser(request);
        
        ClassicAssert.IsNull(result);
    }


    [Test]
    public async Task RegisterUser_Should_ReturnUser()
    {
        // Arrange
        var loginRequest = new LoginRequest
        {
            Dni = "70919049K",
            Password = "CalamarSureño123",
        };
        userRepositoryMock.Setup(repo => repo.GetByUsernameAsync(It.IsAny<string>())).ReturnsAsync((User)null);
        userRepositoryMock.Setup(repo => repo.AddAsync(It.IsAny<User>())).Returns(Task.FromResult(new User { Id = "1" }));

        // Act
        var result = await userService.RegisterUser(loginRequest);

        // Assert
        ClassicAssert.IsNotNull(result);
        ClassicAssert.AreEqual("70919049K", result.Dni);
        ClassicAssert.AreEqual("User", result.Role.ToString());
    }

    [Test]
    public async Task RegisterUser_ShouldReturn_ExistingUser()
    {
        var loginRequest = new LoginRequest
        {
            Dni = "43080644B", // DNI ya existente
            Password = "Password123"
        };

        var existingUser = new User
        {
            Id = "1",
            Dni = "43080644B",
            Password = "HashedPassword123",
            Role = Role.Admin
        };

        // Configurar el mock para devolver un usuario existente
        userRepositoryMock
            .Setup(repo => repo.GetByUsernameAsync(loginRequest.Dni))
            .ReturnsAsync(existingUser);
        // Act
        var result = Assert.ThrowsAsync<UserAlreadyExistsException>(async () =>
            await userService.RegisterUser(loginRequest)
        );

        Assert.That(result.Message, Is.EqualTo($"A user with the username '{loginRequest.Dni}' already exists."));
        
    }

    [Test]
    public async Task UpdateMyPassword_Success()
    {
        // Arrange
        var userId = "test-user-id";
        var oldPassword = "old-password";
        var newPassword = "new-password";
        var hashedNewPassword = BCrypt.Net.BCrypt.HashPassword(newPassword);

        var user = new User
        {
            Id = userId,
            Password = BCrypt.Net.BCrypt.HashPassword(oldPassword),
            Dni = "test-dni"
        };

        var updatePasswordRequest = new UpdatePasswordRequest
        {
            Password = newPassword
        };

        // Set up HttpContext with claims
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId) };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var mockHttpContext = new DefaultHttpContext { User = principal };

        _httpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext);

        userRepositoryMock.Setup(repo => repo.GetByIdAsync(userId))
            .ReturnsAsync(user);

        userRepositoryMock.Setup(repo => repo.UpdateAsync(It.IsAny<User>()))
            .Returns(Task.CompletedTask)
            .Callback<User>(u => u.Password = hashedNewPassword); // Ensure the password is updated

        _cache.Setup(cache => cache.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await userService.UpdateMyPassword(updatePasswordRequest);

        // Assert
        Assert.Multiple(() =>
        {
            ClassicAssert.IsNotNull(result);
            ClassicAssert.AreEqual(userId, result.Id);
            ClassicAssert.IsTrue(BCrypt.Net.BCrypt.Verify(newPassword, result.Password));
        });

        userRepositoryMock.Verify(repo => repo.UpdateAsync(It.Is<User>(u => u.Password == hashedNewPassword)), Times.Once);
        _cache.Verify(cache => cache.KeyDeleteAsync(userId, It.IsAny<CommandFlags>()), Times.Once);
        _cache.Verify(cache => cache.KeyDeleteAsync("users:" + user.Dni.Trim().ToUpper(), It.IsAny<CommandFlags>()), Times.Once);
    }
    
    [Test]
    public async Task UpdateMyPassword_NotSuccess_UserNotFound()
    {
        // Arrange
        var userId = "non-existent-user-id";
        var newPassword = "new-password";

        var updatePasswordRequest = new UpdatePasswordRequest
        {
            Password = newPassword
        };

        // Set up HttpContext with claims
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId) };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        var mockHttpContext = new DefaultHttpContext { User = principal };

        _httpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext);

        userRepositoryMock.Setup(repo => repo.GetByIdAsync(userId))
            .ReturnsAsync((User)null); // User not found

        // Act & Assert
        var ex = Assert.ThrowsAsync<UserNotFoundException>(async () =>
            await userService.UpdateMyPassword(updatePasswordRequest)
        );
        Assert.That(ex.Message, Is.EqualTo($"The user with id: {userId} was not found"));

        userRepositoryMock.Verify(repo => repo.UpdateAsync(It.IsAny<User>()), Times.Never);
        _cache.Verify(cache => cache.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Never);
    }
    
    [Test]
public async Task DeleteMeAsync_Success()
{
    // Arrange
    var userId = "test-user-id";
    var user = new User
    {
        Id = userId,
        Dni = "test-dni",
        IsDeleted = false,
        Role = Role.User
    };

    // Set up HttpContext with claims
    var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId) };
    var identity = new ClaimsIdentity(claims);
    var principal = new ClaimsPrincipal(identity);
    var mockHttpContext = new DefaultHttpContext { User = principal };

    _httpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext);

    userRepositoryMock.Setup(repo => repo.GetByIdAsync(userId))
        .ReturnsAsync(user);

    userRepositoryMock.Setup(repo => repo.UpdateAsync(It.IsAny<User>()))
        .Returns(Task.CompletedTask);

    _cache.Setup(cache => cache.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
        .ReturnsAsync(true);

    // Act
    await userService.DeleteMeAsync();

    // Assert
    userRepositoryMock.Verify(repo => repo.UpdateAsync(It.Is<User>(u => u.IsDeleted && u.Role == Role.Revoked)), Times.Once);
    _cache.Verify(cache => cache.KeyDeleteAsync(userId, It.IsAny<CommandFlags>()), Times.Exactly(2));
    _cache.Verify(cache => cache.KeyDeleteAsync("users:" + user.Dni.Trim().ToUpper(), It.IsAny<CommandFlags>()), Times.Exactly(2));
}

[Test]
public async Task DeleteMeAsync_UserNotFound()
{
    // Arrange
    var userId = "xxx";

    // Set up HttpContext with claims
    var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, userId) };
    var identity = new ClaimsIdentity(claims);
    var principal = new ClaimsPrincipal(identity);
    var mockHttpContext = new DefaultHttpContext { User = principal };

    _httpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext);

    userRepositoryMock.Setup(repo => repo.GetByIdAsync(userId))
        .ReturnsAsync((User)null); // User not found

    // Act & Assert
    var ex = Assert.ThrowsAsync<UserNotFoundException>(async () =>
        await userService.DeleteMeAsync()
    );
    Assert.That(ex.Message, Is.EqualTo($"The user with id: {userId} was not found"));

    userRepositoryMock.Verify(repo => repo.UpdateAsync(It.IsAny<User>()), Times.Never);
    _cache.Verify(cache => cache.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()), Times.Never);
}

}
