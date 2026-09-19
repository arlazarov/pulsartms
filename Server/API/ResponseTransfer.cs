using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace API;

public static class ResponseTransfer
{
  public static IServiceCollection AddOperationalCompression(
    this IServiceCollection services
  )
  {
    services.AddResponseCompression(options =>
    {
      options.EnableForHttps = true;
      options.MimeTypes = ["application/json"];
      options.Providers.Add<BrotliCompressionProvider>();
      options.Providers.Add<GzipCompressionProvider>();
    });
    services.Configure<BrotliCompressionProviderOptions>(options =>
      options.Level = CompressionLevel.Fastest
    );
    services.Configure<GzipCompressionProviderOptions>(options =>
      options.Level = CompressionLevel.Fastest
    );
    return services;
  }

  public static IApplicationBuilder UseOperationalCompression(
    this IApplicationBuilder app
  ) =>
    app.UseWhen(
      context =>
        context.User.Identity?.IsAuthenticated == true
        && context
          .GetEndpoint()
          ?.Metadata.GetMetadata<CompressResponseAttribute>()
          is not null,
      branch => branch.UseResponseCompression()
    );
}
