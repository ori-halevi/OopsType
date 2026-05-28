using System;

namespace OopsType.Infrastructure;

/// <summary>
/// Append-only diagnostic logger. Implementations MUST swallow their own I/O errors —
/// a logger that throws would defeat its purpose (it's used inside every catch block).
/// </summary>
public interface ILogger
{
    void Error(string source, Exception ex);
    void Warn(string source, string message);
    void Info(string message);
}
