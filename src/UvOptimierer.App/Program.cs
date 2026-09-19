using System.Diagnostics;
using UvOptimierer.Core.Modell;
using UvOptimierer.Core.Solver;
using UvOptimierer.CpSat;
using UvOptimierer.Excel;

namespace UvOptimierer.App;

/// <summary>
/// Konsolen-Runner für die Unterrichtsverteilung. Vorläufige Bedienschale, bis die
/// WinForms-Oberfläche folgt. Liest die Arbeitsmappe, optimiert und schreibt die
/// beiden Lösungen zurück (Blatt „Klassen", Spalten D/E).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "/?")
        {
            HilfeAusgeben();
            return args.Length == 0 ? 1 : 0;
        }

        string eingabe = args[0];
        if (!File.Exists(eingabe))
        {
            Console.Error.WriteLine($"Datei nicht gefunden: {eingabe}");
            return 2;
        }

        string verfahren = VerfahrenAusArgumenten(args);
        string ausgabe = AusgabePfad(args, eingabe);
        int? seed = SeedAusArgumenten(args);
        bool verbessern = args.Any(a => a.ToLowerInvariant() is "--verbessern" or "--improve" or "-v");

        try
        {
            Console.WriteLine($"Lese {Path.GetFileName(eingabe)} …");
            var leser = new ArbeitsmappeLeser();
            Kontext k = leser.Lies(eingabe);
            Console.WriteLine($"  {k.NL} Lehrkräfte, {k.NE} Einträge, {k.NW} Wünsche.");
            int fixiert = k.Eintraege.Count(e => Regeln.IstFixiert(e.UrsprungsLehrer));
            Console.WriteLine($"  davon fixiert: {fixiert}");

            Console.WriteLine($"Optimiere mit Verfahren: {VerfahrenName(verfahren)} …");
            var sw = Stopwatch.StartNew();
            var optimierer = OptimiererBauen(verfahren, seed);
            OptimierungsErgebnis erg = optimierer.Loese(k);
            sw.Stop();

            LoesungAusgeben("Lösung 1", erg.Loesung1);
            LoesungAusgeben("Lösung 2", erg.Loesung2);
            if (erg.Warnung != null)
            {
                Console.WriteLine();
                Console.WriteLine("ACHTUNG: " + erg.Warnung);
            }
            Console.WriteLine($"Dauer: {sw.Elapsed.TotalSeconds:0.0} s");

            if (verbessern)
                Console.WriteLine($"Nachträgliche Verbesserung aktiv (L1→L3, L2→L4, Zeitlimit {k.P.Zeitlimit:0} s je Lösung) …");

            Console.WriteLine($"Schreibe Ergebnis nach {Path.GetFileName(ausgabe)} …");
            var schreiber = new ArbeitsmappeSchreiber();
            schreiber.SchreibeUndSpeichere(eingabe, ausgabe, k, erg, verbessern);
            Console.WriteLine("Fertig.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Fehler: " + ex.Message);
            return 3;
        }
    }

    private static void LoesungAusgeben(string titel, Loesung l)
    {
        Console.WriteLine($"  {titel}: Score={l.Score:0.0}, unbesetzt={l.Unbesetzt}, "
                          + $"vollständig={(l.Vollstaendig ? "ja" : "nein")}");
    }

    private static string VerfahrenAusArgumenten(string[] args)
    {
        foreach (var a in args)
        {
            string s = a.ToLowerInvariant();
            if (s is "--bt" or "--backtracking" or "-bt") return "bt";
            if (s is "--sa" or "--annealing" or "-sa") return "sa";
            if (s is "--cpsat" or "--cp-sat" or "-cp") return "cpsat";
        }
        return "cpsat";   // Standard: CP-SAT (OR-Tools)
    }

    private static Optimierer OptimiererBauen(string verfahren, int? seed) => verfahren switch
    {
        "bt" => Optimierer.FuerVerfahren(Verfahren.Backtracking, seed),
        "cpsat" => new Optimierer(new CpSatSolver()),
        _ => Optimierer.FuerVerfahren(Verfahren.SimulatedAnnealing, seed),
    };

    private static string VerfahrenName(string verfahren) => verfahren switch
    {
        "bt" => "Backtracking + CP",
        "cpsat" => "CP-SAT (OR-Tools)",
        _ => "Simulated Annealing",
    };

    private static string AusgabePfad(string[] args, string eingabe)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] is "-o" or "--out") return args[i + 1];

        string dir = Path.GetDirectoryName(eingabe) ?? ".";
        string name = Path.GetFileNameWithoutExtension(eingabe);
        string ext = Path.GetExtension(eingabe);
        return Path.Combine(dir, $"{name}_Ergebnis{ext}");
    }

    private static int? SeedAusArgumenten(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] is "--seed" && int.TryParse(args[i + 1], out int s)) return s;
        return null;
    }

    private static void HilfeAusgeben()
    {
        Console.WriteLine("UV-Optimierer – Unterrichtsverteilung");
        Console.WriteLine();
        Console.WriteLine("Aufruf:");
        Console.WriteLine("  UvOptimierer.App <datei.xlsm> [--sa|--bt|--cpsat] [-o <ausgabe.xlsm>] [--seed <n>] [--verbessern]");
        Console.WriteLine();
        Console.WriteLine("Optionen:");
        Console.WriteLine("  --sa    Simulated Annealing");
        Console.WriteLine("  --bt    Backtracking + Constraint-Propagation");
        Console.WriteLine("  --cpsat CP-SAT (OR-Tools) – Standard");
        Console.WriteLine("  -o     Ausgabedatei (Standard: <datei>_Ergebnis.<ext>)");
        Console.WriteLine("  --seed Zufalls-Seed für reproduzierbare SA-Läufe");
        Console.WriteLine("  --verbessern  Nachträgliche Lastausgleich-Verbesserung → L3/L4 + Diagnose3/4");
    }
}
