using System.Reflection;
using AppTemplate.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AppTemplate.Api.Data;

public static class ValueObjectConventions
{
    private static readonly Type ConverterDefinition = typeof(ValueObjectConverter<,>);

    /// <summary>Maps every <c>IValueObject&lt;,&gt;</c> in the assembly to its <c>Value</c> column.</summary>
    public static void Apply(ModelConfigurationBuilder builder, Assembly domainAssembly)
    {
        foreach (var type in domainAssembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
            {
                continue;
            }

            foreach (var contract in type.GetInterfaces())
            {
                if (contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IValueObject<,>)
                    && contract.GetGenericArguments()[0] == type)
                {
                    var converterType = ConverterDefinition.MakeGenericType(type, contract.GetGenericArguments()[1]);
                    builder.Properties(type).HaveConversion(converterType);
                }
            }
        }
    }

    private sealed class ValueObjectConverter<TSelf, TValue>()
        : ValueConverter<TSelf, TValue>(v => v.Value, v => Rehydrate(v))
        where TSelf : IValueObject<TSelf, TValue>
    {
        private static TSelf Rehydrate(TValue value) => TSelf.FromTrusted(value);
    }
}
