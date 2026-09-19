using UvOptimierer.Core.Modell;

namespace UvOptimierer.Core.Bewertung;

/// <summary>
/// Bewertungsfunktion. Portierung von <c>BerechneScore</c>, <c>SA_GesamtScore</c>,
/// <c>WunschPrioFuer</c>, <c>HatAntiPflicht</c> und <c>LehrerUVStunden</c>.
/// </summary>
public static class Score
{
    /// <summary>Höchste positive bzw. Anti-Priorität einer Lehrkraft für eine Klasse.</summary>
    /// <summary>
    /// Höchste Priorität eines (Anti-)Wunsches für Lehrkraft + Klasse + Fach. Ein Wunsch ohne
    /// angegebenes Fach gilt für alle Fächer der Klasse; mit Fach nur für dieses Fach.
    /// </summary>
    public static int WunschPrioFuer(Kontext k, string lName, string klasse, string fach, bool positiv)
    {
        int mx = 0;
        foreach (var w in k.Wuensche)
        {
            if (w.LehrerName != lName) continue;
            if (positiv)
            {
                if (w.WunschKlasse != klasse) continue;
                if (w.WunschFach != "" && !string.Equals(w.WunschFach, fach, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (w.WunschPrio > mx) mx = w.WunschPrio;
            }
            else
            {
                if (w.AntiKlasse != klasse) continue;
                if (w.AntiFach != "" && !string.Equals(w.AntiFach, fach, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (w.AntiPrio > mx) mx = w.AntiPrio;
            }
        }
        return mx;
    }

    /// <summary>Absolutes Verbot (Anti-Wunsch der Priorität 3) für Lehrkraft + Klasse + Fach.</summary>
    public static bool HatAntiPflicht(Kontext k, string lName, string klasse, string fach)
    {
        foreach (var w in k.Wuensche)
            if (w.LehrerName == lName && w.AntiKlasse == klasse && w.AntiPrio == 3
                && (w.AntiFach == "" || string.Equals(w.AntiFach, fach, System.StringComparison.OrdinalIgnoreCase)))
                return true;
        return false;
    }

    /// <summary>Summe der UV-Wertstunden, die dieser Lehrkraft (fix oder per Lösung) zugewiesen sind.</summary>
    public static double LehrerUvStunden(Kontext k, int lIdx, IReadOnlyList<string> loesung)
    {
        string name = k.Lehrer[lIdx].Name;
        double s = 0;
        for (int e = 0; e < k.NE; e++)
        {
            var ein = k.Eintraege[e];
            if (!Regeln.IstUv(ein.Fach)) continue;
            if (Regeln.IstFixiert(ein.UrsprungsLehrer))
            {
                if (string.Equals(ein.UrsprungsLehrer, name, StringComparison.OrdinalIgnoreCase))
                    s += ein.WertUv;
            }
            else if (string.Equals(loesung[e], name, StringComparison.OrdinalIgnoreCase))
            {
                s += ein.WertUv;
            }
        }
        return s;
    }

    /// <summary>Wie <see cref="LehrerUvStunden"/>, aber auf Basis von <see cref="Eintrag.Lehrer"/> (Backtracking-Zustand).</summary>
    public static double LehrerUvStundenBt(Kontext k, int lIdx)
    {
        string name = k.Lehrer[lIdx].Name;
        double s = 0;
        for (int e = 0; e < k.NE; e++)
        {
            var ein = k.Eintraege[e];
            if (!Regeln.IstUv(ein.Fach)) continue;
            if (string.Equals(ein.Lehrer, name, StringComparison.OrdinalIgnoreCase))
                s += ein.WertUv;
        }
        return s;
    }

    /// <summary>
    /// Score der Zuweisung von Lehrkraft <paramref name="lIdx"/> auf Eintrag <paramref name="e"/>.
    /// Exakte Portierung von <c>BerechneScore</c> (Klassenleitung, Wünsche/Anti-Wünsche,
    /// Kontinuität, Kapazitäts-Score, Unterbesetzungs-Malus, Schutzfachgruppen-Malus).
    /// </summary>
    public static double Berechne(Kontext k, int lIdx, int e)
    {
        var p = k.P;
        var lehr = k.Lehrer[lIdx];
        var ein = k.Eintraege[e];
        double sc = 0;

        if (lehr.KlassenleitungKlasse == ein.Klasse && lehr.KlassenleitungKlasse != "")
            sc += p.ScoreKl;

        int wp = WunschPrioFuer(k, lehr.Name, ein.Klasse, ein.Fach, positiv: true);
        if (wp == 3) sc += p.ScoreW3;
        if (wp == 2) sc += p.ScoreW2;
        if (wp == 1) sc += p.ScoreW1;

        int ap = WunschPrioFuer(k, lehr.Name, ein.Klasse, ein.Fach, positiv: false);
        if (ap == 2) sc -= p.ScoreA2;
        if (ap == 1) sc -= p.ScoreA1;

        for (int kk = 0; kk < e; kk++)
        {
            if (k.Eintraege[kk].Klasse == ein.Klasse && k.Eintraege[kk].Lehrer == lehr.Name)
            {
                sc += p.ScoreKont;
                break;
            }
        }

        double freieStd = lehr.SollWst - lehr.IstWst;
        if (freieStd > 1000) freieStd = 1000;
        if (freieStd < -1000) freieStd = -1000;
        if (freieStd >= 0) sc += freieStd * p.ScoreFreiF;
        else sc += freieStd * p.ScoreUeberF;

        double nachZuweisung = freieStd - ein.Wst;
        if (nachZuweisung < 0 && nachZuweisung > -k.Toleranz)
            sc += nachZuweisung * p.ScoreUnterF;

        if (lehr.IstSchutzLehrer)
        {
            double sfSoll = lehr.SollWst;
            double sfIst = lehr.IstWst;
            if (sfSoll > 0)
            {
                double sfAusl = sfIst / sfSoll;
                if (sfAusl >= 0.8)
                    sc -= p.SchutzMalus * (sfAusl - 0.8) * 5;
            }
        }

        return sc;
    }

    /// <summary>
    /// Gesamtscore einer Lösung inkl. Strafen: -500 je unbesetztem Eintrag, Kapazitätsstrafe
    /// (Faktor 50) und – als Erweiterung – individuelle Überlaststrafe (Faktor 50), falls für
    /// eine Lehrkraft <see cref="Lehrer.MaxUeberlast"/> gesetzt ist.
    /// </summary>
    public static double GesamtScore(Kontext k, IReadOnlyList<string> loesung)
    {
        double sc = 0;
        for (int e = 0; e < k.NE; e++)
        {
            string l = loesung[e];
            if (!Regeln.IstBelegt(l)) { sc -= 500; continue; }
            int lIdx = k.LehrerIdx(l);
            if (lIdx >= 0) sc += Berechne(k, lIdx, e);
        }

        if (k.Toleranz < Kontext.KeineKapazitaet)
        {
            for (int i = 0; i < k.NL; i++)
            {
                var lehr = k.Lehrer[i];
                if (lehr.IstWst > lehr.SollWst + k.Toleranz)
                    sc -= (lehr.IstWst - lehr.SollWst - k.Toleranz) * 50;
            }
        }

        // Erweiterung: harte individuelle Über-/Unterlastgrenze → im SA/BT als Strafe.
        for (int i = 0; i < k.NL; i++)
        {
            var lehr = k.Lehrer[i];
            if (lehr.MaxUeberlast is double mu && lehr.IstWst > lehr.SollWst + mu)
                sc -= (lehr.IstWst - lehr.SollWst - mu) * 50;
            if (lehr.MaxUnterlast is double mn && lehr.IstWst < lehr.SollWst - mn)
                sc -= (lehr.SollWst - mn - lehr.IstWst) * 50;
        }

        return sc;
    }
}
