using Google.OrTools.Sat;
using UvOptimierer.Core.Bewertung;
using UvOptimierer.Core.Fachlogik;
using UvOptimierer.Core.Modell;
using UvOptimierer.Core.Solver;

namespace UvOptimierer.CpSat;

/// <summary>
/// Drittes Lösungsverfahren: exakte Optimierung mit Google OR-Tools (CP-SAT).
/// <para>
/// Modelliert die Zuweisung als ganzzahliges Constraint-Programm. Harte Bedingungen:
/// nur fachlich qualifizierte Lehrkräfte (KannFach), kein absoluter Anti-Wunsch (Prio 3),
/// höchstens 2 UV-Wertstunden je Lehrkraft, Konsistenz (gleiche Klasse+Fach → derselbe
/// Lehrer, außer UV) sowie – falls gesetzt – die individuelle Überlastgrenze
/// <see cref="Lehrer.MaxUeberlast"/>. Die reguläre Kapazitätstoleranz und unbesetzte
/// Einträge gehen als Strafterme in die Zielfunktion ein.
/// </para>
/// <para>
/// <b>Hinweis zur Zielfunktion:</b> CP-SAT benötigt eine <em>lineare</em> Zielfunktion.
/// Optimiert werden daher die zuweisungs­lokalen, ordnungsunabhängigen Score-Anteile
/// (Klassenleitung, Wünsche/Anti-Wünsche) minus Kapazitäts- und Unbesetzt-Strafen. Die
/// last­abhängigen, ordnungsabhängigen Terme des VBA-Scores (freie-Stunden-Faktor,
/// Kontinuität, Schutzmalus) lassen sich nicht exakt linearisieren; das Ergebnis wird
/// deshalb anschließend mit demselben <see cref="Score.GesamtScore"/> wie die anderen
/// Verfahren bewertet, sodass die Lösungen vergleichbar bleiben.
/// </para>
/// </summary>
public sealed class CpSatSolver : ILoesungsSolver
{
    public string Name => "CP-SAT (OR-Tools)";

    private const long Scale = 100;        // Stunden → Zenti-Stunden (ganzzahlig)
    private const long ScoreScale = 100;   // Score → ganzzahlige Zielkoeffizienten

    /// <inheritdoc/>
    public string? LetzteWarnung { get; private set; }
    private const long UvGrenze = 2 * Scale;

    public bool Loese(Kontext k, string[] loesung, SolverOptionen opt)
    {
        int nE = k.NE, nL = k.NL;
        k.Toleranz = k.P.Toleranz;
        long capTol = Runden(k.P.Toleranz);

        bool hatSperre = opt.HatSperre;
        int sperrIdx = opt.HatSperre ? opt.SperrIdx : -1;
        string sperrName = opt.HatSperre ? opt.SperrName : "";

        // Ausgangszustand: alles „?", fixierte Einträge gesetzt.
        for (int e = 0; e < nE; e++) loesung[e] = "?";
        for (int e = 0; e < nE; e++)
            if (Regeln.IstFixiert(k.Eintraege[e].UrsprungsLehrer))
                loesung[e] = k.Eintraege[e].UrsprungsLehrer;

        // --------------------------------------------------------
        // Fixe Lasten + gruppen-erzwungene Zuweisungen bestimmen
        // --------------------------------------------------------
        var fixedLoad = new long[nL];
        var fixedUv = new long[nL];
        var forced = new string?[nE];   // Name, wenn Eintrag ohne Entscheidung feststeht

        // Für nicht-UV-Gruppen (Klasse+Fach) den erzwingenden Fix-Lehrer ermitteln.
        var gruppeFix = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int e = 0; e < nE; e++)
        {
            var ein = k.Eintraege[e];
            if (Regeln.IstUv(ein.Fach)) continue;
            if (!Regeln.IstFixiert(ein.UrsprungsLehrer)) continue;
            string key = GruppenKey(ein);
            if (!gruppeFix.ContainsKey(key)) gruppeFix[key] = ein.UrsprungsLehrer;
        }

        for (int e = 0; e < nE; e++)
        {
            var ein = k.Eintraege[e];
            long w = Runden(ein.WertUv);
            if (Regeln.IstFixiert(ein.UrsprungsLehrer))
            {
                forced[e] = ein.UrsprungsLehrer;
                Belaste(k, fixedLoad, fixedUv, ein, ein.UrsprungsLehrer, w);
            }
            else if (!Regeln.IstUv(ein.Fach) && gruppeFix.TryGetValue(GruppenKey(ein), out var ft))
            {
                forced[e] = ft;
                loesung[e] = ft;
                Belaste(k, fixedLoad, fixedUv, ein, ft, w);
            }
        }

        // --------------------------------------------------------
        // Entscheidungs-Slots bilden (Konsistenz durch Gruppierung)
        // --------------------------------------------------------
        var slots = new List<Slot>();
        var slotProGruppe = new Dictionary<string, Slot>(StringComparer.Ordinal);

        for (int e = 0; e < nE; e++)
        {
            if (forced[e] != null) continue;   // fixiert oder gruppen-erzwungen
            var ein = k.Eintraege[e];

            if (Regeln.IstUv(ein.Fach))
            {
                slots.Add(new Slot { IstUv = true, Members = { e } });
            }
            else
            {
                string key = GruppenKey(ein);
                if (!slotProGruppe.TryGetValue(key, out var slot))
                {
                    slot = new Slot { IstUv = false };
                    slotProGruppe[key] = slot;
                    slots.Add(slot);
                }
                slot.Members.Add(e);
            }
        }

        // Kandidaten + Gewicht je Slot bestimmen.
        foreach (var slot in slots)
        {
            int rep = slot.Members[0];
            var einRep = k.Eintraege[rep];
            long weight = 0;
            foreach (int e in slot.Members) weight += Runden(k.Eintraege[e].WertUv);
            slot.Weight = weight;

            bool slotHatSperre = hatSperre && slot.Members.Contains(sperrIdx);
            for (int i = 0; i < nL; i++)
            {
                if (slotHatSperre && k.Lehrer[i].Name == sperrName) continue;
                if (!FachLogik.KannFach(k.Lehrer[i], einRep.Fach, einRep.IstOberstufe)) continue;
                if (Score.HatAntiPflicht(k, k.Lehrer[i].Name, einRep.Klasse, einRep.Fach)) continue;
                slot.Cand.Add(i);
            }
        }

        // --------------------------------------------------------
        // CP-SAT-Modell
        // --------------------------------------------------------
        var model = new CpModel();
        var teacherTerms = new List<(BoolVar, long)>[nL];
        var teacherUvTerms = new List<(BoolVar, long)>[nL];
        for (int i = 0; i < nL; i++) { teacherTerms[i] = new(); teacherUvTerms[i] = new(); }

        var ziel = LinearExpr.NewBuilder();

        for (int sIdx = 0; sIdx < slots.Count; sIdx++)
        {
            var slot = slots[sIdx];
            var lits = new List<ILiteral>();
            slot.Vars = new BoolVar[slot.Cand.Count];
            for (int c = 0; c < slot.Cand.Count; c++)
            {
                int t = slot.Cand[c];
                var v = model.NewBoolVar($"x_s{sIdx}_t{t}");
                slot.Vars[c] = v;
                lits.Add(v);

                teacherTerms[t].Add((v, slot.Weight));
                if (slot.IstUv) teacherUvTerms[t].Add((v, slot.Weight));

                long koeff = 0;
                foreach (int e in slot.Members)
                    koeff += (long)Math.Round(StatischerScore(k, t, e) * ScoreScale);
                if (koeff != 0) ziel.AddTerm(v, koeff);
            }

            var un = model.NewBoolVar($"un_s{sIdx}");
            slot.Un = un;
            lits.Add(un);
            ziel.AddTerm(un, -(long)Math.Round(k.P.CpUnbesetztGewicht * ScoreScale));

            model.AddExactlyOne(lits);
        }

        // Kapazität: harte individuelle Überlastgrenze + UV-Grenze + weiche Toleranzstrafe.
        long obergrenze = 0;
        for (int e = 0; e < nE; e++) obergrenze += Runden(k.Eintraege[e].WertUv);

        for (int i = 0; i < nL; i++)
        {
            var lehr = k.Lehrer[i];

            // UV-Grenze (hart): fixe UV-Last + Entscheidungs-UV ≤ 2.
            if (teacherUvTerms[i].Count > 0)
            {
                long uvRest = UvGrenze - fixedUv[i];
                if (uvRest < 0) uvRest = 0;
                model.Add(SummeExpr(teacherUvTerms[i]) <= uvRest);
            }

            if (teacherTerms[i].Count == 0) continue;
            var last = SummeExpr(teacherTerms[i]);

            // Individuelle Überlastgrenze (hart).
            if (lehr.MaxUeberlast is double mu)
            {
                long capMax = Runden(lehr.SollWst + mu) - fixedLoad[i];
                if (capMax < 0) capMax = 0;
                model.Add(last <= capMax);
            }

            // Individuelle Unterlastgrenze (hart): Gesamtlast ≥ Soll − MaxUnterlast.
            if (lehr.MaxUnterlast is double mn)
            {
                long capMin = Runden(lehr.SollWst - mn) - fixedLoad[i];
                if (capMin > 0) model.Add(last >= capMin);
            }

            // Toleranzgrenzen (Zenti-Stunden), Entscheidungslast bezogen (fixe Last herausgerechnet).
            long capOben = Runden(lehr.SollWst) + capTol - fixedLoad[i];
            long capUnten = Runden(lehr.SollWst) - capTol - fixedLoad[i];

            if (k.P.HarteGrenzen)
            {
                // Toleranz hart erzwingen (kann das Modell unlösbar machen).
                model.Add(last <= capOben);
                if (capUnten > 0) model.Add(last >= capUnten);
            }
            else
            {
                long gUeber = (long)Math.Round(k.P.CpUeberGewicht);
                if (gUeber > 0)
                {
                    var over = model.NewIntVar(0, obergrenze, $"over_t{i}");
                    model.Add(last - over <= capOben);
                    ziel.AddTerm(over, -gUeber);   // gUeber Score-Einheiten je Überlaststunde
                }

                long gUnter = (long)Math.Round(k.P.CpUnterGewicht);
                if (gUnter > 0 && capUnten > 0)
                {
                    var under = model.NewIntVar(0, capUnten, $"under_t{i}");
                    model.Add(last + under >= capUnten);
                    ziel.AddTerm(under, -gUnter);  // gUnter Score-Einheiten je Unterlaststunde
                }
            }
        }

        model.Maximize(ziel);

        // --------------------------------------------------------
        // Lösen
        // --------------------------------------------------------
        var solver = new CpSolver();
        var parameter = new List<string> { "num_search_workers:8" };
        if (opt.ZeitlimitSek > 0) parameter.Add($"max_time_in_seconds:{opt.ZeitlimitSek.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        solver.StringParameters = string.Join(",", parameter);

        CpSolverStatus status = solver.Solve(model);
        bool loesbar = status is CpSolverStatus.Optimal or CpSolverStatus.Feasible;

        LetzteWarnung = null;
        if (!loesbar)
        {
            LetzteWarnung = status == CpSolverStatus.Infeasible
                ? "CP-SAT: Keine zulässige Lösung – die harten Bedingungen widersprechen sich "
                  + "(z. B. „Harte Toleranzgrenzen“ oder zu enge Max-Über-/Unterlastgrenzen). "
                  + "Bitte diese Grenzen lockern oder auf weiche Strafen umstellen."
                : "CP-SAT: Innerhalb des Zeitlimits wurde keine gültige Lösung gefunden. "
                  + "Zeitlimit erhöhen oder harte Grenzen lockern.";
        }

        if (loesbar)
        {
            foreach (var slot in slots)
            {
                string name = "?";
                for (int c = 0; c < slot.Cand.Count; c++)
                {
                    if (solver.BooleanValue(slot.Vars![c]))
                    {
                        name = k.Lehrer[slot.Cand[c]].Name;
                        break;
                    }
                }
                foreach (int e in slot.Members) loesung[e] = name;
            }
        }

        // Zustand konsistent hinterlassen (IstWst aus Lösung).
        Suchhilfen.ResetIstWst(k);
        for (int e = 0; e < nE; e++)
        {
            if (Regeln.IstFixiert(k.Eintraege[e].UrsprungsLehrer)) continue;
            if (!Regeln.IstBelegt(loesung[e])) continue;
            int idx = k.LehrerIdx(loesung[e]);
            if (idx >= 0) k.Lehrer[idx].IstWst += k.Eintraege[e].WertUv;
        }

        for (int e = 0; e < nE; e++)
            if (!Regeln.IstBelegt(loesung[e])) return false;
        return loesbar;
    }

    // ------------------------------------------------------------
    // Hilfsmittel
    // ------------------------------------------------------------
    private sealed class Slot
    {
        public bool IstUv;
        public List<int> Members { get; } = new();
        public long Weight;
        public List<int> Cand { get; } = new();
        public BoolVar[]? Vars;
        public BoolVar? Un;
    }

    private static string GruppenKey(Eintrag e)
        => e.Klasse.ToLowerInvariant() + "\u0001" + e.Fach.ToLowerInvariant();

    private static long Runden(double stunden) => (long)Math.Round(stunden * Scale);

    private static LinearExpr SummeExpr(List<(BoolVar v, long w)> terme)
    {
        var b = LinearExpr.NewBuilder();
        foreach (var (v, w) in terme) b.AddTerm(v, w);
        return b;
    }

    private static void Belaste(Kontext k, long[] load, long[] uv, Eintrag ein, string name, long w)
    {
        int idx = k.LehrerIdx(name);
        if (idx < 0) return;
        load[idx] += w;
        if (Regeln.IstUv(ein.Fach)) uv[idx] += w;
    }

    /// <summary>Ordnungsunabhängiger Score-Anteil (Klassenleitung + Wünsche/Anti-Wünsche).</summary>
    private static double StatischerScore(Kontext k, int i, int e)
    {
        var p = k.P;
        var lehr = k.Lehrer[i];
        var ein = k.Eintraege[e];
        double sc = 0;

        if (lehr.KlassenleitungKlasse == ein.Klasse && lehr.KlassenleitungKlasse != "")
            sc += p.ScoreKl;

        int wp = Score.WunschPrioFuer(k, lehr.Name, ein.Klasse, ein.Fach, positiv: true);
        if (wp == 3) sc += p.ScoreW3;
        else if (wp == 2) sc += p.ScoreW2;
        else if (wp == 1) sc += p.ScoreW1;

        int ap = Score.WunschPrioFuer(k, lehr.Name, ein.Klasse, ein.Fach, positiv: false);
        if (ap == 2) sc -= p.ScoreA2;
        else if (ap == 1) sc -= p.ScoreA1;

        return sc;
    }
}
