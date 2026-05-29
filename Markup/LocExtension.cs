using System;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;

namespace OopsType.Markup;

/// <summary>
/// XAML markup extension that resolves to a translated string via the active language
/// <see cref="ResourceDictionary"/> merged in by <c>LocalizationService</c>.
///
/// <para>Usage: <c>Text="{loc:T Caret_Title}"</c> — equivalent to
/// <c>Text="{DynamicResource Caret_Title}"</c> but shorter and conveys intent.</para>
///
/// <para>The class is named <c>T</c> (with the conventional <c>Extension</c> suffix WPF
/// strips when resolving markup tags) so the XAML invocation reads as plain English: "T" for
/// "translate".</para>
///
/// <para>Because this delegates to <see cref="DynamicResourceExtension"/>, switching the active
/// language at runtime (which replaces the merged dictionary) causes every binding to re-resolve
/// and refresh automatically — no need to rebind or reload windows.</para>
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    /// <summary>Resource key looked up in <see cref="Application.Resources"/>. Same key across
    /// every language pack — only the value differs.</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public TExtension() { }
    public TExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var dyn = new DynamicResourceExtension(Key);

        // Setter.Value and friends do not accept the resolved value of a DynamicResourceExtension
        // (a ResourceReferenceExpression) — they special-case the markup-extension object itself
        // and set up the deferred lookup on their own. So when the target is *not* a normal
        // DependencyObject/DependencyProperty pair (the common case), return the extension
        // unwrapped and let the host handle it.
        //
        // Targets we've actually seen here:
        //   - DependencyObject + DependencyProperty → normal binding; resolve via ProvideValue.
        //   - Setter + PropertyInfo (Setter.Value)  → return the extension itself.
        //   - Another MarkupExtension              → we're being composed; return the extension.
        var target = (serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget)?.TargetObject;
        var property = (serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget)?.TargetProperty;

        if (target is DependencyObject && property is DependencyProperty)
            return dyn.ProvideValue(serviceProvider);

        return dyn;
    }
}
