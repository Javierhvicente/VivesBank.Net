using System.IO.Compression;
using System.Text.Json;
using VivesBankApi.Backup;
using VivesBankApi.Backup.Exceptions;
using VivesBankApi.Backup.Service;
using VivesBankApi.Rest.Clients.Models;
using VivesBankApi.Rest.Clients.Repositories;
using VivesBankApi.Rest.Movimientos.Models;
using VivesBankApi.Rest.Movimientos.Repositories.Movimientos;
using VivesBankApi.Rest.Product.BankAccounts.Models;
using VivesBankApi.Rest.Product.BankAccounts.Repositories;
using VivesBankApi.Rest.Product.Base.Models;
using VivesBankApi.Rest.Product.CreditCard.Models;
using VivesBankApi.Rest.Users.Models;
using VivesBankApi.Rest.Users.Repository;
using Path = System.IO.Path;

namespace VivesBankApi.Utils.Backup
{
    /// <summary>
    /// Servicio encargado de manejar las operaciones de respaldo como la exportación e importación de datos.
    /// @author Raul Fernandez, Samuel Cortes, Javier Hernandez, Alvaro Herrero, German, Tomas
    /// </summary>
    public class BackupService : IBackupService
    {
        // Nombre del directorio temporal usado durante las operaciones de respaldo/exportación
        private static readonly string TempDirName = "StorageServiceTemp";

        // Dependencias inyectadas a través del constructor
        private readonly ILogger<BackupService> _logger;
        private readonly IClientRepository _clientRepository;
        private readonly IUserRepository _userRepository;
        private readonly IProductRepository _productRepository;
        private readonly ICreditCardRepository _creditCardRepository;
        private readonly IAccountsRepository _bankAccountRepository;
        private readonly IMovimientoRepository _movimientoRepository;

        /// <summary>
        /// Constructor que inyecta las dependencias necesarias para el funcionamiento del servicio.
        /// </summary>
        /// <param name="logger">Logger para registrar eventos y errores</param>
        /// <param name="clientService">Servicio de clientes</param>
        /// <param name="userService">Servicio de usuarios</param>
        /// <param name="productService">Servicio de productos</param>
        /// <param name="creditCardService">Servicio de tarjetas de crédito</param>
        /// <param name="bankAccountService">Servicio de cuentas bancarias</param>
        /// <param name="movementService">Servicio de movimientos</param>
        public BackupService(
            ILogger<BackupService> logger,
            IClientRepository clientRepository,
            IUserRepository userRepository,
            IProductRepository productRepository,
            ICreditCardRepository creditCardRepository,
            IAccountsRepository bankAccountRepository,
            IMovimientoRepository movimientoRepository)
        {
            _logger = logger;
            _clientRepository = clientRepository;
            _userRepository = userRepository;
            _productRepository = productRepository;
            _creditCardRepository = creditCardRepository;
            _bankAccountRepository = bankAccountRepository;
            _movimientoRepository = movimientoRepository;
        }

        /// <summary>
        /// Exporta los datos a un archivo ZIP.
        /// </summary>
        /// <param name="zipRequest">Objeto que contiene la solicitud de exportación, incluyendo la ruta del archivo ZIP</param>
        /// <returns>Ruta del archivo ZIP exportado</returns>
        public async Task<string> ExportToZip(BackUpRequest zipRequest)
        {
            try
            {
                if (string.IsNullOrEmpty(zipRequest.FilePath))
                {
                    throw new ArgumentException("La ruta del archivo no puede estar vacía.");
                }

                var directory = Path.GetDirectoryName(zipRequest.FilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tempDir = Path.Combine(Directory.GetCurrentDirectory(), TempDirName);
                Directory.CreateDirectory(tempDir);

                await ExportJsonFiles(tempDir);

                ZipFile.CreateFromDirectory(tempDir, zipRequest.FilePath);

                Directory.Delete(tempDir, true);

                return zipRequest.FilePath;
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new BackupException.BackupPermissionException("No se tienen permisos suficientes para crear el archivo ZIP.", ex);
            }
        }

        /// <summary>
        /// Importa los datos desde un archivo ZIP.
        /// </summary>
        /// <param name="zipFilePath">Objeto que contiene la ruta del archivo ZIP a importar</param>
        public async Task ImportFromZip(BackUpRequest zipFilePath)
        {
            _logger.LogInformation($"Importando datos desde ZIP: {zipFilePath.FilePath}");

            if (!File.Exists(zipFilePath.FilePath))
            {
                throw new BackupException.BackupFileNotFoundException($"El archivo {zipFilePath.FilePath} no fue encontrado.");
            }

            var tempDir = Path.Combine(Directory.GetCurrentDirectory(), TempDirName);

            try
            {
                Directory.CreateDirectory(tempDir);
                ExtractZip(zipFilePath, tempDir);
                await ImportJsonFiles(tempDir);

                _logger.LogInformation("Datos importados exitosamente desde ZIP: {ZipFilePath}", zipFilePath);
            }
            catch (BackupException.BackupFileNotFoundException ex)
            {
                _logger.LogError(ex, "Error: archivo ZIP no encontrado.");
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Error de permisos al acceder al archivo ZIP.");
                throw new BackupException.BackupPermissionException("No se tienen permisos suficientes para acceder al archivo ZIP.", ex);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Error: el archivo JSON es corrupto o no válido.");
                throw new BackupException.BackupPermissionException("El archivo JSON dentro del ZIP es corrupto o no es válido.", ex);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }        
        private async Task ImportJsonFiles(string tempDir)
        {
            foreach (var file in Directory.GetFiles(tempDir, "*.json"))
            {
                try
                {
                    var fileName = Path.GetFileName(file);
                    var content = await File.ReadAllTextAsync(file);

                    switch (fileName)
                    {

                        case "users.json":
                            var users = JsonSerializer.Deserialize<List<User>>(content);
                            if (users != null)
                            {
                                foreach (var user in users)
                                {
                                    var existingUser = await _userRepository.GetByIdAsync(user.Id); 
                                    if (existingUser == null)
                                    {
                                        await _userRepository.AddAsync(user);
                                    }
                                    else
                                    {
                                        _logger.LogWarning($"Usuario con Id {user.Id} ya existe en la base de datos.");
                                    }
                                }
                            }
                            break;
                        
                        case "clients.json":
                            var clients = JsonSerializer.Deserialize<List<Client>>(content);
                            if (clients != null)
                            {
                                foreach (var client in clients)
                                {
                                    var existingClient = await _clientRepository.GetByIdAsync(client.Id); 
                                    if (existingClient == null)
                                    {
                                        await _clientRepository.AddAsync(client);
                                    }
                                    else
                                    {
                                        _logger.LogWarning($"Cliente con Id {client.Id} ya existe en la base de datos.");
                                    }
                                }
                            }
                            break;

                        case "products.json":
                            var products = JsonSerializer.Deserialize<List<Product>>(content);
                            if (products != null)
                            {
                                foreach (var product in products)
                                {
                                    var existingProduct = await _productRepository.GetByIdAsync(product.Id); 
                                    if (existingProduct == null)
                                    {
                                        await _productRepository.AddAsync(product);
                                    }
                                    else
                                    {
                                        _logger.LogWarning($"Producto con Id {product.Id} ya existe en la base de datos.");
                                    }
                                }
                            }
                            break;

                        case "creditCards.json":
                            var creditCards = JsonSerializer.Deserialize<List<CreditCard>>(content);
                            if (creditCards != null)
                            {
                                foreach (var creditCard in creditCards)
                                {
                                    var existingCreditCard = await _creditCardRepository.GetByIdAsync(creditCard.Id); 
                                    if (existingCreditCard == null)
                                    {
                                        await _creditCardRepository.AddAsync(creditCard);
                                    }
                                    else
                                    {
                                        _logger.LogWarning($"Tarjeta de crédito con Id {creditCard.Id} ya existe en la base de datos.");
                                    }
                                }
                            }
                            break;

                        case "bankAccounts.json":
                            var bankAccounts = JsonSerializer.Deserialize<List<Account>>(content);
                            if (bankAccounts != null)
                            {
                                foreach (var bankAccount in bankAccounts)
                                {
                                    var existingBankAccount = await _bankAccountRepository.GetByIdAsync(bankAccount.Id); 
                                    if (existingBankAccount == null)
                                    {
                                        await _bankAccountRepository.AddAsync(bankAccount);
                                    }
                                    else
                                    {
                                        _logger.LogWarning($"Cuenta bancaria con Id {bankAccount.Id} ya existe en la base de datos.");
                                    }
                                }
                            }
                            break;

                        case "movements.json":
                            var movements = JsonSerializer.Deserialize<List<Movimiento>>(content);
                            if (movements != null)
                            {
                                foreach (var movement in movements)
                                {
                                    var existingMovement = await _movimientoRepository.GetMovimientoByIdAsync(movement.Id); 
                                    if (existingMovement == null)
                                    {
                                        await _movimientoRepository.AddMovimientoAsync(movement);
                                    }
                                    else
                                    {
                                        _logger.LogWarning($"Movimiento con Id {movement.Id} ya existe en la base de datos.");
                                    }
                                }
                            }
                            break;

                        default:
                            _logger.LogWarning($"Archivo JSON no reconocido: {fileName}");
                            break;
                    }

                    _logger.LogInformation($"Datos importados exitosamente desde el archivo: {fileName}");
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, $"Error al procesar el archivo JSON: {file}");
                    throw new BackupException.BackupPermissionException($"El archivo JSON {file} es corrupto o no válido.", ex);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error al importar datos desde el archivo: {file}");
                    throw new BackupException.BackupPermissionException($"Hubo un error al importar datos desde el archivo {file}.", ex);
                }
            }
        }        
        /// <summary>
        /// Extrae los archivos desde un archivo ZIP a un directorio temporal.
        /// </summary>
        /// <param name="zipFilePath">Ruta del archivo ZIP a extraer</param>
        /// <param name="tempDir">Directorio temporal donde se extraerán los archivos</param>
        private void ExtractZip(BackUpRequest zipFilePath, string tempDir)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(zipFilePath.FilePath))
                {
                    foreach (var entry in zip.Entries)
                    {
                        var filePath = Path.Combine(tempDir, entry.FullName);
                        Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                        entry.ExtractToFile(filePath, true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al extraer el archivo ZIP");
                throw new BackupException.BackupPermissionException("Hubo un error al extraer el archivo ZIP.", ex);
            }
        }
        
        /// <summary>
        /// Exporta los archivos JSON con los datos de clientes, usuarios, productos, etc.
        /// </summary>
        /// <param name="directoryPath">Ruta del directorio donde se guardarán los archivos JSON</param>
        private async Task ExportJsonFiles(string directoryPath)
        {
            try
            {
                _logger.LogInformation("Exportando archivos JSON a {DirectoryPath}", directoryPath);

                var clientEntities = await _clientRepository.GetAllAsync();
                var userEntities = await _userRepository.GetAllAsync();
                var productEntities = await _productRepository.GetAllAsync();
                var creditCardEntities = await _creditCardRepository.GetAllAsync();
                var bankAccountEntities = await _bankAccountRepository.GetAllAsync();

                var movementEntities = await _movimientoRepository.GetAllMovimientosAsync(); 

                await File.WriteAllTextAsync(Path.Combine(directoryPath, "clients.json"), JsonSerializer.Serialize(clientEntities));
                await File.WriteAllTextAsync(Path.Combine(directoryPath, "users.json"), JsonSerializer.Serialize(userEntities));
                await File.WriteAllTextAsync(Path.Combine(directoryPath, "products.json"), JsonSerializer.Serialize(productEntities));
                await File.WriteAllTextAsync(Path.Combine(directoryPath, "creditCards.json"), JsonSerializer.Serialize(creditCardEntities));
                await File.WriteAllTextAsync(Path.Combine(directoryPath, "bankAccounts.json"), JsonSerializer.Serialize(bankAccountEntities));
                await File.WriteAllTextAsync(Path.Combine(directoryPath, "movements.json"), JsonSerializer.Serialize(movementEntities));

                _logger.LogInformation("Archivos JSON exportados exitosamente: {Files}", string.Join(", ", Directory.GetFiles(directoryPath)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al exportar los archivos JSON");
                throw new BackupException.BackupPermissionException("Hubo un error al exportar los archivos JSON.", ex);
            }
        }
    }    

}