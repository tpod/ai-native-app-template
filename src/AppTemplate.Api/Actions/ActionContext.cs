using AppTemplate.Api.Data;

namespace AppTemplate.Api.Actions;

/// <summary>A non-blocking note attached to a preview, such as "this project already has 90 tasks".</summary>
public sealed record PreviewWarning(string Code, string Message);

/// <summary>
/// Per-call services handed to <c>Load</c>, <c>Apply</c> and <c>Run</c>. One instance per preview, execute or query.
/// </summary>
public sealed class ActionContext
{
    private readonly IReadOnlyList<Guid> _replayIds;
    private readonly List<Guid> _allocatedIds = [];
    private readonly List<PreviewWarning> _warnings = [];

    /// <param name="db">The scoped context. Handlers track changes on it; the pipeline decides whether to save.</param>
    /// <param name="time">The clock handlers must use instead of <see cref="DateTimeOffset.UtcNow"/>.</param>
    /// <param name="replayIds">Ids allocated during the preview, handed out again in order on execute so a create gets the same id.</param>
    /// <param name="ct">The request's cancellation token.</param>
    internal ActionContext(AppDbContext db, TimeProvider time, IReadOnlyList<Guid>? replayIds, CancellationToken ct)
    {
        Db = db;
        Time = time;
        _replayIds = replayIds ?? [];
        CancellationToken = ct;
    }

    public AppDbContext Db { get; }

    public TimeProvider Time { get; }

    public CancellationToken CancellationToken { get; }

    /// <summary>Every id handed out by <see cref="NewId"/>, in order.</summary>
    public IReadOnlyList<Guid> AllocatedIds => _allocatedIds;

    public IReadOnlyList<PreviewWarning> Warnings => _warnings;

    /// <summary>
    /// Allocates an id for a new entity: the next replayed id when one is left, otherwise <see cref="Guid.CreateVersion7()"/>.
    /// Always use this instead of creating ids directly, so execute reproduces the preview.
    /// </summary>
    public Guid NewId()
    {
        var id = _allocatedIds.Count < _replayIds.Count ? _replayIds[_allocatedIds.Count] : Guid.CreateVersion7();
        _allocatedIds.Add(id);
        return id;
    }

    /// <summary>Records a warning shown with the preview. It does not stop the action.</summary>
    public void Warn(string code, string message) => _warnings.Add(new PreviewWarning(code, message));
}
