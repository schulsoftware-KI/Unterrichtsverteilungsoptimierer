namespace UvOptimierer.Core.Modell;

/// <summary>
/// Ein zu besetzender Unterrichtsposten (Zeile im Blatt „Klassen"). Entspricht <c>tEintrag</c>.
/// </summary>
public sealed class Eintrag
{
    /// <summary>Klasse (Spalte A; leere Zellen übernehmen die zuletzt gesetzte Klasse).</summary>
    public string Klasse { get; init; } = "";

    /// <summary>Fach (Spalte B).</summary>
    public string Fach { get; init; } = "";

    /// <summary>Wochenstunden (Spalte C).</summary>
    public double Wst { get; init; }

    /// <summary>Gewichteter Wert aus KlassenUV (Spalte I). Steuert die Kapazitätsrechnung.</summary>
    public double WertUv { get; init; }

    /// <summary>
    /// Fixierter Lehrer (Spalte F). „?" bedeutet nicht fixiert.
    /// <see cref="Regeln.IstFixiert"/> entscheidet, ob ein Eintrag als fixiert gilt.
    /// </summary>
    public string UrsprungsLehrer { get; init; } = "?";

    /// <summary>Aktuell zugewiesener Lehrer (veränderlicher Suchzustand, im Backtracking genutzt).</summary>
    public string Lehrer { get; set; } = "?";

    /// <summary>1-basierte Excel-Zeilennummer (für das Zurückschreiben der Lösung).</summary>
    public int Zeile { get; init; }

    /// <summary>Gehört der Eintrag zur Oberstufe (Klassenname enthält ef/q1/q2).</summary>
    public bool IstOberstufe { get; init; }
}
