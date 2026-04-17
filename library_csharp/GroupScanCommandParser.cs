using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CheatEngine.Library;

public sealed class GroupScanElement
{
    public bool Wildcard { get; set; }

    public int Offset { get; set; }

    public VariableType VariableType { get; set; }

    public string UserValue { get; set; } = string.Empty;

    public ulong ValueInt { get; set; }

    public double ValueFloat { get; set; }

    public CustomType? CustomType { get; set; }

    public int ByteSize { get; set; }

    public string Command { get; set; } = string.Empty;

    public bool Picked { get; set; }
}

public sealed class GroupScanCommandParser
{
    private static readonly HashSet<char> ValueTypePrefixes = new("1248FDCSW".ToCharArray());
    private int _calculatedBlockSize;

    public GroupScanCommandParser(string command = "")
    {
        Parse(command);
    }

    public NumberFormatInfo FloatSettings { get; private set; } = CultureInfo.InvariantCulture.NumberFormat;

    public List<GroupScanElement> Elements { get; } = new();

    public int BlockSize { get; private set; }

    public int BlockAlignment { get; private set; }

    public bool OutOfOrder { get; private set; }

    public bool TypeAligned { get; private set; }

    public void Parse(string command)
    {
        BlockAlignment = 4;
        BlockSize = -1;
        OutOfOrder = false;
        TypeAligned = false;
        _calculatedBlockSize = 0;
        Elements.Clear();
        FloatSettings = CultureInfo.InvariantCulture.NumberFormat;

        foreach (var token in Tokenize(command))
        {
            ParseToken(token);
        }

        if (BlockSize == -1)
        {
            BlockSize = _calculatedBlockSize;
        }

        if (OutOfOrder)
        {
            foreach (var element in Elements)
            {
                if (element.Wildcard)
                {
                    throw new InvalidOperationException("Wildcards/Empty are not allowed for Out of Order scans");
                }
            }
        }

        var hasPicked = false;
        foreach (var element in Elements)
        {
            if (element.Picked)
            {
                hasPicked = true;
                break;
            }
        }

        if (!hasPicked)
        {
            foreach (var element in Elements)
            {
                element.Picked = true;
            }
        }
    }

    private void ParseToken(string token)
    {
        var colonIndex = FindTopLevelColon(token);
        if (colonIndex < 0)
        {
            return;
        }

        var command = token[..colonIndex].Trim().ToUpperInvariant();
        var value = token[(colonIndex + 1)..];
        if (command == "BA")
        {
            BlockAlignment = int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            return;
        }

        if (command == "BS")
        {
            BlockSize = int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            return;
        }

        if (command == "OOO")
        {
            OutOfOrder = true;
            TypeAligned = string.Equals(value, "A", StringComparison.Ordinal);
            return;
        }

        if (command.Length == 0 || !ValueTypePrefixes.Contains(command[0]))
        {
            return;
        }

        var element = new GroupScanElement
        {
            Offset = _calculatedBlockSize,
            Command = command,
            Picked = false,
        };

        var nextCharIndex = 1;
        switch (command[0])
        {
            case '1':
                element.VariableType = VariableType.Byte;
                element.ByteSize = 1;
                break;
            case '2':
                element.VariableType = VariableType.Word;
                element.ByteSize = 2;
                break;
            case '4':
                element.VariableType = VariableType.Dword;
                element.ByteSize = 4;
                break;
            case '8':
                element.VariableType = VariableType.Qword;
                element.ByteSize = 8;
                break;
            case 'F':
                element.VariableType = VariableType.Single;
                element.ByteSize = 4;
                break;
            case 'D':
                element.VariableType = VariableType.Double;
                element.ByteSize = 8;
                break;
            case 'C':
            {
                element.VariableType = VariableType.Custom;
                var openIndex = command.IndexOf('(');
                var closeIndex = command.LastIndexOf(')');
                if (openIndex < 0 || closeIndex <= openIndex)
                {
                    throw new InvalidOperationException("Invalid custom type command");
                }

                var customTypeName = command[(openIndex + 1)..closeIndex];
                element.CustomType = CustomTypeHandler.GetCustomTypeFromName(customTypeName)
                    ?? throw new InvalidOperationException($"Custom type not recognized: {customTypeName}");
                element.ByteSize = element.CustomType.ByteSize;
                nextCharIndex = closeIndex + 1;
                break;
            }
            case 'S':
                if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
                {
                    value = value[1..^1];
                }

                if (command.Length >= 2 && command[1] == 'U')
                {
                    element.VariableType = VariableType.UnicodeString;
                    element.ByteSize = value.Length * 2;
                    nextCharIndex = 2;
                }
                else
                {
                    element.VariableType = VariableType.String;
                    element.ByteSize = value.Length;
                }

                break;
            case 'W':
                element.VariableType = VariableType.String;
                element.ByteSize = int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                value = string.Empty;
                break;
            default:
                throw new InvalidOperationException("Invalid groupscan command");
        }

        if (command.Length > nextCharIndex)
        {
            if (command[nextCharIndex] == 'P')
            {
                element.Picked = true;
            }
            else
            {
                throw new InvalidOperationException("Invalid groupscan command");
            }
        }

        element.UserValue = value;
        _calculatedBlockSize += element.ByteSize;
        element.Wildcard = string.IsNullOrEmpty(value)
            || (element.VariableType is not VariableType.String and not VariableType.UnicodeString && value == "*");

        if (!element.Wildcard)
        {
            switch (element.VariableType)
            {
                case VariableType.Byte:
                case VariableType.Word:
                case VariableType.Dword:
                case VariableType.Qword:
                case VariableType.Custom:
                    element.ValueInt = CeFuncProc.StrToQWordEx(value);
                    break;
                case VariableType.Single:
                case VariableType.Double:
                    element.ValueFloat = ParseFloatingPoint(value);
                    break;
            }
        }

        Elements.Add(element);
    }

    private static int FindTopLevelColon(string token)
    {
        var bracketDepth = 0;
        var inQuote = false;
        for (var index = 0; index < token.Length; index++)
        {
            switch (token[index])
            {
                case '\'':
                    inQuote = !inQuote;
                    break;
                case '(' when !inQuote:
                    bracketDepth++;
                    break;
                case ')' when !inQuote && bracketDepth > 0:
                    bracketDepth--;
                    break;
                case ':' when !inQuote && bracketDepth == 0:
                    return index;
            }
        }

        return -1;
    }

    private static IEnumerable<string> Tokenize(string command)
    {
        var builder = new StringBuilder();
        var inQuote = false;
        var bracketDepth = 0;

        foreach (var character in command)
        {
            if (character == '\'')
            {
                inQuote = !inQuote;
                builder.Append(character);
                continue;
            }

            if (!inQuote)
            {
                if (character == '(')
                {
                    bracketDepth++;
                    builder.Append(character);
                    continue;
                }

                if (character == ')')
                {
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    builder.Append(character);
                    continue;
                }

                if (char.IsWhiteSpace(character) && bracketDepth == 0)
                {
                    if (builder.Length > 0)
                    {
                        yield return builder.ToString().Trim();
                        builder.Clear();
                    }

                    continue;
                }
            }

            builder.Append(character);
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString().Trim();
        }
    }

    private double ParseFloatingPoint(string value)
    {
        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, FloatSettings, out var parsed))
        {
            return parsed;
        }

        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out parsed))
        {
            return parsed;
        }

        var normalized = value.Replace(',', '.');
        return double.Parse(normalized, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
    }
}