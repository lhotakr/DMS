using System;
using System.Collections.Generic;
using System.Text;

using DMS.Core.Articles;

namespace DMS.Core.Transactions;

/// <summary>
/// Centrální místo, které rozhoduje, co se má stát po zadání transakce.
/// V budoucnu zde bude kontrola oprávnění a směrování na konkrétní obrazovky.
/// </summary>
public sealed class TransactionDispatcher
{
    public TransactionResult Dispatch(TransactionCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Code))
        {
            return TransactionResult.Fail("", "Nebyla zadána žádná transakce.");
        }

        return command.Code switch
        {
            "ART03" => OpenArticleCard(command),
            "DOC03" => OpenArticleDocuments(command),
            "SCR03" => OpenArticleScreens(command),
            "SCR10" => TransactionResult.Ok("SCR10", null, "Otevřena fronta přípravy sít."),
            "ORD10" => TransactionResult.Ok("ORD10", null, "Otevřen přehled zakázek."),
            _ => TransactionResult.Fail(command.Code, $"Neznámá transakce: {command.Code}")
        };
    }

    private static TransactionResult OpenArticleCard(TransactionCommand command)
    {
        if (!ArticleNumberValidator.IsValid(command.Parameter))
        {
            return TransactionResult.Fail("ART03", "Transakce ART03 očekává desetimístné SAP číslo artiklu.");
        }

        return TransactionResult.Ok(
            "ART03",
            command.Parameter,
            $"Otevřena karta artiklu {command.Parameter}.");
    }

    private static TransactionResult OpenArticleDocuments(TransactionCommand command)
    {
        if (!ArticleNumberValidator.IsValid(command.Parameter))
        {
            return TransactionResult.Fail("DOC03", "Transakce DOC03 očekává desetimístné SAP číslo artiklu.");
        }

        return TransactionResult.Ok(
            "DOC03",
            command.Parameter,
            $"Otevřena dokumentace artiklu {command.Parameter}.");
    }

    private static TransactionResult OpenArticleScreens(TransactionCommand command)
    {
        if (!ArticleNumberValidator.IsValid(command.Parameter))
        {
            return TransactionResult.Fail("SCR03", "Transakce SCR03 očekává desetimístné SAP číslo artiklu.");
        }

        return TransactionResult.Ok(
            "SCR03",
            command.Parameter,
            $"Otevřena síta artiklu {command.Parameter}.");
    }
}