using System.Text.RegularExpressions;
using Android.Text;
using Android.Text.Style;
using Android.Widget;
using Java.Lang;
using Microsoft.Maui.ApplicationModel;

namespace Besm6.Android.Ui;

/// <summary>
/// Native MAUI Editor with Android span-based syntax highlighting. The text and
/// selection remain native; spans are reapplied after edits and theme changes.
/// </summary>
public sealed class SyntaxEditor : Editor
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "program", "subroutine", "function", "end", "stop", "call", "print", "read",
        "write", "format", "dimension", "common", "data", "do", "if", "then", "else",
        "endif", "enddo", "goto", "go", "to", "return", "integer", "real", "double",
        "precision", "logical", "character", "complex", "implicit", "external",
        "parameter", "continue", "equivalence", "save", "begin", "procedure", "array",
        "for", "step", "until", "while", "switch", "own", "value", "boolean", "true",
        "false", "label", "string", "comment", "atx", "xta", "aex", "aax", "aox",
        "arx", "avx", "amx", "asx", "asn", "anx", "ntr", "rta", "yta", "ati", "ita",
        "its", "sti", "mtj", "vtm", "utm", "uza", "u1a", "vjm", "vlm", "vim", "vzm",
        "utc", "wtc", "mod", "acx", "apx", "aux", "xts", "var", "const", "type",
        "record", "repeat", "until", "writeln", "readln", "and", "or", "not", "div",
        "nil", "uses", "unit", "interface", "implementation",
    };

    private static readonly Regex ControlLine = new(
        @"^\s*\*(?:name|fortran|forex|algol|pascal|assem|execute|end|tape|trans|b\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LanguageLine = new(
        @"^\s*\*(fortran|forex|algol|pascal|assem|b)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Token = new(
        @"'(?:''|[^'])*'|""(?:""""|[^""])*""|//[^\n]*|![^\n]*|`[0-7]+|\b\d+(?:\.\d*)?(?:[edED][+-]?\d+)?\b|[\p{L}_][\p{L}\p{N}_]*|\*\d+|[^\p{L}\p{N}_]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly List<ForegroundColorSpan> _spans = new();
    private Watcher? _watcher;
    private EditText? _edit;
    private bool _applying;

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler?.PlatformView is EditText edit)
        {
            _edit = edit;
            _watcher = new Watcher(this);
            edit.AddTextChangedListener(_watcher);
            if (Application.Current is { } app) app.RequestedThemeChanged += OnThemeChanged;
            ApplyHighlight();
        }
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (_edit is not null && _watcher is not null)
        {
            _edit.RemoveTextChangedListener(_watcher);
            _edit = null;
            _watcher = null;
        }
        if (Application.Current is { } app) app.RequestedThemeChanged -= OnThemeChanged;
        base.OnHandlerChanging(args);
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) =>
        Dispatcher.Dispatch(ApplyHighlight);

    internal void ApplyHighlight()
    {
        if (_applying || _edit?.EditableText is not { } editable) return;
        _applying = true;
        try
        {
            foreach (var span in _spans) editable.RemoveSpan(span);
            _spans.Clear();

            string text = editable.ToString();
            bool dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            string language = string.Empty;
            int lineStart = 0;

            while (lineStart <= text.Length)
            {
                int lineEnd = text.IndexOf('\n', lineStart);
                if (lineEnd < 0) lineEnd = text.Length;
                string line = text.Substring(lineStart, lineEnd - lineStart);

                if (ControlLine.IsMatch(line))
                {
                    AddSpan(editable, lineStart, lineEnd, dark ? "#DF5B49" : "#8E2C20");
                    var match = LanguageLine.Match(line);
                    if (match.Success) language = match.Groups[1].Value.ToLowerInvariant();
                }
                else if (IsComment(line, language))
                {
                    AddSpan(editable, lineStart, lineEnd, dark ? "#B7A99B" : "#8C7C70");
                }
                else
                {
                    foreach (Match token in Token.Matches(line))
                    {
                        string value = token.Value;
                        string? color = null;
                        if (value[0] is '\'' or '"') color = dark ? "#82C79A" : "#2E6A43";
                        else if (value.StartsWith("//", StringComparison.Ordinal) || value[0] == '!')
                            color = dark ? "#B7A99B" : "#8C7C70";
                        else if (char.IsDigit(value[0]) || value[0] == '`')
                            color = dark ? "#7FB0FF" : "#2563EB";
                        else if (Keywords.Contains(value) || value.Length > 1 && value[0] == '*')
                            color = dark ? "#E8A15C" : "#B35C1E";
                        if (color is not null)
                            AddSpan(editable, lineStart + token.Index,
                                lineStart + token.Index + token.Length, color);
                    }
                }

                lineStart = lineEnd + 1;
            }
        }
        finally
        {
            _applying = false;
        }
    }

    private void AddSpan(IEditable editable, int start, int end, string color)
    {
        if (end <= start) return;
        var span = new ForegroundColorSpan(global::Android.Graphics.Color.ParseColor(color));
        editable.SetSpan(span, start, end, SpanTypes.ExclusiveExclusive);
        _spans.Add(span);
    }

    private static bool IsComment(string line, string language)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal)) return true;
        if (language is "fortran" or "forex" or "")
            return trimmed.Length > 0 && (trimmed[0] is 'c' or 'C' or '*')
                && (trimmed.Length == 1 || char.IsWhiteSpace(trimmed[1]));
        return false;
    }

    private sealed class Watcher(SyntaxEditor owner) : Java.Lang.Object, ITextWatcher
    {
        public void AfterTextChanged(IEditable? s) => owner.ApplyHighlight();
        public void BeforeTextChanged(ICharSequence? s, int start, int count, int after) { }
        public void OnTextChanged(ICharSequence? s, int start, int before, int count) { }
    }
}
