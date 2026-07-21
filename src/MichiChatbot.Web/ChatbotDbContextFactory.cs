using MichiChatbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MichiChatbot.Web;

/// <summary>
/// Design-time factory for the <c>dotnet ef</c> tools (migrations add / script / update). The tools
/// don't start the app, so no <c>IConfiguration</c> is injected — we build a minimal config pipeline
/// by hand and read the SAME <c>ConnectionStrings:Chatbot</c> the running app uses. This lives in the
/// Web (composition-root) project so config-file loading stays out of Infrastructure, and it passes
/// <see cref="NullTenantAccessor"/> because there is no request-scoped tenant at design time.
/// </summary>
public sealed class ChatbotDbContextFactory : IDesignTimeDbContextFactory<ChatbotDbContext>
{
    public ChatbotDbContext CreateDbContext(string[] args)
    {
        // dotnet ef runs from the startup project's directory, so its appsettings files resolve here.
        // Environment variables (e.g. ConnectionStrings__Chatbot) override the file for CI/prod.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            // The password lives in User Secrets (dev), never in appsettings. The runtime host loads
            // User Secrets automatically in Development; this hand-built pipeline must opt in explicitly.
            .AddUserSecrets(typeof(ChatbotDbContextFactory).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Chatbot")
            ?? throw new InvalidOperationException(
                "Connection string 'Chatbot' not found. Set ConnectionStrings:Chatbot in appsettings "
                + "or the ConnectionStrings__Chatbot environment variable.");

        var options = new DbContextOptionsBuilder<ChatbotDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ChatbotDbContext(options, NullTenantAccessor.Instance);
    }
}
