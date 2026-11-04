using System;
using System.Collections.Generic;
using System.Globalization;

namespace DMS.Desktop.Views.Mes;

/// <summary>
/// Safe arithmetic evaluator for MES06 calculated columns.
/// Supports + - * /, unary signs, parentheses, numeric literals and named variables.
/// No reflection, scripting, or arbitrary method calls.
/// </summary>
internal static class Mes06FormulaEngine
{
    public static bool TryEvaluate(
        string expression,
        IReadOnlyDictionary<string, double> variables,
        out double result,
        out string error)
    {
        result = 0d;
        error = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new FormatException("Formula is empty.");
            var parser = new Parser(expression, variables);
            result = parser.Parse();
            if (double.IsNaN(result) || double.IsInfinity(result))
                throw new ArithmeticException("Formula produced a non-finite value.");
            return true;
        }
        catch (Exception ex) when (ex is FormatException || ex is ArithmeticException || ex is KeyNotFoundException)
        {
            error = ex.Message;
            return false;
        }
    }

    private sealed class Parser
    {
        private readonly string _source;
        private readonly IReadOnlyDictionary<string, double> _variables;
        private int _position;

        public Parser(string source, IReadOnlyDictionary<string, double> variables)
        {
            _source = source;
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
        }

        public double Parse()
        {
            var value = ParseExpression();
            SkipWhitespace();
            if (_position != _source.Length)
                throw new FormatException($"Unexpected character at position {_position + 1}.");
            return value;
        }

        private double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (Take('+')) value += ParseTerm();
                else if (Take('-')) value -= ParseTerm();
                else return value;
            }
        }

        private double ParseTerm()
        {
            var value = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (Take('*')) value *= ParseFactor();
                else if (Take('/'))
                {
                    var denominator = ParseFactor();
                    if (denominator == 0d)
                        throw new ArithmeticException("Division by zero.");
                    value /= denominator;
                }
                else return value;
            }
        }

        private double ParseFactor()
        {
            SkipWhitespace();
            if (Take('+')) return ParseFactor();
            if (Take('-')) return -ParseFactor();
            if (Take('('))
            {
                var value = ParseExpression();
                SkipWhitespace();
                if (!Take(')')) throw new FormatException("Missing closing parenthesis.");
                return value;
            }
            if (_position >= _source.Length)
                throw new FormatException("Unexpected end of formula.");

            var start = _position;
            if (char.IsLetter(_source[_position]) || _source[_position] == '_')
            {
                _position++;
                while (_position < _source.Length &&
                       (char.IsLetterOrDigit(_source[_position]) || _source[_position] == '_'))
                    _position++;
                var identifier = _source.Substring(start, _position - start);
                if (!_variables.TryGetValue(identifier, out var variable))
                    throw new KeyNotFoundException($"Unknown variable: {identifier}.");
                return variable;
            }
            while (_position < _source.Length &&
                   (char.IsDigit(_source[_position]) || _source[_position] == '.'))
                _position++;
            if (start == _position || !double.TryParse(
                    _source.Substring(start, _position - start),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var number))
                throw new FormatException($"Invalid number at position {start + 1}.");
            return number;
        }

        private void SkipWhitespace()
        {
            while (_position < _source.Length && char.IsWhiteSpace(_source[_position]))
                _position++;
        }

        private bool Take(char character)
        {
            if (_position >= _source.Length || _source[_position] != character)
                return false;
            _position++;
            return true;
        }
    }
}
