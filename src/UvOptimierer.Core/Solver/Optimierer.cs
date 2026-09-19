using UvOptimierer.Core.Modell;

namespace UvOptimierer.Core.Solver;

/// <summary>Verfügbare Lösungsverfahren.</summary>
public enum Verfahren
{
    /// <summary>Backtracking mit Constraint-Propagation.</summary>
    Backtracking,

    /// <summary>Simulated Annealing (mit Backtracking-Warmstart).</summary>
    SimulatedAnnealing
}

/// <summary>
/// Orchestriert einen Optimierungslauf und erzeugt – wie das VBA-Original – zwei
/// alternative Verteilungen: Lösung 1 frei, Lösung 2 mit einer Sperre auf dem ersten
/// nicht-fixierten belegten Eintrag (erzwingt eine andere Besetzung).
/// <para>
/// Für Simulated Annealing wird vor jedem Lauf per Backtracking eine Startlösung erzeugt
/// (Warmstart); schlägt der Warmstart komplett fehl, läuft SA mit eigener Greedy-Phase.
/// </para>
/// </summary>
public sealed class Optimierer
{
    private readonly ILoesungsSolver _hauptSolver;
    private readonly BacktrackingSolver? _warmStart;

    /// <param name="hauptSolver">Der eigentliche Solver (Backtracking oder SA).</param>
    /// <param name="warmStart">Optionaler Backtracking-Warmstart (für SA); null bei reinem Backtracking.</param>
    public Optimierer(ILoesungsSolver hauptSolver, BacktrackingSolver? warmStart = null)
    {
        _hauptSolver = hauptSolver;
        _warmStart = warmStart;
    }

    /// <summary>Baut den Optimierer für ein Verfahren mit Standardkomponenten.</summary>
    /// <param name="v">Verfahren.</param>
    /// <param name="seed">Optionaler Zufalls-Seed für SA (Reproduzierbarkeit in Tests).</param>
    public static Optimierer FuerVerfahren(Verfahren v, int? seed = null) => v switch
    {
        Verfahren.Backtracking => new Optimierer(new BacktrackingSolver()),
        Verfahren.SimulatedAnnealing => new Optimierer(new SaSolver(seed), new BacktrackingSolver()),
        _ => throw new ArgumentOutOfRangeException(nameof(v))
    };

    /// <summary>Führt den Lauf aus und liefert beide Lösungen. Mit <paramref name="nurEine"/> = true
    /// wird nur Lösung 1 berechnet (Lösung 2 = Lösung 1); spart den zweiten Lauf.</summary>
    public OptimierungsErgebnis Loese(Kontext k, bool nurEine = false)
    {
        Suchhilfen.SchutzFlagsSetzen(k);
        double tolVal = k.P.Toleranz;

        // ---- Lösung 1: frei ----
        var roh1 = NeueLoesung(k);
        bool ok1 = LoeseEinzeln(k, roh1, hatSperre: false, sperrIdx: -1, sperrName: "", tolVal);
        var loesung1 = Loesung.Aus(k, roh1, ok1);

        Loesung loesung2;
        if (nurEine)
        {
            loesung2 = loesung1;
        }
        else
        {
            // ---- Sperr-Eintrag: erster nicht-fixierter Eintrag mit zugewiesenem Lehrer ----
            int sperrIdx = -1;
            string sperrName = "";
            for (int e = 0; e < k.NE; e++)
            {
                if (Regeln.IstFixiert(k.Eintraege[e].UrsprungsLehrer)) continue;
                if (Regeln.IstBelegt(roh1[e])) { sperrIdx = e; sperrName = roh1[e]; break; }
            }

            // ---- Lösung 2: mit Sperre (oder Kopie von Lösung 1, wenn keine Sperre möglich) ----
            if (sperrIdx < 0 || !ok1)
            {
                loesung2 = Loesung.Aus(k, roh1, ok1);
            }
            else
            {
                var roh2 = NeueLoesung(k);
                bool ok2 = LoeseEinzeln(k, roh2, hatSperre: true, sperrIdx, sperrName, tolVal);
                loesung2 = Loesung.Aus(k, roh2, ok2);
            }
        }

        return new OptimierungsErgebnis
        {
            Verfahren = _hauptSolver.Name,
            Loesung1 = loesung1,
            Loesung2 = loesung2,
            Warnung = _hauptSolver.LetzteWarnung
        };
    }

    /// <summary>Ein einzelner Lauf inkl. optionalem Backtracking-Warmstart (SA).</summary>
    private bool LoeseEinzeln(Kontext k, string[] loesung, bool hatSperre, int sperrIdx, string sperrName, double tolVal)
    {
        k.Toleranz = tolVal;
        bool hatStart = false;

        if (_warmStart is not null)
        {
            // Backtracking-Startlösung (mit Zeitlimit, ggf. Toleranz-Fallback in BacktrackingSolver.Loese).
            var start = NeueLoesung(k);
            var btOpt = new SolverOptionen
            {
                ZeitlimitSek = k.P.Zeitlimit,
                HatSperre = hatSperre,
                SperrIdx = sperrIdx,
                SperrName = sperrName
            };
            _warmStart.Loese(k, start, btOpt);

            for (int e = 0; e < k.NE; e++)
            {
                loesung[e] = start[e];
                if (Regeln.IstBelegt(start[e])) hatStart = true;
            }
            k.Toleranz = tolVal;
        }

        var opt = new SolverOptionen
        {
            ZeitlimitSek = k.P.Zeitlimit,
            HatSperre = hatSperre,
            SperrIdx = sperrIdx,
            SperrName = sperrName,
            HatStartLoesung = hatStart
        };
        return _hauptSolver.Loese(k, loesung, opt);
    }

    private static string[] NeueLoesung(Kontext k)
    {
        var a = new string[k.NE];
        for (int e = 0; e < k.NE; e++) a[e] = "?";
        return a;
    }
}
