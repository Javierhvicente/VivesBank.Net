using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VivesBankApi.Rest.Clients.Dto;
using VivesBankApi.Rest.Clients.Mappers;
using VivesBankApi.Rest.Clients.Service;
using VivesBankApi.Rest.Clients.storage.Config;
using VivesBankApi.Rest.Movimientos.Services.Movimientos;
using VivesBankApi.Rest.Movimientos.Storage;
using Path = System.IO.Path;

namespace VivesBankApi.Rest.Clients.Controller
{
    /// <summary>
    /// Controlador que gestiona todas las operaciones relacionadas con los clientes en la API.
    /// </summary>
    /// <author>Raul Fernandez, Javier Hernandez, Samuel Cortes, Alvaro Herrero, German, Tomas</author>
    /// <version>1.0</version>
    [ApiController]
    [Route("api/[controller]")]
    public class ClientController : ControllerBase
    {
        private readonly IClientService _clientService;
        private ILogger _logger;
        private readonly IMovimientoService _movimientoService;
        private readonly IMovimientoStoragePDF _movimientoStoragePDF;
        
        /// <summary>
        /// Constructor del controlador de clientes.
        /// </summary>
        public ClientController(
            IClientService clientService, 
            ILogger<ClientController> logger, 
            IMovimientoService movimientoService,
            IMovimientoStoragePDF movimientoStoragePDF
        )
        {
            _movimientoService = movimientoService;
            _movimientoStoragePDF = movimientoStoragePDF;
            _clientService = clientService;
            _logger = logger;
        }

        /// <summary>
        /// Obtiene todos los clientes paginados.
        /// </summary>
        [HttpGet]
        [Authorize("AdminPolicy")]
        public async Task<ActionResult<PageResponse<ClientResponse>>> GetAllUsersAsync(
            [FromQuery] int pageNumber = 0, 
            [FromQuery] int pageSize = 10,
            [FromQuery] string fullName = "",
            [FromQuery] bool? isDeleted = null,
            [FromQuery] string direction = "asc")
        {
            PagedList<ClientResponse> pagedList =  await _clientService.GetAllClientsAsync(
                pageNumber, pageSize, fullName, isDeleted, direction
            );

            return new PageResponse<ClientResponse>
            {
                Content = pagedList.ToList(),
                TotalPages = pagedList.PageCount,
                TotalElements = pagedList.TotalCount,
                PageSize = pagedList.PageSize,
                PageNumber = pagedList.PageNumber,
                TotalPageElements = pagedList.Count,
                Empty = pagedList.Count == 0,
                First = pagedList.IsFirstPage,
                Last = pagedList.IsLastPage,
                SortBy = "fullName",
                Direction = direction
            };
        }

        /// <summary>
        /// Obtiene un cliente por su ID.
        /// </summary>
        [HttpGet("{id}")]
        [Authorize("AdminPolicy")]
        public async Task<ActionResult<ClientResponse>> GetById(string id)
        {
            _logger.LogInformation($"Getting client with id {id}");
            return await _clientService.GetClientByIdAsync(id);
        }

        /// <summary>
        /// Obtiene los datos del cliente autenticado.
        /// </summary>
        [HttpGet("me")] 
        [Authorize("ClientPolicy")]
        public async Task<ActionResult<ClientResponse>> GetMyClientData()
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Unauthorized();
            }
            _logger.LogInformation("Getting my client data");
            return await _clientService.GettingMyClientData();
        }

        /// <summary>
        /// Crea un nuevo cliente como usuario.
        /// </summary>
        [HttpPost("toclient")]
        [Authorize("UserPolicy")]
        public async Task<IActionResult> CreateClientAsUser([FromBody] ClientRequest request)
        {
            _logger.LogInformation("Creating new client");
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            var client = await _clientService.CreateClientAsync(request);
            return Ok(new { client });
        }

        /// <summary>
        /// Actualiza los datos de un cliente por ID.
        /// </summary>
        [HttpPut("{id}")]
        [Authorize("AdminPolicy")]
        public async Task<ActionResult<ClientResponse>> UpdateClient(string id, ClientUpdateRequest request)
        {
            _logger.LogInformation($"Updating client with id {id}");
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            var client = await _clientService.UpdateClientAsync(id, request);
            return Ok(client);
        }

        /// <summary>
        /// Actualiza los datos del cliente autenticado.
        /// </summary>
        [HttpPut("me")]
        [Authorize("ClientPolicy")]
        public async Task<ActionResult<ClientResponse>> UpdateMeAsClient([FromBody] ClientUpdateRequest request)
        {
            _logger.LogInformation($"Updating client registered on the system");
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var client = await _clientService.UpdateMeAsync(request);
            return Ok(client);
        }

        /// <summary>
        /// Exporta los datos del cliente autenticado a un archivo JSON.
        /// </summary>
        [HttpGet("me/export")]
        [Authorize("ClientPolicy")]
        public async Task<IActionResult> GetMeDataAsClient()
        {
            _logger.LogInformation("Exporting client data as a JSON file");
            try
            {
                var user = await _clientService.GettingMyClientData();
                var fileStream = await _clientService.ExportOnlyMeData(user.FromDtoResponse());
                return File(fileStream, "application/json", "user.json");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error exporting client: {ex.Message}");
                return StatusCode(500, new { message = "Error exporting client", details = ex.Message });
            }
        }

        /// <summary>
        /// Exporta las transacciones del cliente autenticado a un archivo PDF.
        /// </summary>
        [HttpGet("me/export/transactions")]
        [Authorize("ClientPolicy")]
        public async Task<IActionResult> ExportPdf()
        {
            _logger.LogInformation("Exporting client's transactions as a PDF file");
            var client = await _clientService.GettingMyClientData();
            var movimientos = await _movimientoService.FindAllMovimientosByClientAsync(client.Id);
            var fileStream = await _movimientoStoragePDF.Export(movimientos);

            return File(fileStream, "application/pdf", "Movimientos.pdf");
        }

        /// <summary>
        /// Elimina un cliente por ID.
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize("AdminPolicy")]
        public async Task DeleteClient(string id)
        {
            _logger.LogInformation($"Deleting client with id {id}");
            await _clientService.LogicDeleteClientAsync(id);
        }

        /// <summary>
        /// Elimina el cliente autenticado.
        /// </summary>
        [HttpDelete("baja")]
        [Authorize("ClientPolicy")]
        public async Task DeleteMeClient()
        {
            _logger.LogInformation($"Deleting client registered on the system");
            await _clientService.DeleteMe();
        }
        
        [HttpDelete("borrarDatos")]
        [Authorize("ClientPolicy")]
        public async Task DeleteMeData()
        {
            _logger.LogInformation($"Deleting client data registered on the system");
            await _clientService.DeleteMeData();
        }

        /// <summary>
        /// Actualiza la foto del DNI del cliente.
        /// </summary>
        [HttpPatch("{clientId}/dni")]
        public async Task<IActionResult> UpdateClientDniPhotoFtpAsync(string clientId, IFormFile file)
        {
            _logger.LogInformation($"Request to update DNI photo for client with ID: {clientId}");

            if (file == null || file.Length == 0)
            {
                return BadRequest("No file was provided or the file is empty.");
            }

            var fileName = await _clientService.UpdateClientPhotoDniAsync(clientId, file);
            return Ok(new { message = "DNI photo updated successfully", fileName });
        }

        /// <summary>
        /// Obtiene una foto del cliente por el nombre del archivo.
        /// </summary>
        [HttpGet("photo/{fileName}")]
        [Authorize("AdminPolicy")]
        public async Task<IActionResult> GetPhotoByFileNameAsync(string fileName)
        {
            _logger.LogInformation($"Request to get photo with file name: {fileName}");

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return BadRequest(new { message = "File name must be provided." });
            }

            var fileStream = await _clientService.GetFileAsync(fileName);
            if (fileStream == null)
            {
                return NotFound(new { message = $"File with name {fileName} not found." });
            }

            var fileExtension = Path.GetExtension(fileName);
            var mimeType = MimeTypes.GetMimeType(fileExtension);

            return File(fileStream, mimeType, fileName);
        }

        /// <summary>
        /// Obtiene un archivo del FTP por su nombre.
        /// </summary>
        [HttpGet("ftp/{fileName}")]
        [Authorize("AdminPolicy")]
        public async Task<IActionResult> GetFileFromFtpAsync(string fileName)
        {
            _logger.LogInformation($"Request to get file with file name: {fileName}");

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return BadRequest(new { message = "File name must be provided." });
            }

            var fileStream = await _clientService.GetFileFromFtpAsync(fileName);
            if (fileStream == null)
            {
                return NotFound(new { message = $"File with name {fileName} not found." });
            }

            var fileExtension = Path.GetExtension(fileName)?.ToLower();
            var mimeType = MimeTypes.GetMimeType(fileExtension);

            fileStream.Seek(0, SeekOrigin.Begin);

            return File(fileStream, mimeType, fileName);
        }

        /// <summary>
        /// Obtiene la foto de perfil del cliente autenticado.
        /// </summary>
        [HttpGet("me-photo")]
        [Authorize("ClientPolicy")]
        public async Task<IActionResult> GetMyProfilePhotoAsync()
        {
            _logger.LogInformation("Request to get my profile photo.");
            var fileStream = await _clientService.GettingMyProfilePhotoAsync();
            var mimeType = MimeTypes.GetMimeType(Path.GetExtension(fileStream.Name));
            return File(fileStream, mimeType, Path.GetFileName(fileStream.Name));
        }

        /// <summary>
        /// Actualiza la foto de perfil del cliente autenticado.
        /// </summary>
        [HttpPatch("me-photo")]
        [Authorize("ClientPolicy")]
        public async Task<IActionResult> UpdateMyProfilePhotoAsync(IFormFile file)
        {
            _logger.LogInformation("Request to update my profile photo.");

            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("No file provided.");
                return BadRequest("No file provided.");
            }

            var newFileName = await _clientService.UpdateMyProfilePhotoAsync(file);
            _logger.LogInformation($"Profile photo updated successfully: {newFileName}");

            return Ok(new { message = "Profile photo updated successfully", fileName = newFileName });
        }

        /// <summary>
        /// Actualiza la foto del DNI del cliente autenticado.
        /// </summary>
        [HttpPatch("me-dni-photo")]
        [Authorize("ClientPolicy")]
        public async Task<IActionResult> UpdateMyDniPhotoAsync(IFormFile file)
        {
            _logger.LogInformation("Request to update my DNI photo.");

            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("No file provided.");
                return BadRequest("No file provided.");
            }

            var newFileName = await _clientService.UpdateMyPhotoDniAsync(file);
            _logger.LogInformation($"DNI photo updated successfully: {newFileName}");

            return Ok(new { message = "DNI photo updated successfully", fileName = newFileName });
        }

        /// <summary>
        /// Obtiene la foto del DNI del cliente autenticado desde el FTP.
        /// </summary>
        [HttpGet("me-dni-photo")]
        [Authorize("ClientPolicy")]
        public async Task<IActionResult> GetMyDniPhotoFromFtpAsync()
        {
            _logger.LogInformation("Request to get my DNI photo from FTP.");

            var fileStream = await _clientService.GettingMyDniPhotoFromFtpAsync();

            var mimeType = MimeTypes.GetMimeType(Path.GetExtension(fileStream.Name));

            _logger.LogInformation($"Returning DNI photo from FTP: {Path.GetFileName(fileStream.Name)} with MIME type: {mimeType}");
            return File(fileStream, mimeType, Path.GetFileName(fileStream.Name));
        }
    }
}