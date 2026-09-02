using MesDataManager.Application.Archives;

using Microsoft.Extensions.Localization;

namespace MesDataManager.Web;

/// <summary>
/// Traduce un <see cref="ArchiveException"/> nel messaggio da mostrare. La chiave di risorsa
/// viaggia con l'eccezione, quindi la traduzione avviene qui e non nel servizio: e' l'unico
/// punto che conosce la lingua dell'utente.
/// </summary>
public static class ArchiveExceptionLocalization
{
    public static string Localize(this ArchiveException exception, IStringLocalizer<Strings> localizer)
    {
        string message = exception.MessageArguments.Length == 0
            ? localizer[exception.MessageKey]
            : localizer[exception.MessageKey, exception.MessageArguments];

        // Quando l'errore riguarda un campo preciso, l'etichetta tradotta davanti al messaggio
        // risparmia all'operatore la caccia al campo sbagliato.
        return exception.Field is null
            ? message
            : $"{localizer[exception.Field]}: {message}";
    }
}
