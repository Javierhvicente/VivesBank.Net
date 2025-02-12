using System.Security.Claims;
using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework.Legacy;
using VivesBankApi.Rest.Clients.Controller;
using VivesBankApi.Rest.Clients.Dto;
using VivesBankApi.Rest.Clients.Exceptions;
using VivesBankApi.Rest.Clients.Models;
using VivesBankApi.Rest.Clients.Service;
using VivesBankApi.Rest.Movimientos.Models;
using VivesBankApi.Rest.Movimientos.Services.Movimientos;
using VivesBankApi.Rest.Movimientos.Storage;
using VivesBankApi.Rest.Users.Exceptions;
using VivesBankApi.Rest.Users.Models;
using Path = System.IO.Path;


namespace Tests.Rest.Clients.Controller;

public class ClientControllerTest
{
    private Mock<IClientService> _service;
    private Mock<ILogger<ClientController>> _logger;
    private Mock<IMovimientoStoragePDF> _movimientoStoragePdf;
    private Mock<IMovimientoService> _movimientoService;
    private ClientController _clientController;

    [SetUp]
    public void Setup()
    {
        _service = new Mock<IClientService>();
        _logger = new Mock<ILogger<ClientController>>();
        _movimientoStoragePdf = new Mock<IMovimientoStoragePDF>();
        _movimientoService = new Mock<IMovimientoService>();
        _clientController = new ClientController(_service.Object, _logger.Object, _movimientoService.Object, _movimientoStoragePdf.Object);
    }

    
    [Test]
    public async Task GetAllUsersAsync_ReturnsPaginatedResults()
    {
        // Arrange
        var pageNumber = 1;
        var pageSize = 2;
        var fullName = "Test";
        bool? isDeleted = false;
        var direction = "asc";

        var pagedList = new PagedList<ClientResponse>(
            new List<ClientResponse>
            {
                new ClientResponse { Id = "1", Fullname = "Test Client 1" },
                new ClientResponse { Id = "2", Fullname = "Test Client 2" }
            },
            totalCount: 5,
            pageNumber: pageNumber,
            pageSize: pageSize
        );

        _service.Setup(s => s.GetAllClientsAsync(pageNumber, pageSize, fullName, isDeleted, direction))
            .ReturnsAsync(pagedList);

        // Act
        var result = await _clientController.GetAllUsersAsync(pageNumber, pageSize, fullName, isDeleted, direction) as ActionResult<PageResponse<ClientResponse>>;

        // Assert
        ClassicAssert.NotNull(result);
        ClassicAssert.NotNull(result.Value);

        var pageResponse = result.Value;
        ClassicAssert.AreEqual(pagedList.TotalCount, pageResponse.TotalElements);
        ClassicAssert.AreEqual(pagedList.PageCount, pageResponse.TotalPages);
        ClassicAssert.AreEqual(pagedList.PageSize, pageResponse.PageSize);
        ClassicAssert.AreEqual(pagedList.PageNumber, pageResponse.PageNumber);
        ClassicAssert.AreEqual(pagedList.Count, pageResponse.TotalPageElements);
        ClassicAssert.AreEqual(pagedList.ToList(), pageResponse.Content);
        ClassicAssert.AreEqual(direction, pageResponse.Direction);
        ClassicAssert.AreEqual("fullName", pageResponse.SortBy);
        ClassicAssert.AreEqual(pagedList.IsFirstPage, pageResponse.First);
        ClassicAssert.AreEqual(pagedList.IsLastPage, pageResponse.Last);
        ClassicAssert.AreEqual(pagedList.Count == 0, pageResponse.Empty);
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
    public async Task GetMyClientData_WhenUserIsNotAuthenticated()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity()); // Usuario vacío (no autenticado)

        _clientController.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        // Act
        var result = await _clientController.GetMyClientData();

        // Assert
        Assert.That(result.Result, Is.InstanceOf<UnauthorizedResult>());
    }

    [Test]
    public async Task GetMyClientData_whenUserIsUnAuthorized()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new Claim[] { new Claim(ClaimTypes.Role, "Admin") }));

        _clientController.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
        
        var result = await _clientController.GetMyClientData();
        
        Assert.That(result.Result, Is.InstanceOf<UnauthorizedResult>());
    }

    [Test]
    public async Task CreateClientAsUser_ShouldReturnToken()
    {
        var request = new ClientRequest { FullName = "Test Client", Address = "Test Address" };
        var token = "test_token";

        _service.Setup(s => s.CreateClientAsync(request)).ReturnsAsync(token);
        
        var result = await _clientController.CreateClientAsUser(request);
        
        ClassicAssert.NotNull(result);
        ClassicAssert.IsInstanceOf<OkObjectResult>(result);

        var okResult = result as OkObjectResult;
        ClassicAssert.NotNull(okResult.Value);
        var responseObject = JObject.FromObject(okResult.Value);
        string returnedToken = responseObject["client"]?.ToString();

        ClassicAssert.AreEqual(token, returnedToken);
    }

    [Test]
    public async Task CreateClientAsUser_UserDontExists()
    {
        var request = new ClientRequest { FullName = "Updated Client" };
        var id = "qq";
        var createdClient = new ClientResponse { Id = "1", Fullname = "Updated Client" };
        var token = "test_token";
        
        _service.Setup(s => s.CreateClientAsync( request)).ThrowsAsync(new UserNotFoundException(id));
        var ex = Assert.ThrowsAsync<UserNotFoundException>(async () => 
            await _clientController.CreateClientAsUser(request));
    
        Assert.That(ex.Message, Is.EqualTo($"The user with id: {id} was not found"));
    }

    [Test]
    public async Task CreateClientAsUser_AlreadyExists()
    {
        var request = new ClientRequest { FullName = "Updated Client" };
        var id = "1";
        var createdClient = new ClientResponse { Id = "1", Fullname = "Updated Client" };
        var token = "test_token";

        _service.Setup(s => s.CreateClientAsync(request))
            .ThrowsAsync(new ClientExceptions.ClientAlreadyExistsException(id));
        var ex = Assert.ThrowsAsync<ClientExceptions.ClientAlreadyExistsException>(async () =>
            await _clientController.CreateClientAsUser(request));

        Assert.That(ex.Message, Is.EqualTo($"A client already exists with this user id 1"));
    }

    [Test]
    public async Task CreateClientAsUser_BadRequest_underLimit()
    {
        var invalidRequest = new ClientRequest 
        { 
            FullName = "Abc",
            Address = "Short"
        };
        
        _clientController.ModelState.AddModelError("FullName", "The name must be at least 5 characters");
        _clientController.ModelState.AddModelError("Address", "The address must be at least 10 characters");
        
        var result = await _clientController.CreateClientAsUser(invalidRequest);
        
        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult!.StatusCode, Is.EqualTo(400));

        var errors = badRequestResult.Value as SerializableError;
        Assert.That(errors, Is.Not.Null);
        Assert.That(errors!.ContainsKey("FullName"));
        Assert.That(errors.ContainsKey("Address"));
    }
    
    [Test]
    public async Task CreateClientAsUser_BadRequest_AboveLimit()
    {
        var invalidRequest = new ClientRequest 
        { 
            FullName = "Abcaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            Address = "Shortaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        };
        
        _clientController.ModelState.AddModelError("FullName", "The name must me at most 50 characters");
        _clientController.ModelState.AddModelError("Address", "The address must me at most 100 characters");
        
        var result = await _clientController.CreateClientAsUser(invalidRequest);
        
        var badRequestResult = result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult!.StatusCode, Is.EqualTo(400));

        var errors = badRequestResult.Value as SerializableError;
        Assert.That(errors, Is.Not.Null);
        Assert.That(errors!.ContainsKey("FullName"));
        Assert.That(errors.ContainsKey("Address"));
    }

    [Test]
    public async Task UpdateMeClient_SholdReturn_ClientResponse()
    {
        var updateRequest = new ClientUpdateRequest
        {
            FullName = "Updated Client",
            Address = "Updated Address"
        };
        var response = new ClientResponse
        {
            Id = "1",
            Fullname = "Updated Client",
            Address = "Updated Address"
        };
        _service.Setup(s => s.UpdateMeAsync(updateRequest)).ReturnsAsync(response);

        var res = await _clientController.UpdateMeAsClient(updateRequest);
        var okResult = res.Result as OkObjectResult;
        Assert.That(okResult!.StatusCode, Is.EqualTo(200), "Expected status code 200");

        var returnedClient = okResult.Value as ClientResponse;
        Assert.That(returnedClient, Is.Not.Null, "Expected ClientResponse to be not null");
        Assert.That(returnedClient!.Fullname, Is.EqualTo(updateRequest.FullName), "Expected Fullname to be 'Updated Client'");
        Assert.That(returnedClient.Address, Is.EqualTo(updateRequest.Address), "Expected Address to be 'Updated Address'");
    }

    [Test]
    public async Task UpdateMeClient_ClientNotFoundAsync()
    {
        // Arrange
        var updateRequest = new ClientUpdateRequest { FullName = "Updated Client" };

        _service.Setup(s => s.UpdateMeAsync(updateRequest)).ThrowsAsync(new ClientExceptions.ClientNotFoundException("1"));

        var result = Assert.ThrowsAsync<ClientExceptions.ClientNotFoundException>(async () =>
            await _clientController.UpdateMeAsClient(updateRequest));
        // Assert
        ClassicAssert.AreEqual(result.Message, "Client not found by id 1");
    }

    [Test]
    public async Task UpdateMeClient_BadRequest_Aboceimit()
    {
        var invalidRequest = new ClientUpdateRequest 
        { 
            FullName = "Abcaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            Address = "Shortaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        };
        
        _clientController.ModelState.AddModelError("FullName", "FullName must be between 3 and 80 characters.");
        _clientController.ModelState.AddModelError("Address", "Address must be between 3 and 80 characters.");
        
        var result = await _clientController.UpdateMeAsClient(invalidRequest);
        
        var badRequestResult = result.Result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult!.StatusCode, Is.EqualTo(400));

        var errors = badRequestResult.Value as SerializableError;
        Assert.That(errors, Is.Not.Null);
        Assert.That(errors!.ContainsKey("FullName"));
        Assert.That(errors.ContainsKey("Address"));
    }
    
    [Test]
    public async Task UpdateMeClient_BadRequest_UnderLimit()
    {
        var invalidRequest = new ClientUpdateRequest 
        { 
            FullName = "ad",
            Address = "Sh"
        };
        
        _clientController.ModelState.AddModelError("FullName", "FullName must be between 3 and 80 characters.");
        _clientController.ModelState.AddModelError("Address", "Address must be between 3 and 80 characters.");
        
        var result = await _clientController.UpdateMeAsClient(invalidRequest);
        
        var badRequestResult = result.Result as BadRequestObjectResult;
        Assert.That(badRequestResult, Is.Not.Null);
        Assert.That(badRequestResult!.StatusCode, Is.EqualTo(400));

        var errors = badRequestResult.Value as SerializableError;
        Assert.That(errors, Is.Not.Null);
        Assert.That(errors!.ContainsKey("FullName"));
        Assert.That(errors.ContainsKey("Address"));
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
        ClassicAssert.NotNull(result.Result);
        ClassicAssert.IsInstanceOf<OkObjectResult>(result.Result);
        var updatedClientResult = result.Result as OkObjectResult;
        ClassicAssert.NotNull(updatedClientResult.Value);
        ClassicAssert.AreEqual(updatedClient, updatedClientResult.Value);
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

    [Test]
    public async Task DeleteMeAsClient_ShouldLogicDeleteClient()
    {
        // Arrange
        _service.Setup(s => s.DeleteMe()).Returns(Task.CompletedTask);

        // Act
        await _clientController.DeleteMeClient();

        _service.Verify(_service => _service.DeleteMe(), Times.Once());
    }

    [Test]
    public async Task DeleteMeAsClient_NotFound()
    {
        _service.Setup(s => s.DeleteMe()).ThrowsAsync(new ClientExceptions.ClientNotFoundException("1"));

        Assert.ThrowsAsync<ClientExceptions.ClientNotFoundException>(_clientController.DeleteMeClient);
        _service.Verify(_service => _service.DeleteMe(), Times.Once());
    }

    [Test]
    public async Task DeleteMeAsClient_UsreNotFound()
    {
        _service.Setup(s => s.DeleteMe()).ThrowsAsync(new UserNotFoundException("1"));

        Assert.ThrowsAsync<UserNotFoundException>(_clientController.DeleteMeClient);
        _service.Verify(_service => _service.DeleteMe(), Times.Once());
    }

    [Test]
    public async Task ExportMeData_ReturnsJsonFile_WhenExporting()
    {
        // Arrange
        var client = new ClientResponse { Id = "1", Fullname = "Test Client 1" };
        var jsonContent = "{\"Id\":\"1\", \"Fullname\":\"Test Client 1\"}";
    
        _service.Setup(s => s.GettingMyClientData()).ReturnsAsync(client);
        _service.Setup(s => s.ExportOnlyMeData(It.IsAny<Client>()))
            .ReturnsAsync(() =>
            {
                var json = "{\"Id\":\"1\", \"Fullname\":\"Test Client 1\"}";
                var tempFile = System.IO.Path.GetTempFileName();
                File.WriteAllText(tempFile, json);
                return new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Delete);
            });



        // Act
        var result = await _clientController.GetMeDataAsClient();

        // Assert
        ClassicAssert.IsInstanceOf<FileStreamResult>(result);
        var fileResult = result as FileStreamResult;
        ClassicAssert.NotNull(fileResult);
        ClassicAssert.AreEqual("application/json", fileResult.ContentType);

        using var reader = new StreamReader(fileResult.FileStream);
        var returnedJson = await reader.ReadToEndAsync();
        var returnedClient = JsonConvert.DeserializeObject<ClientResponse>(returnedJson);

        ClassicAssert.AreEqual(client.Id, returnedClient.Id);
        ClassicAssert.AreEqual(client.Fullname, returnedClient.Fullname);
    }
    
    [Test]
    public async Task ExportPdf_ReturnsFileStreamResult()
    {
        // Arrange
        var client = new ClientResponse() { Id = "client123" };
        var movimientos = new List<Movimiento>
        {
            new Movimiento { Id = "mov1", ClienteGuid = "client123" },
            new Movimiento { Id = "mov2", ClienteGuid = "client123" }
        };
        var tempFilePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFilePath, "Fake PDF Content");

        var fileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        _service.Setup(s => s.GettingMyClientData()).ReturnsAsync(client);
        _movimientoService.Setup(s => s.FindAllMovimientosByClientAsync(client.Id)).ReturnsAsync(movimientos);
        _movimientoStoragePdf.Setup(s => s.Export(movimientos)).ReturnsAsync(fileStream);

        // Act
        var result = await _clientController.ExportPdf();

        // Assert
        ClassicAssert.IsInstanceOf<FileStreamResult>(result);
        var fileResult = result as FileStreamResult;
        ClassicAssert.AreEqual("application/pdf", fileResult.ContentType);
        ClassicAssert.AreEqual("Movimientos.pdf", fileResult.FileDownloadName);
        ClassicAssert.NotNull(fileResult.FileStream);

        // Cleanup
        fileStream.Dispose();
        File.Delete(tempFilePath);
    }
    
    [Test]
    public async Task GetFileFromFtpAsync_ReturnsFileStreamResult_WhenFileExists()
    {
        // Arrange
        var fileName = "testfile.png";
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "Fake file content");
        var fileStream = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Delete);
        
        _service.Setup(s => s.GetFileFromFtpAsync(fileName)).ReturnsAsync(fileStream);

        // Act
        var result = await _clientController.GetFileFromFtpAsync(fileName);

        // Assert
        ClassicAssert.IsInstanceOf<FileStreamResult>(result);
        var fileResult = result as FileStreamResult;
        ClassicAssert.NotNull(fileResult);
        ClassicAssert.AreEqual("image/png", fileResult.ContentType);
    }

    [Test]
    public async Task GetFileFromFtpAsync_ReturnsNotFound_WhenFileDoesNotExist()
    {
        // Arrange
        var fileName = "nonexistent.png";
        _service.Setup(s => s.GetFileFromFtpAsync(fileName)).ReturnsAsync((FileStream)null);

        // Act
        var result = await _clientController.GetFileFromFtpAsync(fileName);

        // Assert
        ClassicAssert.IsInstanceOf<NotFoundObjectResult>(result);
        var notFoundResult = result as NotFoundObjectResult;
        ClassicAssert.AreEqual(404, notFoundResult.StatusCode);
    }

    [Test]
    public async Task GetMyDniPhotoFromFtpAsync_ReturnsFileStreamResult_WhenFileExists()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, "Fake DNI photo content");
        var fileStream = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Delete);
        
        _service.Setup(s => s.GettingMyDniPhotoFromFtpAsync()).ReturnsAsync(fileStream);

        // Act
        var result = await _clientController.GetMyDniPhotoFromFtpAsync();

        // Assert
        ClassicAssert.IsInstanceOf<FileStreamResult>(result);
        var fileResult = result as FileStreamResult;
        ClassicAssert.NotNull(fileResult);
    }
    
    [Test]
    public async Task UpdateClientDniPhotoFtpAsync_ValidFile_ReturnsOk()
    {
        // Arrange
        var clientId = "123";
        var fileName = "testDni.png";
        var fileMock = new Mock<IFormFile>();
        var fileContent = "test content";
        var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(fileContent));

        fileMock.Setup(f => f.OpenReadStream()).Returns(memoryStream);
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.Length).Returns(memoryStream.Length);

        _service.Setup(s => s.UpdateClientPhotoDniAsync(clientId, It.IsAny<IFormFile>()))
            .ReturnsAsync(fileName);

        // Act
        var result = await _clientController.UpdateClientDniPhotoFtpAsync(clientId, fileMock.Object);

        // Assert
        ClassicAssert.IsInstanceOf<OkObjectResult>(result);
        var okResult = result as OkObjectResult;
        ClassicAssert.NotNull(okResult);
        ClassicAssert.AreEqual(200, okResult.StatusCode);
    }

    [Test]
    public async Task UpdateClientDniPhotoFtpAsync_NoFileProvided_ReturnsBadRequest()
    {
        // Arrange
        var clientId = "123";
        IFormFile file = null;

        // Act
        var result = await _clientController.UpdateClientDniPhotoFtpAsync(clientId, file);

        // Assert
        ClassicAssert.IsInstanceOf<BadRequestObjectResult>(result);
        var badRequestResult = result as BadRequestObjectResult;
        ClassicAssert.NotNull(badRequestResult);
        ClassicAssert.AreEqual(400, badRequestResult.StatusCode);
        ClassicAssert.AreEqual("No file was provided or the file is empty.", badRequestResult.Value);
    }

    [Test]
    public async Task UpdateClientDniPhotoFtpAsync_EmptyFile_ReturnsBadRequest()
    {
        // Arrange
        var clientId = "123";
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(0);

        // Act
        var result = await _clientController.UpdateClientDniPhotoFtpAsync(clientId, fileMock.Object);

        // Assert
        ClassicAssert.IsInstanceOf<BadRequestObjectResult>(result);
        var badRequestResult = result as BadRequestObjectResult;
        ClassicAssert.NotNull(badRequestResult);
        ClassicAssert.AreEqual(400, badRequestResult.StatusCode);
        ClassicAssert.AreEqual("No file was provided or the file is empty.", badRequestResult.Value);
    }
    
    [Test]
    public async Task DeleteMeData_Success()
    {
        var clientResponse = new ClientResponse { Id = "1", Fullname = "Deleted Client" };

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Role, "Client")
        }));

        _clientController.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        _service.Setup(s => s.DeleteMeData()).ReturnsAsync(clientResponse);

        // Act
        var result = await _clientController.DeleteMeData();

        // Assert
        Assert.That(result.Result, Is.InstanceOf<OkObjectResult>());
        var okResult = result.Result as OkObjectResult;
        Assert.That(okResult.Value, Is.EqualTo(clientResponse));
    }
    
    [Test]
    public async Task DeleteMeData_ThrowsUserNotFoundException_WhenClientDoesNotExist()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim(ClaimTypes.NameIdentifier, "999"), // Cliente inexistente
            new Claim(ClaimTypes.Role, "Client")
        }));

        _clientController.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        _service.Setup(s => s.DeleteMeData()).ThrowsAsync(new UserNotFoundException("999"));

        // Act & Assert
        var ex = Assert.ThrowsAsync<UserNotFoundException>(async () =>
            await _clientController.DeleteMeData());

        Assert.That(ex.Message, Is.EqualTo("The user with id: 999 was not found"));
    }
    
}
