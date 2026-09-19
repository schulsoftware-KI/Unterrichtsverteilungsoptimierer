using UvOptimierer.Core.Modell;

namespace UvOptimierer.Core.Fachlogik;

/// <summary>
/// Fach-Namens-Analyse und Qualifikationsprüfung. Portierung der VBA-Funktionen
/// <c>KannFach</c>, <c>FachStamm</c>, <c>FachLevelPraefix</c>, <c>FachGruppenPraefix</c>,
/// <c>FachSuffixPraefix</c> und <c>IstOberstufenFach</c>.
/// </summary>
public static class FachLogik
{
    /// <summary>Mehrfache Leerzeichen auf eines reduzieren und trimmen (Untis liefert z. B. „D  G1").</summary>
    public static string NormLeer(string s)
    {
        s = s.Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s;
    }

    private static bool IstZiffer(char c) => c >= '0' && c <= '9';

    /// <summary>„D L1" → „D", „EK L2" → „EK", sonst „".</summary>
    public static string FachLevelPraefix(string fach)
    {
        string f = NormLeer(fach);
        int pos = f.IndexOf(' ');
        if (pos <= 0) return "";
        string suffix = f[(pos + 1)..].Trim();
        if (suffix.Length < 2) return "";
        if (char.ToUpperInvariant(suffix[0]) != 'L') return "";
        for (int k = 1; k < suffix.Length; k++)
            if (!IstZiffer(suffix[k])) return "";
        return f[..pos].Trim();
    }

    /// <summary>„M G1" → „M", sonst „" (G gefolgt von reinen Ziffern).</summary>
    public static string FachGruppenPraefix(string fach)
    {
        string f = NormLeer(fach);
        int pos = f.IndexOf(' ');
        if (pos <= 0) return "";
        string suffix = f[(pos + 1)..]; // im VBA hier bewusst nicht getrimmt
        if (suffix.Length < 2) return "";
        if (char.ToUpperInvariant(suffix[0]) != 'G') return "";
        for (int k = 1; k < suffix.Length; k++)
            if (!IstZiffer(suffix[k])) return "";
        return f[..pos].Trim();
    }

    /// <summary>
    /// Allgemeiner Buchstaben+Ziffer-Suffix (z. B. „EK K2" → „EK"), aber ohne reine
    /// G- und L-Gruppen (die decken die beiden Funktionen oben ab).
    /// </summary>
    public static string FachSuffixPraefix(string fach)
    {
        string f = NormLeer(fach);
        int pos = f.IndexOf(' ');
        if (pos <= 0) return "";
        string suffix = f[(pos + 1)..].Trim();
        if (suffix.Length < 2) return "";
        if (!char.IsLetter(suffix[0])) return "";          // muss mit Buchstabe beginnen
        if (!suffix.Any(IstZiffer)) return "";              // muss eine Ziffer enthalten
        char c0 = char.ToUpperInvariant(suffix[0]);
        if (c0 == 'G' && suffix.Length >= 2 && IstZiffer(suffix[1])) return ""; // "G<Ziffer>…"
        if (c0 == 'L' && suffix.Length >= 2 && IstZiffer(suffix[1])) return ""; // "L<Ziffer>…"
        return f[..pos].Trim();
    }

    /// <summary>
    /// Stamm eines Fachs: abschließende Ziffern entfernen. „D L1" → „D L", „SB2" → „SB",
    /// „D" → „" (ohne Endziffer kein Gruppenmitglied).
    /// </summary>
    public static string FachStamm(string fach)
    {
        string f = fach.Trim();
        int n = f.Length;
        if (n == 0) return "";
        while (n >= 1 && IstZiffer(f[n - 1])) n--;
        if (n == f.Length) return "";  // keine Endziffer
        if (n < 1) return "";
        return f[..n];
    }

    /// <summary>Oberstufenfach, wenn EF/Q1/Q2 enthalten oder ein Gruppen-/Level-/Suffix-Präfix greift.</summary>
    public static bool IstOberstufenFach(string fach)
    {
        if (string.IsNullOrEmpty(fach)) return false;
        string fu = fach.ToUpperInvariant();
        if (fu.Contains("EF") || fu.Contains("Q1") || fu.Contains("Q2")) return true;
        if (FachGruppenPraefix(fach) != "") return true;
        if (FachLevelPraefix(fach) != "") return true;
        if (FachSuffixPraefix(fach) != "") return true;
        return false;
    }

    /// <summary>
    /// Kann die Lehrkraft das Fach unterrichten? Exakter Treffer immer erlaubt; sonst
    /// Stamm-Matching (gleicher Stamm nach Entfernen der Endziffern). Bei Oberstufe muss
    /// zusätzlich <see cref="Lehrer.OberstufeOk"/> für die passende Fachspalte gesetzt sein.
    /// </summary>
    public static bool KannFach(Lehrer lehr, string fach, bool oberstufe)
    {
        for (int f = 0; f < lehr.Faecher.Length; f++)
        {
            string lf = lehr.Faecher[f];
            if (string.IsNullOrEmpty(lf)) continue;
            if (string.Equals(lf, fach, StringComparison.OrdinalIgnoreCase))
                return true;
            string p1 = FachStamm(lf);
            string p2 = FachStamm(fach);
            if (p1 != "" && p2 != "" && string.Equals(p1, p2, StringComparison.OrdinalIgnoreCase))
                return oberstufe ? lehr.OberstufeOk[f] : true;
        }
        return false;
    }
}
