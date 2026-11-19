using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Actions;

/// <summary>
/// A mutation handler: loads one aggregate, applies a change to it, and is previewed before it executes.
/// </summary>
/// <remarks>
/// <para>
/// Besides the members below, the handler class must declare two public <b>convention methods</b>.
/// Their OneOf error arguments are the single source of truth for the action's error codes, so they
/// cannot be interface members. <see cref="ActionRegistry"/> checks them at startup.
/// </para>
/// <list type="bullet">
/// <item>
/// <c>Task&lt;OneOf&lt;TAggregate, E1, …&gt;&gt; Load(TInput input, ActionContext ctx)</c> loads the target aggregate,
/// or creates it. A create adds the new aggregate to <c>ctx.Db</c> and takes its id from <see cref="ActionContext.NewId"/>.
/// </item>
/// <item>
/// <c>Task&lt;OneOf&lt;Success, E1, …&gt;&gt; Apply(TAggregate target, TInput input, ActionContext ctx)</c> converts the
/// input into value objects and calls domain behaviour. It never saves.
/// </item>
/// </list>
/// <para>Every error argument <c>E1, …</c> must derive from <see cref="AppError"/> and declare <see cref="ErrorAttribute"/>.</para>
/// </remarks>
/// <typeparam name="TInput">The input record.</typeparam>
/// <typeparam name="TAggregate">The aggregate the mutation changes.</typeparam>
public interface IMutation<TInput, TAggregate> where TAggregate : class, IAggregateRoot
{
    /// <summary>One-line human summary shown in the preview. Called after <c>Load</c>, before <c>Apply</c>.</summary>
    string Summarize(TAggregate target, TInput input);

    /// <summary>The execute response body, built from the saved aggregate.</summary>
    object ToResult(TAggregate target);
}
