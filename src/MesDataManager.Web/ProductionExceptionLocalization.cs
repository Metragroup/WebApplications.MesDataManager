using MesDataManager.Application.Archives;
using MesDataManager.Application.Production;

using Microsoft.Extensions.Localization;

namespace MesDataManager.Web;

/// <summary>
/// Traduce un <see cref="ProductionException"/> nel messaggio da mostrare, come
/// <see cref="ArchiveExceptionLocalization"/> fa per le anagrafiche: la chiave di risorsa viaggia
/// con l'eccezione, la lingua la conosce solo la UI.
/// </summary>
public static class ProductionExceptionLocalization
{
    public static string Localize(this ProductionException exception, IStringLocalizer<Strings> localizer)
    {
        string message = exception.MessageArguments.Length == 0
            ? localizer[exception.MessageKey]
            : localizer[exception.MessageKey, exception.MessageArguments];

        return exception.Field is null
            ? message
            : $"{localizer[ResourceKeys.Field(exception.Field)]}: {message}";
    }
}
