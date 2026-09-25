using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using MySqlManagementStudio.Services;
using MySqlManagementStudio.ViewModels;
using TextMateSharp.Grammars;

namespace MySqlManagementStudio.Controls;

public partial class SqlQueryEditor : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SqlQueryEditor, string?>(nameof(Text), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    // Shared across all editors — constructing RegistryOptions / loading grammars is expensive.
    private static readonly RegistryOptions SharedRegistryOptions = new(ThemeName.DarkPlus);

    private TextMate.Installation? _textMate;
    private CompletionWindow? _completionWindow;
    private SqlIntelliSenseService? _intelliSense;
    private bool _updatingFromBinding;
    private bool _schemaLoadQueued;
    private bool _textMateQueued;

    public SqlQueryEditor()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && !_updatingFromBinding && Editor?.Document is not null)
        {
            var newText = change.GetNewValue<string?>() ?? string.Empty;
            if (Editor.Document.Text != newText)
            {
                _updatingFromBinding = true;
                Editor.Document.Text = newText;
                _updatingFromBinding = false;
            }
        }
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Editor.Document ??= new TextDocument();
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 2;

        _updatingFromBinding = true;
        Editor.Document.Text = Text ?? string.Empty;
        _updatingFromBinding = false;

        Editor.TextChanged += OnEditorTextChanged;
        Editor.TextArea.TextEntered += OnTextEntered;
        Editor.TextArea.TextEntering += OnTextEntering;
        Editor.TextArea.KeyDown += OnEditorKeyDown;
        ApplyContextMenuGestures();

        // Defer TextMate until after the first paint so tab open / connect clicks stay snappy.
        if (!_textMateQueued && _textMate is null)
        {
            _textMateQueued = true;
            Dispatcher.UIThread.Post(InstallTextMate, DispatcherPriority.Background);
        }
    }

    private void InstallTextMate()
    {
        if (_textMate is not null || Editor is null)
            return;

        _textMate = Editor.InstallTextMate(SharedRegistryOptions);
        var language = SharedRegistryOptions.GetLanguageByExtension(".sql");
        if (language is not null)
            _textMate.SetGrammar(SharedRegistryOptions.GetScopeByLanguageId(language.Id));
    }

    private void ApplyContextMenuGestures()
    {
        if (Editor.ContextMenu is not ContextMenu menu)
            return;

        var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.InputGesture = (item.Header?.ToString(), isMac) switch
            {
                ("Cut", true) => new KeyGesture(Key.X, KeyModifiers.Meta),
                ("Cut", false) => new KeyGesture(Key.X, KeyModifiers.Control),
                ("Copy", true) => new KeyGesture(Key.C, KeyModifiers.Meta),
                ("Copy", false) => new KeyGesture(Key.C, KeyModifiers.Control),
                ("Paste", true) => new KeyGesture(Key.V, KeyModifiers.Meta),
                ("Paste", false) => new KeyGesture(Key.V, KeyModifiers.Control),
                ("Select All", true) => new KeyGesture(Key.A, KeyModifiers.Meta),
                ("Select All", false) => new KeyGesture(Key.A, KeyModifiers.Control),
                _ => item.InputGesture
            };
        }
    }

    private void OnCutClick(object? sender, RoutedEventArgs e)
    {
        Editor.Cut();
        SyncTextFromEditor();
    }

    private void OnCopyClick(object? sender, RoutedEventArgs e) => Editor.Copy();

    private async void OnPasteClick(object? sender, RoutedEventArgs e)
    {
        Editor.Paste();
        // Paste may be async on some platforms; sync bound text shortly after.
        await Task.Delay(10);
        SyncTextFromEditor();
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e) => Editor.SelectAll();

    private void SyncTextFromEditor()
    {
        var docText = Editor.Document.Text;
        if (Text == docText)
            return;

        _updatingFromBinding = true;
        Text = docText;
        _updatingFromBinding = false;
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Editor.TextChanged -= OnEditorTextChanged;
        Editor.TextArea.TextEntered -= OnTextEntered;
        Editor.TextArea.TextEntering -= OnTextEntering;
        Editor.TextArea.KeyDown -= OnEditorKeyDown;
        _completionWindow?.Close();
        _completionWindow = null;
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_updatingFromBinding)
            return;

        SyncTextFromEditor();
    }

    private async void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
            return;

        var ch = e.Text[0];
        if (ch == '.' || char.IsLetter(ch) || ch == '`' || ch == '_')
            await ShowCompletionAsync(autoTriggered: true);
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (e.Text?.Length > 0 && _completionWindow is not null && !char.IsLetterOrDigit(e.Text[0]) && e.Text[0] is not '_' and not '`')
            _completionWindow.CompletionList.RequestInsertion(e);
    }

    private async void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            await ShowCompletionAsync(autoTriggered: false);
        }
    }

    private async Task ShowCompletionAsync(bool autoTriggered)
    {
        await EnsureSchemaAsync();

        var (start, prefix) = GetCompletionPrefix();
        if (autoTriggered && prefix.Length == 0)
            return;

        var service = GetIntelliSense();
        var suggestions = service.GetSuggestions(prefix);
        if (suggestions.Count == 0)
            return;

        _completionWindow?.Close();
        _completionWindow = new CompletionWindow(Editor.TextArea)
        {
            Width = 420,
            MaxHeight = 280
        };
        _completionWindow.Closed += (_, _) => _completionWindow = null;

        // Replace only the current word/prefix.
        if (prefix.Length > 0)
            _completionWindow.StartOffset = start;

        foreach (var item in suggestions)
            _completionWindow.CompletionList.CompletionData.Add(item);

        _completionWindow.Show();
    }

    private (int Start, string Prefix) GetCompletionPrefix()
    {
        var offset = Editor.CaretOffset;
        var doc = Editor.Document;
        if (offset <= 0)
            return (0, string.Empty);

        var start = offset;
        while (start > 0)
        {
            var c = doc.GetCharAt(start - 1);
            if (char.IsLetterOrDigit(c) || c is '_' or '`' or '.')
                start--;
            else
                break;
        }

        var prefix = doc.GetText(start, offset - start);
        // For db.table style, complete on the segment after the last dot.
        var dot = prefix.LastIndexOf('.');
        if (dot >= 0)
        {
            start += dot + 1;
            prefix = prefix[(dot + 1)..];
        }

        return (start, prefix);
    }

    private SqlIntelliSenseService GetIntelliSense()
    {
        if (DataContext is QueryTabViewModel tab)
            return tab.IntelliSense;

        return _intelliSense ??= new SqlIntelliSenseService(new MySqlDatabaseService());
    }

    private async Task EnsureSchemaAsync()
    {
        // Lazy: only load schema metadata when IntelliSense is actually needed.
        if (_schemaLoadQueued)
            return;

        if (DataContext is not QueryTabViewModel tab)
            return;

        _schemaLoadQueued = true;
        try
        {
            await tab.IntelliSense.EnsureSchemaAsync(tab.Profile, tab.Database);
        }
        catch
        {
            // Ignore metadata failures for IntelliSense.
        }
        finally
        {
            _schemaLoadQueued = false;
        }
    }
}
