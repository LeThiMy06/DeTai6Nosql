using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using OnlineSurvey.Infrastructure;
using OnlineSurvey.Models;

namespace OnlineSurvey.Services;

public sealed class MongoAdminRepository : IAdminRepository
{
    private readonly IMongoCollection<AdminAccount> _admins;
    private readonly IPasswordHasher<AdminAccount> _passwordHasher;

    public MongoAdminRepository(
        IMongoClient mongoClient,
        IOptions<AuthDatabaseSettings> authDatabaseOptions,
        IPasswordHasher<AdminAccount> passwordHasher)
    {
        var database = mongoClient.GetDatabase(authDatabaseOptions.Value.DatabaseName);
        _admins = database.GetCollection<AdminAccount>("admins");
        _passwordHasher = passwordHasher;
    }

    public Task InitializeAsync()
    {
        var usernameIndex = new CreateIndexModel<AdminAccount>(
            Builders<AdminAccount>.IndexKeys.Ascending(x => x.NormalizedUsername),
            new CreateIndexOptions
            {
                Name = "NormalizedUsername_1",
                Unique = true
            });

        var initialAccountIndex = new CreateIndexModel<AdminAccount>(
            Builders<AdminAccount>.IndexKeys.Ascending(x => x.IsInitialAccount),
            new CreateIndexOptions<AdminAccount>
            {
                Name = "InitialAdmin_1",
                Unique = true,
                PartialFilterExpression = Builders<AdminAccount>.Filter.Eq(x => x.IsInitialAccount, true)
            });

        _admins.Indexes.CreateOne(usernameIndex);
        return _admins.Indexes.CreateOneAsync(initialAccountIndex);
    }

    public async Task<bool> HasAnyAsync() =>
        await _admins.CountDocumentsAsync(FilterDefinition<AdminAccount>.Empty) > 0;

    public async Task<string?> GetAnyUsernameAsync() =>
        (await _admins.Find(FilterDefinition<AdminAccount>.Empty).FirstOrDefaultAsync())?.Username;

    public async Task<AdminAccount?> AuthenticateAsync(string username, string password)
    {
        var normalizedUsername = NormalizeUsername(username);
        var admin = await _admins
            .Find(x => x.NormalizedUsername == normalizedUsername && x.IsActive)
            .FirstOrDefaultAsync();

        if (admin is null)
        {
            return null;
        }

        var verification = _passwordHasher.VerifyHashedPassword(
            admin,
            admin.PasswordHash,
            password);

        if (verification == PasswordVerificationResult.Failed)
        {
            return null;
        }

        var update = Builders<AdminAccount>.Update
            .Set(x => x.LastLoginAt, DateTime.UtcNow);

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            admin.PasswordHash = _passwordHasher.HashPassword(admin, password);
            update = update.Set(x => x.PasswordHash, admin.PasswordHash);
        }

        await _admins.UpdateOneAsync(x => x.Id == admin.Id, update);
        return admin;
    }

    public async Task<bool> CreateInitialAsync(string username, string password)
    {
        if (await HasAnyAsync())
        {
            return false;
        }

        var admin = new AdminAccount
        {
            Username = username.Trim(),
            NormalizedUsername = NormalizeUsername(username)
        };
        admin.PasswordHash = _passwordHasher.HashPassword(admin, password);

        try
        {
            await _admins.InsertOneAsync(admin);
            return true;
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    private static string NormalizeUsername(string username) =>
        username.Trim().ToUpperInvariant();
}
