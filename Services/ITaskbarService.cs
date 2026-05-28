using System.Windows;

namespace OopsType.Services;

public interface ITaskbarService
{
    bool TryGetPrimaryTaskbarRect(out Rect rect);
}
