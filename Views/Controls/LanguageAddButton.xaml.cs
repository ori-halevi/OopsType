using System;
using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using OopsType.Models;

namespace OopsType.Views.Controls;

/// <summary>
/// "Add language" button that opens a searchable popup of languages. System-installed layouts are
/// grouped and badged at the top (they're what the user most likely wants), the rest follow. Picking
/// an item runs <see cref="SelectCommand"/> with the language code and closes the popup — far nicer
/// than the old "add a blank row and hand-type a code" flow.
/// </summary>
public partial class LanguageAddButton : UserControl
{
    private ICollectionView? _view;

    public LanguageAddButton() => InitializeComponent();

    /// <summary>The full list of <see cref="LanguageOption"/> to choose from.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(LanguageAddButton),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Invoked with the chosen language code (string) when the user picks an item.</summary>
    public static readonly DependencyProperty SelectCommandProperty = DependencyProperty.Register(
        nameof(SelectCommand), typeof(ICommand), typeof(LanguageAddButton),
        new PropertyMetadata(null));

    public ICommand? SelectCommand
    {
        get => (ICommand?)GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (LanguageAddButton)d;
        if (e.NewValue == null)
        {
            self.List.ItemsSource = null;
            self._view = null;
            return;
        }

        // Group by the (already-localized) Group header. The VM sorts installed-first, so the
        // "detected" group lands at the top naturally.
        var cvs = new CollectionViewSource { Source = e.NewValue };
        cvs.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LanguageOption.Group)));
        var view = cvs.View;
        view.Filter = self.MatchesSearch;
        self._view = view;
        self.List.ItemsSource = view;
    }

    private bool MatchesSearch(object item)
    {
        var q = SearchBox?.Text?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        if (item is not LanguageOption o) return false;
        return o.Code.Contains(q, StringComparison.OrdinalIgnoreCase)
            || o.EnglishName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || o.NativeName.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        Popup.IsOpen = true;
        Dispatcher.BeginInvoke(new Action(() => SearchBox.Focus()),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
        if (_view != null)
            EmptyHint.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnChoose(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string code)
        {
            if (SelectCommand?.CanExecute(code) == true)
                SelectCommand.Execute(code);
        }
        Popup.IsOpen = false;
    }
}
