using System.Text.Json.Serialization;

namespace SocietyLedger.Application.DTOs.Auth
{
    public class RegisterResponse : LoginResponse
    {
        [JsonIgnore]
        public long? SocietyId { get; set; }

        [JsonIgnore]
        public long? UserId { get; set; }
    }
}
