using ClosedXML.Excel;

namespace UvOptimierer.Excel;

/// <summary>
/// Ordnet die Blätter einer Arbeitsmappe: zuerst die Eingabeblätter „Parameter", „KlassenUV",
/// „Klassen", danach die vom Programm erzeugten Auswertungsblätter in fester Reihenfolge,
/// zuletzt alle übrigen Blätter (Lehrerliste, Lehrerwünsche, Fachgruppen …) in bisheriger Folge.
/// </summary>
public static class BlattReihenfolge
{
    private static readonly string[] Reihenfolge =
    {
        // Navigationsblatt zuerst, dann Eingabeblätter
        "Inhalt",
        "Lehrerliste", "Parameter", "KlassenUV", "Klassen",
        // vom Programm beschriebene Blätter
        "Diagnose1", "Diagnose2", "Diagnose3", "Diagnose4",
        "Lehrerbelegung", "FachgruppenLehrer",
        "Neueinstellung", "Neueinstellung_Eingabe",
        "Neueinstellung_Simulation", "Neueinstellung_Sim_Kombi"
    };

    public static void Ordne(IXLWorkbook wb)
    {
        // Zuerst die bekannten Blätter in fester Reihenfolge (nur die vorhandenen).
        var geordnet = new List<IXLWorksheet>();
        foreach (var name in Reihenfolge)
            if (wb.Worksheets.TryGetWorksheet(name, out var ws))
                geordnet.Add(ws);

        // Dann der Rest in aktueller Reihenfolge.
        var bekannt = new HashSet<string>(Reihenfolge, StringComparer.OrdinalIgnoreCase);
        var rest = wb.Worksheets
            .OrderBy(w => w.Position)
            .Where(w => !bekannt.Contains(w.Name))
            .ToList();

        int pos = 1;
        foreach (var ws in geordnet) ws.Position = pos++;
        foreach (var ws in rest) ws.Position = pos++;
    }
}
