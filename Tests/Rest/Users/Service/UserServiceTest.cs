using System.Text.Json;
using Moq;
using NUnit.Framework.Legacy;
using StackExchange.Redis;
using VivesBankApi.Rest.Users.Dtos;
using VivesBankApi.Rest.Users.Exceptions;
using VivesBankApi.Rest.Users.Mapper;
using VivesBankApi.Rest.Users.Models;
using VivesBankApi.Rest.Users.Repository;
using VivesBankApi.Rest.Users.Service;
using Role = VivesBankApi.Rest.Users.Models.Role;

namespace Tests.Rest.Users.Service;

public class UserServiceTest
{
    private Mock<IConnectionMultiplexer> _connection;
    private Mock<IDatabase> _cache;
    private Mock<IUserRepository> userRepositoryMock;
    private UserService userService;
    private User _user1;
    private User _user2;

    [SetUp]
    public void SetUp()
    {
        _connection = new Mock<IConnectionMultiplexer>();
        _cache = new Mock<IDatabase>();
        _connection.Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<string>())).Returns(_cache.Object);
        
        userRepositoryMock = new Mock<IUserRepository>();
        userService = new UserService(userRepositoryMock.Object, _connection.Object);
        
        _user1 = new User
        {
            Id = "1",
            Username = "43080644B",
            Password = "Password123",
            Role = Role.Admin,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            IsDeleted = false
        };

        _user2 = new User
        {
            Id = "2",
            Username = "86896998P",
            Password = "SecurePass456",
            Role = Role.User,
            CreatedAt = DateTime.Now.AddDays(-1),
            UpdatedAt = DateTime.Now,
            IsDeleted = false
        };
    }

    [Test]
    public async Task GetAll()
    {
        // Arrange
        var mockUsers = new List<User> { _user1, _user2 };
        
        userRepositoryMock.Setup(repo => repo.GetAllAsync()).ReturnsAsync(mockUsers);

        // Act
        var result = await userService.GetAllUsersAsync();

        // Assert
        Assert.Multiple(() =>
        {
            ClassicAssert.IsNotNull(result);
            ClassicAssert.AreEqual(2, result.Count);
            
            ClassicAssert.AreEqual(_user1.Username, result[0].Username);
            ClassicAssert.AreEqual(_user1.Password, result[0].Password);
            ClassicAssert.AreEqual(_user1.Role, result[0].Role);
            
            ClassicAssert.AreEqual(_user2.Username, result[1].Username);
            ClassicAssert.AreEqual(_user2.Password, result[1].Password);
            ClassicAssert.AreEqual(_user2.Role, result[1].Role);
        });

        // Verify
        userRepositoryMock.Verify(repo => repo.GetAllAsync(), Times.Once);
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
            ClassicAssert.AreEqual(_user1.Username, result.Username);
            ClassicAssert.AreEqual(_user1.Password, result.Password);
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
            ClassicAssert.AreEqual(_user1.Username, result.Username);
            ClassicAssert.AreEqual(_user1.Password, result.Password);
        });

        // Verify
        userRepositoryMock.Verify(repo => repo.GetByIdAsync(_user1.Id), Times.Once);
    }
    
    [Test]
    public async Task GetUserByIdAsync_NotExist()
    {
        // Arrange
        userRepositoryMock.Setup(repo => repo.GetByIdAsync("3")).ReturnsAsync((User)null);

        // Act
        var result = await userService.GetUserByIdAsync("3");

        // Assert
        ClassicAssert.IsNull(result);

        // Verify
        userRepositoryMock.Verify(repo => repo.GetByIdAsync("3"), Times.Once);
    }
    
    [Test]
    public async Task AddUserAsync()
    {
        // Arrange
        var userRequest = new CreateUserRequest
        {
            Username = "43080644B",
            Password = "Password123",
            Role = "Admin"
        };

        var newUser = UserMapper.ToUser(userRequest);
        
        userRepositoryMock.Setup(repo => repo.GetByUsernameAsync(userRequest.Username)).ReturnsAsync(_user2);
        userRepositoryMock.Setup(repo => repo.GetByUsernameAsync(userRequest.Username)).ReturnsAsync((User)null);
        userRepositoryMock.Setup(repo => repo.AddAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        // Act
        var result = await userService.AddUserAsync(userRequest);

        // Assert
        Assert.Multiple(() =>
        {
            ClassicAssert.IsNotNull(result);
            ClassicAssert.AreEqual(newUser.Username, result.Username);
            ClassicAssert.IsTrue(BCrypt.Net.BCrypt.Verify(userRequest.Password, result.Password), "Password hash does not match");
            ClassicAssert.AreEqual(newUser.Role, result.Role);
        });

        // Verify
        userRepositoryMock.Verify(repo => repo.GetByUsernameAsync(userRequest.Username), Times.Once);
        userRepositoryMock.Verify(repo => repo.AddAsync(It.IsAny<User>()), Times.Once);
    }
    
    [Test]
    public void AddUserAsync_AlreadyExists()
    {
        // Arrange
        var userRequest = new CreateUserRequest
        {
            Username = "43080644B",
            Password = "Password123",
            Role = Role.Admin.ToString()
        };

        var existingUser = new User
        {
            Id = "1",
            Username = "43080644B",
            Password = "Password123",
            Role = Role.Admin
        };

        userRepositoryMock.Setup(repo => repo.GetByUsernameAsync(userRequest.Username)).ReturnsAsync(existingUser);

        // Act & Assert
        var ex = Assert.ThrowsAsync<UserAlreadyExistsException>(async () =>
            await userService.AddUserAsync(userRequest)
        );
        Assert.That(ex.Message, Is.EqualTo($"A user with the username '{userRequest.Username}' already exists."));

        // Verify
        userRepositoryMock.Verify(repo => repo.GetByUsernameAsync(userRequest.Username), Times.Once);
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
            ClassicAssert.AreEqual(_user1.Username, result.Username); 
            ClassicAssert.AreEqual(_user1.Password, result.Password); 
            ClassicAssert.AreEqual(_user1.Role, result.Role);
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
        var result = await userService.GetUserByUsernameAsync(username);

        // Assert
        ClassicAssert.IsNull(result);

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
            Username = "43080644B",
            Password = "UpdatedPassword123",
            Role = "User"
        };

        var existingUser = new User
        {
            Id = userId,
            Username = "43080644B",
            Password = "Password123",
            Role = Role.Admin
        };

        var updatedUser = new User
        {
            Id = userId,
            Username = "43080644B",
            Password = "UpdatedPassword123",
            Role = Role.User
        };

        userRepositoryMock.Setup(repo => repo.GetByIdAsync(userId)).ReturnsAsync(existingUser);
        userRepositoryMock.Setup(repo => repo.GetByUsernameAsync(userUpdateRequest.Username)).ReturnsAsync((User)null);
        userRepositoryMock.Setup(repo => repo.UpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        // Act
        var result = await userService.UpdateUserAsync(userId, userUpdateRequest);

        // Assert
        Assert.Multiple(() =>
        {
            ClassicAssert.IsNotNull(result);
            ClassicAssert.AreEqual(updatedUser.Username, result.Username);
            ClassicAssert.IsTrue(BCrypt.Net.BCrypt.Verify(userUpdateRequest.Password, result.Password), "Password hash does not match");
            ClassicAssert.AreEqual(updatedUser.Role, result.Role);
        });

        // Verify
        userRepositoryMock.Verify(repo => repo.GetByIdAsync(userId), Times.Once);
        userRepositoryMock.Verify(repo => repo.GetByUsernameAsync(userUpdateRequest.Username), Times.Once);
        userRepositoryMock.Verify(repo => repo.UpdateAsync(It.IsAny<User>()), Times.Once);
    }
    
    [Test]
    public void UpdateUserAsync_WhenUsernameIsInvalid(){
        // Arrange
        var userId = "1";
        var userUpdateRequest = new UserUpdateRequest
        {
            Username = "",
            Password = "UpdatedPassword123",
            Role = "User"
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidUserException>(async () =>
            await userService.UpdateUserAsync(userId, userUpdateRequest)
        );

        // Verify
        userRepositoryMock.Verify(repo => repo.GetByIdAsync(userId), Times.Never);
        userRepositoryMock.Verify(repo => repo.GetByUsernameAsync(userUpdateRequest.Username), Times.Never);
        userRepositoryMock.Verify(repo => repo.UpdateAsync(It.IsAny<User>()), Times.Never);
    }
    
    [Test]
    public async Task UpdateUserAsync_WhenFoundInCache()
    {
        // Arrange
        var userId = "1";
        var userUpdateRequest = new UserUpdateRequest
        {
            Username = "43080644B",
            Password = "UpdatedPassword123",
            Role = "User"
        };

        var existingUser = new User
        {
            Id = userId,
            Username = "43080644B",
            Password = "Password123",
            Role = Role.Admin
        };

        var updatedUser = new User
        {
            Id = userId,
            Username = "43080644B",
            Password = "UpdatedPassword123",
            Role = Role.User
        };
        
        _cache.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
           .ReturnsAsync((RedisValue)JsonSerializer.Serialize(existingUser));
        userRepositoryMock.Setup(repo => repo.GetByUsernameAsync(userUpdateRequest.Username)).ReturnsAsync((User)null);
        userRepositoryMock.Setup(repo => repo.UpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);

        // Act
        var result = await userService.UpdateUserAsync(userId, userUpdateRequest);

        // Assert
        Assert.Multiple(() =>
        {
            ClassicAssert.IsNotNull(result);
            ClassicAssert.AreEqual(updatedUser.Username, result.Username);
            ClassicAssert.IsTrue(BCrypt.Net.BCrypt.Verify(userUpdateRequest.Password, result.Password), "Password hash does not match");
            ClassicAssert.AreEqual(updatedUser.Role, result.Role);
        });

        // Verify
        userRepositoryMock.Verify(repo => repo.GetByIdAsync(userId), Times.Never);
        userRepositoryMock.Verify(repo => repo.GetByUsernameAsync(userUpdateRequest.Username), Times.Once);
        userRepositoryMock.Verify(repo => repo.UpdateAsync(It.IsAny<User>()), Times.Once);
    }
    
    [Test]
    public async Task UpdateUserAsync_NotExist()
    {
        // Arrange
        var userId = "999"; 
        var userUpdateRequest = new UserUpdateRequest { Username = "43080644B" };
    
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
        var userToUpdate = new User { Id = userId, Username = "43080644B" }; 
        var userUpdateRequest = new UserUpdateRequest { Username = "43080644B" };
        var existingUserSameName = new User { Id = "2", Username = "43080644B" };

        userRepositoryMock.Setup(repo => repo.GetByIdAsync(It.IsAny<string>())).ReturnsAsync(userToUpdate);
        userRepositoryMock.Setup(repo => repo.GetByUsernameAsync(It.IsAny<string>())).ReturnsAsync(existingUserSameName);
    
        // Act & Assert
        var ex = Assert.ThrowsAsync<UserAlreadyExistsException>(async () =>
            await userService.UpdateUserAsync(userId, userUpdateRequest)
        );
        Assert.That(ex.Message, Is.EqualTo($"A user with the username '{userUpdateRequest.Username}' already exists."));
    }


    
    [Test]
    public async Task DeleteUserAsync()
    {
        // Arrange
        var userId = "1"; 
        var userToUpdate = new User { Id = userId, Username = "TestUser", IsDeleted = false };
        
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
        var userToUpdate = new User { Id = userId, Username = "TestUser", IsDeleted = false };
        
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
        var userToUpdate = new User { Id = userId, Username = "TestUser", IsDeleted = false };
        
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
        var userToUpdate = new User { Id = userId, Username = "TestUser", IsDeleted = false }; 
        
        userRepositoryMock.Setup(repo => repo.GetByIdAsync(It.IsAny<string>())).ReturnsAsync(userToUpdate);
        userRepositoryMock.Setup(repo => repo.DeleteAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
    
        // Act
        await userService.DeleteUserAsync(userId, false); // Eliminación física
    
        // Assert
        userRepositoryMock.Verify(repo => repo.DeleteAsync(It.IsAny<string>()), Times.Once);
    }
}
