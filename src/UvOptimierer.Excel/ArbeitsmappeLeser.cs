using System.Text.RegularExpressions;
using ClosedXML.Excel;
using UvOptimierer.Core.Fachlogik;
using UvOptimierer.Core.Modell;

namespace UvOptimierer.Excel;

/// <summary>
/// Liest ein UV-Arbeitsblatt (.xlsx/.xlsm) in ein <see cref="Kontext"/>-Objekt ein.
/// Spalten werden über die <b>Kopfzeile</b> (bzw. bei Parametern über das Label in Spalte A)
/// gefunden; feste Spaltenpositionen aus <see cref="SpaltenLayout"/> dienen nur noch als
/// Rückfall, falls eine Überschrift fehlt. Dadurch dürfen Spalten verschoben werden.
/// </summary>
public sealed class ArbeitsmappeLeser
{
    public Kontext Lies(string pfad) => Lies(pfad, null);

    public Kontext Lies(string pfad, ScoreParameter? parameterUeberschreibung)
    {
        using var wb = new XLWorkbook(pfad);
        return Lies(wb, parameterUeberschreibung);
    }

    public Kontext Lies(IXLWorkbook wb) => Lies(wb, null);

    public Kontext Lies(IXLWorkbook wb, ScoreParameter? parameterUeberschreibung)
    {
        var p = parameterUeberschreibung ?? LiesParameter(wb);
        var fachgruppen = LiesFachgruppen(wb);
        var lehrer = LiesLehrer(wb, p.GlobalMaxUeberlast);
        var wuensche = LiesWuensche(wb);
        var eintraege = LiesEintraege(wb);

        return new Kontext(lehrer, eintraege, wuensche, p) { Fachgruppen = fachgruppen };
    }

    public ScoreParameter LiesParameter(string pfad)
    {
        using var wb = new XLWorkbook(pfad);
        return LiesParameter(wb);
    }

    // ===============================================================
    // Lehrerliste
    // ===============================================================
    private static List<Lehrer> LiesLehrer(IXLWorkbook wb, double globalMaxUeberlast)
    {
        var ws = Blatt(wb, SpaltenLayout.BlattLehrerliste);
        const int kopf = 1;

        int cName = SpalteOderFest(ws, kopf, SpaltenLayout.LehrerName, "Name");
        int cKl = SpalteOderFest(ws, kopf, SpaltenLayout.LehrerKlassenleitung, "Klassenleitung", "Klassenleiter", "KL");
        int cSoll = SpalteOderFest(ws, kopf, SpaltenLayout.LehrerSollWst,
            "Soll-Anrechnungen", "Soll-Anrechnung", "Soll Anrechnung", "SollAnrechnungen", "Soll-Anr", "Soll");
        int cUeber = SpalteContains(ws, kopf, "ueberlast");   // Max-Überlast (optional)
        int cUnter = SpalteContains(ws, kopf, "unterlast");  // Max-Unterlast (optional)

        var fachSpalten = FachSpalten(ws, kopf, SpaltenLayout.LehrerFachAnzahl);
        if (fachSpalten.Count == 0)
            for (int i = 0; i < SpaltenLayout.LehrerFachAnzahl; i++)
                fachSpalten.Add(SpaltenLayout.LehrerFach1 + i);

        var liste = new List<Lehrer>();
        int letzte = LetzteZeile(ws, cName);
        for (int r = 2; r <= letzte; r++)
        {
            string name = S(ws.Cell(r, cName));
            if (name == "" || name.ToUpperInvariant() == "SUMME") continue;

            double? maxUeberlast = null;
            if (cUeber > 0 && Num(ws.Cell(r, cUeber)) is double vu && vu >= 0) maxUeberlast = vu;
            if (maxUeberlast == null && globalMaxUeberlast > 0) maxUeberlast = globalMaxUeberlast;

            double? maxUnterlast = null;
            if (cUnter > 0 && Num(ws.Cell(r, cUnter)) is double vn && vn >= 0) maxUnterlast = vn;

            var l = new Lehrer
            {
                Name = name,
                KlassenleitungKlasse = cKl > 0 ? S(ws.Cell(r, cKl)) : "",
                SollWst = Num(ws.Cell(r, cSoll)) ?? 0,
                MaxUeberlast = maxUeberlast,
                MaxUnterlast = maxUnterlast
            };
            for (int fi = 0; fi < fachSpalten.Count && fi < SpaltenLayout.LehrerFachAnzahl; fi++)
            {
                string fn = NormLeerzeichen(S(ws.Cell(r, fachSpalten[fi])));
                l.Faecher[fi] = fn;
                l.OberstufeOk[fi] = FachLogik.IstOberstufenFach(fn);
            }
            liste.Add(l);
        }
        return liste;
    }

    /// <summary>Findet alle Fach-Spalten anhand von Überschriften „Fach 1"…„Fach N" (Reihenfolge nach Nummer).</summary>
    private static List<int> FachSpalten(IXLWorksheet ws, int kopfZeile, int maxAnzahl)
    {
        int last = LetzteSpalte(ws, kopfZeile);
        var treffer = new List<(int nr, int col)>();
        for (int c = 1; c <= last; c++)
        {
            var m = Regex.Match(Norm(S(ws.Cell(kopfZeile, c))), @"^fach(\d+)$");
            if (m.Success) treffer.Add((int.Parse(m.Groups[1].Value), c));
        }
        return treffer.OrderBy(t => t.nr).Take(maxAnzahl).Select(t => t.col).ToList();
    }

    // ===============================================================
    // Lehrerwünsche
    // ===============================================================
    private static List<Wunsch> LiesWuensche(IXLWorkbook wb)
    {
        var ws = BlattOderNull(wb, SpaltenLayout.BlattWuensche) ?? BlattOderNull(wb, "Lehrerwuensche");
        var liste = new List<Wunsch>();
        if (ws is null) return liste;

        const int kopf = 1;
        int cLehrer = SpalteOderFest(ws, kopf, SpaltenLayout.WunschLehrer, "Lehrer");
        int cFach = Spalte(ws, kopf, "Fach", "Wunschfach");   // optional; 0 = keine Fach-Spalte
        int cAntiKl = SpalteOderFest(ws, kopf, SpaltenLayout.WunschAntiKlasse,
            "nicht in Klasse", "Anti-Klasse", "AntiKlasse", "nicht Klasse");
        int cAntiFach = Spalte(ws, kopf, "AntiFach", "Anti-Fach", "nicht Fach", "nicht in Fach");   // optional
        int cWunschKl = Spalte(ws, kopf, "Klasse");
        if (cWunschKl == 0 || cWunschKl == cAntiKl) cWunschKl = SpaltenLayout.WunschKlasse;

        var prios = AlleSpalten(ws, kopf, "Prio", "Prioritaet");
        int cWunschPrio = prios.FirstOrDefault(c => c > cWunschKl && (cAntiKl == 0 || c < cAntiKl));
        if (cWunschPrio == 0) cWunschPrio = prios.FirstOrDefault();
        if (cWunschPrio == 0) cWunschPrio = SpaltenLayout.WunschPrio;
        int cAntiPrio = prios.FirstOrDefault(c => cAntiKl > 0 && c > cAntiKl);
        if (cAntiPrio == 0) cAntiPrio = SpaltenLayout.WunschAntiPrio;

        int letzte = LetzteZeile(ws, cLehrer);
        for (int r = 2; r <= letzte; r++)
        {
            string lehrer = S(ws.Cell(r, cLehrer));
            if (lehrer == "") continue;
            liste.Add(new Wunsch
            {
                LehrerName = lehrer,
                WunschKlasse = cWunschKl > 0 ? S(ws.Cell(r, cWunschKl)) : "",
                WunschFach = cFach > 0 ? NormLeerzeichen(S(ws.Cell(r, cFach))) : "",
                WunschPrio = (int)(Num(ws.Cell(r, cWunschPrio)) ?? 0),
                AntiKlasse = cAntiKl > 0 ? S(ws.Cell(r, cAntiKl)) : "",
                AntiFach = cAntiFach > 0 ? NormLeerzeichen(S(ws.Cell(r, cAntiFach))) : "",
                AntiPrio = (int)(Num(ws.Cell(r, cAntiPrio)) ?? 0)
            });
        }
        return liste;
    }

    // ===============================================================
    // Klassen → Einträge
    // ===============================================================
    private static List<Eintrag> LiesEintraege(IXLWorkbook wb)
    {
        var ws = Blatt(wb, SpaltenLayout.BlattKlassen);
        const int kopf = 1;

        int cKlasse = SpalteOderFest(ws, kopf, SpaltenLayout.KlasseKlasse, "Klasse");
        int cFach = SpalteOderFest(ws, kopf, SpaltenLayout.KlasseFach, "Fach");
        int cWst = SpalteOderFest(ws, kopf, SpaltenLayout.KlasseWst, "WSt", "Wochenstunden", "Std", "Stunden");
        int cFix = SpalteOderFest(ws, kopf, SpaltenLayout.KlasseFix, "Fix", "Fixiert");
        int cWert = SpalteOderFest(ws, kopf, SpaltenLayout.KlasseWert, "Wert", "WertUV", "Wert UV");
        int cL1 = SpalteOderFest(ws, kopf, SpaltenLayout.KlasseL1, "Lehrer (L1)", "Lehrer L1", "L1");

        var liste = new List<Eintrag>();
        int maxR = LetzteZeile(ws, cFach);
        string aktK = "";

        for (int r = 2; r <= maxR; r++)
        {
            string fach = NormLeerzeichen(S(ws.Cell(r, cFach)));
            if (fach == "") continue;

            string k = S(ws.Cell(r, cKlasse));
            if (k != "") aktK = k;

            string fix = S(ws.Cell(r, cFix));
            bool istFix = fix != "" && fix != "?";
            string klLower = aktK.ToLowerInvariant();

            liste.Add(new Eintrag
            {
                Klasse = aktK,
                Fach = fach,
                Wst = Num(ws.Cell(r, cWst)) ?? 0,
                WertUv = Num(ws.Cell(r, cWert)) ?? 0,
                UrsprungsLehrer = istFix ? fix : "?",
                Lehrer = istFix ? fix : (cL1 > 0 ? S(ws.Cell(r, cL1)) : ""),
                Zeile = r,
                IstOberstufe = klLower.Contains("ef") || klLower.Contains("q1") || klLower.Contains("q2")
            });
        }
        return liste;
    }

    // ===============================================================
    // Parameter (Label in Spalte A, Wert in der „Wert"-Spalte)
    // ===============================================================
    public static ScoreParameter LiesParameter(IXLWorkbook wb)
    {
        var p = new ScoreParameter();
        var ws = BlattOderNull(wb, SpaltenLayout.BlattParameter);
        if (ws is null) return p;

        // Wert-Spalte über die Kopfzeile suchen (Zeile 1 oder 2), sonst feste Spalte B.
        int wert = Spalte(ws, 1, "Wert");
        if (wert == 0) wert = Spalte(ws, 2, "Wert");
        if (wert == 0) wert = SpaltenLayout.ParamSpalte;

        p.Toleranz = PNum(ws, wert, SpaltenLayout.ParamToleranz, "toleranz") ?? p.Toleranz;
        p.ScoreKl = PNum(ws, wert, SpaltenLayout.ParamKl, "klassenleitung") ?? p.ScoreKl;
        p.ScoreW3 = PNum(ws, wert, SpaltenLayout.ParamW3, "wunschprio3") ?? p.ScoreW3;
        p.ScoreW2 = PNum(ws, wert, SpaltenLayout.ParamW2, "wunschprio2") ?? p.ScoreW2;
        p.ScoreW1 = PNum(ws, wert, SpaltenLayout.ParamW1, "wunschprio1") ?? p.ScoreW1;
        p.ScoreA2 = PNum(ws, wert, SpaltenLayout.ParamA2, "antiwunschprio2") ?? p.ScoreA2;
        p.ScoreA1 = PNum(ws, wert, SpaltenLayout.ParamA1, "antiwunschprio1") ?? p.ScoreA1;
        p.ScoreKont = PNum(ws, wert, SpaltenLayout.ParamKont, "kontinuitaet") ?? p.ScoreKont;
        p.ScoreFreiF = PNum(ws, wert, SpaltenLayout.ParamFreiF, "faktorfreiestunden") ?? p.ScoreFreiF;
        p.ScoreUeberF = PNum(ws, wert, SpaltenLayout.ParamUeberF, "faktorueberlast") ?? p.ScoreUeberF;
        p.ScoreUnterF = PNum(ws, wert, SpaltenLayout.ParamUnterF, "faktorunterbesetzung") ?? p.ScoreUnterF;
        p.Zeitlimit = PNum(ws, wert, SpaltenLayout.ParamZeitlimit, "zeitlimit") ?? p.Zeitlimit;

        string sfg1 = PStr(ws, wert, SpaltenLayout.ParamSchutzFg1, "schutzfachgruppe1");
        string sfg2 = PStr(ws, wert, SpaltenLayout.ParamSchutzFg2, "schutzfachgruppe2");
        if (sfg1 != "" && sfg1 != "0" && sfg1 != "False") p.SchutzFg1 = sfg1;
        if (sfg2 != "" && sfg2 != "0" && sfg2 != "False") p.SchutzFg2 = sfg2;
        p.SchutzMalus = PNum(ws, wert, SpaltenLayout.ParamSchutzMalus, "schutzmalus") ?? p.SchutzMalus;

        if (PNum(ws, wert, SpaltenLayout.ParamSaIter, "saiterationenloesung") is double sa) p.SaIter = (long)sa;
        if (PNum(ws, wert, SpaltenLayout.ParamSimIter, "saiterationensimulation") is double si) p.SimIter = (long)si;
        if (p.SaIter < 0) p.SaIter = 0;
        if (p.SimIter <= 0) p.SimIter = 12000;
        if (PNum(ws, wert, SpaltenLayout.ParamSimNrep, "sawiederholungensimulation") is double sn) p.SimNrep = (long)sn;
        if (PNum(ws, wert, SpaltenLayout.ParamSimVerbSek, "verbesserungsimulation") is double sv) p.SimVerbSek = sv;
        if (p.SimNrep < 1) p.SimNrep = 1;
        if (p.SimVerbSek < 0) p.SimVerbSek = 0;

        p.CpUeberGewicht = PNum(ws, wert, SpaltenLayout.ParamCpUeber, "cpsatueberlast") ?? p.CpUeberGewicht;
        p.CpUnterGewicht = PNum(ws, wert, SpaltenLayout.ParamCpUnter, "cpsatunterlast") ?? p.CpUnterGewicht;
        p.CpUnbesetztGewicht = PNum(ws, wert, SpaltenLayout.ParamCpUnbesetzt, "cpsatunbesetzt") ?? p.CpUnbesetztGewicht;
        if (p.CpUeberGewicht < 0) p.CpUeberGewicht = 0;
        if (p.CpUnterGewicht < 0) p.CpUnterGewicht = 0;
        if (p.CpUnbesetztGewicht < 0) p.CpUnbesetztGewicht = 0;

        int zHart = PZeile(ws, "hartetoleranz");
        var hartZelle = ws.Cell(zHart > 0 ? zHart : SpaltenLayout.ParamHarteGrenzen, wert);
        if (Num(hartZelle) is double hg) p.HarteGrenzen = hg != 0;
        else
        {
            string hgs = S(hartZelle).ToLowerInvariant();
            if (hgs is "ja" or "true" or "wahr" or "x" or "hart") p.HarteGrenzen = true;
        }

        p.GlobalMaxUeberlast = PNum(ws, wert, SpaltenLayout.ParamGlobalMaxUeber, "globalemax") ?? p.GlobalMaxUeberlast;
        if (p.GlobalMaxUeberlast < 0) p.GlobalMaxUeberlast = 0;

        return p;
    }

    /// <summary>Sucht die Zeile, deren Label in Spalte A den (normalisierten) Text enthält. 0 = nicht gefunden.</summary>
    private static int PZeile(IXLWorksheet ws, string labelNorm)
    {
        int letzte = LetzteZeile(ws, 1);
        for (int r = 1; r <= letzte; r++)
            if (Norm(S(ws.Cell(r, 1))).Contains(labelNorm)) return r;
        return 0;
    }

    private static double? PNum(IXLWorksheet ws, int wertSpalte, int fallbackZeile, string labelNorm)
    {
        int z = PZeile(ws, labelNorm);
        return Num(ws.Cell(z > 0 ? z : fallbackZeile, wertSpalte));
    }

    private static string PStr(IXLWorksheet ws, int wertSpalte, int fallbackZeile, string labelNorm)
    {
        int z = PZeile(ws, labelNorm);
        return S(ws.Cell(z > 0 ? z : fallbackZeile, wertSpalte));
    }

    // ===============================================================
    // Fachgruppen
    // ===============================================================
    private static Dictionary<string, string> LiesFachgruppen(IXLWorkbook wb)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var ws = BlattOderNull(wb, SpaltenLayout.BlattFachgruppen);
        if (ws is null) return map;

        // Kopfzeile suchen (Zeile mit „Fach"- und „Fachgruppe"-Spalte); sonst A/B ab Zeile 2.
        int kopf = 0, cFach = 0, cGrp = 0;
        for (int r = 1; r <= 5; r++)
        {
            int cf = Spalte(ws, r, "Fach", "Fach (Kuerzel)", "Kuerzel", "Fachkuerzel");
            int cg = Spalte(ws, r, "Fachgruppe", "Gruppe");
            if (cf > 0 && cg > 0) { kopf = r; cFach = cf; cGrp = cg; break; }
        }
        if (kopf == 0) { kopf = 1; cFach = 1; cGrp = 2; }

        int letzte = LetzteZeile(ws, cFach);
        for (int r = kopf + 1; r <= letzte; r++)
        {
            string fach = S(ws.Cell(r, cFach));
            string gruppe = S(ws.Cell(r, cGrp));
            if (fach != "" && gruppe != "") map[fach] = gruppe;
        }
        return map;
    }

    // ===============================================================
    // Spalten-/Kopfzeilen-Hilfen
    // ===============================================================

    /// <summary>Normalisiert Text für Überschriften-Vergleiche (Kleinbuchstaben, ohne Umlaute/Leer/Sonderzeichen).</summary>
    private static string Norm(string s) => s.Trim().ToLowerInvariant()
        .Replace("ü", "ue").Replace("ö", "oe").Replace("ä", "ae").Replace("ß", "ss")
        .Replace(" ", "").Replace("-", "").Replace("_", "").Replace(".", "")
        .Replace("(", "").Replace(")", "").Replace("/", "");

    /// <summary>Erste Spalte in <paramref name="kopfZeile"/>, deren Überschrift exakt einem der Namen entspricht (normalisiert). 0 = keine.</summary>
    private static int Spalte(IXLWorksheet ws, int kopfZeile, params string[] namen)
    {
        int last = LetzteSpalte(ws, kopfZeile);
        var ziele = namen.Select(Norm).ToArray();
        for (int c = 1; c <= last; c++)
        {
            string h = Norm(S(ws.Cell(kopfZeile, c)));
            if (h == "") continue;
            if (ziele.Contains(h)) return c;
        }
        return 0;
    }

    /// <summary>Wie <see cref="Spalte"/>, aber „enthält" statt exakt (für Teiltreffer wie „…Überlast").</summary>
    private static int SpalteContains(IXLWorksheet ws, int kopfZeile, params string[] teile)
    {
        int last = LetzteSpalte(ws, kopfZeile);
        var ziele = teile.Select(Norm).ToArray();
        for (int c = 1; c <= last; c++)
        {
            string h = Norm(S(ws.Cell(kopfZeile, c)));
            if (h == "") continue;
            if (ziele.Any(z => h.Contains(z))) return c;
        }
        return 0;
    }

    /// <summary>Alle Spalten, deren Überschrift exakt einem der Namen entspricht (normalisiert), aufsteigend.</summary>
    private static List<int> AlleSpalten(IXLWorksheet ws, int kopfZeile, params string[] namen)
    {
        int last = LetzteSpalte(ws, kopfZeile);
        var ziele = namen.Select(Norm).ToArray();
        var res = new List<int>();
        for (int c = 1; c <= last; c++)
        {
            string h = Norm(S(ws.Cell(kopfZeile, c)));
            if (h != "" && ziele.Contains(h)) res.Add(c);
        }
        return res;
    }

    /// <summary>Kopfzeilen-Spalte oder – falls nicht gefunden – die feste Fallback-Spalte.</summary>
    private static int SpalteOderFest(IXLWorksheet ws, int kopfZeile, int fallback, params string[] namen)
    {
        int c = Spalte(ws, kopfZeile, namen);
        return c > 0 ? c : fallback;
    }

    private static int LetzteSpalte(IXLWorksheet ws, int zeile)
        => ws.Row(zeile).LastCellUsed()?.Address.ColumnNumber ?? 0;

    // ===============================================================
    // sonstige Hilfen
    // ===============================================================
    private static IXLWorksheet Blatt(IXLWorkbook wb, string name)
        => BlattOderNull(wb, name) ?? throw new InvalidOperationException(
            $"Benötigtes Blatt „{name}\" fehlt in der Arbeitsmappe.");

    private static IXLWorksheet? BlattOderNull(IXLWorkbook wb, string name)
        => wb.Worksheets.TryGetWorksheet(name, out var ws) ? ws : null;

    private static int LetzteZeile(IXLWorksheet ws, int spalte)
        => spalte > 0 ? (ws.Column(spalte).LastCellUsed()?.Address.RowNumber ?? 1) : 1;

    private static string S(IXLCell c) => c.GetString().Trim();

    private static string NormLeerzeichen(string s)
    {
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s;
    }

    private static double? Num(IXLCell c) => c.TryGetValue(out double d) ? d : null;
}
