namespace AppTemplate.Api.Actions.Outbox;

/// <summary>Outbox worker and delivery settings, bound to the <c>Outbox</c> configuration section.</summary>
public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    /// <summary>False keeps the background worker idle; tests drain the outbox themselves.</summary>
    public bool WorkerEnabled { get; set; } = true;

    public int BatchSize { get; set; } = 20;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How long a claimed row stays reserved; an expired lease is reclaimed. Keep it above <see cref="HandlerTimeout"/>.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(60);

    public TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxAttempts { get; set; } = 10;

    public TimeSpan BackoffBase { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan BackoffCap { get; set; } = TimeSpan.FromHours(1);

    public int RetentionDays { get; set; } = 7;
}
