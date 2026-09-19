using UvOptimierer.Core.Bewertung;
using UvOptimierer.Core.Modell;

namespace UvOptimierer.Core.Solver;

/// <summary>
/// Eine vollständige Verteilung: je Eintrag der zugewiesene Lehrername (oder „?"),
/// dazu ein Ist-Stunden-Schnappschuss je Lehrkraft und die Bewertungskennzahlen.
/// </summary>
public sealed class Loesung
{
    /// <summary>Zuweisung je Eintrag (Index = Eintragsindex im <see cref="Kontext"/>).</summary>
    public required string[] Zuweisung { get; init; }

    /// <summary>Ist-Wertstunden je Lehrkraft (Index = Lehrerindex), aus <see cref="Zuweisung"/> neu aufgebaut.</summary>
    public required double[] IstWst { get; init; }

    /// <summary>Gesamtscore der Lösung (höher = besser).</summary>
    public required double Score { get; init; }

    /// <summary>Anzahl unbesetzter (nicht belegter) Einträge.</summary>
    public required int Unbesetzt { get; init; }

    /// <summary>War der Solver-Lauf vollständig erfolgreich (alle Einträge belegt)?</summary>
    public required bool Vollstaendig { get; init; }

    /// <summary>
    /// Baut eine <see cref="Loesung"/> aus einem rohen Zuweisungs-Array auf: kopiert die
    /// Zuweisung, rekonstruiert die Ist-Stunden je Lehrkraft aus der Lösung (verhindert
    /// Rundungsdrift durch inkrementelle Akkumulation) und berechnet Score/Kennzahlen.
    /// </summary>
    public static Loesung Aus(Kontext k, string[] roh, bool vollstaendig)
    {
        var zuw = (string[])roh.Clone();

        var ist = new double[k.NL];
        for (int e = 0; e < k.NE; e++)
        {
            if (!Regeln.IstBelegt(zuw[e])) continue;
            int idx = k.LehrerIdx(zuw[e]);
            if (idx >= 0) ist[idx] += k.Eintraege[e].WertUv;
        }

        int unbesetzt = 0;
        for (int e = 0; e < k.NE; e++)
            if (!Regeln.IstBelegt(zuw[e])) unbesetzt++;

        // Score im Kontextzustand der Lösung berechnen: IstWst der Lehrkräfte spiegeln.
        var backup = new double[k.NL];
        for (int i = 0; i < k.NL; i++) { backup[i] = k.Lehrer[i].IstWst; k.Lehrer[i].IstWst = ist[i]; }
        double score = Bewertung.Score.GesamtScore(k, zuw);
        for (int i = 0; i < k.NL; i++) k.Lehrer[i].IstWst = backup[i];

        return new Loesung
        {
            Zuweisung = zuw,
            IstWst = ist,
            Score = score,
            Unbesetzt = unbesetzt,
            Vollstaendig = vollstaendig
        };
    }
}

/// <summary>Ergebnis eines Optimierungslaufs: zwei alternative Verteilungen.</summary>
public sealed class OptimierungsErgebnis
{
    public required string Verfahren { get; init; }
    public required Loesung Loesung1 { get; init; }
    public required Loesung Loesung2 { get; init; }
    /// <summary>Optionale Warnung des Solvers (z. B. „keine zulässige Lösung“). null = keine.</summary>
    public string? Warnung { get; init; }
}
