using System.Text;

namespace SpectraEngine.Entities.Generator;

// Line endings are always \n: snapshot tests compare bytes, and analyzers may
// not touch Environment.
internal sealed class SourceWriter
{
    private readonly StringBuilder _text = new();
    private int _indent;

    public SourceWriter Open(string header)
    {
        Line(header);
        Line("{");
        _indent++;
        return this;
    }

    public SourceWriter Close(string trailer = "}")
    {
        _indent--;
        Line(trailer);
        return this;
    }

    public SourceWriter Indent()
    {
        _indent++;
        return this;
    }

    public SourceWriter Outdent()
    {
        _indent--;
        return this;
    }

    public SourceWriter Line(string text = "")
    {
        if (text.Length > 0)
            _text.Append(' ', _indent * 4).Append(text);

        _text.Append('\n');
        return this;
    }

    public override string ToString() => _text.ToString();
}
