using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using OnlineSurvey.Models;
using OnlineSurvey.Services;

namespace OnlineSurvey.Controllers;

[Route("surveys")]
public sealed class SurveysController : Controller
{
    private readonly SurveyService _surveyService;

    public SurveysController(SurveyService surveyService)
    {
        _surveyService = surveyService;
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> Details(string slug)
    {
        var survey = await _surveyService.GetPublishedBySlugAsync(slug);
        if (survey is null && User.Identity?.IsAuthenticated == true)
        {
            var allSurveys = await _surveyService.GetAllAsync();
            survey = allSurveys.FirstOrDefault(s => string.Equals(s.Slug, slug.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (survey is null)
        {
            return NotFound();
        }

        int submittedCount = int.TryParse(Request.Cookies[$"survey-submitted-{survey.Id}"], out var c) ? c : 0;
        if (submittedCount >= 3)
        {
            return View("AlreadySubmitted", survey);
        }

        return View(new SurveyPageViewModel { Survey = survey });
    }

    [HttpPost("{id}/submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(string id, SurveySubmitViewModel model)
    {
        model.Answers ??= new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(model.RespondentName) && Request.Form.TryGetValue("SubmitModel.RespondentName", out var fallbackName))
        {
            model.RespondentName = fallbackName.ToString();
        }
        if (string.IsNullOrWhiteSpace(model.RespondentEmail) && Request.Form.TryGetValue("SubmitModel.RespondentEmail", out var fallbackEmail))
        {
            model.RespondentEmail = fallbackEmail.ToString();
        }
        if (string.IsNullOrWhiteSpace(model.RespondentPhone) && Request.Form.TryGetValue("SubmitModel.RespondentPhone", out var fallbackPhone))
        {
            model.RespondentPhone = fallbackPhone.ToString();
        }

        var survey = await _surveyService.GetByIdAsync(id);
        if (survey is null || survey.Status != "Published")
        {
            return NotFound();
        }

        int submittedCount = int.TryParse(Request.Cookies[$"survey-submitted-{survey.Id}"], out var count) ? count : 0;
        if (submittedCount >= 3)
        {
            return View("AlreadySubmitted", survey);
        }

        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = Request.Headers.UserAgent.ToString();
        var clientKey = SurveyService.HashClient($"{remoteIp}|{userAgent}");

        if (!await _surveyService.AllowSubmissionAsync(survey.Id, clientKey))
        {
            return View("RateLimited", survey);
        }

        if (string.IsNullOrWhiteSpace(model.RespondentName))
        {
            ModelState.AddModelError(nameof(model.RespondentName), "Vui lòng nhập họ và tên của bạn.");
        }

        if (string.IsNullOrWhiteSpace(model.RespondentEmail))
        {
            ModelState.AddModelError(nameof(model.RespondentEmail), "Vui lòng nhập địa chỉ email.");
        }
        else if (!System.Text.RegularExpressions.Regex.IsMatch(model.RespondentEmail.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            ModelState.AddModelError(nameof(model.RespondentEmail), "Địa chỉ email không đúng định dạng (ví dụ: example@gmail.com).");
        }

        if (string.IsNullOrWhiteSpace(model.RespondentPhone))
        {
            ModelState.AddModelError(nameof(model.RespondentPhone), "Vui lòng nhập số điện thoại.");
        }
        else
        {
            var cleanPhone = model.RespondentPhone.Trim().Replace(" ", "").Replace(".", "").Replace("-", "");
            if (!System.Text.RegularExpressions.Regex.IsMatch(cleanPhone, @"^(0|\+84)[35789]\d{8}$"))
            {
                ModelState.AddModelError(nameof(model.RespondentPhone), "Số điện thoại không hợp lệ. Vui lòng nhập số điện thoại gồm 10 chữ số (ví dụ: 0912345678).");
            }
        }

        ValidateAnswers(survey, model.Answers);
        if (!ModelState.IsValid)
        {
            return View("Details", new SurveyPageViewModel
            {
                Survey = survey,
                SubmitModel = model
            });
        }

        var response = new SurveyResponse
        {
            SurveyId = survey.Id,
            SurveyVersion = survey.Version,
            RespondentName = model.RespondentName?.Trim() ?? "",
            RespondentEmail = model.RespondentEmail?.Trim() ?? "",
            RespondentPhone = model.RespondentPhone?.Trim() ?? "",
            Answers = model.Answers
                .Where(x => x.Value is not null && x.Value.Length > 0)
                .Select(x => new SurveyAnswer
                {
                    QuestionId = x.Key,
                    Values = x.Value.Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim())
                        .ToList()
                })
                .Where(x => x.Values.Count > 0)
                .ToList()
        };

        await _surveyService.InsertResponseAsync(response);

        // Redis HASH – cập nhật số liệu thống kê tổng hợp thời gian thực (HINCRBY / HSET)
        await _surveyService.IncrementSurveyStatsAsync(survey.Id);

        // Redis SET – ghi nhận khách tham gia duy nhất
        await _surveyService.TrackUniqueRespondentAsync(survey.Id, clientKey);

        // Redis LIST – đẩy sự kiện nộp bài vào đầu danh sách gần đây
        var eventPayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            respondentName = response.RespondentName,
            respondentEmail = response.RespondentEmail,
            submittedAt = response.SubmittedAt.ToString("o"),
            answerCount = response.Answers.Count
        });
        await _surveyService.PushRecentEventAsync(survey.Id, eventPayload);

        Response.Cookies.Append(
            $"survey-submitted-{survey.Id}",
            (submittedCount + 1).ToString(),
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                MaxAge = TimeSpan.FromMinutes(10),
                SameSite = SameSiteMode.Lax
            });
        return View("Submitted", survey);
    }

    private void ValidateAnswers(Survey survey, Dictionary<string, string[]>? answers)
    {
        answers ??= new Dictionary<string, string[]>();
        var knownQuestionIds = survey.Questions.Select(question => question.Id).ToHashSet();

        foreach (var unknownQuestionId in answers.Keys.Where(key => !knownQuestionIds.Contains(key)))
        {
            ModelState.AddModelError(string.Empty, "Form chứa câu hỏi không hợp lệ.");
        }

        foreach (var question in survey.Questions.OrderBy(x => x.Order))
        {
            answers.TryGetValue(question.Id, out var values);
            values ??= [];
            var cleanValues = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();

            if (question.Required && cleanValues.Length == 0)
            {
                ModelState.AddModelError(
                    $"Answers[{question.Id}]",
                    "Vui lòng trả lời câu hỏi này.");
                continue;
            }

            if (cleanValues.Length == 0)
            {
                continue;
            }

            var validOptions = question.Options.Select(x => x.Id).ToHashSet();
            var valid = question.Type switch
            {
                "single_choice" => cleanValues.Length == 1 && validOptions.Contains(cleanValues[0]),
                "multiple_choice" => cleanValues.All(validOptions.Contains),
                "text" or "textarea" => cleanValues.Length == 1 && cleanValues[0].Length <= 5000,
                _ => cleanValues.Length <= 1
            };

            if (!valid)
            {
                ModelState.AddModelError(
                    $"Answers[{question.Id}]",
                    "Câu trả lời không hợp lệ.");
            }
        }
    }
}
