using System.Windows;

namespace OopsType.Models;

public readonly record struct CaretInfo(bool Found, Rect ScreenRect, CaretSource Source);

public enum CaretSource { GuiThreadInfo, UiAutomation, MouseFallback, None }
