namespace MosaicGenerator.Core.Imaging;

/// <summary>Thrown for uploads the pipeline refuses; the message is safe to show to the user.</summary>
public sealed class InvalidImageException(string message) : Exception(message);
