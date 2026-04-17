using System;
using System.Globalization;

namespace CheatEngine.Library;

public sealed class AddressParser
{
    private string _input = string.Empty;
    private int _position;
    private CpuContext? _specialContext;

    public void SetSpecialContext(CpuContext? context)
    {
        _specialContext = context;
    }

    public nuint GetAddress(string text, bool skipSymbolHandler = false)
    {
        if (!skipSymbolHandler)
        {
            var resolved = SymbolHandler.Default.GetAddressFromName(text, false, out var hasError, _specialContext);
            if (resolved != 0 && !hasError)
            {
                return resolved;
            }
        }

        _input = string.IsNullOrWhiteSpace(text) ? "0" : text.ToUpperInvariant();
        _position = 0;
        var result = ParseExpression();
        SkipWhitespace();
        if (_position != _input.Length)
        {
            throw new FormatException("This is not a valid address");
        }

        return unchecked((nuint)result);
    }

    public nuint GetBaseAddress(string text)
    {
        var max = 0UL;
        foreach (var token in text.Split(['+', '-', '*', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseValueToken(token.ToUpperInvariant(), out var value))
            {
                max = Math.Max(max, value);
            }
        }

        return (nuint)max;
    }

    private ulong ParseExpression()
    {
        var value = ParseTerm();
        while (true)
        {
            SkipWhitespace();
            if (!TryRead('+') && !TryRead('-'))
            {
                return value;
            }

            var op = _input[_position - 1];
            var right = ParseTerm();
            value = op == '+' ? value + right : unchecked(value - right);
        }
    }

    private ulong ParseTerm()
    {
        var value = ParseFactor();
        while (true)
        {
            SkipWhitespace();
            if (!TryRead('*'))
            {
                return value;
            }

            value *= ParseFactor();
        }
    }

    private ulong ParseFactor()
    {
        SkipWhitespace();
        var start = _position;
        while (_position < _input.Length && IsValueCharacter(_input[_position]))
        {
            _position++;
        }

        if (_position == start)
        {
            throw new FormatException("This is not a valid address");
        }

        var token = _input[start.._position];
        if (!TryParseValueToken(token, out var value))
        {
            throw new FormatException("This is not a valid address");
        }

        return value;
    }

    private static bool IsValueCharacter(char value)
    {
        return char.IsAsciiLetterOrDigit(value) || value == '$';
    }

    private bool TryParseValueToken(string token, out ulong value)
    {
        if (TryReadRegister(token, out value))
        {
            return true;
        }

        if (token.StartsWith('$'))
        {
            return ulong.TryParse(token[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return ulong.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private bool TryReadRegister(string token, out ulong value)
    {
        value = 0;
        var context = _specialContext;
        if (context is null)
        {
            return false;
        }

        return token switch
        {
            "EAX" or "RAX" => (value = context.Rax) >= 0,
            "EBX" or "RBX" => (value = context.Rbx) >= 0,
            "ECX" or "RCX" => (value = context.Rcx) >= 0,
            "EDX" or "RDX" => (value = context.Rdx) >= 0,
            "ESI" or "RSI" => (value = context.Rsi) >= 0,
            "EDI" or "RDI" => (value = context.Rdi) >= 0,
            "EBP" or "RBP" => (value = context.Rbp) >= 0,
            "ESP" or "RSP" => (value = context.Rsp) >= 0,
            "EIP" or "RIP" => (value = context.Rip) >= 0,
            "R8" => (value = context.R8) >= 0,
            "R9" => (value = context.R9) >= 0,
            "R10" => (value = context.R10) >= 0,
            "R11" => (value = context.R11) >= 0,
            "R12" => (value = context.R12) >= 0,
            "R13" => (value = context.R13) >= 0,
            "R14" => (value = context.R14) >= 0,
            "R15" => (value = context.R15) >= 0,
            _ => false,
        };
    }

    private void SkipWhitespace()
    {
        while (_position < _input.Length && char.IsWhiteSpace(_input[_position]))
        {
            _position++;
        }
    }

    private bool TryRead(char value)
    {
        SkipWhitespace();
        if (_position < _input.Length && _input[_position] == value)
        {
            _position++;
            return true;
        }

        return false;
    }
}

public static class AddressParserGlobal
{
    public static AddressParser MainThreadAddressParser { get; } = new();

    public static nuint GetAddress(string text)
    {
        return MainThreadAddressParser.GetAddress(text);
    }
}