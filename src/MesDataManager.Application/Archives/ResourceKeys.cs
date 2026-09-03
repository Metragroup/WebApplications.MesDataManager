namespace MesDataManager.Application.Archives;

/// <summary>
/// Costruisce le chiavi di risorsa a partire dai nomi che il catalogo conosce.
/// <para>
/// Le chiavi in <c>Strings*.resx</c> sono raggruppate per tipo di testo tramite un prefisso
/// (<c>Archive.</c>, <c>Field.</c>, <c>Action.</c>, <c>Msg.</c>, <c>Error.</c>, <c>Nav.</c>,
/// <c>App.</c>). Dentro il proprio gruppo l'etichetta di un campo continua a chiamarsi come la
/// proprieta' C# e il nome di un'anagrafica come la sua chiave di catalogo: nessuno deve
/// dichiarare a mano cio' che si ricava per convenzione. Questa classe e' l'unico punto che
/// conosce i prefissi, cosi' cambiarli resta un'operazione sola.
/// </para>
/// </summary>
public static class ResourceKeys
{
    /// <summary>Nome mostrato di un'anagrafica, dalla sua chiave di catalogo.</summary>
    public static string Archive(string archiveKey) => $"Archive.{archiveKey}";

    /// <summary>Etichetta di una colonna, dal nome della proprieta' di dominio.</summary>
    public static string Field(string fieldName) => $"Field.{fieldName}";

    /// <summary>Messaggio d'errore, dal nome del motivo dell'errore.</summary>
    public static string Error(string errorName) => $"Error.{errorName}";

    /// <summary>Messaggio informativo o nota esplicativa mostrata all'operatore.</summary>
    public static string Message(string messageName) => $"Msg.{messageName}";
}
