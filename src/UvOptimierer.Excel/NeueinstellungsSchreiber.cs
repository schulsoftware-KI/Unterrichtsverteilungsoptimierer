using System.Globalization;
using ClosedXML.Excel;
using UvOptimierer.Core.Modell;
using UvOptimierer.Core.Solver;

namespace UvOptimierer.Excel;

/// <summary>
/// Bedarfs-/Neueinstellungsanalyse (Portierung von <c>NeueinstellungsAnalyse_Erstellen</c>).
/// Arbeitet auf einer fertigen Ergebnisdatei: liest Lehrerliste und Klassen für Bedarf und
/// Qualifikationsdichte je Fachgruppe und die echte Überlast aus den Diagnose-Blättern
/// (bevorzugt Diagnose3), und schreibt die Blätter „Neueinstellung" (Analyse) und
/// „Neueinstellung_Eingabe" (Kandidatenprofile).
/// </summary>
public sealed class NeueinstellungsSchreiber
{
    private const string OutSheet = "Neueinstellung";
    private const string InSheet = "Neueinstellung_Eingabe";
    private const int MaxPf = 4;   // Fächer je Kandidatenprofil

    private static readonly XLColor CH  = XLColor.FromArgb(31, 73, 125);
    private static readonly XLColor CSH = XLColor.FromArgb(68, 114, 196);
    private static readonly XLColor CKrit = XLColor.FromArgb(220, 80, 80);
    private static readonly XLColor CHoch = XLColor.FromArgb(255, 150, 80);
    private static readonly XLColor CMit = XLColor.FromArgb(240, 200, 80);
    private static readonly XLColor COk = XLColor.FromArgb(198, 239, 206);
    private static readonly XLColor CGrau = XLColor.FromArgb(230, 230, 230);
    private static readonly XLColor CWeiss = XLColor.White;
    private static readonly XLColor CRahmen = XLColor.FromArgb(200, 200, 200);
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static string F1(double v) => v.ToString("0.0", De);

    private sealed class Grp
    {
        public string Name = "";
        public double Bed;       // offener Bedarf (WSt)
        public double BedOs;     // davon Oberstufe (WSt)
        public int Qual;         // qualifizierte Lehrkräfte
        public double Ueberl;    // echte Überlast lt. Diagnose (WSt)
        public int Betroffen;    // betroffene Lehrkräfte lt. Diagnose
    }

    private readonly List<Grp> _grp = new();
    private readonly Dictionary<string, int> _idx = new(StringComparer.OrdinalIgnoreCase);

    private int GrpIdx(string key, bool anlegen)
    {
        if (_idx.TryGetValue(key, out int i)) return i;
        if (!anlegen) return -1;
        _grp.Add(new Grp { Name = key });
        _idx[key] = _grp.Count - 1;
        return _grp.Count - 1;
    }

    /// <summary>Erzeugt die Analyse in der angegebenen (Ergebnis-)Datei und speichert sie in place.
    /// Rückgabe: (Diagnose gefunden?, Name des verwendeten Diagnose-Blatts).</summary>
    public (bool diagGefunden, string diagName) Erstelle(string datei)
    {
        using var wb = new XLWorkbook(datei);
        var k = new ArbeitsmappeLeser().Lies(wb);

        BedarfUndQualBerechnen(k);
        bool diagGefunden = LiesUeberlast(wb, out string diagName);

        SchreibeAnalyse(wb, k, diagGefunden, diagName);

        Inhaltsverzeichnis.Erzeuge(wb);
        BlattReihenfolge.Ordne(wb);
        wb.Save();
        return (diagGefunden, diagName);
    }

    // ================================================================
    // Teil A/1: Bedarf, Oberstufen-Anteil, Qualifikationsdichte
    // ================================================================
    private void BedarfUndQualBerechnen(Kontext k)
    {
        _grp.Clear();
        _idx.Clear();

        // Bedarf (nur nicht fixierte Einträge)
        foreach (var e in k.Eintraege)
        {
            double wert = e.WertUv;
            if (wert == 0) wert = e.Wst;
            if (wert <= 0) continue;
            if (Regeln.IstFixiert(e.UrsprungsLehrer)) continue;

            string key = Suchhilfen.FachEngpassKey(k, e.Fach);
            if (key == "" || string.Equals(key, "alle", StringComparison.OrdinalIgnoreCase)) continue;

            int gi = GrpIdx(key, true);
            _grp[gi].Bed += wert;
            if (IstOberstufenKlasse(e.Klasse)) _grp[gi].BedOs += wert;
        }

        // Qualifikationsdichte: je Gruppe Anzahl Lehrkräfte, die sie unterrichten können
        foreach (var l in k.Lehrer)
        {
            var gesehen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in l.Faecher)
            {
                string f = (raw ?? "").Trim();
                if (f == "") continue;
                string key = Suchhilfen.FachEngpassKey(k, f);
                if (key == "") key = f;
                if (string.Equals(key, "alle", StringComparison.OrdinalIgnoreCase)) continue;
                if (!gesehen.Add(key)) continue;
                int gi = GrpIdx(key, true);
                _grp[gi].Qual++;
            }
        }
    }

    // ================================================================
    // Teil A/2: echte Überlast aus dem Diagnose-Blatt (Abschnitt 4) lesen
    // ================================================================
    private bool LiesUeberlast(IXLWorkbook wb, out string diagName)
    {
        IXLWorksheet? wsD = null;
        diagName = "";
        foreach (var name in new[] { "Diagnose3", "Diagnose1", "Diagnose2" })
        {
            if (wb.Worksheets.TryGetWorksheet(name, out var ws)) { wsD = ws; diagName = name; break; }
        }
        if (wsD == null) return false;

        int lastR = wsD.LastRowUsed()?.RowNumber() ?? 0;
        int secRow = 0;
        for (int r = 1; r <= lastR; r++)
        {
            string s = wsD.Cell(r, 1).GetString();
            if (s.IndexOf("UEBERLAST-VERTEILUNG NACH FACHGRUPPE", StringComparison.OrdinalIgnoreCase) >= 0)
            { secRow = r; break; }
        }
        if (secRow == 0) return false;

        int hdrRow = 0;
        for (int r = secRow; r <= Math.Min(secRow + 5, lastR); r++)
        {
            if (string.Equals(wsD.Cell(r, 1).GetString().Trim(), "Fachgruppe", StringComparison.OrdinalIgnoreCase))
            { hdrRow = r; break; }
        }
        if (hdrRow == 0) return false;

        for (int r = hdrRow + 1; r <= lastR; r++)
        {
            string key = wsD.Cell(r, 1).GetString().Trim();
            if (key == "") break;
            if (!wsD.Cell(r, 2).TryGetValue(out double ov)) break;
            int betr = wsD.Cell(r, 4).TryGetValue(out double b) ? (int)Math.Round(b) : 0;
            int gi = GrpIdx(key, true);
            _grp[gi].Ueberl = ov;
            _grp[gi].Betroffen = betr;
        }
        return true;
    }

    private static bool IstOberstufenKlasse(string klasse)
    {
        string k = klasse.Trim().ToUpperInvariant();
        if (k == "") return false;
        int cp = k.IndexOf(',');
        string first = cp > 0 ? k[..cp].Trim() : k;
        return first.StartsWith("EF") || first.StartsWith("Q1") || first.StartsWith("Q2");
    }

    // ================================================================
    // Ausgabe
    // ================================================================
    private void SchreibeAnalyse(IXLWorkbook wb, Kontext k, bool diagGefunden, string diagName)
    {
        var ws = SheetNeu(wb, OutSheet);
        int zRow = 1;

        SectionHeader(ws, zRow, "NEUEINSTELLUNGS-ANALYSE", CH, 8); zRow++;
        var hinweis = ws.Cell(zRow, 1);
        if (diagGefunden)
            hinweis.Value = $"Leitsignal: echte Überlast je Fachgruppe aus '{diagName}'. Hohe Überlast oder " +
                            "wenige qualifizierte Lehrkräfte sprechen für eine Einstellung in diesem Fach.";
        else
        {
            hinweis.Value = "ACHTUNG: Kein Diagnose-Blatt gefunden. Bitte zuerst die Optimierung ausführen " +
                            "(erzeugt Diagnose1). Ohne sie liegt kein verlässliches Überlast-Signal vor – unten nur Bedarf und Qualifikationsdichte.";
            hinweis.Style.Font.FontColor = XLColor.FromArgb(200, 0, 0);
        }
        hinweis.Style.Font.Italic = true;
        zRow += 2;

        // ---- Teil A: Tabelle ----
        Sortiere(diagGefunden);
        TableHeader(ws, zRow, new[] { "Fachgruppe", "Bedarf (WSt)", "Qual. Lehrer",
            "Überlast (WSt)", "Betroffene Lehrer", "davon Oberstufe", "Robustheit", "Bewertung" }, CSH);
        zRow++;

        bool anyRow = false;
        foreach (var g in _grp)
        {
            if (g.Bed <= 0 && g.Ueberl <= 0) continue;
            anyRow = true;

            string rob = g.Qual == 0 ? "KEIN Lehrer"
                       : g.Qual <= 2 ? $"fragil ({g.Qual})"
                       : $"ok ({g.Qual})";

            string bew; XLColor rc;
            if (diagGefunden)
            {
                if (g.Ueberl > 15) { bew = "Kritisch"; rc = CKrit; }
                else if (g.Ueberl > 6) { bew = "Hoch"; rc = CHoch; }
                else if (g.Ueberl > 0) { bew = "Moderat"; rc = CMit; }
                else if (g.Qual <= 2) { bew = "kein Engpass, aber fragil"; rc = CMit; }
                else { bew = "kein Engpass"; rc = COk; }
            }
            else { bew = "(Optimierung ausführen)"; rc = CGrau; }

            ws.Cell(zRow, 1).Value = g.Name;
            SetzeZahl(ws, zRow, 2, g.Bed, "0.0");
            ws.Cell(zRow, 3).Value = g.Qual;
            if (diagGefunden)
            {
                SetzeZahl(ws, zRow, 4, g.Ueberl, "0.0");
                ws.Cell(zRow, 5).Value = g.Betroffen;
            }
            else { ws.Cell(zRow, 4).Value = "-"; ws.Cell(zRow, 5).Value = "-"; }
            ws.Cell(zRow, 6).Value = g.BedOs > 0 ? $"ja ({F1(g.BedOs)} WSt)" : "-";
            ws.Cell(zRow, 7).Value = rob;
            ws.Cell(zRow, 8).Value = bew;
            FarbeZeile(ws, zRow, 1, 8, rc);
            zRow++;
        }
        if (!anyRow)
        {
            ws.Cell(zRow, 1).Value = "Keine Fachgruppen mit Bedarf oder Überlast gefunden.";
            ws.Cell(zRow, 1).Style.Font.Italic = true; zRow++;
        }
        zRow += 2;

        // ---- Teil C: Empfehlung ----
        SectionHeader(ws, zRow, "EMPFEHLUNG", CH, 8); zRow++;
        ws.Cell(zRow, 1).Value = diagGefunden
            ? "Größte Überlast (ideale Hauptfächer):"
            : "Größter Bedarf (Überlast erst nach Optimierung verfügbar):";
        ws.Cell(zRow, 1).Style.Font.Bold = true; zRow++;

        int rang = 0;
        foreach (var g in _grp)
        {
            double val = diagGefunden ? g.Ueberl : g.Bed;
            if (val <= 0) continue;
            rang++;
            ws.Cell(zRow, 1).Value = diagGefunden
                ? $"{rang}. {g.Name}  (Überlast {F1(g.Ueberl)} WSt, {g.Betroffen} Lehrkräfte{(g.BedOs > 0 ? ", Oberstufe relevant" : "")})"
                : $"{rang}. {g.Name}  (Bedarf {F1(g.Bed)} WSt)";
            zRow++;
            if (rang >= 3) break;
        }
        if (rang == 0) { ws.Cell(zRow, 1).Value = "(keine)"; ws.Cell(zRow, 1).Style.Font.Italic = true; zRow++; }

        zRow++;
        ws.Cell(zRow, 1).Value = "Resilienz-Kandidaten (Bedarf vorhanden, aber nur 1–2 qualifizierte Lehrkräfte):";
        ws.Cell(zRow, 1).Style.Font.Bold = true; zRow++;
        int resAnz = 0;
        foreach (var g in _grp)
        {
            if (g.Bed > 0 && g.Qual <= 2)
            {
                ws.Cell(zRow, 1).Value = $"- {g.Name}  ({g.Qual} Lehrkraft/-kräfte)";
                zRow++; resAnz++;
            }
        }
        if (resAnz == 0) { ws.Cell(zRow, 1).Value = "(keine)"; ws.Cell(zRow, 1).Style.Font.Italic = true; zRow++; }
        zRow++;
        ws.Cell(zRow, 1).Value = "Eine realistische 2-Fächer-Kombination sollte möglichst zwei der oben genannten Fächer abdecken.";
        ws.Cell(zRow, 1).Style.Font.Italic = true; zRow += 2;

        // ---- Teil B: Kandidatenprofile ----
        ProfileBewerten(wb, ws, k, ref zRow, diagGefunden);

        double[] breiten = { 24, 12, 12, 14, 17, 16, 14, 24 };
        for (int c = 1; c <= 8; c++) ws.Column(c).Width = breiten[c - 1];
    }

    // ================================================================
    // Teil B: Kandidatenprofile bewerten
    // ================================================================
    private void ProfileBewerten(IXLWorkbook wb, IXLWorksheet ws, Kontext k, ref int zRow, bool diagGefunden)
    {
        var wsIn = EingabeSheetSicherstellen(wb);

        SectionHeader(ws, zRow, "BEWERTUNG DER KANDIDATENPROFILE", CH, 8); zRow++;
        ws.Cell(zRow, 1).Value = diagGefunden
            ? $"Bewertung gegen die echte Überlast. Profile im Blatt '{InSheet}' eintragen, dann neu berechnen."
            : $"Ohne Diagnose wird gegen den Bedarf bewertet (nur grobe Vorab-Näherung). Bitte Optimierung ausführen.";
        ws.Cell(zRow, 1).Style.Font.Italic = true; zRow += 2;

        var profile = new List<(string Name, double Dep, double Nutz, double RestKap, int Eng, bool Os, string Fae)>();
        int lastR = wsIn.LastRowUsed()?.RowNumber() ?? 0;
        for (int r = 4; r <= lastR; r++)
        {
            string pname = wsIn.Cell(r, 1).GetString().Trim();
            if (pname == "") continue;
            double dep = wsIn.Cell(r, 2).TryGetValue(out double d) ? d : 0;

            var covKeys = new List<string>();
            var covVals = new List<double>();
            bool brauchtOs = false;
            var faecher = new List<string>();
            for (int c = 1; c <= MaxPf; c++)
            {
                string f = wsIn.Cell(r, 2 + c).GetString().Trim();
                while (f.Contains("  ")) f = f.Replace("  ", " ");
                if (f == "") continue;
                faecher.Add(f);
                string key = Suchhilfen.FachEngpassKey(k, f);
                if (key == "") key = f;
                if (covKeys.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase))) continue;
                covKeys.Add(key);
                int gi = GrpIdx(key, false);
                double val = 0;
                if (gi >= 0)
                {
                    val = diagGefunden ? _grp[gi].Ueberl : _grp[gi].Bed;
                    if (_grp[gi].BedOs > 0) brauchtOs = true;
                }
                covVals.Add(val);
            }

            // Zielgrößen absteigend
            var paare = covKeys.Zip(covVals, (kk, vv) => (kk, vv)).OrderByDescending(x => x.vv).ToList();

            double nutzen = 0, restKap = dep;
            int anzEng = 0;
            foreach (var (_, vv) in paare)
            {
                if (vv > 0) anzEng++;
                double take = Math.Min(vv, restKap);
                if (take < 0) take = 0;
                nutzen += take;
                restKap -= take;
                if (restKap < 0) restKap = 0;
            }

            profile.Add((pname, dep, nutzen, dep - nutzen, anzEng, brauchtOs, string.Join(", ", faecher)));
        }

        if (profile.Count == 0)
        {
            ws.Cell(zRow, 1).Value = $"(Noch keine Profile im Blatt '{InSheet}' eingetragen.)";
            ws.Cell(zRow, 1).Style.Font.Italic = true; zRow++;
            return;
        }

        profile = profile.OrderByDescending(p => p.Nutz).ToList();

        string lblZiel = diagGefunden ? "Gedeckte Überlast (WSt)" : "Gedeckter Bedarf (WSt)";
        TableHeader(ws, zRow, new[] { "Rang", "Profil", "Deputat", "Fachgruppen",
            lblZiel, "Rest-Kapazität", "Engpässe getroffen", "Oberstufe relevant" }, CSH);
        zRow++;

        int rangP = 0;
        foreach (var p in profile)
        {
            rangP++;
            XLColor rc = p.Nutz > 12 ? COk : p.Nutz > 5 ? CMit : p.Nutz > 0 ? CHoch : CKrit;
            ws.Cell(zRow, 1).Value = rangP;
            ws.Cell(zRow, 2).Value = p.Name;
            SetzeZahl(ws, zRow, 3, p.Dep, "0.0");
            ws.Cell(zRow, 4).Value = p.Fae;
            SetzeZahl(ws, zRow, 5, p.Nutz, "0.0");
            SetzeZahl(ws, zRow, 6, p.RestKap, "0.0");
            ws.Cell(zRow, 7).Value = p.Eng;
            ws.Cell(zRow, 8).Value = p.Os ? "ja" : "-";
            FarbeZeile(ws, zRow, 1, 8, rc);
            zRow++;
        }

        zRow++;
        ws.Cell(zRow, 1).Value = "'Gedeckte Überlast' = wie viel reale Überlast das Deputat rechnerisch abbauen kann (greedy).";
        ws.Cell(zRow, 1).Style.Font.Italic = true;
        ws.Cell(zRow, 1).Style.Font.FontColor = XLColor.FromArgb(128, 128, 128);
        zRow++;
    }

    private static IXLWorksheet EingabeSheetSicherstellen(IXLWorkbook wb)
    {
        if (wb.Worksheets.TryGetWorksheet(InSheet, out var vorhanden)) return vorhanden;

        var ws = wb.Worksheets.Add(InSheet);
        var titel = ws.Range(ws.Cell(1, 1), ws.Cell(1, 6)).Merge();
        ws.Cell(1, 1).Value = "KANDIDATENPROFILE FÜR NEUEINSTELLUNG";
        titel.Style.Font.Bold = true; titel.Style.Font.FontSize = 13;
        titel.Style.Fill.BackgroundColor = CH; titel.Style.Font.FontColor = CWeiss;

        var unter = ws.Range(ws.Cell(2, 1), ws.Cell(2, 6)).Merge();
        ws.Cell(2, 1).Value = "Pro Zeile ein Profil. Deputat in WSt. Fächer wie in der Lehrerliste (z. B. D, GE, M, E, SP).";
        unter.Style.Font.Italic = true; unter.Style.Font.FontColor = XLColor.FromArgb(128, 128, 128);

        string[] kopf = { "Profil-Name", "Deputat (WSt)", "Fachgruppe 1", "Fachgruppe 2", "Fachgruppe 3", "Fachgruppe 4" };
        for (int c = 0; c < kopf.Length; c++)
        {
            var cell = ws.Cell(3, c + 1);
            cell.Value = kopf[c];
            cell.Style.Font.Bold = true; cell.Style.Fill.BackgroundColor = CSH; cell.Style.Font.FontColor = CWeiss;
        }

        ws.Cell(4, 1).Value = "Beispiel (löschen)"; ws.Cell(4, 2).Value = 25;
        ws.Cell(4, 3).Value = "D"; ws.Cell(4, 4).Value = "GE";
        var beispiel = ws.Range(ws.Cell(4, 1), ws.Cell(4, 6));
        beispiel.Style.Font.Italic = true; beispiel.Style.Font.FontColor = XLColor.FromArgb(150, 150, 150);

        double[] breiten = { 22, 13, 10, 10, 10, 10 };
        for (int c = 1; c <= 6; c++) ws.Column(c).Width = breiten[c - 1];
        return ws;
    }

    // Sortiert nach Überlast (falls Diagnose vorhanden), sonst nach Bedarf; Gleichstand: weniger Qual zuerst.
    private void Sortiere(bool diagGefunden)
    {
        _grp.Sort((a, b) =>
        {
            double ka = diagGefunden ? a.Ueberl : a.Bed;
            double kb = diagGefunden ? b.Ueberl : b.Bed;
            int c = kb.CompareTo(ka);
            if (c != 0) return c;
            return a.Qual.CompareTo(b.Qual);
        });
        // Index-Tabelle nach dem Sortieren neu aufbauen (für Teil B).
        _idx.Clear();
        for (int i = 0; i < _grp.Count; i++) _idx[_grp[i].Name] = i;
    }

    // ---- kleine Blatt-Helfer ----
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
