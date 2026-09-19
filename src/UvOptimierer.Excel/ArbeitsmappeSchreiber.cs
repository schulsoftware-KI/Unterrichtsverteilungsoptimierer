using ClosedXML.Excel;
using UvOptimierer.Core.Modell;
using UvOptimierer.Core.Solver;

namespace UvOptimierer.Excel;

/// <summary>
/// Schreibt die beiden gefundenen Lösungen zurück in die Arbeitsmappe.
/// Portierung von <c>SchreibeLoesungen</c>: Ergebnis-Lehrer landen im Blatt „Klassen"
/// in Spalte D (Lösung 1) und E (Lösung 2), inklusive formatierter Kopfzeile.
/// </summary>
public sealed class ArbeitsmappeSchreiber
{
    /// <summary>
    /// Öffnet <paramref name="quellPfad"/>, trägt die Lösungen ein und speichert unter
    /// <paramref name="zielPfad"/> (kann identisch mit der Quelle sein).
    /// </summary>
    public void SchreibeUndSpeichere(string quellPfad, string zielPfad, Kontext k, OptimierungsErgebnis erg,
                                     bool verbessern = false, bool nurBeste = false)
    {
        using var wb = new XLWorkbook(quellPfad);
        Schreibe(wb, k, erg, nurBeste);
        SchreibeParameter(wb, k.P);

        var diagnose = new DiagnoseSchreiber();
        if (verbessern && !nurBeste)
        {
            var loes3 = (string[])erg.Loesung1.Zuweisung.Clone();
            var loes4 = (string[])erg.Loesung2.Zuweisung.Clone();
            Verbesserer.VerbessereSA(k, loes3, k.P.Zeitlimit);
            Verbesserer.VerbessereSA(k, loes4, k.P.Zeitlimit);
            var l3 = Loesung.Aus(k, loes3, true);
            var l4 = Loesung.Aus(k, loes4, true);
            SchreibeVerbesserung(wb, k, l3, l4);
            diagnose.BaueDiagnoseSheets(wb, k, erg, l3, l4);
        }
        else
        {
            diagnose.BaueDiagnoseSheets(wb, k, erg, null, null, nurBeste);
        }

        Inhaltsverzeichnis.Erzeuge(wb);
        BlattReihenfolge.Ordne(wb);
        if (string.Equals(quellPfad, zielPfad, StringComparison.OrdinalIgnoreCase))
            wb.Save();
        else
            wb.SaveAs(zielPfad);
    }

    /// <summary>Trägt die Lösung(en) ein. Ergebnisspalten werden über die Kopfzeile gefunden
    /// („Lehrer (L1)"/„(L2)"), sonst über die feste Fallback-Spalte.</summary>
    public void Schreibe(IXLWorkbook wb, Kontext k, OptimierungsErgebnis erg, bool nurBeste = false)
    {
        if (!wb.Worksheets.TryGetWorksheet(SpaltenLayout.BlattKlassen, out var ws))
            throw new InvalidOperationException("Blatt „Klassen\" fehlt – Lösungen können nicht geschrieben werden.");

        var blau = XLColor.FromArgb(68, 114, 196);
        int cL1 = SpalteOderFest(ws, 1, SpaltenLayout.KlasseL1, "Lehrer (L1)", "Lehrer L1", "L1");
        KopfSetzen(ws, cL1, "Lehrer (L1)", blau);

        int cL2 = 0;
        if (!nurBeste)
        {
            cL2 = SpalteOderFest(ws, 1, SpaltenLayout.KlasseL2, "Lehrer (L2)", "Lehrer L2", "L2");
            KopfSetzen(ws, cL2, "Lehrer (L2)", blau);
        }

        for (int e = 0; e < k.NE; e++)
        {
            int zeile = k.Eintraege[e].Zeile;
            ws.Cell(zeile, cL1).Value = erg.Loesung1.Zuweisung[e];
            if (!nurBeste)
                ws.Cell(zeile, cL2).Value = erg.Loesung2.Zuweisung[e];
        }
    }

    /// <summary>Schreibt die verwendeten Parameter zurück. Zeile wird über das Label in Spalte A
    /// gefunden (sonst am Ende angehängt), die Wert-Spalte über die Kopfzeile „Wert".</summary>
    private static void SchreibeParameter(IXLWorkbook wb, ScoreParameter p)
    {
        if (!wb.Worksheets.TryGetWorksheet(SpaltenLayout.BlattParameter, out var ws))
            ws = wb.Worksheets.Add(SpaltenLayout.BlattParameter);

        int wert = HeaderSpalte(ws, 1, "Wert");
        if (wert == 0) wert = HeaderSpalte(ws, 2, "Wert");
        if (wert == 0) wert = SpaltenLayout.ParamSpalte;
        int anhang = (ws.Column(1).LastCellUsed()?.Address.RowNumber ?? 1) + 1;

        void Setze(string labelNorm, string anzeige, double w)
        {
            int z = ParamZeile(ws, labelNorm);
            if (z == 0) { z = anhang++; ws.Cell(z, 1).Value = anzeige; }
            ws.Cell(z, wert).Value = w;
        }
        void SetzeText(string labelNorm, string anzeige, string w)
        {
            int z = ParamZeile(ws, labelNorm);
            if (z == 0) { z = anhang++; ws.Cell(z, 1).Value = anzeige; }
            ws.Cell(z, wert).Value = w;
        }

        Setze("toleranz", "Toleranz (Soll-Überschreitung)", p.Toleranz);
        Setze("klassenleitung", "Klassenleitung", p.ScoreKl);
        Setze("wunschprio3", "Wunsch Prio 3", p.ScoreW3);
        Setze("wunschprio2", "Wunsch Prio 2", p.ScoreW2);
        Setze("wunschprio1", "Wunsch Prio 1", p.ScoreW1);
        Setze("antiwunschprio2", "Anti-Wunsch Prio 2", p.ScoreA2);
        Setze("antiwunschprio1", "Anti-Wunsch Prio 1", p.ScoreA1);
        Setze("kontinuitaet", "Kontinuität", p.ScoreKont);
        Setze("faktorfreiestunden", "Faktor freie Stunden", p.ScoreFreiF);
        Setze("faktorueberlast", "Faktor Überlastung", p.ScoreUeberF);
        Setze("faktorunterbesetzung", "Faktor Unterbesetzung", p.ScoreUnterF);
        Setze("zeitlimit", "Zeitlimit (Sek.)", p.Zeitlimit);
        SetzeText("schutzfachgruppe1", "Schutzfachgruppe 1", p.SchutzFg1);
        SetzeText("schutzfachgruppe2", "Schutzfachgruppe 2", p.SchutzFg2);
        Setze("schutzmalus", "Schutz-Malus-Faktor", p.SchutzMalus);
        Setze("saiterationenloesung", "SA-Iterationen Loesungssuche", p.SaIter);
        Setze("saiterationensimulation", "SA-Iterationen Simulation", p.SimIter);
        Setze("sawiederholungensimulation", "SA-Wiederholungen Simulation", p.SimNrep);
        Setze("verbesserungsimulation", "Verbesserung Simulation (Sek)", p.SimVerbSek);
        Setze("cpsatueberlast", "CP-SAT Überlast-Strafe (Score/Std)", p.CpUeberGewicht);
        Setze("cpsatunterlast", "CP-SAT Unterlast-Strafe (Score/Std)", p.CpUnterGewicht);
        Setze("cpsatunbesetzt", "CP-SAT Unbesetzt-Strafe (Score/Eintrag)", p.CpUnbesetztGewicht);
        Setze("hartetoleranz", "Harte Toleranzgrenzen (0/1)", p.HarteGrenzen ? 1 : 0);
        Setze("globalemax", "Globale Max-Überlast (WSt)", p.GlobalMaxUeberlast);
    }

    /// <summary>Schreibt die nachverbesserten Lösungen L3/L4; Spalten über die Kopfzeile gefunden.</summary>
    private static void SchreibeVerbesserung(IXLWorkbook wb, Kontext k, Loesung l3, Loesung l4)
    {
        if (!wb.Worksheets.TryGetWorksheet(SpaltenLayout.BlattKlassen, out var ws))
            return;

        var hell = XLColor.FromArgb(142, 169, 219);
        int cL3 = SpalteOderFest(ws, 1, SpaltenLayout.KlasseL3, "Lehrer (L3)", "Lehrer L3", "L3");
        int cL4 = SpalteOderFest(ws, 1, SpaltenLayout.KlasseL4, "Lehrer (L4)", "Lehrer L4", "L4");
        KopfSetzen(ws, cL3, "Lehrer (L3)", hell);
        KopfSetzen(ws, cL4, "Lehrer (L4)", hell);

        for (int e = 0; e < k.NE; e++)
        {
            int zeile = k.Eintraege[e].Zeile;
            ws.Cell(zeile, cL3).Value = l3.Zuweisung[e];
            ws.Cell(zeile, cL4).Value = l4.Zuweisung[e];
        }
    }

    // ---- Kopfzeilen-/Label-Hilfen (analog ArbeitsmappeLeser) ----
    private static void KopfSetzen(IXLWorksheet ws, int spalte, string text, XLColor farbe)
    {
        var c = ws.Cell(1, spalte);
        c.Value = text;
        c.Style.Font.Bold = true;
        c.Style.Fill.BackgroundColor = farbe;
        c.Style.Font.FontColor = XLColor.White;
    }

    private static string Norm(string s) => s.Trim().ToLowerInvariant()
        .Replace("ü", "ue").Replace("ö", "oe").Replace("ä", "ae").Replace("ß", "ss")
        .Replace(" ", "").Replace("-", "").Replace("_", "").Replace(".", "")
        .Replace("(", "").Replace(")", "").Replace("/", "");

    private static int HeaderSpalte(IXLWorksheet ws, int kopfZeile, params string[] namen)
    {
        int last = ws.Row(kopfZeile).LastCellUsed()?.Address.ColumnNumber ?? 0;
        var ziele = namen.Select(Norm).ToArray();
        for (int c = 1; c <= last; c++)
        {
            string h = Norm(ws.Cell(kopfZeile, c).GetString());
            if (h != "" && ziele.Contains(h)) return c;
        }
        return 0;
    }

    private static int SpalteOderFest(IXLWorksheet ws, int kopfZeile, int fallback, params string[] namen)
    {
        int c = HeaderSpalte(ws, kopfZeile, namen);
        return c > 0 ? c : fallback;
    }

    private static int ParamZeile(IXLWorksheet ws, string labelNorm)
    {
        int letzte = ws.Column(1).LastCellUsed()?.Address.RowNumber ?? 1;
        for (int r = 1; r <= letzte; r++)
            if (Norm(ws.Cell(r, 1).GetString()).Contains(labelNorm)) return r;
        return 0;
    }
}
