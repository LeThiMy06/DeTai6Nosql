using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using OnlineSurvey.Models;
using StackExchange.Redis;

/// <summary>
/// Cấu trúc dữ liệu Redis dùng trong ứng dụng:
///   STRING  – Cache biểu mẫu khảo sát (GetPublishedBySlugAsync)
///   HASH    – Thống kê khảo sát realtime (IncrementSurveyStatsAsync / GetSurveyStatsAsync) & Giới hạn tốc độ gửi bài (AllowSubmissionAsync)
///   SET     – Đếm khách tham gia duy nhất (TrackUniqueRespondentAsync / GetUniqueRespondentCountAsync)
///   LIST    – Nhật ký sự kiện gửi bài gần đây (PushRecentEventAsync / GetRecentEventsAsync)
/// </summary>

namespace OnlineSurvey.Services;

public sealed class SurveyService
{
    private const int SubmissionLimit = 5;
    private const int RecentEventsLimit = 20;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(1);
    private readonly ISurveyRepository _repository;
    private readonly IDatabase _redis;
    private readonly ILogger<SurveyService> _logger;

    public SurveyService(
        ISurveyRepository repository,
        IConnectionMultiplexer redis,
        ILogger<SurveyService> logger)
    {
        _repository = repository;
        _redis = redis.GetDatabase();
        _logger = logger;
    }

    public bool IsRedisConnected => _redis.Multiplexer.IsConnected;

    public async Task<Survey?> GetPublishedBySlugAsync(string slug)
    {
        slug = slug.Trim().ToLowerInvariant();
        var cacheKey = CacheKey(slug);

        if (IsRedisConnected)
        {
            try
            {
                var cached = await _redis.StringGetAsync(cacheKey);
                if (!cached.IsNullOrEmpty)
                {
                    var cachedSurvey = JsonSerializer.Deserialize<Survey>(cached.ToString());
                    if (cachedSurvey is not null)
                    {
                        return cachedSurvey;
                    }

                    await _redis.KeyDeleteAsync(cacheKey);
                }
            }
            catch (RedisException exception)
            {
                _logger.LogWarning(exception, "Redis cache read failed for survey slug {Slug}", slug);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "Invalid survey cache for slug {Slug}", slug);
                try { await _redis.KeyDeleteAsync(cacheKey); } catch (RedisException) { }
            }
        }

        var survey = await _repository.GetPublishedBySlugAsync(slug);
        if (survey is not null && IsRedisConnected)
        {
            try
            {
                await _redis.StringSetAsync(
                    cacheKey,
                    JsonSerializer.Serialize(survey),
                    CacheDuration);
            }
            catch (RedisException exception)
            {
                _logger.LogWarning(exception, "Redis cache write failed for survey slug {Slug}", slug);
            }
        }

        return survey;
    }

    public Task<Survey?> GetByIdAsync(string id) => _repository.GetByIdAsync(id);
    public Task<List<Survey>> GetAllAsync() => _repository.GetAllAsync();
    public Task InsertResponseAsync(SurveyResponse response) => _repository.InsertResponseAsync(response);

    public async Task CreateAsync(Survey survey)
    {
        await _repository.InsertAsync(survey);
        await InvalidateAsync(survey.Slug);
    }

    public async Task UpdateAsync(Survey survey, string? oldSlug = null)
    {
        await _repository.UpdateAsync(survey);
        await InvalidateAsync(oldSlug);
        await InvalidateAsync(survey.Slug);
    }

    public async Task DeleteAsync(Survey survey)
    {
        await _repository.DeleteAsync(survey.Id);
        await InvalidateAsync(survey.Slug);
    }

    public Task<List<SurveyResponse>> GetResponsesAsync(string surveyId) =>
        _repository.GetResponsesAsync(surveyId);

    private static readonly ConcurrentDictionary<string, (int Count, DateTime Expiry)> _memoryRateLimit = new();

    // ───────────────── Redis HASH: Giới hạn tốc độ gửi bài (Rate Limiter) ─────────────────

    public async Task<bool> AllowSubmissionAsync(string surveyId, string clientKey)
    {
        var key = $"rate:client:{clientKey}";
        if (!IsRedisConnected)
        {
            var now = DateTime.UtcNow;
            var entry = _memoryRateLimit.AddOrUpdate(
                $"{key}:{surveyId}",
                _ => (1, now.Add(RateWindow)),
                (_, old) => old.Expiry < now ? (1, now.Add(RateWindow)) : (old.Count + 1, old.Expiry)
            );

            return entry.Count <= SubmissionLimit;
        }

        try
        {
            var count = await _redis.HashIncrementAsync(key, surveyId, 1);

            if (count == 1)
            {
                await _redis.KeyExpireAsync(key, RateWindow);
            }

            return count <= SubmissionLimit;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Redis rate limiter unavailable for survey {SurveyId}, using memory fallback", surveyId);
            var now = DateTime.UtcNow;
            var entry = _memoryRateLimit.AddOrUpdate(
                $"{key}:{surveyId}",
                _ => (1, now.Add(RateWindow)),
                (_, old) => old.Expiry < now ? (1, now.Add(RateWindow)) : (old.Count + 1, old.Expiry)
            );

            return entry.Count <= SubmissionLimit;
        }
    }

    public async Task InvalidateAsync(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug) || !IsRedisConnected)
        {
            return;
        }

        try
        {
            await _redis.KeyDeleteAsync(CacheKey(slug.Trim().ToLowerInvariant()));
        }
        catch (RedisException exception)
        {
            _logger.LogWarning(exception, "Redis cache invalidation failed for survey slug {Slug}", slug);
        }
    }

    // ───────────────── Redis HASH: Thống kê số liệu khảo sát (HINCRBY / HSET / HGETALL) ─────────────────

    /// <summary>
    /// Cập nhật số liệu thống kê realtime cho khảo sát vào Redis HASH.
    /// </summary>
    public async Task IncrementSurveyStatsAsync(string surveyId)
    {
        if (!IsRedisConnected) return;
        var key = $"stats:survey:{surveyId}";
        try
        {
            await _redis.HashIncrementAsync(key, "total_responses", 1);
            await _redis.HashSetAsync(key, "last_submitted_at", DateTime.UtcNow.ToString("o"));
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis HASH increment failed for survey {SurveyId}", surveyId);
        }
    }

    /// <summary>
    /// Lấy số liệu thống kê khảo sát từ Redis HASH (HGETALL).
    /// </summary>
    public async Task<(long TotalResponses, string? LastSubmittedAt)> GetSurveyStatsAsync(string surveyId)
    {
        if (!IsRedisConnected) return (0, null);
        var key = $"stats:survey:{surveyId}";
        try
        {
            var entries = await _redis.HashGetAllAsync(key);
            if (entries.Length == 0)
            {
                return (0, null);
            }

            var dict = entries.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());
            long total = dict.TryGetValue("total_responses", out var s) && long.TryParse(s, out var count) ? count : 0;
            string? last = dict.TryGetValue("last_submitted_at", out var d) ? d : null;
            return (total, last);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis HASH get failed for survey {SurveyId}", surveyId);
            return (0, null);
        }
    }

    // ───────────────── Redis SET: Khách tham gia duy nhất ─────────────────

    /// <summary>
    /// Thêm clientKey vào Redis Set để đếm khách tham gia duy nhất.
    /// </summary>
    public async Task TrackUniqueRespondentAsync(string surveyId, string clientKey)
    {
        if (!IsRedisConnected) return;
        var key = $"unique:survey:{surveyId}";
        try
        {
            await _redis.SetAddAsync(key, clientKey);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis SET add failed for survey {SurveyId}", surveyId);
        }
    }

    /// <summary>
    /// Trả về số lượng khách tham gia duy nhất (SCARD).
    /// </summary>
    public async Task<long> GetUniqueRespondentCountAsync(string surveyId)
    {
        if (!IsRedisConnected) return 0;
        var key = $"unique:survey:{surveyId}";
        try
        {
            return await _redis.SetLengthAsync(key);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis SET length failed for survey {SurveyId}", surveyId);
            return 0;
        }
    }

    // ───────────────── Redis LIST: Sự kiện gửi bài gần đây ─────────────────

    /// <summary>
    /// Đẩy một sự kiện gửi bài vào đầu Redis List (LPUSH) và cắt giữ tối đa RecentEventsLimit phần tử.
    /// </summary>
    public async Task PushRecentEventAsync(string surveyId, string eventJson)
    {
        if (!IsRedisConnected) return;
        var key = $"events:survey:{surveyId}";
        try
        {
            await _redis.ListLeftPushAsync(key, eventJson);
            await _redis.ListTrimAsync(key, 0, RecentEventsLimit - 1);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis LIST push failed for survey {SurveyId}", surveyId);
        }
    }

    /// <summary>
    /// Lấy danh sách sự kiện gửi gần đây (LRANGE 0..count-1).
    /// </summary>
    public async Task<List<string>> GetRecentEventsAsync(string surveyId, int count = 20)
    {
        if (!IsRedisConnected) return [];
        var key = $"events:survey:{surveyId}";
        try
        {
            var values = await _redis.ListRangeAsync(key, 0, count - 1);
            return values.Select(v => v.ToString()).ToList();
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis LIST range failed for survey {SurveyId}", surveyId);
            return [];
        }
    }

    // ───────────────── Utilities ─────────────────

    public static string HashClient(string value)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }

    private static string CacheKey(string slug) => $"survey:published:{slug}";
}
