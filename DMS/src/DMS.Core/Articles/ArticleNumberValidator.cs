using System;
using System.Collections.Generic;
using System.Text;

namespace DMS.Core.Articles;

/// <summary>
/// Centrální validace SAP čísla artiklu.
/// Pro skleněné artikly aktuálně počítáme s desetimístným číselným kódem,
/// například 1000018165.
/// </summary>
public static class ArticleNumberValidator
{
    public static bool IsValid(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.Length == 10
               && value.All(char.IsDigit);
    }
}