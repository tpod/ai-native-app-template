using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Actions.Preview;

public sealed class PreviewOptions
{
    public const string SectionName = "Preview";
    public const int MinKeyBytes = 32;

    public string SigningKey { get; set; } = string.Empty;
}

public sealed class PreviewOptionsValidator : IValidateOptions<PreviewOptions>
{
    public ValidateOptionsResult Validate(string? name, PreviewOptions options)
        => Encoding.UTF8.GetByteCount(options.SigningKey ?? string.Empty) >= PreviewOptions.MinKeyBytes
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{PreviewOptions.SectionName}:SigningKey must be at least {PreviewOptions.MinKeyBytes} UTF-8 bytes.");
}

/// <summary>In Development only, a missing key becomes a random per-process key.</summary>
public sealed class PreviewOptionsPostConfigure(IHostEnvironment environment, ILogger<PreviewOptionsPostConfigure> logger)
    : IPostConfigureOptions<PreviewOptions>
{
    public void PostConfigure(string? name, PreviewOptions options)
    {
        if (!string.IsNullOrEmpty(options.SigningKey) || !environment.IsDevelopment())
        {
            return;
        }

        options.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
#pragma warning disable CA1848 // One-off startup message.
        logger.LogWarning("Preview:SigningKey is not set; using a random key. Previews will not survive a restart.");
#pragma warning restore CA1848
    }
}

public static class PreviewServiceCollectionExtensions
{
    public static IServiceCollection AddPreviewTokens(this IServiceCollection services)
    {
        services.AddOptions<PreviewOptions>().BindConfiguration(PreviewOptions.SectionName).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PreviewOptions>, PreviewOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<PreviewOptions>, PreviewOptionsPostConfigure>());
        services.TryAddSingleton<PreviewTokenSigner>();
        return services;
    }
}
