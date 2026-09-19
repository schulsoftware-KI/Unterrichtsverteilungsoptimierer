using UvOptimierer.Core.Bewertung;
using UvOptimierer.Core.Fachlogik;
using UvOptimierer.Core.Modell;

namespace UvOptimierer.Core.Solver;

/// <summary>
/// Simulated Annealing. Portierung von <c>SA_Solve</c> samt Hilfsfunktionen
/// (<c>SA_BesterKandidat</c>, <c>SA_ZufallsKandidat</c>, <c>SA_GesamtScore</c>,
/// <c>SA_LehrerIdx</c>) sowie der greedy MRV-Startphase und der Konsistenz-Nachbearbeitung.
/// <para>
/// Der Solver ist eigenständig: ohne Startlösung (<see cref="SolverOptionen.HatStartLoesung"/>
/// = false) erzeugt er selbst eine greedy MRV-Startbelegung. Der Orchestrierer
/// <see cref="Optimierer"/> reicht bei Bedarf eine Backtracking-Warmstartlösung ein.
/// </para>
/// <para>
/// Die individuelle Überlastgrenze (<see cref="Lehrer.MaxUeberlast"/>) wirkt in diesem
/// Verfahren – wie im Konzept festgelegt – als <em>Strafterm</em> (siehe
/// <see cref="Score.GesamtScore"/>), nicht als harte Schranke. Die Kandidatenauswahl
/// verwendet daher nur die reine Toleranzprüfung wie im VBA-Original.
/// </para>
/// </summary>
public sealed class SaSolver : ILoesungsSolver
{
    public string Name => "Simulated Annealing";

    private readonly Random _rng;

    /// <param name="seed">Optionaler Zufalls-Seed für reproduzierbare Läufe (z. B. Tests).
    /// Ohne Angabe zeitbasiert (entspricht dem VBA-<c>Randomize</c>).</param>
    public SaSolver(int? seed = null)
        => _rng = seed is int s ? new Random(s) : new Random();

    public bool Loese(Kontext k, string[] loesung, SolverOptionen opt)
    {
        int nE = k.NE;
        double tolVal = k.P.Toleranz;
        // g_toleranz während des SA-Laufs auf den Nennwert setzen (BerechneScore liest k.Toleranz).
        k.Toleranz = tolVal;
        bool hatSperre = opt.HatSperre;
        int sperrIdx = opt.HatSperre ? opt.SperrIdx : -1;
        string sperrName = opt.HatSperre ? opt.SperrName : "";

        // ------ Startlösung ------
        Suchhilfen.ResetIstWst(k);

        if (opt.HatStartLoesung)
        {
            // IstWSt aus übergebener Lösung aufbauen (fixierte Einträge zählt ResetIstWst bereits).
            for (int e = 0; e < nE; e++)
            {
                if (Regeln.IstFixiert(k.Eintraege[e].UrsprungsLehrer)) continue;
                if (Regeln.IstBelegt(loesung[e]))
                {
                    int idx = k.LehrerIdx(loesung[e]);
                    if (idx >= 0) k.Lehrer[idx].IstWst += k.Eintraege[e].WertUv;
                }
            }
        }
        else
        {
            GreedyStart(k, loesung, tolVal, hatSperre, sperrIdx, sperrName);
        }

        // ------ SA-Verbesserungsphase ------
        int nOffen = 0;
        for (int e = 0; e < nE; e++)
            if (!Regeln.IstFixiert(k.Eintraege[e].UrsprungsLehrer)) nOffen++;

        if (nOffen > 0)
            Verbesserungsphase(k, loesung, tolVal, hatSperre, sperrIdx, sperrName, nOffen, opt);

        KonsistenzNachbearbeitung(k, loesung);
        IstWstNeuAufbauen(k, loesung);

        // Fixierte Einträge sicher eintragen.
        for (int e = 0; e < nE; e++)
            if (Regeln.IstFixiert(k.Eintraege[e].UrsprungsLehrer))
                loesung[e] = k.Eintraege[e].UrsprungsLehrer;

        // SA liefert immer eine vollständige Belegung (Fallbacks decken Notfälle ab).
        for (int e = 0; e < nE; e++)
            if (!Regeln.IstBelegt(loesung[e])) return false;
        return true;
    }

    // ================================================================
    // Greedy MRV-Startphase
    // ================================================================
    private void GreedyStart(Kontext k, string[] loesung, double tolVal,
                             bool hatSperre, int sperrIdx, string sperrName)
    {
        int nE = k.NE, nL = k.NL;

        var ord = new List<int>();
        var kand = new List<int>();
        for (int e = 0; e < nE; e++)
        {
            if (!Regeln.IstFixiert(k.Eintraege[e].UrsprungsLehrer))
            {
                int c = 0;
                var ein = k.Eintraege[e];
                for (int i = 0; i < nL; i++)
                {
                    if (FachLogik.KannFach(k.Lehrer[i], ein.Fach, ein.IstOberstufe)
                        && !Score.HatAntiPflicht(k, k.Lehrer[i].Name, ein.Klasse, ein.Fach))
                        c++;
                }
                ord.Add(e);
                kand.Add(c);
            }
            else
            {
                loesung[e] = k.Eintraege[e].UrsprungsLehrer;
            }
        }

        // Insertion-Sort nach Kandidatenanzahl aufsteigend (MRV) – stabil wie im VBA.
        for (int a = 1; a < ord.Count; a++)
        {
            int tmp = ord[a], tmpK = kand[a];
            int j = a - 1;
            while (j >= 0 && kand[j] > tmpK)
            {
                ord[j + 1] = ord[j];
                kand[j + 1] = kand[j];
                j--;
            }
            ord[j + 1] = tmp;
            kand[j + 1] = tmpK;
        }

        // Erster Durchlauf: bester Kandidat (mit Kapazitätsprüfung in BesterKandidat).
        foreach (int e in ord)
        {
            loesung[e] = BesterKandidat(k, e, tolVal, hatSperre, sperrIdx, sperrName, loesung);
            if (loesung[e] != "?")
            {
                int idx = k.LehrerIdx(loesung[e]);
                if (idx >= 0) k.Lehrer[idx].IstWst += k.Eintraege[e].WertUv;
            }
        }

        // Zweiter Durchlauf: noch unbesetzte Einträge notfalls ohne Kapazitätsschranke.
        foreach (int e in ord)
        {
            if (Regeln.IstBelegt(loesung[e])) continue;
            var ein = k.Eintraege[e];

            // Konsistenz-Zwang: Geschwister-Eintrag (gleiche Klasse+Fach, kein UV) bereits belegt?
            string zwang = "";
            for (int e2 = 0; e2 < nE; e2++)
            {
                if (e2 == e) continue;
                var a = k.Eintraege[e2];
                if (Gleich(a.Klasse, ein.Klasse) && Gleich(a.Fach, ein.Fach) && !Regeln.IstUv(ein.Fach))
                {
                    if (Regeln.IstFixiert(a.Lehrer)) { zwang = a.Lehrer; break; }
                    if (Regeln.IstBelegt(loesung[e2])) { zwang = loesung[e2]; break; }
                }
            }

            double best = double.NegativeInfinity;
            string bestNm = "?";
            if (zwang != "")
            {
                bestNm = zwang;
            }
            else
            {
                // Erst mit Kapazitätsprüfung.
                for (int i = 0; i < nL; i++)
                {
                    if (hatSperre && e == sperrIdx && k.Lehrer[i].Name == sperrName) continue;
                    if (!FachLogik.KannFach(k.Lehrer[i], ein.Fach, ein.IstOberstufe)) continue;
                    if (Score.HatAntiPflicht(k, k.Lehrer[i].Name, ein.Klasse, ein.Fach)) continue;
                    if (k.Lehrer[i].IstWst + ein.WertUv > k.Lehrer[i].SollWst + k.Toleranz) continue;
                    if (Regeln.IstUv(ein.Fach) && Score.LehrerUvStunden(k, i, loesung) + ein.WertUv > 2) continue;
                    double sc = Score.Berechne(k, i, e);
                    if (sc > best) { best = sc; bestNm = k.Lehrer[i].Name; }
                }
                // Dann ohne Kapazitätsprüfung (Überlast in Kauf nehmen), UV bleibt hart.
                if (bestNm == "?")
                {
                    for (int i = 0; i < nL; i++)
                    {
                        if (hatSperre && e == sperrIdx && k.Lehrer[i].Name == sperrName) continue;
                        if (!FachLogik.KannFach(k.Lehrer[i], ein.Fach, ein.IstOberstufe)) continue;
                        if (Score.HatAntiPflicht(k, k.Lehrer[i].Name, ein.Klasse, ein.Fach)) continue;
                        if (Regeln.IstUv(ein.Fach) && Score.LehrerUvStunden(k, i, loesung) + ein.WertUv > 2) continue;
                        double sc = Score.Berechne(k, i, e);
                        if (sc > best) { best = sc; bestNm = k.Lehrer[i].Name; }
                    }
                }
            }

            if (bestNm != "?")
            {
                loesung[e] = bestNm;
                int idx = k.LehrerIdx(bestNm);
                if (idx >= 0) k.Lehrer[idx].IstWst += ein.WertUv;
            }
        }
    }

    // ================================================================
    // SA-Verbesserungsphase
    // ================================================================
    private void Verbesserungsphase(Kontext k, string[] loesung, double tolVal,
                                    bool hatSperre, int sperrIdx, string sperrName,
                                    int nOffen, SolverOptionen opt)
    {
        int nE = k.NE;
        double temp = opt.StartTemp;
        const double tempMin = 0.1;
        long maxIter;
        double kuehlung;
        if (nOffen <= 20) { maxIter = 2000; kuehlung = 0.98; }
        else if (nOffen <= 100) { maxIter = 10000; kuehlung = 0.995; }
        else { maxIter = 30000; kuehlung = 0.998; }
        if (k.P.SaIter > 0) maxIter = k.P.SaIter;
        if (opt.MaxIterOverride > 0) maxIter = opt.MaxIterOverride;

        double scoreCurr = Score.GesamtScore(k, loesung);

        for (long iteration = 1; iteration <= maxIter; iteration++)
        {
            int e1;
            do { e1 = _rng.Next(nE); }
            while (Regeln.IstFixiert(k.Eintraege[e1].UrsprungsLehrer));

            if (_rng.NextDouble() < 0.5)
            {
                // ---- Neuer Lehrer für e1 ----
                string altL1 = loesung[e1];
                string neuL1 = ZufallsKandidat(k, e1, tolVal, hatSperre, sperrIdx, sperrName, loesung);
                if (neuL1 == altL1 || !Regeln.IstBelegt(neuL1)) { }
                else
                {
                    double scoreAlt1 = 0, scoreNeu1 = 0;
                    if (Regeln.IstBelegt(altL1))
                    {
                        int aIdx1 = k.LehrerIdx(altL1);
                        if (aIdx1 >= 0)
                        {
                            scoreAlt1 = Score.Berechne(k, aIdx1, e1);
                            k.Lehrer[aIdx1].IstWst -= k.Eintraege[e1].WertUv;
                        }
                    }
                    int nIdx1 = k.LehrerIdx(neuL1);
                    if (nIdx1 >= 0)
                    {
                        k.Lehrer[nIdx1].IstWst += k.Eintraege[e1].WertUv;
                        scoreNeu1 = Score.Berechne(k, nIdx1, e1);
                    }
                    loesung[e1] = neuL1;
                    double delta = scoreNeu1 - scoreAlt1;
                    double scoreNew = scoreCurr + delta;

                    bool akzeptiert;
                    if (delta >= 0) akzeptiert = true;
                    else if (temp > 0.001)
                    {
                        double accept = (delta / temp < -700) ? 0 : Math.Exp(delta / temp);
                        akzeptiert = _rng.NextDouble() < accept;
                    }
                    else akzeptiert = false;

                    if (akzeptiert)
                    {
                        scoreCurr = scoreNew;
                    }
                    else
                    {
                        // Rückgängig
                        loesung[e1] = altL1;
                        if (neuL1 != "?")
                        {
                            int idx = k.LehrerIdx(neuL1);
                            if (idx >= 0) k.Lehrer[idx].IstWst -= k.Eintraege[e1].WertUv;
                        }
                        if (altL1 != "?")
                        {
                            int idx = k.LehrerIdx(altL1);
                            if (idx >= 0) k.Lehrer[idx].IstWst += k.Eintraege[e1].WertUv;
                        }
                    }
                }
            }
            else
            {
                // ---- Zwei Einträge tauschen ----
                int e2try = -1;
                for (int tryCount = 0; tryCount < 10; tryCount++)
                {
                    int e2 = _rng.Next(nE);
                    if (e2 == e1 || Regeln.IstFixiert(k.Eintraege[e2].Lehrer)) continue;
                    if (hatSperre && e2 == sperrIdx) continue;
                    int lIdx1 = k.LehrerIdx(loesung[e1]);
                    int lIdx2 = k.LehrerIdx(loesung[e2]);
                    if (lIdx1 < 0 || lIdx2 < 0) continue;
                    var eA = k.Eintraege[e1];
                    var eB = k.Eintraege[e2];
                    if (!FachLogik.KannFach(k.Lehrer[lIdx1], eB.Fach, eB.IstOberstufe)) continue;
                    if (!FachLogik.KannFach(k.Lehrer[lIdx2], eA.Fach, eA.IstOberstufe)) continue;
                    if (Score.HatAntiPflicht(k, k.Lehrer[lIdx1].Name, eB.Klasse, eB.Fach)) continue;
                    if (Score.HatAntiPflicht(k, k.Lehrer[lIdx2].Name, eA.Klasse, eA.Fach)) continue;

                    bool uvOk = true;
                    if (Regeln.IstUv(eB.Fach) && !Regeln.IstUv(eA.Fach))
                        if (Score.LehrerUvStunden(k, lIdx1, loesung) + eB.WertUv > 2) uvOk = false;
                    if (uvOk && Regeln.IstUv(eA.Fach) && !Regeln.IstUv(eB.Fach))
                        if (Score.LehrerUvStunden(k, lIdx2, loesung) + eA.WertUv > 2) uvOk = false;

                    if (uvOk) { e2try = e2; break; }
                }
                if (e2try < 0) { }
                else
                {
                    string altL1 = loesung[e1], altL2 = loesung[e2try];
                    var eA = k.Eintraege[e1];
                    var eB = k.Eintraege[e2try];
                    double sA1 = 0, sA2 = 0, sN1 = 0, sN2 = 0;
                    int iA1 = k.LehrerIdx(altL1);
                    int iA2 = k.LehrerIdx(altL2);
                    if (iA1 >= 0) sA1 = Score.Berechne(k, iA1, e1);
                    if (iA2 >= 0) sA2 = Score.Berechne(k, iA2, e2try);
                    if (iA1 >= 0) k.Lehrer[iA1].IstWst += -eA.WertUv + eB.WertUv;
                    if (iA2 >= 0) k.Lehrer[iA2].IstWst += -eB.WertUv + eA.WertUv;
                    loesung[e1] = altL2; loesung[e2try] = altL1;
                    if (iA2 >= 0) sN1 = Score.Berechne(k, iA2, e1);
                    if (iA1 >= 0) sN2 = Score.Berechne(k, iA1, e2try);
                    double delta = (sN1 + sN2) - (sA1 + sA2);
                    double scoreNew = scoreCurr + delta;

                    bool akzeptiert = delta >= 0
                        || (temp > 0.001 && _rng.NextDouble() < ((delta / temp < -700) ? 0 : Math.Exp(delta / temp)));

                    if (akzeptiert)
                    {
                        scoreCurr = scoreNew;
                    }
                    else
                    {
                        loesung[e1] = altL1; loesung[e2try] = altL2;
                        if (iA1 >= 0) k.Lehrer[iA1].IstWst += eA.WertUv - eB.WertUv;
                        if (iA2 >= 0) k.Lehrer[iA2].IstWst += eB.WertUv - eA.WertUv;
                    }
                }
            }

            temp *= kuehlung;
            if (temp < tempMin) break;
        }
    }

    // ================================================================
    // Konsistenz-Nachbearbeitung & IstWSt-Neuaufbau
    // ================================================================
    private static void KonsistenzNachbearbeitung(Kontext k, string[] loesung)
    {
        int nE = k.NE;
        for (int e = 0; e < nE; e++)
        {
            if (Regeln.IstFixiert(k.Eintraege[e].UrsprungsLehrer)) continue;
            if (!Regeln.IstBelegt(loesung[e])) continue;
            for (int e2 = e + 1; e2 < nE; e2++)
            {
                if (Regeln.IstFixiert(k.Eintraege[e2].UrsprungsLehrer)) continue;
                if (!Gleich(k.Eintraege[e2].Klasse, k.Eintraege[e].Klasse)) continue;
                if (!Gleich(k.Eintraege[e2].Fach, k.Eintraege[e].Fach)) continue;
                if (!Regeln.IstUv(k.Eintraege[e2].Fach))
                    loesung[e2] = loesung[e];   // gleicher Lehrer, Kapazität egal
            }
        }
    }

    private static void IstWstNeuAufbauen(Kontext k, string[] loesung)
    {
        Suchhilfen.ResetIstWst(k);
        for (int e = 0; e < k.NE; e++)
        {
            if (Regeln.IstFixiert(k.Eintraege[e].UrsprungsLehrer)) continue;
            if (!Regeln.IstBelegt(loesung[e])) continue;
            int idx = k.LehrerIdx(loesung[e]);
            if (idx >= 0) k.Lehrer[idx].IstWst += k.Eintraege[e].WertUv;
        }
    }

    // ================================================================
    // Kandidatenauswahl (SA_BesterKandidat / SA_ZufallsKandidat)
    // ================================================================
    private static string BesterKandidat(Kontext k, int e, double tolVal,
                                         bool hatSperre, int sperrIdx, string sperrName, string[] loesung)
    {
        int nL = k.NL;
        var ein = k.Eintraege[e];
        double best = double.NegativeInfinity;
        string bestNm = "?";

        // Stufe 1: mit Kapazitätsprüfung.
        for (int i = 0; i < nL; i++)
        {
            if (hatSperre && e == sperrIdx && k.Lehrer[i].Name == sperrName) continue;
            if (!FachLogik.KannFach(k.Lehrer[i], ein.Fach, ein.IstOberstufe)) continue;
            if (Score.HatAntiPflicht(k, k.Lehrer[i].Name, ein.Klasse, ein.Fach)) continue;
            if (tolVal < Kontext.KeineKapazitaet
                && k.Lehrer[i].IstWst + ein.WertUv > k.Lehrer[i].SollWst + tolVal) continue;
            if (Regeln.IstUv(ein.Fach) && Score.LehrerUvStunden(k, i, loesung) + ein.WertUv > 2) continue;
            double sc = Score.Berechne(k, i, e);
            if (sc > best) { best = sc; bestNm = k.Lehrer[i].Name; }
        }
        if (bestNm != "?") return bestNm;

        // Stufe 2: ohne Kapazitätsschranke, UV bleibt hart.
        for (int i = 0; i < nL; i++)
        {
            if (hatSperre && e == sperrIdx && k.Lehrer[i].Name == sperrName) continue;
            if (!FachLogik.KannFach(k.Lehrer[i], ein.Fach, ein.IstOberstufe)) continue;
            if (Score.HatAntiPflicht(k, k.Lehrer[i].Name, ein.Klasse, ein.Fach)) continue;
            if (Regeln.IstUv(ein.Fach) && Score.LehrerUvStunden(k, i, loesung) + ein.WertUv > 2) continue;
            double sc = Score.Berechne(k, i, e);
            if (sc > best) { best = sc; bestNm = k.Lehrer[i].Name; }
        }
        // Stufe 3 im VBA identisch zu Stufe 2 (nur KannFach + Anti + UV) → bereits abgedeckt.
        return bestNm;
    }

    private string ZufallsKandidat(Kontext k, int e, double tolVal,
                                   bool hatSperre, int sperrIdx, string sperrName, string[] loesung)
    {
        int nL = k.NL;
        var ein = k.Eintraege[e];
        var geeignet = new List<int>();

        for (int i = 0; i < nL; i++)
        {
            if (hatSperre && e == sperrIdx && k.Lehrer[i].Name == sperrName) continue;
            if (!FachLogik.KannFach(k.Lehrer[i], ein.Fach, ein.IstOberstufe)) continue;
            if (Score.HatAntiPflicht(k, k.Lehrer[i].Name, ein.Klasse, ein.Fach)) continue;
            if (tolVal < Kontext.KeineKapazitaet
                && k.Lehrer[i].IstWst + ein.WertUv > k.Lehrer[i].SollWst + tolVal) continue;
            if (Regeln.IstUv(ein.Fach) && Score.LehrerUvStunden(k, i, loesung) + ein.WertUv > 2) continue;
            geeignet.Add(i);
        }
        if (geeignet.Count > 0)
            return k.Lehrer[geeignet[_rng.Next(geeignet.Count)]].Name;

        // Fallback: ohne Kapazitätsschranke, Anti-Wunsch + UV bleiben hart.
        geeignet.Clear();
        for (int i = 0; i < nL; i++)
        {
            if (hatSperre && e == sperrIdx && k.Lehrer[i].Name == sperrName) continue;
            if (!FachLogik.KannFach(k.Lehrer[i], ein.Fach, ein.IstOberstufe)) continue;
            if (Score.HatAntiPflicht(k, k.Lehrer[i].Name, ein.Klasse, ein.Fach)) continue;
            if (Regeln.IstUv(ein.Fach) && Score.LehrerUvStunden(k, i, loesung) + ein.WertUv > 2) continue;
            geeignet.Add(i);
        }
        if (geeignet.Count == 0) return "?";
        return k.Lehrer[geeignet[_rng.Next(geeignet.Count)]].Name;
    }

    private static bool Gleich(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
