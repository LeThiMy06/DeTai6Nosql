using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace OnlineSurvey.Models;

[BsonIgnoreExtraElements]
public sealed class Survey
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonRepresentation(BsonType.ObjectId)]
    public string CreatedByAdminId { get; set; } = "";

    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public int Version { get; set; } = 1;
    public int EstimatedMinutes { get; set; } = 5;
    public List<SurveyQuestion> Questions { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public sealed class SurveyQuestion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Order { get; set; }
    public string Type { get; set; } = "text";
    public string Text { get; set; } = "";
    public bool Required { get; set; }
    public List<QuestionOption> Options { get; set; } = [];
}

[BsonIgnoreExtraElements]
public sealed class QuestionOption
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
}

[BsonIgnoreExtraElements]
public sealed class SurveyResponse
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonRepresentation(BsonType.ObjectId)]
    public string SurveyId { get; set; } = "";

    public int SurveyVersion { get; set; }
    public string RespondentName { get; set; } = "";
    public string RespondentEmail { get; set; } = "";
    public string RespondentPhone { get; set; } = "";
    public List<SurveyAnswer> Answers { get; set; } = [];
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public sealed class SurveyAnswer
{
    public string QuestionId { get; set; } = "";
    public List<string> Values { get; set; } = [];
}
