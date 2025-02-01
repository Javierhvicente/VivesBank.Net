﻿using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NUnit.Framework.Legacy;

namespace Tests.Rest.Product.CreditCard.Controller;

using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;
using VivesBankApi.Rest.Product.CreditCard.Controller;
using VivesBankApi.Rest.Product.CreditCard.Dto;
using VivesBankApi.Rest.Product.CreditCard.Service;

[TestFixture]
public class CreditCardControllerTest
{
    private Mock<ICreditCardService> _mockService;
    private Mock<ILogger<CreditCardController>> _mockLogger;
    private CreditCardController _controller;


    [SetUp]
    public void SetUp()
    {
        _mockService = new Mock<ICreditCardService>();
        _mockLogger = new Mock<ILogger<CreditCardController>>();
        _controller = new CreditCardController(_mockService.Object, _mockLogger.Object);
    }

    [Test]
    public async Task GetAllCardsAdminAsyncReturnsOk()
    {
        var cards = new List<CreditCardAdminResponse>
        {
            new() { Id = "1", CardNumber = "1234" },
            new() { Id = "2", CardNumber = "5678" }
        };
        
        var result = await _controller.GetAllCardsAdminAsync();

        var okResult = result.Result as OkObjectResult;
        ClassicAssert.IsNotNull(okResult);
        ClassicAssert.AreEqual(200, okResult.StatusCode);
        ClassicAssert.AreEqual(cards, okResult.Value);
    }

    [Test]
    public async Task GetCardByIdAdminAsyncReturnsOk()
    {
        var cardId = "1";
        var card = new CreditCardAdminResponse { Id = cardId, CardNumber = "1234" };

        _mockService.Setup(service => service.GetCreditCardByIdAdminAsync(cardId)).ReturnsAsync(card);

        var result = await _controller.GetCardByIdAdminAsync(cardId);

        var okResult = result.Result as OkObjectResult;
        ClassicAssert.IsNotNull(okResult);
        ClassicAssert.AreEqual(200, okResult.StatusCode);
        ClassicAssert.AreEqual(card, okResult.Value);
    }

    [Test]
    public async Task GetCardByIdAdminAsyncNotExist()
    {
        var cardId = "99";
        _mockService.Setup(service => service.GetCreditCardByIdAdminAsync(cardId)).ReturnsAsync((CreditCardAdminResponse?)null);

        var result = await _controller.GetCardByIdAdminAsync(cardId);

        var okResult = result.Result as OkObjectResult;
        ClassicAssert.IsNotNull(okResult);
        ClassicAssert.AreEqual(200, okResult.StatusCode);
        ClassicAssert.IsNull(okResult.Value);
    }

    [Test]
    public async Task CreateCardAsyncReturnsCreated()
    {
        var createRequest = new CreditCardRequest { CardNumber = "1234" };
        var createdCard = new CreditCardClientResponse { Id = "1", CardNumber = "1234" };

        _mockService.Setup(service => service.CreateCreditCardAsync(createRequest)).ReturnsAsync(createdCard);

        var result = await _controller.CreateCardAsync(createRequest);

        var createdAtActionResult = result.Result as CreatedAtActionResult;
        ClassicAssert.IsNotNull(createdAtActionResult);
        ClassicAssert.AreEqual(201, createdAtActionResult.StatusCode);
        ClassicAssert.AreEqual("GetCardByIdAdminAsync", createdAtActionResult.ActionName);
        ClassicAssert.AreEqual(createdCard, createdAtActionResult.Value);
    }

    
    [Test]
    public async Task DeleteCardAsyncReturnsNoContent()
    {
        var cardId = "1";
        _mockService.Setup(service => service.DeleteCreditCardAsync(cardId)).Returns(Task.CompletedTask);

        var result = await _controller.DeleteCardAsync(cardId);

        var noContentResult = result as NoContentResult;
        ClassicAssert.IsNotNull(noContentResult);
        ClassicAssert.AreEqual(204, noContentResult.StatusCode);
    }
    
    [Test]
    public async Task DeleteCardAsync_WhenCardNotExists_ReturnsNotFound()
    {
        var cardId = "99";
        _mockService.Setup(service => service.DeleteCreditCardAsync(cardId))
            .ThrowsAsync(new System.Collections.Generic.KeyNotFoundException()); 

        var result = await _controller.DeleteCardAsync(cardId);

        var notFoundResult = result as NotFoundResult;
        ClassicAssert.IsNotNull(notFoundResult, "Result should be of type NotFoundResult.");
        ClassicAssert.AreEqual(404, notFoundResult.StatusCode, "StatusCode should be 404.");
    }
    
    [Test]
    public async Task ImportCreditCardsFromJson_WhenValidFile_ReturnsOkResult()
    {
        // Arrange
        var mockFile = new Mock<IFormFile>();

        var fileContent = "[{\"Id\": \"1\", \"AccountId\": \"1\", \"CardNumber\": \"1234567890123456\", \"Pin\": \"123\", \"Cvc\": \"123\", \"ExpirationDate\": \"2025-12-31\", \"CreatedAt\": \"2023-01-01T00:00:00\", \"UpdatedAt\": \"2023-01-01T00:00:00\", \"IsDeleted\": false}]";
        var fileStream = new MemoryStream(Encoding.UTF8.GetBytes(fileContent));

        mockFile.Setup(f => f.OpenReadStream()).Returns(fileStream);
        mockFile.Setup(f => f.Length).Returns(fileStream.Length);
        mockFile.Setup(f => f.FileName).Returns("creditcards.json");
        mockFile.Setup(f => f.ContentType).Returns("application/json");

        var creditCardServiceMock = new Mock<ICreditCardService>();
        var loggerMock = new Mock<ILogger<CreditCardController>>();

        var controller = new CreditCardController(creditCardServiceMock.Object, loggerMock.Object);

        var result = await controller.ImportCreditCardsFromJson(mockFile.Object);

        ClassicAssert.IsInstanceOf<OkObjectResult>(result);  

        var okResult = result as OkObjectResult;
        ClassicAssert.IsNotNull(okResult);
        var creditCards = okResult.Value as List<VivesBankApi.Rest.Product.CreditCard.Models.CreditCard>;
        ClassicAssert.IsNotNull(creditCards);
        ClassicAssert.AreEqual(1, creditCards.Count);
        ClassicAssert.AreEqual("1", creditCards[0].Id);
        ClassicAssert.AreEqual("1234567890123456", creditCards[0].CardNumber);
        ClassicAssert.AreEqual("123", creditCards[0].Pin);
    }
}