namespace UvOptimierer.Core.Modell;

/// <summary>
/// Eine Lehrkraft. Entspricht dem VBA-Typ <c>tLehrer</c>.
/// <para>
/// <see cref="IstWst"/> ist bewusst veränderlich: Die Solver führen die aktuelle
/// Auslastung während der Suche mit (genau wie das VBA-Original über <c>g_lehrer(i).istWSt</c>),
/// und <see cref="Bewertung.Score"/> liest sie direkt aus.
/// </para>
/// </summary>
public sealed class Lehrer
{
    /// <summary>Name bzw. Kürzel (Spalte A der Lehrerliste).</summary>
    public string Name { get; init; } = "";

    /// <summary>Bis zu 20 Fächer (Spalten B..U). Leere Einträge sind der leere String.</summary>
    public string[] Faecher { get; } = new string[20];

    /// <summary>Pro Fachspalte: darf die Lehrkraft dieses Fach in der Oberstufe unterrichten.</summary>
    public bool[] OberstufeOk { get; } = new bool[20];

    /// <summary>Klasse, deren Klassenleitung die Lehrkraft innehat (Spalte V); sonst leer.</summary>
    public string KlassenleitungKlasse { get; init; } = "";

    /// <summary>Soll-Wochenstunden / Anrechnungen (Spalte W).</summary>
    public double SollWst { get; init; }

    /// <summary>
    /// Optionale individuelle Überlastgrenze (neue Spalte „Max-Überlast" der Lehrerliste).
    /// <c>null</c> = keine Grenze. Wo gesetzt, gilt als harte Bedingung
    /// <c>IstWst ≤ SollWst + MaxUeberlast</c> (im Backtracking garantiert, im SA als Strafe).
    /// </summary>
    public double? MaxUeberlast { get; init; }

    /// <summary>Optionale individuelle Unterlastgrenze (leer = keine). Wenn gesetzt, gilt
    /// <c>IstWst ≥ SollWst − MaxUnterlast</c> (in CP-SAT hart, in SA/BT als Strafe).</summary>
    public double? MaxUnterlast { get; init; }

    /// <summary>Gehört die Lehrkraft zu einer aktiven Schutzfachgruppe (einmalig gesetzt).</summary>
    public bool IstSchutzLehrer { get; set; }

    /// <summary>Laufende Ist-Wochenstunden während der Optimierung (veränderlicher Zustand).</summary>
    public double IstWst { get; set; }
}
