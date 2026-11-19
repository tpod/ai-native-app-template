using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Actions;

/// <summary>
/// A read-only query handler.
/// </summary>
/// <remarks>
/// The handler class must declare the public <b>convention method</b>
/// <c>Task&lt;OneOf&lt;TOutput, E1, …&gt;&gt; Run(TInput input, ActionContext ctx)</c>.
/// Its OneOf error arguments are the single source of truth for the query's error codes; each must derive
/// from <see cref="AppError"/> and declare <see cref="ErrorAttribute"/>. <see cref="ActionRegistry"/> checks it at startup.
/// </remarks>
/// <typeparam name="TInput">The input record.</typeparam>
/// <typeparam name="TOutput">The success result.</typeparam>
public interface IQuery<TInput, TOutput>;
