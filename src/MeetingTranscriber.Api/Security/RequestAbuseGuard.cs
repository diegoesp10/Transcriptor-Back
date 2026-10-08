using MeetingTranscriber.Api.Configuration;
using MeetingTranscriber.Application;

namespace MeetingTranscriber.Api.Security;

public enum AbuseSignal { Authentication, UnknownRoute, RateViolation }
public sealed record RequestRejection(Error Error, int RetryAfterSeconds)
{
    public string Code => Error.Code.ToString();
}

public sealed class RequestAbuseGuard(RequestLimitOptions settings, TimeProvider clock)
{
    private readonly object sync = new();
    private readonly Dictionary<string, ClientState> clients = new(StringComparer.Ordinal);
    private readonly Queue<DateTimeOffset> globalRequests = new();
    private DateTimeOffset nextCleanup;
    private int activeUploads, activeRequests;

    public int TrackedIps { get { lock (sync) return clients.Count; } }

    public RequestRejection? TryEnter(string ip, bool upload, out IDisposable? admission)
    {
        admission = null;
        lock (sync)
        {
            var now = clock.GetUtcNow();
            if (now >= nextCleanup)
            {
                foreach (var key in clients.Where(p => p.Value.Active == 0 && p.Value.BlockedUntil <= now
                    && p.Value.LastSeen + TimeSpan.FromMinutes(settings.IdleRetentionMinutes) <= now).Select(p => p.Key).ToArray())
                    clients.Remove(key);
                nextCleanup = now.AddSeconds(30);
            }
            clients.TryGetValue(ip, out var client);
            if (client is not null && client.BlockedUntil > now) return Blocked(client, now);
            while (globalRequests.TryPeek(out var globalTimestamp) && globalTimestamp.AddMinutes(1) <= now) globalRequests.Dequeue();
            if (globalRequests.Count >= settings.GlobalPerMinute)
                return new(ErrorCatalog.Get(FailureCode.GlobalRequestLimited), Seconds(globalRequests.Peek().AddMinutes(1), now));
            if (activeRequests >= settings.MaxConcurrentRequests) return new(ErrorCatalog.Get(FailureCode.ServerConcurrencyLimited), 2);
            if (client is null)
            {
                // Never evict live penalties or allocate an unbounded cache under an IP flood.
                if (clients.Count >= settings.MaxTrackedIps) return new(ErrorCatalog.Get(FailureCode.SecurityCapacityReached), 30);
                clients[ip] = client = new() { LastSeen = now, SignalWindow = now };
            }
            if (client.BlockedUntil > now) return Blocked(client, now);
            client.LastSeen = now;
            while (client.Burst.TryPeek(out var timestamp) && timestamp + TimeSpan.FromSeconds(settings.BurstWindowSeconds) <= now)
                client.Burst.Dequeue();
            if (client.Burst.Count >= settings.PerIpBurstRequests)
            {
                Signal(client, AbuseSignal.RateViolation, now);
                return client.BlockedUntil > now ? Blocked(client, now)
                    : new(ErrorCatalog.Get(FailureCode.RequestBurstLimited), Seconds(client.Burst.Peek().AddSeconds(settings.BurstWindowSeconds), now));
            }
            client.Burst.Enqueue(now);
            if (client.Active >= settings.PerIpConcurrentRequests || (upload && (client.Uploads > 0 || activeUploads >= settings.MaxConcurrentUploads)))
                return new(ErrorCatalog.Get(FailureCode.RequestConcurrencyLimited), 2);
            client.Active++;
            activeRequests++;
            globalRequests.Enqueue(now);
            if (upload) { client.Uploads++; activeUploads++; }
            admission = new Admission(this, client, upload);
            return null;
        }
    }

    public void Record(string ip, AbuseSignal signal)
    {
        lock (sync)
        {
            // Only admitted clients can create state; rejected traffic never grows the dictionary.
            if (clients.TryGetValue(ip, out var client)) Signal(client, signal, clock.GetUtcNow());
        }
    }

    private void Signal(ClientState client, AbuseSignal signal, DateTimeOffset now)
    {
        if (client.BlockedUntil > now) return; // Requests during a ban don't extend it.
        if (client.SignalWindow.AddMinutes(1) <= now)
        {
            client.SignalWindow = now;
            client.AuthFailures = client.UnknownRoutes = client.RateViolations = 0;
        }
        var reached = signal switch
        {
            AbuseSignal.Authentication => ++client.AuthFailures >= settings.AuthenticationFailuresPerMinute,
            AbuseSignal.UnknownRoute => ++client.UnknownRoutes >= settings.UnknownRoutesPerMinute,
            _ => ++client.RateViolations >= settings.RateViolationsPerMinute
        };
        if (!reached) return;
        client.Penalties = Math.Min(16, client.Penalties + 1);
        var duration = Math.Min(settings.MaxBlockSeconds, settings.BlockSeconds * (1L << (client.Penalties - 1)));
        client.BlockedUntil = now.AddSeconds(duration);
        client.SignalWindow = client.BlockedUntil;
        client.AuthFailures = client.UnknownRoutes = client.RateViolations = 0;
        client.Burst.Clear();
    }

    private static int Seconds(DateTimeOffset until, DateTimeOffset now) => Math.Max(1, (int)Math.Ceiling((until - now).TotalSeconds));
    private static RequestRejection Blocked(ClientState client, DateTimeOffset now) =>
        new(ErrorCatalog.Get(FailureCode.ClientTemporarilyBlocked), Seconds(client.BlockedUntil, now));

    private sealed class ClientState
    {
        public DateTimeOffset LastSeen, SignalWindow, BlockedUntil;
        public Queue<DateTimeOffset> Burst { get; } = new();
        public int Active, Uploads, AuthFailures, UnknownRoutes, RateViolations, Penalties;
    }

    private sealed class Admission(RequestAbuseGuard owner, ClientState client, bool upload) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lock (owner.sync)
            {
                client.Active--;
                owner.activeRequests--;
                if (upload) { client.Uploads--; owner.activeUploads--; }
            }
        }
    }
}
