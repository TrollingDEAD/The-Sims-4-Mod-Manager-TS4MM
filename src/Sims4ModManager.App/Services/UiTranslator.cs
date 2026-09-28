using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Sims4ModManager.Core.Localization;

namespace Sims4ModManager.App.Services;

/// <summary>
/// Translates the fixed texts written in XAML (captions, button labels, tooltips, column headers) when
/// an element is loaded - also inside templates. Texts that come from bindings are translated by the
/// view models (<see cref="L"/>), so they are left alone here. Only active for non-German languages.
/// </summary>
public static class UiTranslator
{
    private static readonly DependencyProperty[] TextProperties =
    {
        FrameworkElement.ToolTipProperty,
        TextBlock.TextProperty,
        ContentControl.ContentProperty,
        HeaderedContentControl.HeaderProperty,
        HeaderedItemsControl.HeaderProperty,
        Window.TitleProperty,
        Wpf.Ui.Controls.TextBox.PlaceholderTextProperty,
        Wpf.Ui.Controls.TitleBar.TitleProperty,
    };

    public static void Enable()
    {
        if (L.IsGerman)
            return;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Translate((FrameworkElement)sender)), handledEventsToo: true);
        // Loaded only reaches elements that subscribe to it themselves; the first layout pass reaches every visible one.
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.SizeChangedEvent,
            new SizeChangedEventHandler((sender, e) =>
            {
                if (e.PreviousSize.Width == 0 && e.PreviousSize.Height == 0)
                    Translate((FrameworkElement)sender);
            }), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ToolTipOpeningEvent,
            new ToolTipEventHandler((sender, _) => TranslateProperty((DependencyObject)sender, FrameworkElement.ToolTipProperty)), handledEventsToo: true);
    }

    public static void Translate(FrameworkElement element)
    {
        try
        {
            TranslateCore(element);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            App.Log(ex); // a missed translation must never take the UI down
        }
    }

    private static void TranslateCore(FrameworkElement element)
    {
        foreach (var property in TextProperties)
            TranslateProperty(element, property);

        switch (element)
        {
            case TextBlock text when text.Inlines.Count > 0:
                TranslateInlines(text.Inlines);
                break;
            case DataGrid grid:
                foreach (var column in grid.Columns)
                    if (column.Header is string header && L.TryTranslate(header, out var translated))
                        column.Header = translated;
                break;
            case ListView { View: GridView view }:
                foreach (var column in view.Columns)
                    if (column.Header is string header && L.TryTranslate(header, out var translated))
                        column.Header = translated;
                break;
        }
    }

    private static void TranslateInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines.ToList())
        {
            if (inline is Run run && !IsBound(run, Run.TextProperty) && L.TryTranslate(run.Text, out var text))
                run.Text = text;
            else if (inline is Span span)
                TranslateInlines(span.Inlines);
        }
    }

    private static void TranslateProperty(DependencyObject element, DependencyProperty property)
    {
        if (!property.OwnerType.IsInstanceOfType(element) && !IsAttachedTo(element, property))
            return;
        if (element.GetValue(property) is not string value || value.Length == 0 || IsBound(element, property))
            return;
        if (L.TryTranslate(value, out var translated))
            element.SetCurrentValue(property, translated);
    }

    private static bool IsAttachedTo(DependencyObject element, DependencyProperty property) =>
        property == FrameworkElement.ToolTipProperty && element is FrameworkElement;

    private static bool IsBound(DependencyObject element, DependencyProperty property) =>
        DependencyPropertyHelper.GetValueSource(element, property).IsExpression;
}
