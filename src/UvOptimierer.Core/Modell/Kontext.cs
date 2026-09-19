using System.Diagnostics.CodeAnalysis;

namespace UvOptimierer.Core.Modell;

/// <summary>
/// Bündelt Eingabedaten und den veränderlichen Laufzeitzustand einer Optimierung.
/// Ersetzt die globalen VBA-Variablen (<c>g_lehrer</c>, <c>g_wuensche</c>, <c>g_toleranz</c>,
/// <c>g_sperrIdx</c>, …) durch ein sauber übergebenes Objekt.
/// <para>
/// Indizes sind 0-basiert (im Gegensatz zu den 1-basierten VBA-Arrays); die Reihenfolge der
/// Einträge entspricht der Lesereihenfolge aus dem Blatt „Klassen" und ist für die
/// Kontinuitäts-Bewertung relevant.
/// </para>
/// </summary>
public sealed class Kontext
{
    public required IReadOnlyList<Lehrer> Lehrer { get; init; }
    public required IReadOnlyList<Eintrag> Eintraege { get; init; }
    public required IReadOnlyList<Wunsch> Wuensche { get; init; }
    public required ScoreParameter P { get; init; }

    /// <summary>Optionales Mapping Fach → Fachgruppe (Blatt „Fachgruppen"), nur für Schutzfachgruppen relevant.</summary>
    public IReadOnlyDictionary<string, string> Fachgruppen { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    public int NL => Lehrer.Count;
    public int NE => Eintraege.Count;
    public int NW => Wuensche.Count;

    /// <summary>
    /// Aktuell wirksame Kapazitätstoleranz. Startet mit <see cref="ScoreParameter.Toleranz"/>,
    /// wird vom Backtracking-Fallback temporär auf 9999 gesetzt ("keine Kapazitätsschranke").
    /// Entspricht dem globalen <c>g_toleranz</c> und wird sowohl von den Kapazitätsprüfungen
    /// als auch von <see cref="Bewertung.Score.Berechne"/> gelesen.
    /// </summary>
    public double Toleranz { get; set; }

    /// <summary>„Kein Kapazitätslimit"-Sentinel (VBA-Konvention 9999).</summary>
    public const double KeineKapazitaet = 9999;

    // Sperre für die zweite Lösung: erzwingt, dass ein bestimmter Eintrag NICHT an SperrName geht.
    public int SperrIdx { get; set; } = -1;   // 0-basiert; -1 = keine Sperre
    public string SperrName { get; set; } = "";

    private readonly Dictionary<string, int> _lehrerIndex;

    [SetsRequiredMembers]
    public Kontext(IReadOnlyList<Lehrer> lehrer, IReadOnlyList<Eintrag> eintraege,
                   IReadOnlyList<Wunsch> wuensche, ScoreParameter p)
    {
        Lehrer = lehrer; Eintraege = eintraege; Wuensche = wuensche; P = p;
        Toleranz = p.Toleranz;
        _lehrerIndex = new Dictionary<string, int>(lehrer.Count, StringComparer.Ordinal);
        for (int i = 0; i < lehrer.Count; i++) _lehrerIndex[lehrer[i].Name] = i;
    }

    /// <summary>Index einer Lehrkraft anhand des Namens; -1 wenn unbekannt/leer/„?".</summary>
    public int LehrerIdx(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "?") return -1;
        return _lehrerIndex.TryGetValue(name, out var i) ? i : -1;
    }
}
