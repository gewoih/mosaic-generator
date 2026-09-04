using System.Globalization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using MosaicGenerator.Core.Domain;
using MosaicGenerator.Core.Imaging;
using MosaicGenerator.Core.Pipeline;
using MosaicGenerator.Core.Rendering;
using MosaicGenerator.Core.Skia;
using MosaicGenerator.Core.Validation;
using MosaicGenerator.Web.Options;
using MosaicGenerator.Web.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<MosaicOptions>()
    .Bind(builder.Configuration.GetSection(MosaicOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

MosaicOptions mosaicOptions = builder.Configuration
    .GetSection(MosaicOptions.SectionName)
    .Get<MosaicOptions>() ?? new MosaicOptions();

// Generation is synchronous, so the upload has to be bounded before it reaches a controller.
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = (long)(mosaicOptions.MaxUploadBytes * 1.5);
});

builder.WebHost.ConfigureKestrel(options =>
{
    // Deliberately above the user-facing limit. Kestrel aborts an oversized body mid-stream with a
    // bare 400 that no view can dress up, so leave headroom for the controller to answer a modest
    // overshoot with a readable message; this stays the hard backstop for the rest.
    options.Limits.MaxRequestBodySize = (long)(mosaicOptions.MaxUploadBytes * 1.5);
});

// An HTML number input always posts with a dot, whatever the server's locale. Binding is pinned
// to the invariant culture so a fractional grout width parses; the views format for display with
// an explicit ru-RU culture, which is a separate concern.
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture);
    options.SupportedCultures = [CultureInfo.InvariantCulture];
    options.SupportedUICultures = [CultureInfo.InvariantCulture];
    options.RequestCultureProviders.Clear();
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IImageLoader, SkiaImageLoader>();
builder.Services.AddSingleton<IMosaicRenderer, SkiaMosaicRenderer>();
builder.Services.AddSingleton<IResultStore, TempResultStore>();
builder.Services.AddSingleton<ISourceStore, TempSourceStore>();

builder.Services.AddSingleton<IPaletteRepository>(provider =>
{
    MosaicOptions options = provider.GetRequiredService<IOptions<MosaicOptions>>().Value;
    IWebHostEnvironment environment = provider.GetRequiredService<IWebHostEnvironment>();

    string directory = Path.IsPathRooted(options.PaletteDirectory)
        ? options.PaletteDirectory
        : Path.Combine(environment.ContentRootPath, options.PaletteDirectory);

    return new JsonPaletteRepository(directory);
});

builder.Services.AddSingleton(provider =>
{
    MosaicOptions options = provider.GetRequiredService<IOptions<MosaicOptions>>().Value;

    return new MosaicGenerationOptions
    {
        ImageLimits = new ImageLoadLimits
        {
            MaxDeclaredPixels = options.MaxDeclaredPixels,
            MaxDecodedPixels = options.MaxDecodedPixels,
        },
        ValidationLimits = new ValidationLimits(),
        Cartoon = RenderOptions.Cartoon with
        {
            PixelsPerStep = options.CartoonPixelsPerStep,
            MaxLongSidePx = options.MaxLongSidePx,
            MaxTotalPixels = options.MaxTotalPixels,
        },
        Scheme = RenderOptions.Scheme with
        {
            PixelsPerStep = options.SchemePixelsPerStep,
            MaxLongSidePx = options.MaxLongSidePx,
            MaxTotalPixels = options.MaxTotalPixels,
        },
    };
});

builder.Services.AddSingleton<MosaicGenerationService>();
builder.Services.AddControllersWithViews();

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseRequestLocalization();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapControllers();

app.Run();
