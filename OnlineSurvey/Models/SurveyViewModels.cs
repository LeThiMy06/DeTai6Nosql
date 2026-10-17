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
    public string Status { get; set; } = "Published";
    public int Version { get; set; } = 1;
}

public sealed class SurveySubmitViewModel
{
    [Display(Name = "Họ và tên")]
    [Required(ErrorMessage = "Vui lòng nhập họ và tên của bạn.")]
    [StringLength(120, ErrorMessage = "Họ và tên không được vượt quá 120 ký tự.")]
    public string RespondentName { get; set; } = "";

    [Display(Name = "Email")]
    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email.")]
    [EmailAddress(ErrorMessage = "Địa chỉ email không đúng định dạng (ví dụ: example@gmail.com).")]
    [RegularExpression(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", ErrorMessage = "Địa chỉ email không đúng định dạng (ví dụ: example@gmail.com).")]
    [StringLength(150, ErrorMessage = "Email không được vượt quá 150 ký tự.")]
    public string RespondentEmail { get; set; } = "";

    [Display(Name = "Số điện thoại")]
    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [RegularExpression(@"^(0|\+84)[35789]\d{8}$", ErrorMessage = "Số điện thoại không hợp lệ. Vui lòng nhập số điện thoại gồm 10 chữ số (ví dụ: 0912345678).")]
    public string RespondentPhone { get; set; } = "";

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
    /// <summary>Số khách tham gia duy nhất – lấy từ Redis SET (SCARD).</summary>
    public long UniqueRespondents { get; set; }
    /// <summary>Sự kiện gửi bài gần đây – lấy từ Redis LIST (LRANGE).</summary>
    public List<RecentEventItem> RecentEvents { get; set; } = [];
    public List<QuestionResultViewModel> Questions { get; set; } = [];
    public List<SurveyResponse> Responses { get; set; } = [];
}

public sealed class RecentEventItem
{
    public string RespondentName { get; set; } = "";
    public string RespondentEmail { get; set; } = "";
    public DateTime SubmittedAt { get; set; }
    public int AnswerCount { get; set; }
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
