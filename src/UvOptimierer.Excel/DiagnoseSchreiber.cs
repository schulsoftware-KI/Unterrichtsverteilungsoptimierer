using System.Globalization;
using ClosedXML.Excel;
using UvOptimierer.Core.Modell;
using UvOptimierer.Core.Solver;

namespace UvOptimierer.Excel;

/// <summary>
/// Baut die Auswertungs-/Diagnoseblätter nach dem Vorbild der VBA-Prozeduren
/// <c>BaueErgebnisSheets</c>, <c>DiagnoseSheet_Erstellen</c>,
/// <c>LehrerbelegungSheet_Erstellen</c> und <c>FachgruppenLehrerSheet_Erstellen</c>.
/// Erzeugt vier Blätter: „Diagnose1", „Diagnose2", „Lehrerbelegung", „FachgruppenLehrer".
/// </summary>
public sealed class DiagnoseSchreiber
{
    // Farben (RGB-Werte wie im VBA)
    private static readonly XLColor CTitel = XLColor.FromArgb(0, 70, 127);
    private static readonly XLColor CH     = XLColor.FromArgb(31, 73, 125);
    private static readonly XLColor CSH     = XLColor.FromArgb(68, 114, 196);
    private static readonly XLColor CSH2    = XLColor.FromArgb(142, 169, 219);
    private static readonly XLColor CSH3    = XLColor.FromArgb(0, 112, 192);
    private static readonly XLColor CSH4    = XLColor.FromArgb(0, 176, 240);
    private static readonly XLColor COk     = XLColor.FromArgb(198, 239, 206);
    private static readonly XLColor CWa     = XLColor.FromArgb(240, 200, 80);
    private static readonly XLColor CFe     = XLColor.FromArgb(220, 80, 80);
    private static readonly XLColor CRot    = XLColor.FromArgb(220, 80, 95);
    private static readonly XLColor CNe     = XLColor.FromArgb(242, 242, 242);
    private static readonly XLColor CWe     = XLColor.FromArgb(255, 255, 255);
    private static readonly XLColor CWeiss  = XLColor.White;
    private static readonly XLColor CRahmen = XLColor.FromArgb(200, 200, 200);
    private static readonly XLColor CDunkelrot = XLColor.FromArgb(156, 0, 6);
    private static readonly XLColor CGruen  = XLColor.FromArgb(0, 97, 0);

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static string F2(double v) => v.ToString("0.00", De);

    /// <summary>
    /// Baut die Diagnoseblätter. Ohne <paramref name="loes3"/>/<paramref name="loes4"/>:
    /// Diagnose1, Diagnose2, Lehrerbelegung (L1 vs. L2), FachgruppenLehrer.
    /// Mit L3/L4 zusätzlich Diagnose3, Diagnose4 und eine Lehrerbelegung über alle vier Lösungen.
    /// </summary>
    public void BaueDiagnoseSheets(IXLWorkbook wb, Kontext k, OptimierungsErgebnis erg,
                                   Loesung? loes3 = null, Loesung? loes4 = null, bool nurBeste = false)
    {
        string methode = erg.Verfahren;
        DiagnoseSheet(wb, k, erg.Loesung1, "Diagnose1", $"Loesung 1 [{methode}]");

        if (nurBeste)
        {
            // Nur die beste Lösung: kein Diagnose2–4, einspaltige Lehrerbelegung.
            foreach (var alt in new[] { "Diagnose2", "Diagnose3", "Diagnose4" })
                if (wb.Worksheets.TryGetWorksheet(alt, out var d)) d.Delete();
            LehrerbelegungSheet(wb, k, new[] { (erg.Loesung1, "LOESUNG 1") });
            FachgruppenLehrerSheet(wb, k);
            return;
        }

        DiagnoseSheet(wb, k, erg.Loesung2, "Diagnose2", $"Loesung 2 [{methode}]");

        if (loes3 != null && loes4 != null)
        {
            DiagnoseSheet(wb, k, loes3, "Diagnose3", "Loesung 3 [SA-Verbesserung]");
            DiagnoseSheet(wb, k, loes4, "Diagnose4", "Loesung 4 [SA-Verbesserung]");
            LehrerbelegungSheet(wb, k, new[]
            {
                (erg.Loesung1, "LOESUNG 1"), (erg.Loesung2, "LOESUNG 2"),
                (loes3, "LOESUNG 3"), (loes4, "LOESUNG 4")
            });
        }
        else
        {
            LehrerbelegungSheet(wb, k, new[]
            {
                (erg.Loesung1, "LOESUNG 1"), (erg.Loesung2, "LOESUNG 2")
            });
        }
        FachgruppenLehrerSheet(wb, k);
    }

    // ================================================================
    // Hilfsroutinen (SectionHeader / TableHeader / FarbeZeile / Sheet)
    // ================================================================
    private static IXLWorksheet SheetNeu(IXLWorkbook wb, string name)
    {
        if (wb.Worksheets.TryGetWorksheet(name, out var alt)) alt.Delete();
        return wb.Worksheets.Add(name);
    }

    private static void SectionHeader(IXLWorksheet ws, int row, string titel, XLColor farbe, int breite)
    {
        ws.Range(ws.Cell(row, 1), ws.Cell(row, breite)).Merge();
        var c = ws.Cell(row, 1);
        c.Value = titel;
        c.Style.Font.Bold = true;
        c.Style.Font.FontColor = CWeiss;
        c.Style.Font.FontSize = 12;
        c.Style.Fill.BackgroundColor = farbe;
        ws.Row(row).Height = 20;
    }

    private static void TableHeader(IXLWorksheet ws, int row, string[] headers, XLColor farbe)
    {
        for (int j = 0; j < headers.Length; j++)
        {
            var c = ws.Cell(row, j + 1);
            c.Value = headers[j];
            c.Style.Font.Bold = true;
            c.Style.Font.FontColor = CWeiss;
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

    private static string LehrerFaecherListe(Lehrer l)
    {
        string s = "";
        foreach (var f in l.Faecher)
        {
            if (string.IsNullOrEmpty(f)) continue;
            s = s == "" ? f : s + ", " + f;
        }
        return s;
    }

    // ================================================================
    // DIAGNOSE-SHEET (je Lösung)
    // ================================================================
    private static void DiagnoseSheet(IXLWorkbook wb, Kontext k, Loesung loes, string sheetName, string titel)
    {
        var ws = SheetNeu(wb, sheetName);
        double tol = k.Toleranz;
        int zRow = 1;

        SectionHeader(ws, zRow, titel + " - DIAGNOSE", CTitel, 8);
        zRow += 2;

        // ---- 1. LEHRERAUSLASTUNG ----
        SectionHeader(ws, zRow, "1. LEHRERAUSLASTUNG", CH, 8); zRow++;
        TableHeader(ws, zRow,
            new[] { "Lehrer", "Ist (Wert)", "Soll (Wert)", "Ist-Soll", "+/-Tol?", "Klassen", "Status" }, CSH);
        zRow++;

        double gSoll = 0, gIst = 0;
        for (int i = 0; i < k.NL; i++)
        {
            var lehrer = k.Lehrer[i];
            double ist = loes.IstWst[i];
            double soll = lehrer.SollWst;
            double diff = ist - soll;

            string sTxt; XLColor rc;
            if (ist == 0) { sTxt = "Keine Zuweisung"; rc = CWa; }
            else if (Math.Abs(diff) <= tol) { sTxt = "OK"; rc = COk; }
            else if (diff > tol) { sTxt = $"UEBERLASTET (+{F2(Math.Round(diff, 2))})"; rc = CFe; }
            else { sTxt = $"Unterbesetzt ({F2(Math.Round(diff, 2))})"; rc = CWa; }

            string kl = "";
            for (int e = 0; e < k.NE; e++)
                if (loes.Zuweisung[e] == lehrer.Name)
                {
                    string klasse = k.Eintraege[e].Klasse;
                    if (!kl.Contains(klasse))
                        kl = kl == "" ? klasse : kl + ", " + klasse;
                }

            ws.Cell(zRow, 1).Value = lehrer.Name;
            SetzeZahl(ws, zRow, 2, ist, "0.00");
            SetzeZahl(ws, zRow, 3, soll, "0.00");
            SetzeZahl(ws, zRow, 4, diff, "0.00");
            ws.Cell(zRow, 5).Value = Math.Abs(diff) <= tol ? "Ja" : "Nein";
            ws.Cell(zRow, 6).Value = kl;
            ws.Cell(zRow, 7).Value = sTxt;
            FarbeZeile(ws, zRow, 1, 7, rc);
            if (diff > tol)
            {
                ws.Cell(zRow, 4).Style.Font.Bold = true;
                ws.Cell(zRow, 4).Style.Font.FontColor = CDunkelrot;
            }
            gSoll += soll; gIst += ist;
            zRow++;
        }
        ws.Cell(zRow, 1).Value = "GESAMT"; ws.Cell(zRow, 1).Style.Font.Bold = true;
        SetzeZahl(ws, zRow, 2, gIst, "0.00"); ws.Cell(zRow, 2).Style.Font.Bold = true;
        SetzeZahl(ws, zRow, 3, gSoll, "0.00"); ws.Cell(zRow, 3).Style.Font.Bold = true;
        SetzeZahl(ws, zRow, 4, gIst - gSoll, "0.00"); ws.Cell(zRow, 4).Style.Font.Bold = true;
        FarbeZeile(ws, zRow, 1, 7, CNe);
        zRow += 2;

        // ---- 2. NICHT BELEGTE STUNDEN ----
        int nb = 0;
        for (int e = 0; e < k.NE; e++)
            if (!Regeln.IstBelegt(loes.Zuweisung[e])) nb++;
        SectionHeader(ws, zRow, $"2. NICHT BELEGTE STUNDEN ({nb})", CH, 4); zRow++;
        if (nb == 0)
        {
            var c = ws.Cell(zRow, 1);
            c.Value = "Alle Stunden belegt.";
            c.Style.Font.Bold = true; c.Style.Font.FontColor = CGruen;
            FarbeZeile(ws, zRow, 1, 4, COk); zRow += 2;
        }
        else
        {
            TableHeader(ws, zRow, new[] { "Klasse", "Fach", "WSt", "Grund" }, CSH); zRow++;
            for (int e = 0; e < k.NE; e++)
            {
                if (Regeln.IstBelegt(loes.Zuweisung[e])) continue;
                var ein = k.Eintraege[e];
                ws.Cell(zRow, 1).Value = ein.Klasse;
                ws.Cell(zRow, 2).Value = ein.Fach;
                ws.Cell(zRow, 3).Value = ein.Wst;
                ws.Cell(zRow, 4).Value = "Kein Lehrer zugewiesen (kein qualifizierter Lehrer oder Kapazitaet erschoepft)";
                FarbeZeile(ws, zRow, 1, 4, CFe); zRow++;
            }
            zRow++;
        }

        // ---- 3. WUNSCH-ANALYSE ----
        SectionHeader(ws, zRow, "3. WUNSCH-ANALYSE", CH, 6); zRow++;
        TableHeader(ws, zRow,
            new[] { "Lehrer", "Art", "Klasse", "Prio", "Erfuellt?", "Bemerkung" }, CSH);
        zRow++;
        bool hatW = false;
        for (int w = 0; w < k.NW; w++)
        {
            var wu = k.Wuensche[w];
            if (wu.WunschKlasse != "" && wu.WunschPrio > 0)
            {
                hatW = true;
                bool erf = false;
                for (int e = 0; e < k.NE; e++)
                    if (k.Eintraege[e].Klasse == wu.WunschKlasse && loes.Zuweisung[e] == wu.LehrerName
                        && (wu.WunschFach == "" || string.Equals(k.Eintraege[e].Fach, wu.WunschFach, StringComparison.OrdinalIgnoreCase)))
                    { erf = true; break; }
                ws.Cell(zRow, 1).Value = wu.LehrerName;
                ws.Cell(zRow, 2).Value = "Wunsch";
                ws.Cell(zRow, 3).Value = wu.WunschFach != "" ? $"{wu.WunschKlasse} / {wu.WunschFach}" : wu.WunschKlasse;
                ws.Cell(zRow, 4).Value = wu.WunschPrio;
                ws.Cell(zRow, 5).Value = erf ? "Ja" : "Nein";
                if (wu.WunschPrio == 3 && !erf)
                {
                    ws.Cell(zRow, 6).Value = "!!! PFLICHT-WUNSCH NICHT ERFUELLT";
                    FarbeZeile(ws, zRow, 1, 6, CFe);
                }
                else if (!erf)
                {
                    ws.Cell(zRow, 6).Value = $"Nicht erfuellt (Prio {wu.WunschPrio})";
                    FarbeZeile(ws, zRow, 1, 6, CWa);
                }
                else
                {
                    ws.Cell(zRow, 6).Value = "Erfuellt";
                    FarbeZeile(ws, zRow, 1, 6, COk);
                }
                zRow++;
            }
            if (wu.AntiKlasse != "" && wu.AntiPrio > 0)
            {
                hatW = true;
                bool verl = false;
                for (int e = 0; e < k.NE; e++)
                    if (k.Eintraege[e].Klasse == wu.AntiKlasse && loes.Zuweisung[e] == wu.LehrerName
                        && (wu.AntiFach == "" || string.Equals(k.Eintraege[e].Fach, wu.AntiFach, StringComparison.OrdinalIgnoreCase)))
                    { verl = true; break; }
                ws.Cell(zRow, 1).Value = wu.LehrerName;
                ws.Cell(zRow, 2).Value = "Anti-Wunsch";
                ws.Cell(zRow, 3).Value = wu.AntiFach != "" ? $"{wu.AntiKlasse} / {wu.AntiFach}" : wu.AntiKlasse;
                ws.Cell(zRow, 4).Value = wu.AntiPrio;
                ws.Cell(zRow, 5).Value = !verl ? "Eingehalten" : "VERLETZT";
                if (wu.AntiPrio == 3 && verl)
                {
                    ws.Cell(zRow, 6).Value = "!!! PFLICHT-ANTI-WUNSCH VERLETZT";
                    FarbeZeile(ws, zRow, 1, 6, CFe);
                }
                else if (verl)
                {
                    ws.Cell(zRow, 6).Value = $"Verletzt (Prio {wu.AntiPrio})";
                    FarbeZeile(ws, zRow, 1, 6, CWa);
                }
                else
                {
                    ws.Cell(zRow, 6).Value = "Eingehalten";
                    FarbeZeile(ws, zRow, 1, 6, COk);
                }
                zRow++;
            }
        }
        if (!hatW)
        {
            ws.Cell(zRow, 1).Value = "Keine Wuensche.";
            ws.Cell(zRow, 1).Style.Font.Italic = true;
            zRow++;
        }
        zRow++;

        ws.Column(1).Width = 16; ws.Column(2).Width = 12; ws.Column(3).Width = 16;
        ws.Column(4).Width = 10; ws.Column(5).Width = 12; ws.Column(6).Width = 28;
        ws.Column(7).Width = 28;

        // ---- 4. UEBERLAST-VERTEILUNG NACH FACHGRUPPE ----
        zRow += 2;
        SectionHeader(ws, zRow, "4. UEBERLAST-VERTEILUNG NACH FACHGRUPPE", CH, 8); zRow++;
        ws.Cell(zRow, 1).Value = "Je Fachgruppe: absolute Ueberlast-WSt aller betroffenen Lehrer + 50%-Anteil";
        ws.Cell(zRow, 1).Style.Font.Italic = true; zRow++;
        TableHeader(ws, zRow,
            new[] { "Fachgruppe", "Ueberlast gesamt (WSt)", "50%-Anteil (WSt)", "Betroffene Lehrer" }, CSH);
        zRow++;

        var fach = new List<string>();
        var wstGes = new Dictionary<string, double>();   // gesamt
        var wst50 = new Dictionary<string, double>();     // 50%-Anteil
        var lehrerAnz = new Dictionary<string, int>();

        for (int oli = 0; oli < k.NL; oli++)
        {
            var L = k.Lehrer[oli];
            if (L.SollWst <= 0) continue;
            double ueberl = loes.IstWst[oli] - L.SollWst;
            if (ueberl <= 0) continue;

            // Nur tatsächlich unterrichtete Fachgruppen (aus den Einträgen)
            var grpKeys = new List<string>();
            for (int e = 0; e < k.NE; e++)
            {
                if (loes.Zuweisung[e] != L.Name) continue;
                string key = Suchhilfen.FachEngpassKey(k, k.Eintraege[e].Fach);
                if (key == "") continue;
                if (!grpKeys.Any(g => string.Equals(g, key, StringComparison.OrdinalIgnoreCase)))
                    grpKeys.Add(key);
            }
            if (grpKeys.Count == 0) continue;

            double anteil = ueberl / grpKeys.Count;
            double halbeA = anteil / 2;
            foreach (var g in grpKeys)
            {
                string kanon = fach.FirstOrDefault(f => string.Equals(f, g, StringComparison.OrdinalIgnoreCase)) ?? g;
                if (!fach.Contains(kanon)) fach.Add(kanon);
                wst50[kanon] = wst50.GetValueOrDefault(kanon) + halbeA;
                wstGes[kanon] = wstGes.GetValueOrDefault(kanon) + anteil;
                lehrerAnz[kanon] = lehrerAnz.GetValueOrDefault(kanon) + 1;
            }
        }

        fach.Sort((a, b) => wstGes[b].CompareTo(wstGes[a]));
        foreach (var f in fach)
        {
            double ges = wstGes[f];
            if (ges <= 0) continue;
            ws.Cell(zRow, 1).Value = f;
            SetzeZahl(ws, zRow, 2, ges, "0.0");
            SetzeZahl(ws, zRow, 3, wst50[f], "0.0");
            ws.Cell(zRow, 4).Value = lehrerAnz[f];
            XLColor col = ges > 20 ? CFe
                        : ges > 10 ? XLColor.FromArgb(255, 150, 80)
                        : ges > 4 ? CWa
                        : CNe;
            FarbeZeile(ws, zRow, 1, 4, col);
            zRow++;
        }
    }

    // ================================================================
    // LEHRERBELEGUNGS-SHEET (2 oder 4 Lösungen nebeneinander)
    // ================================================================
    private static void LehrerbelegungSheet(IXLWorkbook wb, Kontext k, (Loesung loes, string label)[] loesungen)
    {
        var ws = SheetNeu(wb, "Lehrerbelegung");
        double tol = k.Toleranz;
        int n = loesungen.Length;          // 2 oder 4
        int totalCols = n * 6;
        int zRow = 1;
        string[] hdrsNorm = { "Klasse", "Fach", "WSt", "Fix?", "KL?", "" };
        string[] hdrsFix = { "Klasse", "Fach", "WSt", "Fix?", "KL?", "Fixiert" };
        XLColor[] bandFarbe = { CSH, CSH2, CSH3, CSH4 };

        // Ein Block zeigt die „Fixiert"-Spalte, wenn es nur 2 Lösungen gibt oder es der letzte Block ist.
        bool NutztFixSpalte(int b) => n == 2 || b == n - 1;

        string titel = n == 1 ? "LEHRERBELEGUNG - Loesung 1"
                     : n == 4 ? "LEHRERBELEGUNG - Loesung 1 vs. 2 vs. 3 vs. 4"
                     : "LEHRERBELEGUNG - Loesung 1 vs. Loesung 2";
        SectionHeader(ws, zRow, titel, CTitel, totalCols);
        zRow += 2;

        for (int i = 0; i < k.NL; i++)
        {
            var lehr = k.Lehrer[i];

            // Lehrer-Kopfzeile
            ws.Range(ws.Cell(zRow, 1), ws.Cell(zRow, totalCols)).Merge();
            var kopf = ws.Cell(zRow, 1);
            kopf.Value = $"{lehr.Name}   Soll: {F2(lehr.SollWst)} WSt   KL: " +
                         $"{(lehr.KlassenleitungKlasse != "" ? lehr.KlassenleitungKlasse : "-")}   " +
                         $"Faecher: {LehrerFaecherListe(lehr)}";
            kopf.Style.Font.Bold = true; kopf.Style.Font.FontSize = 11;
            kopf.Style.Fill.BackgroundColor = CH; kopf.Style.Font.FontColor = CWeiss;
            ws.Row(zRow).Height = 18; zRow++;

            // Lösung-Bänder
            for (int b = 0; b < n; b++)
            {
                int c0 = b * 6 + 1;
                ws.Cell(zRow, c0).Value = loesungen[b].label;
                var band = ws.Range(ws.Cell(zRow, c0), ws.Cell(zRow, c0 + 5));
                band.Style.Fill.BackgroundColor = bandFarbe[b];
                band.Style.Font.Bold = true; band.Style.Font.FontColor = CWeiss;
                band.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            zRow++;

            // Spalten-Header
            for (int b = 0; b < n; b++)
            {
                int c0 = b * 6 + 1;
                var hdr = NutztFixSpalte(b) ? hdrsFix : hdrsNorm;
                for (int j = 0; j < 6; j++)
                {
                    var c = ws.Cell(zRow, c0 + j);
                    c.Value = hdr[j]; c.Style.Fill.BackgroundColor = bandFarbe[b]; c.Style.Font.FontColor = CWeiss;
                }
            }
            zRow++;

            // Einträge je Lösung sammeln
            var listen = new List<int>[n];
            int maxN = 0;
            for (int b = 0; b < n; b++)
            {
                listen[b] = new List<int>();
                for (int e = 0; e < k.NE; e++)
                    if (loesungen[b].loes.Zuweisung[e] == lehr.Name) listen[b].Add(e);
                maxN = Math.Max(maxN, listen[b].Count);
            }

            // Datenzeilen
            for (int rowIdx = 0; rowIdx < maxN; rowIdx++)
            {
                XLColor rc = (rowIdx % 2 == 0) ? CWe : CNe;
                for (int b = 0; b < n; b++)
                {
                    int c0 = b * 6 + 1;
                    if (rowIdx >= listen[b].Count) continue;
                    var ein = k.Eintraege[listen[b][rowIdx]];
                    bool kl = lehr.KlassenleitungKlasse == ein.Klasse && lehr.KlassenleitungKlasse != "";
                    bool fix = Regeln.IstFixiert(ein.UrsprungsLehrer);
                    ws.Cell(zRow, c0 + 0).Value = ein.Klasse;
                    ws.Cell(zRow, c0 + 1).Value = ein.Fach;
                    ws.Cell(zRow, c0 + 2).Value = ein.Wst;
                    ws.Cell(zRow, c0 + 3).Value = fix ? "Ja" : "-";
                    ws.Cell(zRow, c0 + 4).Value = kl ? "KL" : "";
                    if (fix && NutztFixSpalte(b)) ws.Cell(zRow, c0 + 5).Value = "Fixiert";
                    FarbeZeile(ws, zRow, c0, c0 + 5, rc);
                    if (kl) ws.Cell(zRow, c0 + 4).Style.Font.Bold = true;
                }
                zRow++;
            }
            if (maxN == 0)
            {
                for (int b = 0; b < n; b++)
                {
                    var c = ws.Cell(zRow, b * 6 + 1);
                    c.Value = "(keine Zuweisung)"; c.Style.Font.Italic = true;
                }
                FarbeZeile(ws, zRow, 1, totalCols, CWa); zRow++;
            }

            // Summen-Zeile
            for (int b = 0; b < n; b++)
            {
                int c0 = b * 6 + 1;
                double ist = loesungen[b].loes.IstWst[i];
                double diff = ist - lehr.SollWst;
                ws.Cell(zRow, c0).Value = "Summe Werte, Soll-Anr, Diff"; ws.Cell(zRow, c0).Style.Font.Bold = true;
                SetzeZahl(ws, zRow, c0 + 2, ist, "0.00"); ws.Cell(zRow, c0 + 2).Style.Font.Bold = true;
                SetzeZahl(ws, zRow, c0 + 3, lehr.SollWst, "0.00");
                SetzeZahl(ws, zRow, c0 + 4, diff, "0.00"); ws.Cell(zRow, c0 + 4).Style.Font.Bold = true;
                XLColor cs = diff > tol ? CRot : diff < -tol ? CWa : COk;
                FarbeZeile(ws, zRow, c0, c0 + 5, cs);
                ws.Cell(zRow, c0).Style.Font.Bold = true;
            }
            zRow += 2;
        }

        // Spaltenbreiten (Muster je 6er-Block)
        int[] breiten = { 10, 14, 6, 7, 6, 8 };
        for (int col = 1; col <= totalCols; col++)
            ws.Column(col).Width = breiten[(col - 1) % 6];
    }

    // ================================================================
    // FACHGRUPPEN-JE-LEHRER-SHEET
    // ================================================================
    private static void FachgruppenLehrerSheet(IXLWorkbook wb, Kontext k)
    {
        var ws = SheetNeu(wb, "FachgruppenLehrer");
        int zRow = 1;

        SectionHeader(ws, zRow, "FACHGRUPPEN JE LEHRER", CH, 5); zRow += 2;
        var kopf = new[] { "Lehrer", "Soll-WSt", "Fachgruppen (Engpass-relevant)", "Alle eingetragenen Faecher", "Anzahl Gruppen" };
        for (int j = 0; j < kopf.Length; j++)
        {
            var c = ws.Cell(zRow, j + 1);
            c.Value = kopf[j]; c.Style.Fill.BackgroundColor = CSH;
            c.Style.Font.Bold = true; c.Style.Font.FontColor = CWeiss;
        }
        zRow++;

        for (int i = 0; i < k.NL; i++)
        {
            var l = k.Lehrer[i];
            var grpKeys = new List<string>();
            string allF = "";
            foreach (var raw in l.Faecher)
            {
                string fN = (raw ?? "").Trim();
                if (fN == "") continue;
                allF = allF == "" ? fN : allF + ", " + fN;
                string fGrp = Suchhilfen.FachEngpassKey(k, fN);
                if (fGrp == "") continue;
                if (!grpKeys.Any(g => string.Equals(g, fGrp, StringComparison.OrdinalIgnoreCase)))
                    grpKeys.Add(fGrp);
            }
            string grpLst = string.Join(", ", grpKeys);

            XLColor rc = (i % 2 == 1) ? CNe : CWe;
            ws.Cell(zRow, 1).Value = l.Name;
            SetzeZahl(ws, zRow, 2, l.SollWst, "0.#");
            ws.Cell(zRow, 3).Value = grpLst;
            ws.Cell(zRow, 4).Value = allF;
            ws.Cell(zRow, 5).Value = grpKeys.Count;
            if (allF != "" && grpKeys.Count == 0)
                FarbeZeile(ws, zRow, 1, 5, CRot);
            else
                FarbeZeile(ws, zRow, 1, 5, rc);
            zRow++;
        }

        ws.Column(1).Width = 20; ws.Column(2).Width = 10; ws.Column(3).Width = 40;
        ws.Column(4).Width = 50; ws.Column(5).Width = 14;
    }

    private static void SetzeZahl(IXLWorksheet ws, int row, int col, double wert, string format)
    {
        var c = ws.Cell(row, col);
        c.Value = wert;
        c.Style.NumberFormat.Format = format;
    }
}
