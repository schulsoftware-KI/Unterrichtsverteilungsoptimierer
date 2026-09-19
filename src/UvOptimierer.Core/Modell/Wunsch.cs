namespace UvOptimierer.Core.Modell;

/// <summary>Lehrerwunsch bzw. Anti-Wunsch (Blatt „Lehrerwünsche"). Entspricht <c>tWunsch</c>.</summary>
public sealed class Wunsch
{
    public string LehrerName { get; init; } = "";
    public string WunschKlasse { get; init; } = "";
    /// <summary>Optionales Fach zum Wunsch. Leer = gilt für alle Fächer der Wunschklasse.</summary>
    public string WunschFach { get; init; } = "";
    public int WunschPrio { get; init; }
    public string AntiKlasse { get; init; } = "";
    /// <summary>Optionales Fach zum Anti-Wunsch. Leer = gilt für alle Fächer der Anti-Klasse.</summary>
    public string AntiFach { get; init; } = "";
    public int AntiPrio { get; init; }
}
