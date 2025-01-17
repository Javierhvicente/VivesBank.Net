using Newtonsoft.Json;

namespace VivesBankApi.Rest.Users.Dtos;

public class UserUpdateRequest
{
    [JsonProperty("username")] 
    public string? Username { get; set; } = null;

    [JsonProperty("password")] 
    public string? Password { get; set; } = null;

    [JsonProperty("role")]
    public string? Role { get; set; } = null;
}
