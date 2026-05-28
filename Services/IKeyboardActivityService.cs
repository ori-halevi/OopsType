using System;

namespace OopsType.Services;

public interface IKeyboardActivityService : IDisposable
{
    event Action? KeyPressed;
    DateTime LastKeyTimeUtc { get; }
    void Start();
}
