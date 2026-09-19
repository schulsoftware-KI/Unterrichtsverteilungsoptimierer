using UvOptimierer.Core.Bewertung;
using UvOptimierer.Core.Fachlogik;
using UvOptimierer.Core.Modell;

namespace UvOptimierer.Core.Solver;

/// <summary>
/// Deterministische Nachverbesserung einer bestehenden Lösung (Portierung von
/// <c>VerbessereSA</c>). Reiner Lastausgleich: überlastete Lehrkräfte geben Gruppen
/// (gleiche Klasse+Fach, UV einzeln) an unterlastete, qualifizierte Kolleg:innen ab
/// (Transfer) oder tauschen Gruppen (Tausch), solange die Summe der absoluten
/// Ist-Soll-Abweichungen dadurch sinkt. Alle harten Regeln (Qualifikation,
/// Anti-Wunsch Prio 3, UV ≤ 2 WSt, Fixierung, Konsistenz) bleiben gewahrt. Dadurch
/// kann sich die Lösung nie verschlechtern.
/// </summary>
public static class Verbesserer
{
    /// <summary>Verbessert <paramref name="loesung"/> in place bis zum Zeitlimit oder Stillstand.</summary>
    public static void VerbessereSA(Kontext k, string[] loesung, double zeitLimitSek)
    {
        int nL = k.NL, nE = k.NE;
        var ist = new double[nL];
        NeuAufbauIst(k, loesung, ist);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int passOhneVerbesserung = 0;

        while (sw.Elapsed.TotalSeconds < zeitLimitSek)
        {
            bool improved = false;

            // ---------------- Transfer-Block ----------------
            for (int li = 0; li < nL; li++)
            {
                if (k.Lehrer[li].SollWst <= 0) continue;
                if (ist[li] <= k.Lehrer[li].SollWst) continue;

                for (int e = 0; e < nE; e++)
                {
                    if (loesung[e] != k.Lehrer[li].Name) continue;
                    if (Regeln.IstFixiert(k.Eintraege[e].UrsprungsLehrer)) continue;

                    bool istUv = Regeln.IstUv(k.Eintraege[e].Fach);
                    var grp = new List<int>();
                    double grpWert = 0;
                    if (istUv) { grp.Add(e); grpWert = k.Eintraege[e].WertUv; }
                    else
                    {
                        for (int ek = 0; ek < nE; ek++)
                        {
                            if (loesung[ek] != k.Lehrer[li].Name) continue;
                            if (Regeln.IstFixiert(k.Eintraege[ek].UrsprungsLehrer)) continue;
                            if (!GleichKF(k, ek, e)) continue;
                            grp.Add(ek); grpWert += k.Eintraege[ek].WertUv;
                        }
                    }

                    for (int lj = 0; lj < nL; lj++)
                    {
                        if (lj == li) continue;
                        if (k.Lehrer[lj].SollWst <= 0) continue;
                        if (!FachLogik.KannFach(k.Lehrer[lj], k.Eintraege[e].Fach, k.Eintraege[e].IstOberstufe)) continue;
                        if (Score.HatAntiPflicht(k, k.Lehrer[lj].Name, k.Eintraege[e].Klasse, k.Eintraege[e].Fach)) continue;

                        double liNeu = ist[li] - grpWert;
                        double ljNeu = ist[lj] + grpWert;
                        double abwAlt = Math.Abs(ist[li] - k.Lehrer[li].SollWst) + Math.Abs(ist[lj] - k.Lehrer[lj].SollWst);
                        double abwNeu = Math.Abs(liNeu - k.Lehrer[li].SollWst) + Math.Abs(ljNeu - k.Lehrer[lj].SollWst);
                        if (abwNeu >= abwAlt) continue;

                        // Konsistenz (für Nicht-UV): jede Gruppenkomponente übertragbar?
                        bool konsOK = true;
                        if (!istUv)
                        {
                            foreach (int gi in grp)
                            {
                                if (!FachLogik.KannFach(k.Lehrer[lj], k.Eintraege[gi].Fach, k.Eintraege[gi].IstOberstufe))
                                { konsOK = false; break; }
                                for (int ef = 0; ef < nE; ef++)
                                {
                                    if (!GleichKF(k, ef, gi)) continue;
                                    if (Regeln.IstFixiert(k.Eintraege[ef].UrsprungsLehrer) &&
                                        !string.Equals(k.Eintraege[ef].UrsprungsLehrer, k.Lehrer[lj].Name, StringComparison.OrdinalIgnoreCase))
                                    { konsOK = false; break; }
                                }
                                if (!konsOK) break;
                            }
                        }
                        if (!konsOK) continue;

                        if (istUv && Score.LehrerUvStunden(k, lj, loesung) + grpWert > 2) continue;

                        // Übertragung
                        ist[li] -= grpWert; ist[lj] += grpWert;
                        foreach (int gi in grp) loesung[gi] = k.Lehrer[lj].Name;
                        improved = true;
                        break; // nächster Eintrag von li
                    }
                }
            }

            // ---------------- Tausch-Block ----------------
            for (int li = 0; li < nL; li++)
            {
                if (k.Lehrer[li].SollWst <= 0) continue;
                if (ist[li] <= k.Lehrer[li].SollWst) continue;

                for (int e = 0; e < nE; e++)
                {
                    if (loesung[e] != k.Lehrer[li].Name) continue;
                    if (Regeln.IstFixiert(k.Eintraege[e].UrsprungsLehrer)) continue;

                    bool eUv = Regeln.IstUv(k.Eintraege[e].Fach);
                    var grpX = new List<int>();
                    double wertX = 0;
                    if (eUv) { grpX.Add(e); wertX = k.Eintraege[e].WertUv; }
                    else
                    {
                        for (int ekx = 0; ekx < nE; ekx++)
                        {
                            if (loesung[ekx] != k.Lehrer[li].Name) continue;
                            if (Regeln.IstFixiert(k.Eintraege[ekx].UrsprungsLehrer)) continue;
                            if (!GleichKF(k, ekx, e)) continue;
                            grpX.Add(ekx); wertX += k.Eintraege[ekx].WertUv;
                        }
                    }
                    if (grpX[0] != e) continue; // nur Repräsentant, verhindert Doppelverarbeitung

                    for (int lj = 0; lj < nL; lj++)
                    {
                        if (lj == li) continue;
                        if (k.Lehrer[lj].SollWst <= 0) continue;

                        for (int eY = 0; eY < nE; eY++)
                        {
                            if (loesung[eY] != k.Lehrer[lj].Name) continue;
                            if (Regeln.IstFixiert(k.Eintraege[eY].UrsprungsLehrer)) continue;

                            bool yUv = Regeln.IstUv(k.Eintraege[eY].Fach);
                            var grpY = new List<int>();
                            double wertY = 0;
                            if (yUv) { grpY.Add(eY); wertY = k.Eintraege[eY].WertUv; }
                            else
                            {
                                for (int eky = 0; eky < nE; eky++)
                                {
                                    if (loesung[eky] != k.Lehrer[lj].Name) continue;
                                    if (Regeln.IstFixiert(k.Eintraege[eky].UrsprungsLehrer)) continue;
                                    if (!GleichKF(k, eky, eY)) continue;
                                    grpY.Add(eky); wertY += k.Eintraege[eky].WertUv;
                                }
                            }
                            if (grpY[0] != eY) continue;

                            // Vollständigkeit: keine dritte Lehrkraft mit gleicher Klasse+Fach
                            if (!eUv && DritterLehrer(k, loesung, e, k.Lehrer[li].Name)) continue;
                            if (!yUv && DritterLehrer(k, loesung, eY, k.Lehrer[lj].Name)) continue;

                            // Qualifikation beidseitig + Anti-Pflicht
                            bool tausOK = true;
                            foreach (int gx in grpX)
                            {
                                if (!FachLogik.KannFach(k.Lehrer[lj], k.Eintraege[gx].Fach, k.Eintraege[gx].IstOberstufe)) { tausOK = false; break; }
                                if (Score.HatAntiPflicht(k, k.Lehrer[lj].Name, k.Eintraege[gx].Klasse, k.Eintraege[gx].Fach)) { tausOK = false; break; }
                            }
                            if (!tausOK) continue;
                            foreach (int gy in grpY)
                            {
                                if (!FachLogik.KannFach(k.Lehrer[li], k.Eintraege[gy].Fach, k.Eintraege[gy].IstOberstufe)) { tausOK = false; break; }
                                if (Score.HatAntiPflicht(k, k.Lehrer[li].Name, k.Eintraege[gy].Klasse, k.Eintraege[gy].Fach)) { tausOK = false; break; }
                            }
                            if (!tausOK) continue;

                            // UV-Constraint nach Tausch
                            if (yUv && !eUv && Score.LehrerUvStunden(k, li, loesung) + wertY > 2) continue;
                            if (eUv && !yUv && Score.LehrerUvStunden(k, lj, loesung) + wertX > 2) continue;

                            double liNeuT = ist[li] - wertX + wertY;
                            double ljNeuT = ist[lj] - wertY + wertX;
                            double abwAltT = Math.Abs(ist[li] - k.Lehrer[li].SollWst) + Math.Abs(ist[lj] - k.Lehrer[lj].SollWst);
                            double abwNeuT = Math.Abs(liNeuT - k.Lehrer[li].SollWst) + Math.Abs(ljNeuT - k.Lehrer[lj].SollWst);
                            if (abwNeuT >= abwAltT) continue;

                            foreach (int gx in grpX) loesung[gx] = k.Lehrer[lj].Name;
                            foreach (int gy in grpY) loesung[gy] = k.Lehrer[li].Name;
                            ist[li] = liNeuT; ist[lj] = ljNeuT;
                            improved = true;
                            goto NaechsterEintragTausch;
                        }
                    }
                    NaechsterEintragTausch: ;
                }
            }

            if (!improved)
            {
                passOhneVerbesserung++;
                if (passOhneVerbesserung >= 3) break;
            }
            else
            {
                passOhneVerbesserung = 0;
            }
        }

        // Konsistenz-Nachbearbeitung: gleiche Klasse+Fach (kein UV) → selber Lehrer
        for (int ep = 0; ep < nE; ep++)
        {
            if (Regeln.IstFixiert(k.Eintraege[ep].UrsprungsLehrer)) continue;
            if (!Regeln.IstBelegt(loesung[ep])) continue;
            for (int ep2 = ep + 1; ep2 < nE; ep2++)
            {
                if (Regeln.IstFixiert(k.Eintraege[ep2].UrsprungsLehrer)) continue;
                if (!GleichKF(k, ep2, ep)) continue;
                if (!Regeln.IstUv(k.Eintraege[ep2].Fach))
                    loesung[ep2] = loesung[ep];
            }
        }
    }

    /// <summary>Gibt es eine dritte Lehrkraft (≠ <paramref name="lehrerName"/>, nicht fixiert)
    /// mit gleicher Klasse+Fach wie Eintrag <paramref name="eRef"/>? Dann ist die Gruppe nicht vollständig.</summary>
    private static bool DritterLehrer(Kontext k, string[] loesung, int eRef, string lehrerName)
    {
        for (int ev = 0; ev < k.NE; ev++)
        {
            if (Regeln.IstFixiert(k.Eintraege[ev].UrsprungsLehrer)) continue;
            if (!GleichKF(k, ev, eRef)) continue;
            if (loesung[ev] != lehrerName) return true;
        }
        return false;
    }

    private static bool GleichKF(Kontext k, int a, int b) =>
        string.Equals(k.Eintraege[a].Klasse, k.Eintraege[b].Klasse, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(k.Eintraege[a].Fach, k.Eintraege[b].Fach, StringComparison.OrdinalIgnoreCase);

    private static void NeuAufbauIst(Kontext k, string[] loesung, double[] ist)
    {
        for (int i = 0; i < ist.Length; i++) ist[i] = 0;
        for (int e = 0; e < k.NE; e++)
        {
            if (!Regeln.IstBelegt(loesung[e])) continue;
            int li = k.LehrerIdx(loesung[e]);
            if (li >= 0) ist[li] += k.Eintraege[e].WertUv;
        }
    }
}
