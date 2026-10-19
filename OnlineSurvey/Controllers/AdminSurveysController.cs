using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using OnlineSurvey.Models;
using OnlineSurvey.Services;

namespace OnlineSurvey.Controllers;

[Authorize(Roles = "Admin")]
[Route("admin/surveys")]
public sealed class AdminSurveysController : Controller
{
    private static readonly HashSet<string> AllowedTypes =
    ["text", "textarea", "single_choice", "multiple_choice"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly SurveyService _surveyService;
    private readonly IMongoDatabase _database;

    public AdminSurveysController(SurveyService surveyService, IMongoDatabase database)
    {
        _surveyService = surveyService;
        _database = database;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        return View(await _surveyService.GetAllAsync());
    }

    [HttpPost("seed-large-data")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SeedLargeData()
    {
        var adminId = CurrentAdminId();
        var (surveyCount, responseCount) = await LargeDataSeeder.SeedAllLargeDataAsync(_database, _surveyService, adminId);
        TempData["Success"] = $"Đã nạp thành công {surveyCount} khảo sát lớn (bao gồm khảo sát 100 câu hỏi) và {responseCount} phản hồi mẫu vào MongoDB!";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("create")]
    public IActionResult Create()
    {
        return View("Editor", new SurveyEditorViewModel());
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SurveyEditorViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Editor", model);
        }

        var questions = ParseQuestions(model.QuestionsJson);
        if (questions is null)
        {
            return View("Editor", model);
        }

        var survey = new Survey
        {
            CreatedByAdminId = CurrentAdminId(),
            Slug = BuildSlug(model.Slug, model.Title),
            Title = model.Title.Trim(),
            Description = model.Description?.Trim() ?? "",
            Questions = questions,
            Status = string.Equals(model.Status, "Draft", StringComparison.OrdinalIgnoreCase) ? "Draft" : "Published"
        };

        try
        {
            await _surveyService.CreateAsync(survey);
            TempData["Success"] = "Đã tạo khảo sát thành công.";
            return RedirectToAction(nameof(Index));
        }
        catch (MongoWriteException)
        {
            ModelState.AddModelError(nameof(model.Slug), "Đường dẫn khảo sát đã tồn tại.");
            return View("Editor", model);
        }
    }

    [HttpGet("{id}/edit")]
    public async Task<IActionResult> Edit(string id)
    {
        var survey = await _surveyService.GetByIdAsync(id);
        if (survey is null)
        {
            return NotFound();
        }

        return View("Editor", ToEditorModel(survey));
    }

    [HttpPost("{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, SurveyEditorViewModel model)
    {
        var existing = await _surveyService.GetByIdAsync(id);
        if (existing is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            model.Id = id;
            return View("Editor", model);
        }

        var questions = ParseQuestions(model.QuestionsJson);
        if (questions is null)
        {
            model.Id = id;
            return View("Editor", model);
        }

        var oldSlug = existing.Slug;
        if (string.IsNullOrWhiteSpace(existing.CreatedByAdminId))
        {
            existing.CreatedByAdminId = CurrentAdminId();
        }
        existing.Title = model.Title.Trim();
        existing.Slug = BuildSlug(model.Slug, model.Title);
        existing.Description = model.Description?.Trim() ?? "";
        existing.Questions = questions;
        existing.Status = string.Equals(model.Status, "Draft", StringComparison.OrdinalIgnoreCase) ? "Draft" : "Published";
        existing.Version++;
        existing.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _surveyService.UpdateAsync(existing, oldSlug);
            TempData["Success"] = "Đã cập nhật khảo sát.";
            return RedirectToAction(nameof(Index));
        }
        catch (MongoWriteException)
        {
            ModelState.AddModelError(nameof(model.Slug), "Đường dẫn khảo sát đã tồn tại.");
            model.Id = id;
            return View("Editor", model);
        }
    }

    [HttpPost("{id}/publish")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(string id)
    {
        var survey = await _surveyService.GetByIdAsync(id);
        if (survey is null)
        {
            return NotFound();
        }

        survey.Status = survey.Status == "Published" ? "Draft" : "Published";
        survey.Version++;
        survey.UpdatedAt = DateTime.UtcNow;
        await _surveyService.UpdateAsync(survey);
        TempData["Success"] = survey.Status == "Published"
            ? "Đã xuất bản khảo sát."
            : "Đã đóng khảo sát.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var survey = await _surveyService.GetByIdAsync(id);
        if (survey is not null)
        {
            await _surveyService.DeleteAsync(survey);
            TempData["Success"] = "Đã xóa khảo sát và câu trả lời liên quan.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id}/results")]
    public async Task<IActionResult> Results(string id)
    {
        var survey = await _surveyService.GetByIdAsync(id);
        if (survey is null)
        {
            return NotFound();
        }

        var responses = await _surveyService.GetResponsesAsync(id);

        // Redis HASH – số liệu thống kê tổng hợp thời gian thực
        var (hashTotalResponses, _) = await _surveyService.GetSurveyStatsAsync(id);
        var totalResponses = hashTotalResponses > 0 ? (int)hashTotalResponses : responses.Count;

        // Redis SET – số khách tham gia duy nhất
        var uniqueCount = await _surveyService.GetUniqueRespondentCountAsync(id);

        // Redis LIST – sự kiện gửi bài gần đây
        var recentRaw = await _surveyService.GetRecentEventsAsync(id, 20);
        var recentEvents = recentRaw
            .Select(json =>
            {
                try
                {
                    return JsonSerializer.Deserialize<RecentEventItem>(json, JsonOptions);
                }
                catch { return null; }
            })
            .Where(e => e is not null)
            .Cast<RecentEventItem>()
            .ToList();

        var model = new SurveyResultsViewModel
        {
            Survey = survey,
            TotalResponses = totalResponses,
            UniqueRespondents = uniqueCount,
            RecentEvents = recentEvents,
            Responses = responses
        };

        foreach (var question in survey.Questions.OrderBy(x => x.Order))
        {
            var result = new QuestionResultViewModel { Question = question };

            if (question.Type is "single_choice" or "multiple_choice")
            {
                result.Options = question.Options.Select(option => new OptionResultViewModel
                {
                    OptionId = option.Id,
                    Text = option.Text,
                    Count = responses.Count(response => response.Answers.Any(answer =>
                        answer.QuestionId == question.Id && answer.Values.Contains(option.Id)))
                }).ToList();
            }
            else
            {
                result.TextAnswers = responses
                    .SelectMany(response => response.Answers
                        .Where(answer => answer.QuestionId == question.Id)
                        .SelectMany(answer => answer.Values))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Take(20)
                    .ToList();
            }

            model.Questions.Add(result);
        }

        return View(model);
    }

    [HttpGet("{id}/export")]
    public async Task<IActionResult> Export(string id)
    {
        var survey = await _surveyService.GetByIdAsync(id);
        if (survey is null)
        {
            return NotFound();
        }

        var responses = await _surveyService.GetResponsesAsync(id);
        var columns = new[] { "Thời gian nộp", "Người nộp", "Email", "Số điện thoại" }
            .Concat(survey.Questions.OrderBy(x => x.Order).Select(x => x.Text));
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", columns.Select(EscapeCsv)));

        foreach (var response in responses)
        {
            var values = new List<string>
            {
                response.SubmittedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"),
                string.IsNullOrWhiteSpace(response.RespondentName) ? "Khách ẩn danh" : response.RespondentName,
                response.RespondentEmail ?? "",
                response.RespondentPhone ?? ""
            };

            values.AddRange(survey.Questions.OrderBy(x => x.Order).Select(question =>
            {
                var answers = response.Answers
                    .Where(answer => answer.QuestionId == question.Id)
                    .SelectMany(answer => answer.Values)
                    .Select(val => question.Options.FirstOrDefault(o => o.Id == val)?.Text ?? val);
                return string.Join("; ", answers);
            }));

            csv.AppendLine(string.Join(",", values.Select(EscapeCsv)));
        }

        var bytes = Encoding.UTF8.GetBytes("\uFEFF" + csv);
        return File(bytes, "text/csv", $"{survey.Slug}-responses.csv");
    }

    private List<SurveyQuestion>? ParseQuestions(string json)
    {
        List<SurveyQuestion>? questions;
        try
        {
            questions = JsonSerializer.Deserialize<List<SurveyQuestion>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            ModelState.AddModelError(nameof(SurveyEditorViewModel.QuestionsJson), "Danh sách câu hỏi không hợp lệ.");
            return null;
        }

        if (questions is null || questions.Count == 0)
        {
            ModelState.AddModelError(nameof(SurveyEditorViewModel.QuestionsJson), "Cần có ít nhất một câu hỏi.");
            return null;
        }

        if (questions.Count > 100)
        {
            ModelState.AddModelError(nameof(SurveyEditorViewModel.QuestionsJson), "Khảo sát chỉ được có tối đa 100 câu hỏi.");
            return null;
        }

        var questionIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < questions.Count; index++)
        {
            var question = questions[index];
            question.Id = string.IsNullOrWhiteSpace(question.Id)
                ? Guid.NewGuid().ToString("N")
                : question.Id;
            question.Order = index + 1;
            question.Text = question.Text?.Trim() ?? "";
            question.Type = question.Type?.Trim().ToLowerInvariant() ?? "";
            question.Options ??= [];

            if (!questionIds.Add(question.Id) || question.Id.Length > 80 ||
                string.IsNullOrWhiteSpace(question.Text) || question.Text.Length > 500 ||
                !AllowedTypes.Contains(question.Type))
            {
                ModelState.AddModelError(nameof(SurveyEditorViewModel.QuestionsJson), "Câu hỏi có dữ liệu không hợp lệ.");
                return null;
            }

            if (question.Type is "single_choice" or "multiple_choice")
            {
                if (question.Options.Count > 50)
                {
                    ModelState.AddModelError(nameof(SurveyEditorViewModel.QuestionsJson), "Mỗi câu hỏi chỉ được có tối đa 50 đáp án.");
                    return null;
                }

                var optionIds = new HashSet<string>(StringComparer.Ordinal);
                question.Options = question.Options
                    .Where(option => !string.IsNullOrWhiteSpace(option.Text))
                    .Select(option => new QuestionOption
                    {
                        Id = string.IsNullOrWhiteSpace(option.Id)
                            ? Guid.NewGuid().ToString("N")
                            : option.Id,
                        Text = option.Text?.Trim() ?? ""
                    })
                    .ToList();

                if (question.Options.Any(option => option.Text.Length > 250 || !optionIds.Add(option.Id)))
                {
                    ModelState.AddModelError(nameof(SurveyEditorViewModel.QuestionsJson), "Đáp án có dữ liệu không hợp lệ hoặc bị trùng.");
                    return null;
                }

                if (question.Options.Count < 2)
                {
                    ModelState.AddModelError(nameof(SurveyEditorViewModel.QuestionsJson), "Câu hỏi lựa chọn cần ít nhất hai đáp án.");
                    return null;
                }
            }
            else
            {
                question.Options = [];
            }
        }

        return questions;
    }

    private static SurveyEditorViewModel ToEditorModel(Survey survey) => new()
    {
        Id = survey.Id,
        Title = survey.Title,
        Slug = survey.Slug,
        Description = survey.Description,
        Status = survey.Status,
        Version = survey.Version,
        QuestionsJson = JsonSerializer.Serialize(survey.Questions, JsonOptions)
    };

    private string CurrentAdminId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Không tìm thấy tài khoản quản trị đang đăng nhập.");

    private static string BuildSlug(string slug, string title)
    {
        var source = string.IsNullOrWhiteSpace(slug) ? title : slug;
        var normalized = source.Normalize(NormalizationForm.FormD);
        var withoutDiacritics = new string(normalized
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .ToArray())
            .Normalize(NormalizationForm.FormC);

        var result = Regex.Replace(withoutDiacritics, "[^a-zA-Z0-9]+", "-")
            .Trim('-')
            .ToLowerInvariant();

        result = result.Length > 120 ? result[..120].Trim('-') : result;

        return string.IsNullOrWhiteSpace(result)
            ? $"survey-{Guid.NewGuid():N}"[..20]
            : result;
    }

    private static string EscapeCsv(string value)
    {
        value ??= "";
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
