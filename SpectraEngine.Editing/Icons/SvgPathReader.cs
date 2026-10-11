using System;
using System.Globalization;
using System.Numerics;

namespace SpectraEngine.Editing.Icons;

// Reads SVG path data a command and a number at a time.
internal ref struct SvgPathReader
{
    private readonly ReadOnlySpan<char> _data;
    private int _at;

    public SvgPathReader(ReadOnlySpan<char> data)
    {
        _data = data;
        _at = 0;
    }

    // The next command, or false at the end. Numbers where a command should
    // stand repeat the one before, and after a move they are lines.
    public bool TryReadCommand(ref char command)
    {
        SkipSeparators();
        if (_at >= _data.Length)
            return false;

        char next = _data[_at];
        if (char.IsAsciiLetter(next))
        {
            command = next;
            _at++;
            return true;
        }

        command = command switch
        {
            'M' => 'L',
            'm' => 'l',
            '\0' or 'Z' or 'z' => throw new FormatException($"A number at {_at} follows no command."),
            _ => command,
        };

        return true;
    }

    public Vector2 ReadPoint()
    {
        float x = ReadNumber();
        float y = ReadNumber();
        return new Vector2(x, y);
    }

    public float ReadNumber()
    {
        SkipSeparators();
        int start = _at;

        if (_at < _data.Length && _data[_at] is '-' or '+')
            _at++;

        int digits = SkipDigits();
        if (_at < _data.Length && _data[_at] == '.')
        {
            _at++;
            digits += SkipDigits();
        }

        if (digits == 0)
            throw new FormatException($"A number was expected at {start}.");

        SkipExponent();
        return float.Parse(_data[start.._at], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // An arc's two flags are one character each and may stand with no space
    // between them or before the number that follows.
    public bool ReadFlag()
    {
        SkipSeparators();
        if (_at >= _data.Length || _data[_at] is not ('0' or '1'))
            throw new FormatException($"An arc flag was expected at {_at}.");

        return _data[_at++] == '1';
    }

    private int SkipDigits()
    {
        int start = _at;
        while (_at < _data.Length && char.IsAsciiDigit(_data[_at]))
            _at++;

        return _at - start;
    }

    // Only when digits follow: "1e" is the number 1 and then a bad command.
    private void SkipExponent()
    {
        if (_at >= _data.Length || _data[_at] is not ('e' or 'E'))
            return;

        int mark = _at;
        _at++;
        if (_at < _data.Length && _data[_at] is '-' or '+')
            _at++;

        if (SkipDigits() == 0)
            _at = mark;
    }

    private void SkipSeparators()
    {
        while (_at < _data.Length && (char.IsWhiteSpace(_data[_at]) || _data[_at] == ','))
            _at++;
    }
}
