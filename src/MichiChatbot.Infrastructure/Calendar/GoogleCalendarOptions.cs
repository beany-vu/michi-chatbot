namespace MichiChatbot.Infrastructure.Calendar;

/// <summary>
/// Where the platform's Calendar API service-account key lives. A file path, not the key content
/// itself (plan.md calls it "GOOGLE_SA_KEY file") — per-machine in dev (User Secrets), a mounted
/// read-only volume in docker (see docker-compose.yml). Never committed.
/// </summary>
public class GoogleCalendarOptions
{
    public const string SectionName = "GoogleCalendar";

    public required string CredentialsPath { get; set; }
}
