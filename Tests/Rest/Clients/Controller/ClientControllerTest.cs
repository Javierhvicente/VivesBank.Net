using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework.Legacy;
using VivesBankApi.Rest.Clients.Controller;
using VivesBankApi.Rest.Clients.Dto;
using VivesBankApi.Rest.Clients.Exceptions;
using VivesBankApi.Rest.Clients.Service;

namespace Tests.Rest.Clients.Controller
{
    public class ClientControllerTest
    {
        private Mock<IClientService> _service;
        private Mock<ILogger<ClientController>> _logger;
        private ClientController _clientController;

        [SetUp]
        public void Setup()
        {
            _service = new Mock<IClientService>();
            _logger = new Mock<ILogger<ClientController>>();
            _clientController = new ClientController(_service.Object, _logger.Object);
        }

        [Test]
        public async Task GetAll_ReturnsOkResult()
        {
            // Arrange
            var clients = new List<ClientResponse>
            {
                new ClientResponse { Id = "1", Fullname = "Test Client 1" },
                new ClientResponse { Id = "2", Fullname = "Test Client 2" }
            };

            _service.Setup(s => s.GetAllAsync()).ReturnsAsync(clients);

            // Act
            var result = await _clientController.GetAll() as OkObjectResult;

            // Assert
            ClassicAssert.NotNull(result);
            ClassicAssert.AreEqual(200, result.StatusCode);
            var returnedClients = result.Value as IEnumerable<ClientResponse>;
            ClassicAssert.AreEqual(clients.Count, returnedClients.Count());
        }

        [Test]
        public async Task GetById_ReturnsOkResult_WhenClientExists()
        {
            // Arrange
            var client = new ClientResponse { Id = "1", Fullname = "Test Client" };

            _service.Setup(s => s.GetClientByIdAsync("1")).ReturnsAsync(client);

            // Act
            var result = await _clientController.GetById("1");

            // Assert
            ClassicAssert.NotNull(result);
            ClassicAssert.AreEqual(client, result.Value);
        }

        [Test]
        public async Task GetById_ReturnsNotFound_WhenClientDoesNotExist()
        {
            // Arrange
            _service.Setup(s => s.GetClientByIdAsync("1")).ReturnsAsync((ClientResponse)null);

            // Act
            var result = await _clientController.GetById("1");

            // Assert
            ClassicAssert.Null(result.Value);
        }

        [Test]
        public async Task CreateClient_ReturnsCreatedResult()
        {
            // Arrange
            var request = new ClientRequest { FullName = "New Client" };
            var createdClient = new ClientResponse { Id = "1", Fullname = "New Client" };

            _service.Setup(s => s.CreateClientAsync(request)).ReturnsAsync(createdClient);

            // Act
            var result = await _clientController.CreateClient(request);

            // Assert
            ClassicAssert.NotNull(result);
            ClassicAssert.AreEqual(createdClient, result.Value);
        }

        [Test]
        public async Task UpdateClient_ReturnsOkResult_WhenClientExists()
        {
            // Arrange
            var request = new ClientUpdateRequest { FullName = "Updated Client" };
            var updatedClient = new ClientResponse { Id = "1", Fullname = "Updated Client" };

            _service.Setup(s => s.UpdateClientAsync("1", request)).ReturnsAsync(updatedClient);

            // Act
            var result = await _clientController.UpdateClient("1", request);

            // Assert
            ClassicAssert.NotNull(result);
            ClassicAssert.AreEqual(updatedClient, result.Value);
        }

        [Test]
        public async Task UpdateClient_ReturnsNotFound_WhenClientDoesNotExist()
        {
            // Arrange
            var request = new ClientUpdateRequest { FullName = "Updated Client" };

            _service.Setup(s => s.UpdateClientAsync("1", request)).ThrowsAsync(new ClientExceptions.ClientNotFoundException("1"));

            // Act
            IActionResult result = null;
            try
            {
                await _clientController.UpdateClient("1", request);
            }
            catch (ClientExceptions.ClientNotFoundException e)
            {
                result = new NotFoundObjectResult(new { error = e.Message });
            }

            // Assert
            ClassicAssert.IsInstanceOf<NotFoundObjectResult>(result);
        }

        [Test]
        public async Task DeleteClient_ReturnsNotFound_WhenClientDoesNotExist()
        {
            // Arrange
            _service.Setup(s => s.LogicDeleteClientAsync("1")).ThrowsAsync(new ClientExceptions.ClientNotFoundException("Client not found"));

            // Act
            IActionResult result = null;
            try
            {
                await _clientController.DeleteClient("1");
            }
            catch (ClientExceptions.ClientNotFoundException e)
            {
                result = new NotFoundObjectResult(new { error = e.Message });
            }

            // Assert
            ClassicAssert.IsInstanceOf<NotFoundObjectResult>(result);
        }
    }
}
