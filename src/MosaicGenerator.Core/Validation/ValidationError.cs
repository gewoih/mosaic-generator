namespace MosaicGenerator.Core.Validation;

public sealed record ValidationError(string Field, string Message);
