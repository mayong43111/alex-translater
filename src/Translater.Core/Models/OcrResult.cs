namespace Translater.Core.Models;

public record OcrResult(
    string Text,
    IReadOnlyList<OcrLine> Lines);

public record OcrLine(string Text, double X, double Y, double Width, double Height);
