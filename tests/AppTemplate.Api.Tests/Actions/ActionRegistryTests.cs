using AppTemplate.Api.Actions;
using AppTemplate.Api.Tests.Infrastructure;
using AppTemplate.Domain.Common;
using OneOf;
using OneOf.Types;
using Shouldly;

namespace AppTemplate.Api.Tests.Actions;

public sealed class ActionRegistryTests
{
    // ---- Sample domain ----

    public readonly record struct SampleId(Guid Value);

    public sealed class SampleAgg : AggregateRoot<SampleId>
    {
        public SampleAgg(SampleId id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Name { get; set; }

        public bool Locked { get; init; }
    }

    [Error("sample.not_found", ErrorKind.NotFound, "Sample not found")]
    public sealed record SampleNotFound : AppError
    {
        public override string Message => "Sample does not exist.";
        public override string Suggestion => "Use another id.";
    }

    [Error("sample.locked", ErrorKind.Conflict, "Sample locked")]
    public sealed record SampleLocked : AppError
    {
        public override string Message => "Sample is locked.";
        public override string Suggestion => "Unlock it first.";
    }

    public sealed record Undeclared : AppError
    {
        public override string Message => "";
        public override string Suggestion => "";
    }

    public sealed record SampleInput(string Name);

    public readonly record struct SampleStructInput(string Name);

    public sealed class SampleClassInput
    {
        public string Name { get; init; } = "";
    }

    public sealed record SampleOutput(int Count);

    private static Task<OneOf<SampleAgg, SampleNotFound>> LoadOk(string name, ActionContext ctx)
        => Task.FromResult<OneOf<SampleAgg, SampleNotFound>>(new SampleAgg(new SampleId(ctx.NewId()), name));

    private static Task<OneOf<Success, SampleLocked>> ApplyOk() => Task.FromResult<OneOf<Success, SampleLocked>>(new Success());

    private static Task<OneOf<SampleOutput, SampleNotFound>> RunOk() => Task.FromResult<OneOf<SampleOutput, SampleNotFound>>(new SampleOutput(1));

    public abstract class SampleMutationBase : IMutation<SampleInput, SampleAgg>
    {
        public string Summarize(SampleAgg target, SampleInput input) => $"{target.Name} -> {input.Name}";

        public object ToResult(SampleAgg target) => target.Name;
    }

    // ---- Valid handlers ----

    [Action("sample_do", "Does it.")]
    public sealed class ValidMutation : IMutation<SampleInput, SampleAgg>
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load(SampleInput input, ActionContext ctx)
            => input.Name == "missing"
                ? Task.FromResult<OneOf<SampleAgg, SampleNotFound>>(new SampleNotFound())
                : Task.FromResult<OneOf<SampleAgg, SampleNotFound>>(new SampleAgg(new SampleId(ctx.NewId()), "before") { Locked = input.Name == "locked" });

        public Task<OneOf<Success, SampleLocked>> Apply(SampleAgg target, SampleInput input, ActionContext ctx)
        {
            if (target.Locked)
            {
                return Task.FromResult<OneOf<Success, SampleLocked>>(new SampleLocked());
            }

            target.Name = input.Name;
            return Task.FromResult<OneOf<Success, SampleLocked>>(new Success());
        }

        public string Summarize(SampleAgg target, SampleInput input) => $"Rename '{target.Name}' to '{input.Name}'";

        public object ToResult(SampleAgg target) => new SampleOutput(target.Name.Length);
    }

    [Query("sample_list", "Lists samples.")]
    public sealed class ValidQuery : IQuery<SampleInput, SampleOutput>
    {
        public Task<OneOf<SampleOutput, SampleNotFound>> Run(SampleInput input, ActionContext ctx)
            => input.Name == "missing"
                ? Task.FromResult<OneOf<SampleOutput, SampleNotFound>>(new SampleNotFound())
                : Task.FromResult<OneOf<SampleOutput, SampleNotFound>>(new SampleOutput(input.Name.Length));
    }

    [Action("sample_destroy", "Destroys it.", Destructive = true, PreviewLifetimeSeconds = 60)]
    public sealed class DestructiveStructInputMutation : IMutation<SampleStructInput, SampleAgg>
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load(SampleStructInput input, ActionContext ctx) => LoadOk(input.Name, ctx);

        public Task<OneOf<Success, SampleLocked, SampleNotFound>> Apply(SampleAgg target, SampleStructInput input, ActionContext ctx)
            => Task.FromResult<OneOf<Success, SampleLocked, SampleNotFound>>(new Success());

        public string Summarize(SampleAgg target, SampleStructInput input) => "destroy";

        public object ToResult(SampleAgg target) => target.Name;
    }

    public sealed class NotAnAction : SampleMutationBase
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load(SampleInput input, ActionContext ctx) => LoadOk(input.Name, ctx);

        public Task<OneOf<Success, SampleLocked>> Apply(SampleAgg target, SampleInput input, ActionContext ctx) => ApplyOk();
    }

    // ---- Invalid handlers: one rule each ----

    [Action("non_error_arg", "Bad error arg.")]
    public sealed class NonErrorArg : SampleMutationBase
    {
        public Task<OneOf<SampleAgg, string>> Load(SampleInput input, ActionContext ctx)
            => Task.FromResult<OneOf<SampleAgg, string>>("nope");

        public Task<OneOf<Success, SampleLocked>> Apply(SampleAgg target, SampleInput input, ActionContext ctx) => ApplyOk();
    }

    [Action("Bad-Name", "Bad name.")]
    public sealed class BadName : SampleMutationBase
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load(SampleInput input, ActionContext ctx) => LoadOk(input.Name, ctx);

        public Task<OneOf<Success, SampleLocked>> Apply(SampleAgg target, SampleInput input, ActionContext ctx) => ApplyOk();
    }

    [Action("empty_description", "  ")]
    public sealed class EmptyDescription : SampleMutationBase
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load(SampleInput input, ActionContext ctx) => LoadOk(input.Name, ctx);

        public Task<OneOf<Success, SampleLocked>> Apply(SampleAgg target, SampleInput input, ActionContext ctx) => ApplyOk();
    }

    [Action("class_input", "Class input.")]
    public sealed class ClassInput : IMutation<SampleClassInput, SampleAgg>
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load(SampleClassInput input, ActionContext ctx) => LoadOk(input.Name, ctx);

        public Task<OneOf<Success, SampleLocked>> Apply(SampleAgg target, SampleClassInput input, ActionContext ctx) => ApplyOk();

        public string Summarize(SampleAgg target, SampleClassInput input) => "";

        public object ToResult(SampleAgg target) => "";
    }

    [Action("missing_apply", "No Apply.")]
    public sealed class MissingApply : SampleMutationBase
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load(SampleInput input, ActionContext ctx) => LoadOk(input.Name, ctx);
    }

    [Query("preview_sample", "Shadows the preview tool of a mutation named sample.")]
    public sealed class PreviewPrefixedQuery : IQuery<SampleInput, SampleOutput>
    {
        public Task<OneOf<SampleOutput, SampleNotFound>> Run(SampleInput input, ActionContext ctx) => RunOk();
    }

    [Action("preview_sample_do", "Mutation whose name collides with generated preview tools.")]
    public sealed class PreviewPrefixedMutation : SampleMutationBase
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load(SampleInput input, ActionContext ctx) => LoadOk(input.Name, ctx);

        public Task<OneOf<Success, SampleLocked>> Apply(SampleAgg target, SampleInput input, ActionContext ctx) => ApplyOk();
    }

    public sealed record ReasonInput(string Name, string Reason);

    [Action("reason_input", "Mutation whose input shadows the reserved reason envelope field.")]
    public sealed class ReasonInputMutation : IMutation<ReasonInput, SampleAgg>
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load(ReasonInput input, ActionContext ctx) => LoadOk(input.Name, ctx);

        public Task<OneOf<Success, SampleLocked>> Apply(SampleAgg target, ReasonInput input, ActionContext ctx) => ApplyOk();

        public string Summarize(SampleAgg target, ReasonInput input) => "";

        public object ToResult(SampleAgg target) => "";
    }

    [Query("duplicate", "First.")]
    public sealed class DuplicateA : IQuery<SampleInput, SampleOutput>
    {
        public Task<OneOf<SampleOutput, SampleNotFound>> Run(SampleInput input, ActionContext ctx) => RunOk();
    }

    [Query("duplicate", "Second.")]
    public sealed class DuplicateB : IQuery<SampleInput, SampleOutput>
    {
        public Task<OneOf<SampleOutput, SampleNotFound>> Run(SampleInput input, ActionContext ctx) => RunOk();
    }

    [Action("both_attributes", "Both.")]
    [Query("both_attributes_query", "Both.")]
    public sealed class BothAttributes : SampleMutationBase
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load(SampleInput input, ActionContext ctx) => LoadOk(input.Name, ctx);

        public Task<OneOf<Success, SampleLocked>> Apply(SampleAgg target, SampleInput input, ActionContext ctx) => ApplyOk();
    }

    [Action("action_without_mutation", "No IMutation.")]
    public sealed class ActionWithoutMutation : IQuery<SampleInput, SampleOutput>
    {
        public Task<OneOf<SampleOutput, SampleNotFound>> Run(SampleInput input, ActionContext ctx) => RunOk();
    }

    [Query("query_without_query", "No IQuery.")]
    public sealed class QueryWithoutQuery : SampleMutationBase
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load(SampleInput input, ActionContext ctx) => LoadOk(input.Name, ctx);

        public Task<OneOf<Success, SampleLocked>> Apply(SampleAgg target, SampleInput input, ActionContext ctx) => ApplyOk();
    }

    [Action("load_wrong_t0", "Load T0 is not the aggregate.")]
    public sealed class LoadWrongT0 : SampleMutationBase
    {
        public Task<OneOf<SampleId, SampleNotFound>> Load(SampleInput input, ActionContext ctx)
            => Task.FromResult<OneOf<SampleId, SampleNotFound>>(new SampleId(ctx.NewId()));

        public Task<OneOf<Success, SampleLocked>> Apply(SampleAgg target, SampleInput input, ActionContext ctx) => ApplyOk();
    }

    [Action("apply_wrong_t0", "Apply T0 is not Success.")]
    public sealed class ApplyWrongT0 : SampleMutationBase
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load(SampleInput input, ActionContext ctx) => LoadOk(input.Name, ctx);

        public Task<OneOf<SampleAgg, SampleLocked>> Apply(SampleAgg target, SampleInput input, ActionContext ctx)
            => Task.FromResult<OneOf<SampleAgg, SampleLocked>>(target);
    }

    [Action("load_not_task", "Load is synchronous.")]
    public sealed class LoadNotTask : SampleMutationBase
    {
        public OneOf<SampleAgg, SampleNotFound> Load(SampleInput input, ActionContext ctx) => new SampleAgg(new SampleId(ctx.NewId()), input.Name);

        public Task<OneOf<Success, SampleLocked>> Apply(SampleAgg target, SampleInput input, ActionContext ctx) => ApplyOk();
    }

    [Query("run_wrong_t0", "Run T0 is not the output.")]
    public sealed class RunWrongT0 : IQuery<SampleInput, SampleOutput>
    {
        public Task<OneOf<string, SampleNotFound>> Run(SampleInput input, ActionContext ctx)
            => Task.FromResult<OneOf<string, SampleNotFound>>("x");
    }

    [Query("missing_run", "No Run.")]
    public sealed class MissingRun : IQuery<SampleInput, SampleOutput>;

    [Action("undeclared_error", "Error without [Error].")]
    public sealed class UndeclaredError : SampleMutationBase
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load(SampleInput input, ActionContext ctx) => LoadOk(input.Name, ctx);

        public Task<OneOf<Success, Undeclared>> Apply(SampleAgg target, SampleInput input, ActionContext ctx)
            => Task.FromResult<OneOf<Success, Undeclared>>(new Success());
    }

    [Action("abstract_handler", "Abstract.")]
    public abstract class AbstractHandler : SampleMutationBase
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load(SampleInput input, ActionContext ctx) => LoadOk(input.Name, ctx);

        public Task<OneOf<Success, SampleLocked>> Apply(SampleAgg target, SampleInput input, ActionContext ctx) => ApplyOk();
    }

    [Action("open_generic_handler", "Open generic.")]
    public sealed class OpenGenericHandler<T> : SampleMutationBase
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load(SampleInput input, ActionContext ctx) => LoadOk(input.Name, ctx);

        public Task<OneOf<Success, SampleLocked>> Apply(SampleAgg target, SampleInput input, ActionContext ctx) => ApplyOk();
    }

    [Query("open_generic_input", "Open generic input.")]
    public sealed class OpenGenericInput<TInput> : IQuery<TInput, SampleOutput>
    {
        public Task<OneOf<SampleOutput, SampleNotFound>> Run(TInput input, ActionContext ctx) => RunOk();
    }

    [Action("generic_load", "Generic Load.")]
    public sealed class GenericLoad : SampleMutationBase
    {
        public Task<OneOf<SampleAgg, SampleNotFound>> Load<TExtra>(SampleInput input, ActionContext ctx) => LoadOk(input.Name, ctx);

        public Task<OneOf<Success, SampleLocked>> Apply(SampleAgg target, SampleInput input, ActionContext ctx) => ApplyOk();
    }

    // ---- Registry: valid ----

    [Fact]
    public void Build_ValidMutation_ListsErrorCodesFromLoadAndApply()
    {
        var r = ActionRegistry.Build([typeof(ValidMutation)], _ => false);
        var d = r.Find("sample_do").ShouldNotBeNull();
        d.Kind.ShouldBe(ActionKind.Mutation);
        d.ErrorCodes.ShouldBe(["sample.locked", "sample.not_found"]);
        d.PreviewLifetime.ShouldBe(TimeSpan.FromSeconds(300));
    }

    [Fact]
    public void Build_ValidMutation_DescribesTypes()
    {
        var d = ActionRegistry.Build([typeof(ValidMutation)], _ => false).Find("sample_do").ShouldNotBeNull();
        d.Description.ShouldBe("Does it.");
        d.HandlerType.ShouldBe(typeof(ValidMutation));
        d.InputType.ShouldBe(typeof(SampleInput));
        d.AggregateType.ShouldBe(typeof(SampleAgg));
        d.OutputType.ShouldBeNull();
        d.ErrorTypes.ShouldBe([typeof(SampleNotFound), typeof(SampleLocked)]);
        d.HasValidator.ShouldBeFalse();
        d.Destructive.ShouldBeFalse();
        d.RunAsync.ShouldBeNull();
    }

    [Fact]
    public void Build_InputHasValidator_AddsValidationFailedCode()
        => ActionRegistry.Build([typeof(ValidMutation)], _ => true).Find("sample_do")!.ErrorCodes.ShouldContain("validation.failed");

    [Fact]
    public void Build_HasValidator_ReceivesInputType()
    {
        var seen = new List<Type>();
        ActionRegistry.Build([typeof(ValidMutation), typeof(ValidQuery)], t =>
        {
            seen.Add(t);
            return false;
        });
        seen.ShouldBe([typeof(SampleInput), typeof(SampleInput)]);
    }

    [Fact]
    public void Build_ValidQuery_DescribesQueryWithZeroLifetime()
    {
        var d = ActionRegistry.Build([typeof(ValidQuery)], _ => true).Find("sample_list").ShouldNotBeNull();
        d.Kind.ShouldBe(ActionKind.Query);
        d.InputType.ShouldBe(typeof(SampleInput));
        d.OutputType.ShouldBe(typeof(SampleOutput));
        d.AggregateType.ShouldBeNull();
        d.ErrorCodes.ShouldBe(["sample.not_found", "validation.failed"]);
        d.PreviewLifetime.ShouldBe(TimeSpan.Zero);
        d.Destructive.ShouldBeFalse();
        d.RunAsync.ShouldNotBeNull();
        d.LoadAsync.ShouldBeNull();
        d.ApplyAsync.ShouldBeNull();
        d.Summarize.ShouldBeNull();
        d.ToResult.ShouldBeNull();
    }

    [Fact]
    public void Build_DestructiveWithRecordStructInput_ReadsAttributeAndDedupesErrors()
    {
        var d = ActionRegistry.Build([typeof(DestructiveStructInputMutation)], _ => false).Find("sample_destroy").ShouldNotBeNull();
        d.Destructive.ShouldBeTrue();
        d.PreviewLifetime.ShouldBe(TimeSpan.FromSeconds(60));
        d.InputType.ShouldBe(typeof(SampleStructInput));
        d.ErrorTypes.ShouldBe([typeof(SampleNotFound), typeof(SampleLocked)]);
        d.ErrorCodes.ShouldBe(["sample.locked", "sample.not_found"]);
    }

    [Fact]
    public void Build_SeveralTypes_ListsAllSortedByNameAndIgnoresUnattributed()
    {
        var r = ActionRegistry.Build([typeof(ValidQuery), typeof(NotAnAction), typeof(ValidMutation), typeof(string)], _ => false);
        r.All.Count.ShouldBe(2);
        r.All.Select(d => d.Name).ShouldBe(["sample_do", "sample_list"]);
        r.Find("missing").ShouldBeNull();
    }

    [Fact]
    public void Build_NoCandidates_ReturnsEmptyRegistry()
        => ActionRegistry.Build([], _ => false).All.ShouldBeEmpty();

    // ---- Registry: violations ----

    [Fact]
    public void Build_SeveralInvalidTypes_ReportsEveryViolation()
    {
        var ex = Should.Throw<ActionRegistrationException>(() => ActionRegistry.Build(
            [typeof(NonErrorArg), typeof(BadName), typeof(EmptyDescription), typeof(ClassInput), typeof(MissingApply), typeof(DuplicateA), typeof(DuplicateB)],
            _ => false));
        ex.Violations.Count.ShouldBe(7);
        ex.Violations.ShouldContain(v => v.Contains(nameof(NonErrorArg)) && v.Contains("AppError"));
        ex.Violations.ShouldContain(v => v.Contains(nameof(BadName)) && v.Contains("^[a-z][a-z0-9_]*$"));
        ex.Violations.ShouldContain(v => v.Contains(nameof(EmptyDescription)) && v.Contains("description"));
        ex.Violations.ShouldContain(v => v.Contains(nameof(ClassInput)) && v.Contains("record"));
        ex.Violations.ShouldContain(v => v.Contains(nameof(MissingApply)) && v.Contains("Apply"));
        ex.Violations.ShouldContain(v => v.Contains(nameof(DuplicateA)) && v.Contains("duplicate") && v.Contains(nameof(DuplicateB)));
        ex.Violations.ShouldContain(v => v.Contains(nameof(DuplicateB)) && v.Contains("unique") && v.Contains(nameof(DuplicateA)));
        foreach (var v in ex.Violations)
        {
            ex.Message.ShouldContain(v);
        }
    }

    [Theory]
    [InlineData(typeof(BothAttributes), "both [Action] and [Query]")]
    [InlineData(typeof(ActionWithoutMutation), "IMutation<TInput, TAggregate>")]
    [InlineData(typeof(QueryWithoutQuery), "IQuery<TInput, TOutput>")]
    [InlineData(typeof(LoadWrongT0), "Load")]
    [InlineData(typeof(ApplyWrongT0), "Success")]
    [InlineData(typeof(LoadNotTask), "Task<OneOf<")]
    [InlineData(typeof(RunWrongT0), "SampleOutput")]
    [InlineData(typeof(MissingRun), "Run")]
    [InlineData(typeof(UndeclaredError), "[Error")]
    [InlineData(typeof(AbstractHandler), "non-abstract")]
    [InlineData(typeof(OpenGenericHandler<>), "non-generic")]
    [InlineData(typeof(OpenGenericInput<>), "non-generic")]
    [InlineData(typeof(GenericLoad), "Load must not be generic")]
    [InlineData(typeof(PreviewPrefixedQuery), "must not start with 'preview_'")]
    [InlineData(typeof(PreviewPrefixedMutation), "must not start with 'preview_'")]
    [InlineData(typeof(ReasonInputMutation), "'reason' is reserved")]
    public void Build_InvalidType_ReportsSingleViolationNamingTypeAndRule(Type type, string rule)
    {
        var ex = Should.Throw<ActionRegistrationException>(() => ActionRegistry.Build([type], _ => false));
        var v = ex.Violations.ShouldHaveSingleItem();
        v.ShouldContain(type.Name.Split('`')[0]);
        v.ShouldContain(rule);
    }

    [Fact]
    public void Build_OpenGenericAmongInvalidTypes_KeepsEveryViolation()
    {
        var ex = Should.Throw<ActionRegistrationException>(() => ActionRegistry.Build(
            [typeof(BadName), typeof(OpenGenericHandler<>), typeof(EmptyDescription)], _ => false));
        ex.Violations.Count.ShouldBe(3);
        ex.Violations.ShouldContain(v => v.Contains(nameof(BadName)));
        ex.Violations.ShouldContain(v => v.Contains("OpenGenericHandler<T>") && v.Contains("non-generic"));
        ex.Violations.ShouldContain(v => v.Contains(nameof(EmptyDescription)));
    }

    // ---- Invokers ----

    [Fact]
    public async Task Invokers_MutationHappyPath_LoadApplySummarizeToResult()
    {
        using var db = new SqliteTestDatabase();
        await using var dbContext = db.NewContext();
        var ctx = new ActionContext(dbContext, TimeProvider.System, null, TestContext.Current.CancellationToken);
        var d = ActionRegistry.Build([typeof(ValidMutation)], _ => false).Find("sample_do").ShouldNotBeNull();
        var handler = new ValidMutation();
        var input = new SampleInput("after");

        var loaded = await d.LoadAsync.ShouldNotBeNull()(handler, input, ctx);
        loaded.Index.ShouldBe(0);
        var target = loaded.Value.ShouldBeOfType<SampleAgg>();
        d.Summarize.ShouldNotBeNull()(handler, target, input).ShouldBe("Rename 'before' to 'after'");

        var applied = await d.ApplyAsync.ShouldNotBeNull()(handler, target, input, ctx);
        applied.Value.ShouldBeOfType<Success>();
        target.Name.ShouldBe("after");
        d.ToResult.ShouldNotBeNull()(handler, target).ShouldBe(new SampleOutput(5));
    }

    [Fact]
    public async Task Invokers_MutationErrors_ReturnErrorValues()
    {
        using var db = new SqliteTestDatabase();
        await using var dbContext = db.NewContext();
        var ctx = new ActionContext(dbContext, TimeProvider.System, null, TestContext.Current.CancellationToken);
        var d = ActionRegistry.Build([typeof(ValidMutation)], _ => false).Find("sample_do").ShouldNotBeNull();
        var handler = new ValidMutation();

        var missing = await d.LoadAsync!(handler, new SampleInput("missing"), ctx);
        missing.Index.ShouldBe(1);
        missing.Value.ShouldBeOfType<SampleNotFound>();

        var locked = await d.LoadAsync(handler, new SampleInput("locked"), ctx);
        var applied = await d.ApplyAsync!(handler, locked.Value, new SampleInput("locked"), ctx);
        applied.Value.ShouldBeOfType<SampleLocked>();
    }

    [Fact]
    public async Task Invokers_Query_RunReturnsOutputOrError()
    {
        using var db = new SqliteTestDatabase();
        await using var dbContext = db.NewContext();
        var ctx = new ActionContext(dbContext, TimeProvider.System, null, TestContext.Current.CancellationToken);
        var d = ActionRegistry.Build([typeof(ValidQuery)], _ => false).Find("sample_list").ShouldNotBeNull();
        var handler = new ValidQuery();

        (await d.RunAsync!(handler, new SampleInput("abc"), ctx)).Value.ShouldBe(new SampleOutput(3));
        (await d.RunAsync(handler, new SampleInput("missing"), ctx)).Value.ShouldBeOfType<SampleNotFound>();
    }

    // ---- ActionContext ----

    [Fact]
    public async Task NewId_WithReplayIds_ReturnsThemInOrderThenFresh()
    {
        using var db = new SqliteTestDatabase();
        await using var dbContext = db.NewContext();
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var ctx = new ActionContext(dbContext, TimeProvider.System, [a, b], TestContext.Current.CancellationToken);

        ctx.NewId().ShouldBe(a);
        ctx.NewId().ShouldBe(b);
        var third = ctx.NewId();
        third.ShouldNotBe(a);
        third.ShouldNotBe(b);
        third.Version.ShouldBe(7);
        ctx.AllocatedIds.ShouldBe([a, b, third]);
    }

    [Fact]
    public async Task NewId_WithoutReplayIds_AllocatesFreshVersion7()
    {
        using var db = new SqliteTestDatabase();
        await using var dbContext = db.NewContext();
        var ctx = new ActionContext(dbContext, TimeProvider.System, null, TestContext.Current.CancellationToken);

        var first = ctx.NewId();
        var second = ctx.NewId();
        first.ShouldNotBe(second);
        first.Version.ShouldBe(7);
        ctx.AllocatedIds.ShouldBe([first, second]);
    }

    [Fact]
    public async Task Warn_Twice_RecordsBoth()
    {
        using var db = new SqliteTestDatabase();
        await using var dbContext = db.NewContext();
        var ctx = new ActionContext(dbContext, TimeProvider.System, null, TestContext.Current.CancellationToken);

        ctx.Warn("sample.one", "First");
        ctx.Warn("sample.two", "Second");

        ctx.Warnings.ShouldBe([new PreviewWarning("sample.one", "First"), new PreviewWarning("sample.two", "Second")]);
    }

    [Fact]
    public async Task Constructor_Arguments_ExposedOnContext()
    {
        using var db = new SqliteTestDatabase();
        await using var dbContext = db.NewContext();
        using var cts = new CancellationTokenSource();
        var ctx = new ActionContext(dbContext, TimeProvider.System, null, cts.Token);

        ctx.Db.ShouldBeSameAs(dbContext);
        ctx.Time.ShouldBeSameAs(TimeProvider.System);
        ctx.CancellationToken.ShouldBe(cts.Token);
        ctx.AllocatedIds.ShouldBeEmpty();
        ctx.Warnings.ShouldBeEmpty();
    }
}
