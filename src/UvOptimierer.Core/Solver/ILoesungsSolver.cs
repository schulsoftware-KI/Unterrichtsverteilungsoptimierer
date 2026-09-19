using UvOptimierer.Core.Modell;

namespace UvOptimierer.Core.Solver;

/// <summary>Steuerparameter eines einzelnen Solver-Laufs.</summary>
public sealed class SolverOptionen
{
    /// <summary>Zeitlimit in Sekunden (0 = kein Limit). Relevant für die Backtracking-Phase.</summary>
    public double ZeitlimitSek { get; init; }

    /// <summary>Ist eine Sperre aktiv (zweite Lösung)?</summary>
    public bool HatSperre { get; init; }

    /// <summary>0-basierter Index des gesperrten Eintrags; -1 wenn keine Sperre.</summary>
    public int SperrIdx { get; init; } = -1;

    /// <summary>Name der Lehrkraft, die für den gesperrten Eintrag nicht zulässig ist.</summary>
    public string SperrName { get; init; } = "";

    /// <summary>Wurde <c>loesung</c> bereits mit einer Startlösung befüllt (SA warmgestartet)?</summary>
    public bool HatStartLoesung { get; init; }

    /// <summary>SA: erzwungene Iterationszahl (0 = automatisch nach Problemgröße bzw. Parameter).</summary>
    public long MaxIterOverride { get; init; }

    /// <summary>SA: Starttemperatur.</summary>
    public double StartTemp { get; init; } = 50;
}

/// <summary>
/// Gemeinsame Schnittstelle aller Lösungsverfahren (Simulated Annealing, Backtracking+CP,
/// später CP-SAT). Ein Solver füllt <paramref name="loesung"/> (je Eintrag den Lehrernamen
/// oder „?") und verändert dabei den Auslastungszustand im <see cref="Kontext"/>.
/// </summary>
public interface ILoesungsSolver
{
    string Name { get; }

    /// <summary>
    /// Löst das Problem. <paramref name="loesung"/> hat Länge <see cref="Kontext.NE"/> und wird
    /// befüllt (bzw. als Startlösung eingelesen, wenn <see cref="SolverOptionen.HatStartLoesung"/>).
    /// Rückgabe: true, wenn ein vollständiges Ergebnis gefunden wurde.
    /// </summary>
    bool Loese(Kontext k, string[] loesung, SolverOptionen opt);

    /// <summary>Optionale Warnung des letzten Laufs (z. B. „keine zulässige Lösung“). null = keine.</summary>
    string? LetzteWarnung => null;
}
