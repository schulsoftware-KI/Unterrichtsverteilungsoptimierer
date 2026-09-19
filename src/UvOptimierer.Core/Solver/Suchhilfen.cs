using UvOptimierer.Core.Fachlogik;
using UvOptimierer.Core.Modell;

namespace UvOptimierer.Core.Solver;

/// <summary>
/// Gemeinsam genutzte Suchhilfen der Solver: Zurücksetzen der Ist-Stunden, zentrale
/// Kapazitätsprüfung (inkl. individueller Überlastgrenze) und Schutzfachgruppen-Flags.
/// </summary>
public static class Suchhilfen
{
    /// <summary>
    /// Portierung von <c>ResetIstWSt</c>: setzt alle Ist-Stunden auf 0, fixiert die
    /// Zuweisung fixierter Einträge (<see cref="Eintrag.Lehrer"/> = UrsprungsLehrer) und
    /// zählt deren Wertstunden auf die jeweilige Lehrkraft. Nicht fixierte Einträge → „?".
    /// </summary>
    public static void ResetIstWst(Kontext k)
    {
        foreach (var l in k.Lehrer) l.IstWst = 0;
        foreach (var e in k.Eintraege)
        {
            if (Regeln.IstFixiert(e.UrsprungsLehrer))
            {
                e.Lehrer = e.UrsprungsLehrer;
                int idx = k.LehrerIdx(e.UrsprungsLehrer);
                if (idx >= 0) k.Lehrer[idx].IstWst += e.WertUv;
            }
            else
            {
                e.Lehrer = "?";
            }
        }
    }

    /// <summary>
    /// Zentrale Kapazitätsprüfung: verträgt die Lehrkraft <paramref name="l"/> die zusätzlichen
    /// <paramref name="wertUv"/> Stunden bei Toleranz <paramref name="toleranz"/>?
    /// Kombiniert die VBA-Prüfung <c>istWSt + wertUV &gt; sollWst + toleranz</c> mit der
    /// individuellen Überlastgrenze <see cref="Lehrer.MaxUeberlast"/> (harte Bedingung).
    /// </summary>
    public static bool KapazitaetOk(Lehrer l, double wertUv, double toleranz)
    {
        if (toleranz < Kontext.KeineKapazitaet && l.IstWst + wertUv > l.SollWst + toleranz)
            return false;
        if (l.MaxUeberlast is double mu && l.IstWst + wertUv > l.SollWst + mu)
            return false;
        return true;
    }

    /// <summary>Schlüssel der Fachgruppe eines Fachs (für Schutzfachgruppen). Portierung von <c>FachEngpassKey</c>.</summary>
    public static string FachEngpassKey(Kontext k, string fach)
    {
        string f = FachLogik.NormLeer(fach);
        if (f == "") return "";
        if (k.Fachgruppen.TryGetValue(f, out var g)) return g;
        int pos = f.IndexOf(' ');
        if (pos > 0)
        {
            string prx = f[..pos].Trim();
            if (k.Fachgruppen.TryGetValue(prx, out var g2)) return g2;
        }
        return "";
    }

    /// <summary>
    /// Portierung von <c>SchutzFlagsSetzen</c>: markiert Lehrkräfte, die zu einer aktiven
    /// Schutzfachgruppe gehören. Ohne aktive Schutzgruppen bleibt alles inaktiv.
    /// </summary>
    public static void SchutzFlagsSetzen(Kontext k)
    {
        string fg1 = k.P.SchutzFg1, fg2 = k.P.SchutzFg2;
        foreach (var l in k.Lehrer)
        {
            l.IstSchutzLehrer = false;
            if (fg1 == "" && fg2 == "") continue;
            foreach (var fach in l.Faecher)
            {
                if (string.IsNullOrEmpty(fach)) continue;
                string sfk = FachEngpassKey(k, fach);
                if (fg1 != "" && string.Equals(sfk, fg1, StringComparison.OrdinalIgnoreCase)) { l.IstSchutzLehrer = true; break; }
                if (fg2 != "" && string.Equals(sfk, fg2, StringComparison.OrdinalIgnoreCase)) { l.IstSchutzLehrer = true; break; }
            }
        }
    }
}
