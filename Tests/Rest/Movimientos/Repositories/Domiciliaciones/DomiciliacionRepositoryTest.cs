﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using VivesBankApi.Rest.Movimientos.Config;
using VivesBankApi.Rest.Movimientos.Models;
using VivesBankApi.Rest.Movimientos.Repositories.Domiciliaciones;
using NUnit.Framework;
using MongoDB.Bson;
using Mongo2Go;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using VivesBankApi.utils.GuuidGenerator;

namespace Tests.Rest.Movimientos.Repositories.Domiciliaciones;

[TestFixture]
[TestOf(typeof(DomiciliacionRepository))]
public class DomiciliacionRepositoryTest
{
    private MongoDbRunner _mongoDbRunner;
    private IMongoDatabase _database;
    private IMongoCollection<Domiciliacion> _collection;
    private Mock<ILogger<DomiciliacionRepository>> _mockLogger;
    private DomiciliacionRepository _repository;
    private Mock<IOptions<MongoDatabaseConfig>> _mockMongoDatabaseSettings;
    private readonly string _dataBaseName = "TestDatabase";

    [SetUp]
    public void SetUp()
    {
        // Inicializar MongoDB en memoria
        _mongoDbRunner = MongoDbRunner.Start();
        
        // Crear configuración de base de datos en memoria
        _mockMongoDatabaseSettings = new Mock<IOptions<MongoDatabaseConfig>>();
        _mockMongoDatabaseSettings.Setup(m => m.Value).Returns(new MongoDatabaseConfig
        {
            ConnectionString = _mongoDbRunner.ConnectionString,
            //DatabaseName = _mongoDbRunner.DatabaseName,
            DatabaseName = _dataBaseName,
            DomiciliacionCollectionName = "Domiciliaciones"
        });

        // Conectar a la base de datos en memoria
        var client = new MongoClient(_mongoDbRunner.ConnectionString);
        _database = client.GetDatabase(_dataBaseName);
        _collection = _database.GetCollection<Domiciliacion>("Domiciliaciones");

        // Mock de Logger
        _mockLogger = new Mock<ILogger<DomiciliacionRepository>>();

        // Crear el repositorio
        _repository = new DomiciliacionRepository(
            _mockMongoDatabaseSettings.Object,
            _mockLogger.Object
        );
    }

    [TearDown]
    public void TearDown()
    {
        // Detener el servidor en memoria
        _mongoDbRunner.Dispose();
    }

    [Test]
    public async Task GetAllDomiciliacionesAsync_ReturnsListOfDomiciliaciones()
    {
        // Arrange
        var expectedList = new List<Domiciliacion>
        {
            new Domiciliacion { Id = "1", 
                Guid = GuuidGenerator.GenerateHash(),
                ClienteGuid = "Cliente1",
                IbanOrigen = "ES12345678901234567890",
                IbanDestino = "ES98765432109876543210",
                Cantidad = 100,
                NombreAcreedor = "Acreedor1",
                FechaInicio = new DateTime(2021, 1, 1),
                Periodicidad = Periodicidad.SEMANAL,
                Activa = true,
                UltimaEjecucion = new DateTime(2024, 1, 1)
            },
            new Domiciliacion { Id = "2", 
                Guid = GuuidGenerator.GenerateHash(),
                ClienteGuid = "Cliente1",
                IbanOrigen = "ES12345678901234567890",
                IbanDestino = "ES98765432109876543210",
                Cantidad = 200,
                NombreAcreedor = "Acreedor2",
                FechaInicio = new DateTime(2024, 10, 23),
                Periodicidad = Periodicidad.ANUAL,
                Activa = true,
                UltimaEjecucion = new DateTime(2025, 1, 2)
            }
        };

        await _collection.InsertManyAsync(expectedList);

        // Act
        var result = await _repository.GetAllDomiciliacionesAsync();

        // Assert
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("Domiciliacion1", result[0].Nombre);
        Assert.AreEqual("Domiciliacion2", result[1].Nombre);
    }
}