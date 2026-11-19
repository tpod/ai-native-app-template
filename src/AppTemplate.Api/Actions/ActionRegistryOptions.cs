using System.Reflection;

namespace AppTemplate.Api.Actions;

/// <summary>Where <see cref="ActionRegistry"/> looks for <c>[Action]</c> and <c>[Query]</c> types.</summary>
public sealed class ActionRegistryOptions
{
    /// <summary>Assemblies whose types are scanned. Defaults to the Api assembly; tests add their fixture assembly.</summary>
    public List<Assembly> Assemblies { get; } = [typeof(ActionRegistryOptions).Assembly];

    /// <summary>Individual types added on top of the scanned assemblies.</summary>
    public List<Type> Types { get; } = [];
}
