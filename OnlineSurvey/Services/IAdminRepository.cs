using OnlineSurvey.Models;

namespace OnlineSurvey.Services;

public interface IAdminRepository
{
    Task InitializeAsync();
    Task<bool> HasAnyAsync();
    Task<string?> GetAnyUsernameAsync();
    Task<AdminAccount?> AuthenticateAsync(string username, string password);
    Task<bool> CreateInitialAsync(string username, string password);
}
