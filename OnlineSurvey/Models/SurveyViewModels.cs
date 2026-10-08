using System.ComponentModel.DataAnnotations;

namespace OnlineSurvey.Models;

public sealed class LoginViewModel
{
    [Required]
    public string Username { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public sealed class SurveyEditorViewModel
{
    public string? Id { get; set; }

    [Required, StringLength(160)]
    [Display(Name = "Tên khảo sát")]
    public string Title { get; set; } = "";

    [StringLength(180)]
    [Display(Name = "Đường dẫn")]
    public string Slug { get; set; } = "";

    [StringLength(1000)]
    [Display(Name = "Mô tả")]
    public string Description { get; set; } = "";

    public string QuestionsJson { get; set; } = "[]";
    public string Status { get; set; } = "Draft";
    public int Version { get; set; } = 1;
}

public sealed class SurveySubmitViewModel
{
    [Display(Name = "Tên người trả lời")]
    public string RespondentName { get; set; } = "";

    public Dictionary<string, string[]> Answers { get; set; } = new();
}

public sealed class SurveyPageViewModel
{
    public Survey Survey { get; set; } = new();
    public SurveySubmitViewModel SubmitModel { get; set; } = new();
}

public sealed class SurveyResultsViewModel
{
    public Survey Survey { get; set; } = new();
    public int TotalResponses { get; set; }
    public List<QuestionResultViewModel> Questions { get; set; } = [];
}

public sealed class QuestionResultViewModel
{
    public SurveyQuestion Question { get; set; } = new();
    public List<OptionResultViewModel> Options { get; set; } = [];
    public List<string> TextAnswers { get; set; } = [];
}

public sealed class OptionResultViewModel
{
    public string OptionId { get; set; } = "";
    public string Text { get; set; } = "";
    public int Count { get; set; }
}
