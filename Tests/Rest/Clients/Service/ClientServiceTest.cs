using NUnit.Framework.Legacy;
using VivesBankApi.Rest.Clients.Models;
using VivesBankApi.Rest.Users.Models;

namespace Tests.Rest.Clients.Service;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Microsoft.Extensions.Logging;
using VivesBankApi.Rest.Clients.Dto;
using VivesBankApi.Rest.Clients.Exceptions;
using VivesBankApi.Rest.Clients.Repositories;
using VivesBankApi.Rest.Users.Exceptions;
using VivesBankApi.Rest.Users.Repository;
using VivesBankApi.Rest.Clients.Service;

public class ClientServiceTests
{
    private readonly Mock<IClientRepository> _clientRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ILogger<ClientService>> _loggerMock;
    private readonly ClientService _clientService;

    public ClientServiceTests()
    {
        _clientRepositoryMock = new Mock<IClientRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _loggerMock = new Mock<ILogger<ClientService>>();
        _clientService = new ClientService(_loggerMock.Object, _userRepositoryMock.Object, _clientRepositoryMock.Object);
    }

    [Test]
    public async Task GetAllAsync_ReturnsClients()
    {
        // Arrange
        var clients = new List<Client>
        {
            new Client { Id = "1", FullName = "John Doe", Adress = "Address 1", UserId = "User1" },
            new Client { Id = "2", FullName = "Jane Doe", Adress = "Address 2", UserId = "User2" }
        };
        _clientRepositoryMock.Setup(repo => repo.GetAllAsync()).ReturnsAsync(clients);

        // Act
        var result = await _clientService.GetAllAsync();

        // Assert
        ClassicAssert.NotNull(result);
        ClassicAssert.AreEqual(2, result.Count);
        ClassicAssert.AreEqual("John Doe", result.First().Fullname);
    }

    [Test]
    public void GetClientByIdAsync_ThrowsClientNotFoundException_WhenClientDoesNotExist()
    {
        // Arrange
        var clientId = "1";
        _clientRepositoryMock.Setup(repo => repo.GetByIdAsync(clientId)).ReturnsAsync((Client)null);

        // Act & Assert
        Assert.ThrowsAsync<ClientExceptions.ClientNotFoundException>(() => _clientService.GetClientByIdAsync(clientId));
    }

    [Test]
    public void CreateClientAsync_ThrowsUserNotFoundException_WhenUserDoesNotExist()
    {
        // Arrange
        var clientRequest = new ClientRequest
        {
            FullName = "John Doe",
            Address = "Address 1",
            UserId = "InvalidUser",
            Photo = "photo.jpg",
            PhotoDni = "dni.jpg"
        };
        _userRepositoryMock.Setup(repo => repo.GetByIdAsync(clientRequest.UserId)).ReturnsAsync((User)null);

        // Act & Assert
        Assert.ThrowsAsync<UserNotFoundException>(() => _clientService.CreateClientAsync(clientRequest));
    }

    [Test]
    public async Task CreateClientAsync_AddsClient()
    {
        // Arrange
        var clientRequest = new ClientRequest
        {
            FullName = "John Doe",
            Address = "Address 1",
            UserId = "ValidUser",
            Photo = "photo.jpg",
            PhotoDni = "dni.jpg"
        };
        var user = new User { Id = "ValidUser" };
        _userRepositoryMock.Setup(repo => repo.GetByIdAsync(clientRequest.UserId)).ReturnsAsync(user);

        // Act
        await _clientService.CreateClientAsync(clientRequest);

        // Assert
        _clientRepositoryMock.Verify(repo => repo.AddAsync(It.Is<Client>(c => c.FullName == "John Doe")), Times.Once);
    }

    [Test]
    public void UpdateClientAsync_ThrowsClientNotFoundException_WhenClientDoesNotExist()
    {
        // Arrange
        var clientId = "1";
        var updateRequest = new ClientUpdateRequest { FullName = "Updated Name", Address = "Updated Address" };
        _clientRepositoryMock.Setup(repo => repo.GetByIdAsync(clientId)).ReturnsAsync((Client)null);

        // Act & Assert
        Assert.ThrowsAsync<ClientExceptions.ClientNotFoundException>(() => _clientService.UpdateClientAsync(clientId, updateRequest));
    }

    [Test]
    public async Task UpdateClientAsync_UpdatesClient()
    {
        // Arrange
        var clientId = "1";
        var updateRequest = new ClientUpdateRequest { FullName = "Updated Name", Address = "Updated Address" };
        var existingClient = new Client { Id = clientId, FullName = "Old Name", Adress = "Old Address" };

        _clientRepositoryMock.Setup(repo => repo.GetByIdAsync(clientId)).ReturnsAsync(existingClient);

        // Act
        await _clientService.UpdateClientAsync(clientId, updateRequest);

        // Assert
        _clientRepositoryMock.Verify(repo => repo.UpdateAsync(It.Is<Client>(c => c.FullName == "Updated Name" && c.Adress == "Updated Address")), Times.Once);
    }

    [Test]
    public void LogicDeleteClientAsync_ThrowsClientNotFoundException_WhenClientDoesNotExist()
    {
        // Arrange
        var clientId = "1";
        _clientRepositoryMock.Setup(repo => repo.GetByIdAsync(clientId)).ReturnsAsync((Client)null);

        // Act & Assert
        Assert.ThrowsAsync<ClientExceptions.ClientNotFoundException>(() => _clientService.LogicDeleteClientAsync(clientId));
    }

    [Test]
    public async Task LogicDeleteClientAsync_SetsClientIsDeleted()
    {
        // Arrange
        var clientId = "1";
        var existingClient = new Client { Id = clientId, IsDeleted = false };
        _clientRepositoryMock.Setup(repo => repo.GetByIdAsync(clientId)).ReturnsAsync(existingClient);

        // Act
        await _clientService.LogicDeleteClientAsync(clientId);

        // Assert
        _clientRepositoryMock.Verify(repo => repo.UpdateAsync(It.Is<Client>(c => c.IsDeleted == true)), Times.Once);
    }
}
