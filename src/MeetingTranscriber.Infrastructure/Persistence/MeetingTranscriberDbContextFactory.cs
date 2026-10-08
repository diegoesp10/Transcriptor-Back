using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MeetingTranscriber.Infrastructure.Persistence;

// Migrations do not start the web host, worker or require API keys/FFmpeg.
public sealed class MeetingTranscriberDbContextFactory : IDesignTimeDbContextFactory<MeetingTranscriberDbContext>
{
    public MeetingTranscriberDbContext CreateDbContext(string[] args)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        string? settingsDirectory = null;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "MeetingTranscriber.Api");
            if (File.Exists(Path.Combine(candidate, "appsettings.json"))) { settingsDirectory = candidate; break; }
            directory = directory.Parent;
        }
        if (settingsDirectory is null) throw new InvalidOperationException("Run EF commands inside the solution directory.");
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new ConfigurationBuilder().SetBasePath(settingsDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("MeetingTranscriber")
            ?? throw new InvalidOperationException("ConnectionStrings:MeetingTranscriber is required.");
        var options = new DbContextOptionsBuilder<MeetingTranscriberDbContext>().UseSqlServer(connection).Options;
        return new MeetingTranscriberDbContext(options);
    }
}
