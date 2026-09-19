using ClosedXML.Excel;

namespace UvOptimierer.Excel;

/// <summary>
/// Erzeugt ein Inhaltsverzeichnis-Blatt „Inhalt" mit anklickbaren Sprunglinks zu allen
/// vorhandenen Blättern, gruppiert nach Kategorie (Eingabe / Ergebnis / Analyse / Weitere).
/// </summary>
public static class Inhaltsverzeichnis
{
    private const string Blatt = "Inhalt";

    // Kategorie-Zuordnung bekannter Blätter
    private static readonly (string Titel, XLColor Farbe, string[] Blaetter)[] Gruppen =
    {
        ("Eingabe", XLColor.FromArgb(89, 89, 89),
            new[] { "Lehrerliste", "Parameter", "KlassenUV", "Klassen", "Lehrerwünsche", "Lehrerwuensche", "Fachgruppen" }),
        ("Ergebnis / Diagnose", XLColor.FromArgb(31, 73, 125),
            new[] { "Diagnose1", "Diagnose2", "Diagnose3", "Diagnose4", "Lehrerbelegung", "FachgruppenLehrer" }),
        ("Neueinstellung / Analyse", XLColor.FromArgb(0, 97, 0),
            new[] { "Neueinstellung", "Neueinstellung_Eingabe", "Neueinstellung_Simulation", "Neueinstellung_Sim_Kombi" }),
    };

    public static void Erzeuge(IXLWorkbook wb)
    {
        if (wb.Worksheets.TryGetWorksheet(Blatt, out var alt)) alt.Delete();
        var ws = wb.Worksheets.Add(Blatt);

        var vorhanden = new HashSet<string>(wb.Worksheets.Select(w => w.Name), StringComparer.OrdinalIgnoreCase);
        var schonGelistet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Blatt };

        int z = 1;
        var titel = ws.Range(ws.Cell(z, 1), ws.Cell(z, 2)).Merge();
        ws.Cell(z, 1).Value = "INHALT – zum Blatt springen";
        titel.Style.Font.Bold = true; titel.Style.Font.FontSize = 14;
        titel.Style.Fill.BackgroundColor = XLColor.FromArgb(31, 73, 125);
        titel.Style.Font.FontColor = XLColor.White;
        ws.Row(z).Height = 22;
        z += 2;

        foreach (var (gTitel, farbe, blaetter) in Gruppen)
        {
            var treffer = blaetter.Where(b => vorhanden.Contains(b) && !schonGelistet.Contains(b)).ToList();
            if (treffer.Count == 0) continue;

            var kopf = ws.Range(ws.Cell(z, 1), ws.Cell(z, 2)).Merge();
            ws.Cell(z, 1).Value = gTitel;
            kopf.Style.Font.Bold = true; kopf.Style.Font.FontColor = XLColor.White;
            kopf.Style.Fill.BackgroundColor = farbe;
            z++;

            foreach (var name in treffer)
            {
                LinkZeile(ws, z, name);
                schonGelistet.Add(name);
                z++;
            }
            z++;
        }

        // Übrige Blätter (unbekannte), in aktueller Reihenfolge
        var rest = wb.Worksheets.OrderBy(w => w.Position)
            .Select(w => w.Name)
            .Where(n => !schonGelistet.Contains(n))
            .ToList();
        if (rest.Count > 0)
        {
            var kopf = ws.Range(ws.Cell(z, 1), ws.Cell(z, 2)).Merge();
            ws.Cell(z, 1).Value = "Weitere";
            kopf.Style.Font.Bold = true; kopf.Style.Font.FontColor = XLColor.White;
            kopf.Style.Fill.BackgroundColor = XLColor.FromArgb(120, 120, 120);
            z++;
            foreach (var name in rest) { LinkZeile(ws, z, name); z++; }
        }

        // ---- Funktionsliste (ersetzt die früheren Excel-Makros) ----
        z++;
        var fKopf = ws.Range(ws.Cell(z, 1), ws.Cell(z, 3)).Merge();
        ws.Cell(z, 1).Value = "Funktionen des Programms (ersetzen die früheren Makros)";
        fKopf.Style.Font.Bold = true; fKopf.Style.Font.FontColor = XLColor.White;
        fKopf.Style.Fill.BackgroundColor = XLColor.FromArgb(84, 60, 120);
        z++;

        (string Name, string Ziel, string Hinweis)[] funktionen =
        {
            ("Optimieren (CP-SAT)", "Diagnose1",
                "Erzeugt die Unterrichtsverteilung und schreibt die Lehrkräfte in Spalte D des Blatts „Klassen“."),
            ("Nachträgliche Verbesserung", "Diagnose3",
                "Gleicht die Last der Lösung weiter aus (L3/L4) und erzeugt Diagnose3/4. Optional."),
            ("Bedarfsanalyse", "Neueinstellung",
                "Fachbedarf, Qualifikationsdichte und echte Überlast je Fachgruppe; Kandidatenprofile bewerten."),
            ("Simulation", "Neueinstellung_Simulation",
                "Prüft je Kandidatenprofil, wie viel reale Überlast eine Neueinstellung abbaut (CP-SAT)."),
            ("Kombi-Simulation", "Neueinstellung_Sim_Kombi",
                "Stellt alle Profile gleichzeitig ein und optimiert den Gesamtplan."),
            ("Parameter", "Parameter",
                "Score-/CP-SAT-Gewichte, globale/individuelle Grenzen und Zeitlimit einstellen."),
        };

        foreach (var (name, ziel, hinweis) in funktionen)
        {
            var c = ws.Cell(z, 2);
            c.Value = name;
            if (vorhanden.Contains(ziel))
            {
                var zielZelle = ws.Workbook.Worksheet(ziel).Cell(1, 1);
                c.SetHyperlink(new XLHyperlink(zielZelle));
                c.Style.Font.FontColor = XLColor.FromArgb(0, 102, 204);
                c.Style.Font.Underline = XLFontUnderlineValues.Single;
            }
            else
            {
                c.Style.Font.FontColor = XLColor.FromArgb(120, 120, 120);
            }
            ws.Cell(z, 3).Value = hinweis;
            z++;
        }

        ws.Column(1).Width = 4;
        ws.Column(2).Width = 30;
        ws.Column(3).Width = 80;
    }

    private static void LinkZeile(IXLWorksheet ws, int z, string blatt)
    {
        var c = ws.Cell(z, 2);
        c.Value = blatt;
        var ziel = ws.Workbook.Worksheet(blatt).Cell(1, 1);
        c.SetHyperlink(new XLHyperlink(ziel));
        c.Style.Font.FontColor = XLColor.FromArgb(0, 102, 204);
        c.Style.Font.Underline = XLFontUnderlineValues.Single;
    }
}
