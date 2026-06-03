using System;

namespace OopsType.Services;

public interface IIdleResetService : IDisposable
{
    void Start();
}
