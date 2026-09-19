using System.Diagnostics;
using UvOptimierer.Core.Bewertung;
using UvOptimierer.Core.Fachlogik;
using UvOptimierer.Core.Modell;

namespace UvOptimierer.Core.Solver;

/// <summary>
/// Backtracking mit Constraint-Propagation (Arc-Consistency) und MRV-Heuristik.
/// Portierung von <c>InitDomains</c>, <c>ArcConsistency</c>, <c>BacktrackingMitCP</c>,
/// <c>DomainZuKandidaten</c>, <c>DomainGroesse</c> und <c>EntferneLehrerAusDomain</c>.
/// Domain-Strings enthalten 0-basierte Lehrer-Indizes, kommagetrennt; Sonderwerte
/// „FIX" (fixierter Eintrag) und „SKIP" (dauerhaft unbesetzbar).
/// </summary>
public sealed class BacktrackingSolver : ILoesungsSolver
{
    public string Name => "Backtracking+CP";

    private const int MaxDepthCap = 2000;

    /// <summary>Ein Backtracking-Lauf inkl. Toleranz-Fallback (9999) analog Optimieren_Backtracking, aber für EINE Lösung.</summary>
    public bool Loese(Kontext k, string[] loesung, SolverOptionen opt)
    {
        k.SperrIdx = opt.HatSperre ? opt.SperrIdx : -1;
        k.SperrName = opt.HatSperre ? opt.SperrName : "";
        double tolVal = k.P.Toleranz;
        k.Toleranz = tolVal;

        var domains = new string[k.NE];
        for (int e = 0; e < k.NE; e++) loesung[e] = "?";

        Suchhilfen.ResetIstWst(k);
        InitDomains(k, domains);
        var sw = Stopwatch.StartNew();
        bool ok = BacktrackingMitCP(k, domains, loesung, opt.ZeitlimitSek, sw);

        if (!ok)
        {
            k.Toleranz = Kontext.KeineKapazitaet;
            for (int e = 0; e < k.NE; e++) loesung[e] = "?";
            Suchhilfen.ResetIstWst(k);
            InitDomains(k, domains);
            sw.Restart();
            ok = BacktrackingMitCP(k, domains, loesung, opt.ZeitlimitSek, sw);
            k.Toleranz = tolVal;
        }

        // Fix-Einträge immer eintragen
        for (int e = 0; e < k.NE; e++)
            if (Regeln.IstFixiert(k.Eintraege[e].UrsprungsLehrer))
                loesung[e] = k.Eintraege[e].UrsprungsLehrer;

        return ok;
    }

    // ---------------------------------------------------------------
    // Domains
    // ---------------------------------------------------------------
    public void InitDomains(Kontext k, string[] domains)
    {
        for (int e = 0; e < k.NE; e++)
        {
            var ein = k.Eintraege[e];
            if (Regeln.IstFixiert(ein.UrsprungsLehrer)) { domains[e] = "FIX"; continue; }

            // Konsistenz-Zwang: gleiche Klasse+Fach schon fixiert (außer UV) → nur dieser Lehrer
            string zwang = "";
            if (!Regeln.IstUv(ein.Fach))
            {
                for (int e2 = 0; e2 < k.NE; e2++)
                {
                    if (e2 == e) continue;
                    var a = k.Eintraege[e2];
                    if (Gleich(a.Klasse, ein.Klasse) && Gleich(a.Fach, ein.Fach) && Regeln.IstFixiert(a.Lehrer))
                    { zwang = a.Lehrer; break; }
                }
            }

            string d = BaueDomain(k, e, zwang, kapazitaetPruefen: true, antiPflichtPruefen: true);

            // Zwang gesetzt, aber Lehrer nicht verfügbar → ohne Kapazität nur diesen Lehrer
            if (zwang != "" && d == "")
                d = BaueDomain(k, e, zwang, kapazitaetPruefen: false, antiPflichtPruefen: false, nurZwang: true);

            // Domain leer → Fallback ohne Kapazitätsprüfung (BT findet immer eine Lösung)
            if (d == "")
                d = BaueDomain(k, e, zwang, kapazitaetPruefen: false, antiPflichtPruefen: true);

            domains[e] = d;
        }
    }

    private string BaueDomain(Kontext k, int e, string zwang, bool kapazitaetPruefen, bool antiPflichtPruefen, bool nurZwang = false)
    {
        var ein = k.Eintraege[e];
        var sb = new List<int>();
        for (int i = 0; i < k.NL; i++)
        {
            var l = k.Lehrer[i];
            if (e == k.SperrIdx && l.Name == k.SperrName) continue;
            if (zwang != "" && l.Name != zwang) continue;
            if (!FachLogik.KannFach(l, ein.Fach, ein.IstOberstufe)) continue;
            if (!nurZwang && antiPflichtPruefen && Score.HatAntiPflicht(k, l.Name, ein.Klasse, ein.Fach)) continue;
            if (kapazitaetPruefen && !Suchhilfen.KapazitaetOk(l, ein.WertUv, k.Toleranz)) continue;
            if (Regeln.IstUv(ein.Fach) && Score.LehrerUvStundenBt(k, i) + ein.WertUv > 2) continue;
            sb.Add(i);
        }
        return string.Join(",", sb);
    }

    private static int DomainGroesse(string domain)
    {
        if (domain == "" || domain == "FIX" || domain == "SKIP") return 0;
        return domain.Split(',').Length;
    }

    private static string EntferneLehrerAusDomain(string domain, int lIdx)
    {
        if (domain == "" || domain == "FIX" || domain == "SKIP") return domain;
        var teile = domain.Split(',');
        var neu = new List<string>(teile.Length);
        foreach (var t in teile)
            if (int.Parse(t.Trim()) != lIdx) neu.Add(t.Trim());
        return string.Join(",", neu);
    }

    /// <summary>Kandidaten eines Domain-Strings, absteigend nach Score sortiert.</summary>
    private static int[] DomainZuKandidaten(Kontext k, string domain, int e)
    {
        var teile = domain.Split(',');
        int n = teile.Length;
        var kand = new int[n];
        var scor = new double[n];
        for (int p = 0; p < n; p++)
        {
            int lIdx = int.Parse(teile[p].Trim());
            kand[p] = lIdx;
            scor[p] = Score.Berechne(k, lIdx, e);
        }
        // stabiler Insertion-Sort absteigend (wie VBA)
        for (int si = 1; si < n; si++)
        {
            int ti = kand[si]; double ts = scor[si]; int sj = si - 1;
            while (sj >= 0 && scor[sj] < ts)
            {
                kand[sj + 1] = kand[sj];
                scor[sj + 1] = scor[sj];
                sj--;
            }
            kand[sj + 1] = ti; scor[sj + 1] = ts;
        }
        return kand;
    }

    // ---------------------------------------------------------------
    // Arc-Consistency
    // ---------------------------------------------------------------
    private static bool ArcConsistency(Kontext k, string[] domains, int zugewiesenerE, int zugewiesenerL)
    {
        var lehrerZ = k.Lehrer[zugewiesenerL];
        var einZ = k.Eintraege[zugewiesenerE];
        for (int e = 0; e < k.NE; e++)
        {
            if (e == zugewiesenerE) continue;
            var ein = k.Eintraege[e];
            if (Regeln.IstFixiert(ein.UrsprungsLehrer)) continue;
            if (Regeln.IstBelegt(ein.Lehrer)) continue;
            if (domains[e] == "" || domains[e] == "SKIP") continue;

            if (!Regeln.IstUv(ein.Fach) && Gleich(ein.Klasse, einZ.Klasse) && Gleich(ein.Fach, einZ.Fach))
            {
                if (!Suchhilfen.KapazitaetOk(lehrerZ, ein.WertUv, k.Toleranz)) domains[e] = "";
                else domains[e] = zugewiesenerL.ToString();
                continue;
            }

            if (!Suchhilfen.KapazitaetOk(lehrerZ, ein.WertUv, k.Toleranz))
                domains[e] = EntferneLehrerAusDomain(domains[e], zugewiesenerL);

            if (Regeln.IstUv(ein.Fach) && Regeln.IstUv(einZ.Fach))
                if (Score.LehrerUvStundenBt(k, zugewiesenerL) + ein.WertUv > 2)
                    domains[e] = EntferneLehrerAusDomain(domains[e], zugewiesenerL);
        }
        return true;
    }

    // ---------------------------------------------------------------
    // Iteratives Backtracking
    // ---------------------------------------------------------------
    public bool BacktrackingMitCP(Kontext k, string[] domains, string[] loesung, double maxSek, Stopwatch sw)
    {
        int maxDepth = Math.Min(k.NE, MaxDepthCap);

        // Stack-Ebenen
        var stE = new int[maxDepth + 1];
        var stLIdx = new int[maxDepth + 1];
        var stKand = new int[maxDepth + 1][];
        var stKPos = new int[maxDepth + 1];
        var stDom = new string[maxDepth + 1][];

        int depth = 0;
        bool goDown = true;

        while (true)
        {
            if (maxSek > 0 && sw.Elapsed.TotalSeconds > maxSek) return false;

            if (goDown)
            {
                // MRV: unbesetzten, nicht fixierten Eintrag mit kleinster Domain wählen
                int bestE = -1, bestSize = int.MaxValue;
                for (int e = 0; e < k.NE; e++)
                {
                    var ein = k.Eintraege[e];
                    if (Regeln.IstFixiert(ein.UrsprungsLehrer)) continue;
                    if (Regeln.IstBelegt(ein.Lehrer)) continue;
                    if (domains[e] == "SKIP") continue;
                    int dSize = DomainGroesse(domains[e]);
                    if (dSize < bestSize) { bestSize = dSize; bestE = e; }
                }

                if (bestE == -1) // alles besetzt → Lösung
                {
                    for (int e = 0; e < k.NE; e++)
                        if (Regeln.IstFixiert(k.Eintraege[e].UrsprungsLehrer))
                            loesung[e] = k.Eintraege[e].UrsprungsLehrer;
                    return true;
                }

                bool weiterMitKandidaten = false;
                if (bestSize == 0)
                {
                    if (domains[bestE] == "")
                    {
                        // Fallback ohne Kapazität (mit Zwang-Prüfung)
                        var einB = k.Eintraege[bestE];
                        string zwangFb = "";
                        if (!Regeln.IstUv(einB.Fach))
                        {
                            for (int e4 = 0; e4 < k.NE; e4++)
                            {
                                if (e4 == bestE) continue;
                                var a = k.Eintraege[e4];
                                if (Gleich(a.Klasse, einB.Klasse) && Gleich(a.Fach, einB.Fach) && Regeln.IstFixiert(a.Lehrer))
                                { zwangFb = a.Lehrer; break; }
                            }
                        }
                        string dFb = BaueDomain(k, bestE, zwangFb, kapazitaetPruefen: false, antiPflichtPruefen: true);
                        if (dFb != "")
                        {
                            domains[bestE] = dFb;
                            bestSize = DomainGroesse(dFb);
                            weiterMitKandidaten = true;
                        }
                    }
                    if (!weiterMitKandidaten)
                    {
                        // wirklich keine Kandidaten → dauerhaft überspringen
                        k.Eintraege[bestE].Lehrer = "?";
                        loesung[bestE] = "?";
                        domains[bestE] = "SKIP";
                        continue; // goDown bleibt true
                    }
                }
                else
                {
                    weiterMitKandidaten = true;
                }

                if (weiterMitKandidaten)
                {
                    if (depth >= maxDepth)
                    {
                        goDown = false;
                    }
                    else
                    {
                        depth++;
                        stE[depth] = bestE;
                        stLIdx[depth] = -1;
                        stKand[depth] = DomainZuKandidaten(k, domains[bestE], bestE);
                        stKPos[depth] = 0;
                        stDom[depth] = (string[])domains.Clone();
                        goDown = false;
                    }
                }
            }

            if (!goDown)
            {
                if (depth == 0) return false;

                if (stKPos[depth] >= stKand[depth].Length)
                {
                    // Backtrack
                    int bE = stE[depth];
                    int lIdx = stLIdx[depth];
                    if (lIdx >= 0 && Regeln.IstBelegt(k.Eintraege[bE].Lehrer))
                    {
                        k.Lehrer[lIdx].IstWst -= k.Eintraege[bE].WertUv;
                        k.Eintraege[bE].Lehrer = "?";
                        loesung[bE] = "?";
                    }
                    Array.Copy(stDom[depth], domains, k.NE);
                    depth--;
                    // goDown bleibt false
                }
                else
                {
                    int bE = stE[depth];
                    int lIdxAlt = stLIdx[depth];
                    if (lIdxAlt >= 0 && Regeln.IstBelegt(k.Eintraege[bE].Lehrer))
                    {
                        k.Lehrer[lIdxAlt].IstWst -= k.Eintraege[bE].WertUv;
                        k.Eintraege[bE].Lehrer = "?";
                        loesung[bE] = "?";
                    }
                    Array.Copy(stDom[depth], domains, k.NE);

                    int lIdx = stKand[depth][stKPos[depth]];
                    stLIdx[depth] = lIdx;
                    stKPos[depth]++;

                    k.Eintraege[bE].Lehrer = k.Lehrer[lIdx].Name;
                    k.Lehrer[lIdx].IstWst += k.Eintraege[bE].WertUv;
                    loesung[bE] = k.Lehrer[lIdx].Name;

                    bool cpOk = ArcConsistency(k, domains, bE, lIdx);
                    if (cpOk)
                    {
                        goDown = true;
                    }
                    else
                    {
                        k.Lehrer[lIdx].IstWst -= k.Eintraege[bE].WertUv;
                        k.Eintraege[bE].Lehrer = "?";
                        loesung[bE] = "?";
                        Array.Copy(stDom[depth], domains, k.NE);
                        goDown = false;
                    }
                }
            }
        }
    }

    private static bool Gleich(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
