namespace MosaicGenerator.Core.Imaging;

/// <summary>
/// How hard the photograph is flattened before it is sampled, and how directionally.
/// <c>RadiusTesserae</c> is the reach along the form, in tesserae;
/// <c>AcrossFraction</c> is how much of that reach survives across it, where the
/// tensor is sure of a direction — 1,0 makes the kernel round again.
/// See docs/ploskosti-spike.md and docs/anizotropnoe-uploshchenie-plan.md.
/// </summary>
public sealed record FlattenSettings(
    double RadiusTesserae, double RangeDe, int Iterations, double AcrossFraction = 0.25);
