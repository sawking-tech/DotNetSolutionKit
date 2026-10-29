using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Storage;

public static class DependencyInjection
{
    /// <summary>
    /// Registers <see cref="IS3ObjectStorage"/> from the <c>S3</c> section: the real client, or with
    /// <c>S3:Enabled=false</c> a storage that keeps nothing, so a service runs before it has a bucket.
    /// </summary>
    public static IServiceCollection AddS3ObjectStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var enabled = configuration.GetValue($"{S3Settings.SectionName}:Enabled", defaultValue: true);

        if (enabled)
        {
            services.AddOptions<S3Settings>()
                .BindConfiguration(S3Settings.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.AddSingleton<IS3Settings>(sp => sp.GetRequiredService<IOptions<S3Settings>>().Value);
            services.AddSingleton<IS3ObjectStorage, S3ObjectStorage>();
        }
        else
        {
            services.AddSingleton<IS3ObjectStorage, NullS3ObjectStorage>();
        }

        return services;
    }
}
