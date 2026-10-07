using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace OnlineSurvey.Models;

public sealed class AdminAccount
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    public string Username { get; set; } = "";
    public string NormalizedUsername { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string DisplayName { get; set; } = "Quản trị viên";
    public string Role { get; set; } = "Admin";
    public bool IsInitialAccount { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
}
