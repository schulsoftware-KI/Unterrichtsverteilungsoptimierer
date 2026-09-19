using System.Globalization;
using ClosedXML.Excel;
using UvOptimierer.Core.Fachlogik;
using UvOptimierer.Core.Modell;
using UvOptimierer.Core.Solver;

namespace UvOptimierer.Excel;

/// <summary>
/// Neueinstellungs-Simulation (Portierung von <c>NeueinstellungsSimulation</c> und
/// <c>NeueinstellungsSimulation_Kombi</c>). Fügt Kandidatenprofile als hypothetische
/// Lehrkräfte ein, lässt Simulated Annealing + Nachverbesserung laufen (wie Diagnose3)
/// und misst die tatsächlich abgebaute Gesamt-Überlast gegenüber einer Baseline.
/// </summary>
public sealed class NeueinstellungsSimulator
{
    private const string InSheet = "Neueinstellung_Eingabe";
    private const string SimSheet = "Neueinstellung_Simulation";
    private const string KombiSheet = "Neueinstellung_Sim_Kombi";

    private static readonly XLColor CH = XLColor.FromArgb(31, 73, 125);
    private static readonly XLColor CSH = XLColor.FromArgb(68, 114, 196);
    private static readonly XLColor CGut = XLColor.FromArgb(198, 239, 206);
    private static readonly XLColor CMit = XLColor.FromArgb(255, 235, 156);
    private static readonly XLColor CSchwach = XLColor.FromArgb(255, 200, 150);
    private static readonly XLColor CNeg = XLColor.FromArgb(240, 190, 190);
    private static readonly XLColor CWeiss = XLColor.White;
    private static readonly XLColor CRahmen = XLColor.FromArgb(200, 200, 200);
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static string F1(double v) => v.ToString("0.0", De);

    private sealed record Profil(string Name, double Dep, string[] Faecher, string FaeText);

    // ================================================================
    // Einzel-Simulation: jedes Profil für sich
    // ================================================================
    public int Erstelle(string datei, ILoesungsSolver solver, IProgress<string>? log)
    {
        using var wb = new XLWorkbook(datei);
        var basis = new ArbeitsmappeLeser().Lies(wb);
        var profile = LiesProfile(wb);
        if (profile.Count == 0)
            throw new InvalidOperationException(
                $"Keine Kandidatenprofile im Blatt '{InSheet}'. Zuerst die Bedarfsanalyse ausführen und dort Profile eintragen.");

        double zeit = Math.Max(1, basis.P.Zeitlimit);

        log?.Report($"Baseline (ohne Neueinstellung, {solver.Name}) …");
        double baseOv = SimLauf(basis, Array.Empty<Lehrer>(), solver, zeit, out _, out int baseUn);

        var erg = new List<(string Name, double Dep, string Fae, double Ov, double Ent, double Zu, int Un)>();
        for (int i = 0; i < profile.Count; i++)
        {
            log?.Report($"Profil {i + 1}/{profile.Count} ({profile[i].Name}) …");
            var neu = BaueTestlehrer(profile[i]);
            double ov = SimLauf(basis, new[] { neu }, solver, zeit, out double[] zu, out int un);
            erg.Add((profile[i].Name, profile[i].Dep, profile[i].FaeText, ov, baseOv - ov, zu[0], un));
        }

        erg = erg.OrderByDescending(x => x.Ent).ToList();
        SchreibeSimSheet(wb, baseOv, baseUn, solver.Name, erg);
        Inhaltsverzeichnis.Erzeuge(wb);
        BlattReihenfolge.Ordne(wb);
        wb.Save();
        return profile.Count;
    }

    // ================================================================
    // Kombi-Simulation: alle Profile gleichzeitig
    // ================================================================
    public int ErstelleKombi(string datei, ILoesungsSolver solver, IProgress<string>? log)
    {
        using var wb = new XLWorkbook(datei);
        var basis = new ArbeitsmappeLeser().Lies(wb);
        var profile = LiesProfile(wb);
        if (profile.Count == 0)
            throw new InvalidOperationException(
                $"Keine Kandidatenprofile im Blatt '{InSheet}'. Zuerst die Bedarfsanalyse ausführen und dort Profile eintragen.");

        double zeit = Math.Max(1, basis.P.Zeitlimit);

        log?.Report($"Baseline (ohne Neueinstellung, {solver.Name}) …");
        double baseOv = SimLauf(basis, Array.Empty<Lehrer>(), solver, zeit, out _, out int baseUn);

        log?.Report($"Kombination – alle {profile.Count} Profile gleichzeitig …");
        var testLehrer = profile.Select(BaueTestlehrer).ToArray();
        double bestOv = SimLauf(basis, testLehrer, solver, zeit, out double[] bestZu, out int bestUn);

        SchreibeKombiSheet(wb, profile, baseOv, baseUn, bestOv, bestUn, bestZu);
        Inhaltsverzeichnis.Erzeuge(wb);
        BlattReihenfolge.Ordne(wb);
        wb.Save();
        return profile.Count;
    }

    // ================================================================
    // Ein Optimierungslauf: erweiterten Kontext bauen, SA + Verbesserung, Überlast messen
    // ================================================================
    private static double SimLauf(Kontext basis, IReadOnlyList<Lehrer> testLehrer, ILoesungsSolver solver,
                                  double zeitlimit, out double[] zuNeu, out int ungedeckt)
    {
        var lehrer = new List<Lehrer>(basis.Lehrer);
        lehrer.AddRange(testLehrer);
        var k = new Kontext(lehrer, basis.Eintraege, basis.Wuensche, basis.P) { Fachgruppen = basis.Fachgruppen };
        Suchhilfen.SchutzFlagsSetzen(k);

        var loesung = new string[k.NE];
        solver.Loese(k, loesung, new SolverOptionen { ZeitlimitSek = zeitlimit });

        var loes = Loesung.Aus(k, loesung, true);

        double ov = 0;
        for (int i = 0; i < k.NL; i++)
        {
            double d = loes.IstWst[i] - k.Lehrer[i].SollWst;
            if (d > 0) ov += d;
        }

        ungedeckt = 0;
        for (int e = 0; e < k.NE; e++)
            if (!Regeln.IstBelegt(loesung[e])) ungedeckt++;

        zuNeu = new double[testLehrer.Count];
        for (int j = 0; j < testLehrer.Count; j++)
            zuNeu[j] = loes.IstWst[basis.NL + j];

        return ov;
    }

    private static Lehrer BaueTestlehrer(Profil p)
    {
        var l = new Lehrer { Name = "<<NEU>> " + p.Name, SollWst = p.Dep, KlassenleitungKlasse = "" };
        int fill = 0;
        foreach (var f in p.Faecher)
        {
            if (f == "" || fill >= 20) continue;
            l.Faecher[fill] = f;
            l.OberstufeOk[fill] = FachLogik.IstOberstufenFach(f);
            fill++;
        }
        return l;
    }

    // ================================================================
    // Profile lesen
    // ================================================================
    private static List<Profil> LiesProfile(IXLWorkbook wb)
    {
        var liste = new List<Profil>();
        if (!wb.Worksheets.TryGetWorksheet(InSheet, out var ws)) return liste;

        int lastR = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (int r = 4; r <= lastR; r++)
        {
            string name = ws.Cell(r, 1).GetString().Trim();
            if (name == "") continue;
            double dep = ws.Cell(r, 2).TryGetValue(out double d) ? d : 0;

            var faecher = new List<string>();
            for (int c = 1; c <= 4; c++)
            {
                string f = ws.Cell(r, 2 + c).GetString().Trim();
                while (f.Contains("  ")) f = f.Replace("  ", " ");
                if (f != "") faecher.Add(f);
            }
            liste.Add(new Profil(name, dep, faecher.ToArray(), string.Join(", ", faecher)));
        }
        return liste;
    }

    // ================================================================
    // Ausgabe Einzel
    // ================================================================
    private static void SchreibeSimSheet(IXLWorkbook wb, double baseOv, int baseUn, string solverName,
        List<(string Name, double Dep, string Fae, double Ov, double Ent, double Zu, int Un)> erg)
    {
        var ws = SheetNeu(wb, SimSheet);
        int z = 1;
        SectionHeader(ws, z, $"NEUEINSTELLUNGS-SIMULATION ({solverName})", CH, 8); z++;
        ws.Cell(z, 1).Value = $"Baseline ohne Neueinstellung:  Gesamt-Überlast = {F1(baseOv)} WSt   |   ungedeckte Einträge = {baseUn}";
        ws.Cell(z, 1).Style.Font.Bold = true; z++;
        ws.Cell(z, 1).Value = $"Je Profil ein Lauf mit {solverName} (deterministisch). " +
                              "'Entlastung' = Rückgang der Gesamt-Überlast gegenüber der Baseline (mehr = besser).";
        ws.Cell(z, 1).Style.Font.Italic = true; z += 2;

        TableHeader(ws, z, new[] { "Rang", "Profil", "Deputat", "Fachgruppen",
            "Überlast nachher (WSt)", "Entlastung (WSt)", "Zugew. WSt (neu)", "Ungedeckt nachher" }, CSH);
        z++;

        int rang = 0;
        foreach (var e in erg)
        {
            rang++;
            XLColor rc = e.Ent > 8 ? CGut : e.Ent > 3 ? CMit : e.Ent > 0 ? CSchwach : CNeg;
            ws.Cell(z, 1).Value = rang;
            ws.Cell(z, 2).Value = e.Name;
            SetzeZahl(ws, z, 3, e.Dep, "0.0");
            ws.Cell(z, 4).Value = e.Fae;
            SetzeZahl(ws, z, 5, e.Ov, "0.0");
            SetzeZahl(ws, z, 6, e.Ent, "0.0");
            SetzeZahl(ws, z, 7, e.Zu, "0.0");
            ws.Cell(z, 8).Value = e.Un;
            FarbeZeile(ws, z, 1, 8, rc);
            z++;
        }

        z++;
        ws.Cell(z, 1).Value = "Hinweis: Die Simulation nutzt den CP-SAT-Solver (deterministisch) mit dem Zeitlimit aus Parameter-Zeile 15 je Lauf. " +
            "Für die Endentscheidung das Sieger-Profil als echte Zeile in die Lehrerliste eintragen und regulär optimieren.";
        ws.Cell(z, 1).Style.Font.Italic = true;
        ws.Cell(z, 1).Style.Font.FontColor = XLColor.FromArgb(128, 128, 128);

        double[] br = { 6, 24, 10, 20, 20, 16, 16, 16 };
        for (int c = 1; c <= 8; c++) ws.Column(c).Width = br[c - 1];
    }

    // ================================================================
    // Ausgabe Kombi
    // ================================================================
    private static void SchreibeKombiSheet(IXLWorkbook wb, List<Profil> profile,
        double baseOv, int baseUn, double bestOv, int bestUn, double[] bestZu)
    {
        var ws = SheetNeu(wb, KombiSheet);
        int z = 1;
        ws.Cell(z, 1).Value = "KOMBINIERTE NEUEINSTELLUNGS-SIMULATION (alle Profile gleichzeitig)";
        ws.Cell(z, 1).Style.Font.Bold = true; ws.Cell(z, 1).Style.Font.FontSize = 13; z += 2;

        int rInfo = z;
        ws.Cell(z, 1).Value = "Baseline-Überlast (ohne Neueinstellung)"; SetzeZahl(ws, z, 3, baseOv, "0.0"); z++;
        ws.Cell(z, 1).Value = "Überlast nachher (alle zusammen)"; SetzeZahl(ws, z, 3, bestOv, "0.0"); z++;
        ws.Cell(z, 1).Value = "Gemeinsame Entlastung"; SetzeZahl(ws, z, 3, baseOv - bestOv, "0.0"); z++;
        ws.Cell(z, 1).Value = $"Ungedeckt nachher (Baseline: {baseUn})"; ws.Cell(z, 3).Value = bestUn; z += 2;
        ws.Range(ws.Cell(rInfo, 1), ws.Cell(z, 1)).Style.Font.Bold = true;

        TableHeader(ws, z, new[] { "Profil", "Deputat (WSt)", "Zugew. WSt (neu)", "Auslastung" }, CH);
        z++;
        double sumZu = 0, sumDep = 0;
        for (int i = 0; i < profile.Count; i++)
        {
            ws.Cell(z, 1).Value = profile[i].Name;
            SetzeZahl(ws, z, 2, profile[i].Dep, "0.0");
            SetzeZahl(ws, z, 3, bestZu[i], "0.0");
            if (profile[i].Dep > 0) ws.Cell(z, 4).Value = (bestZu[i] / profile[i].Dep).ToString("0%", De);
            sumZu += bestZu[i]; sumDep += profile[i].Dep;
            z++;
        }
        ws.Cell(z, 1).Value = "Summe"; ws.Cell(z, 1).Style.Font.Bold = true;
        SetzeZahl(ws, z, 2, sumDep, "0.0"); SetzeZahl(ws, z, 3, sumZu, "0.0"); z += 2;
        ws.Cell(z, 1).Value = "Hinweis: Überlast und Entlastung beziehen sich auf den GESAMTPLAN mit allen neuen Kräften gleichzeitig; " +
            "'Zugew. WSt' zeigt, wie stark jede Stelle real ausgelastet wird.";
        ws.Cell(z, 1).Style.Font.Italic = true;

        ws.Column(1).Width = 40;
        ws.Column(2).Width = 18; ws.Column(3).Width = 18; ws.Column(4).Width = 18;
    }

    // ---- Blatt-Helfer ----
    private static IXLWorksheet SheetNeu(IXLWorkbook wb, string name)
    {
        if (wb.Worksheets.TryGetWorksheet(name, out var alt)) alt.Delete();
        return wb.Worksheets.Add(name);
    }

    private static void SectionHeader(IXLWorksheet ws, int row, string titel, XLColor farbe, int breite)
    {
        var r = ws.Range(ws.Cell(row, 1), ws.Cell(row, breite)).Merge();
        ws.Cell(row, 1).Value = titel;
        r.Style.Font.Bold = true; r.Style.Font.FontColor = CWeiss;
        r.Style.Font.FontSize = 12; r.Style.Fill.BackgroundColor = farbe;
        ws.Row(row).Height = 20;
    }

    private static void TableHeader(IXLWorksheet ws, int row, string[] headers, XLColor farbe)
    {
        for (int j = 0; j < headers.Length; j++)
        {
            var c = ws.Cell(row, j + 1);
            c.Value = headers[j];
            c.Style.Font.Bold = true; c.Style.Font.FontColor = CWeiss;
            c.Style.Fill.BackgroundColor = farbe;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    private static void FarbeZeile(IXLWorksheet ws, int row, int von, int bis, XLColor farbe)
    {
        var r = ws.Range(ws.Cell(row, von), ws.Cell(row, bis));
        r.Style.Fill.BackgroundColor = farbe;
        r.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        r.Style.Border.BottomBorderColor = CRahmen;
    }

    private static void SetzeZahl(IXLWorksheet ws, int row, int col, double wert, string format)
    {
        var c = ws.Cell(row, col);
        c.Value = wert;
        c.Style.NumberFormat.Format = format;
    }
}
