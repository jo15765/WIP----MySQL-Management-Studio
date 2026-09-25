using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace MySqlManagementStudio.Services;

public sealed class SqlCompletionData : ICompletionData
{
    public SqlCompletionData(string text, string description, string kind, double priority = 0)
    {
        Text = text;
        Description = description;
        Kind = kind;
        Priority = priority;
    }

    public string Text { get; }
    public string Kind { get; }
    public object Content => $"{Kind}  {Text}";
    public object Description { get; }
    public double Priority { get; }
    public IImage? Image => null;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }
}
