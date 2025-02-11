using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using VivesBankApi.WebSocket.Model;

namespace VivesBankApi.WebSocket.Service;

/// <summary>
/// Implementation of IWebsocketHandler that handles WebSocket connections and notifications.
/// </summary>
public class WebSocketHandler : IWebsocketHandler
{
    private readonly ILogger _logger;
    private readonly List<System.Net.WebSockets.WebSocket> _sockets = new();
    private readonly ConcurrentDictionary<string, System.Net.WebSockets.WebSocket> _userSockets = new();

    /// <summary>
    /// Initializes a new instance of the WebSocketHandler class.
    /// </summary>
    /// <param name="logger">Logger instance to log information and errors.</param>
    public WebSocketHandler(ILogger<WebSocketHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Handles incoming WebSocket connections.
    /// </summary>
    /// <param name="webSocket">The WebSocket connection to handle.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task HandleAsync(System.Net.WebSockets.WebSocket webSocket)
    {
        _logger.LogInformation("WebSocket connected from {0}", webSocket);
        _sockets.Add(webSocket); 

        var buffer = new byte[1024 * 4]; 

        try
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            while (!result.CloseStatus.HasValue)
            {
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogError($"Error receiving WebSocket data: {ex.Message}");
            await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Error receiving data", CancellationToken.None);
        }
        finally
        {
            _sockets.Remove(webSocket);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
        }
    }

    /// <summary>
    /// Sends a notification to all connected WebSocket clients.
    /// </summary>
    /// <typeparam name="T">The type of the notification data.</typeparam>
    /// <param name="notification">The notification to send to all clients.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task NotifyAllAsync<T>(Notification<T> notification)
    {
        // Serialize notification to JSON while ignoring null values
        var jsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };
        var json = JsonConvert.SerializeObject(notification, jsonSettings);
        _logger.LogInformation($"Notifying all clients: {json}");

        var buffer = Encoding.UTF8.GetBytes(json);

        var tasks = _sockets.Select(socket =>
            socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None))
            .ToArray();
        await Task.WhenAll(tasks); 
    }

    /// <summary>
    /// Sends a notification to a specific user via WebSocket.
    /// </summary>
    /// <typeparam name="T">The type of the notification data.</typeparam>
    /// <param name="username">The username of the user to notify.</param>
    /// <param name="notification">The notification to send.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task NotifyUserAsync<T>(string username, Notification<T> notification)
    {
        if (_userSockets.TryGetValue(username, out var socket))
        {
            var jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented
            };
            var json = JsonConvert.SerializeObject(notification, jsonSettings);
            var buffer = Encoding.UTF8.GetBytes(json);

            await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
            _logger.LogInformation($"Notification sent to {username}: {json}");
        }
        else
        {
            _logger.LogWarning($"User '{username}' not found.");
        }
    }

    /// <summary>
    /// Handles an authenticated WebSocket connection and associates it with a specific user.
    /// </summary>
    /// <param name="webSocket">The WebSocket connection of the authenticated user.</param>
    /// <param name="username">The username of the authenticated user.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task HandleAuthenticatedAsync(System.Net.WebSockets.WebSocket webSocket, string username)
    {
        _logger.LogInformation($"WebSocket connected for user: {username}");
        _userSockets.AddOrUpdate(username, webSocket, (key, oldValue) => webSocket);
        _logger.LogInformation($"Number of connected users: {_userSockets.Count}");

        var buffer = new byte[1024 * 4];

        try
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            while (!result.CloseStatus.HasValue)
            {
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            }
        }
        catch (WebSocketException ex)
        {
            _logger.LogError($"Error receiving WebSocket data for user {username}: {ex.Message}");
            await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Error receiving data", CancellationToken.None);
        }
        finally
        {
            _userSockets.TryRemove(username, out _);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
        }
    }
}