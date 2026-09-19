namespace UvOptimierer.Excel;

/// <summary>
/// Zentrale Definition der Blattnamen, Spalten- und Zeilenpositionen. Alle Werte spiegeln
/// exakt das VBA-Original (1-basierte Excel-Indizes).
/// </summary>
public static class SpaltenLayout
{
    // ---- Blattnamen ----
    public const string BlattLehrerliste = "Lehrerliste";
    public const string BlattKlassen = "Klassen";
    public const string BlattParameter = "Parameter";
    public const string BlattWuensche = "Lehrerwünsche";
    public const string BlattFachgruppen = "Fachgruppen";

    // ---- Lehrerliste (Header Zeile 1, Daten ab Zeile 2) ----
    public const int LehrerName = 1;         // A
    public const int LehrerFach1 = 2;        // B (Fach 1..20 → B..U)
    public const int LehrerFachAnzahl = 20;
    public const int LehrerKlassenleitung = 22; // V
    public const int LehrerSollWst = 23;        // W
    // „Max-Überlast": keine feste Spalte – wird dynamisch per Header-Scan gesucht.

    // ---- Klassen (Header Zeile 1, Daten ab Zeile 2) ----
    public const int KlasseKlasse = 1;   // A
    public const int KlasseFach = 2;     // B
    public const int KlasseWst = 3;      // C
    public const int KlasseL1 = 4;       // D  (Ergebnis Lösung 1)
    public const int KlasseL2 = 5;       // E  (Ergebnis Lösung 2)
    public const int KlasseFix = 6;      // F  (fixierter Lehrer)
    public const int KlasseL3 = 7;       // G  (Ergebnis Lösung 3 – SA-Verbesserung)
    public const int KlasseL4 = 8;       // H  (Ergebnis Lösung 4 – SA-Verbesserung)
    public const int KlasseWert = 9;     // I  (WertUV)

    // ---- Lehrerwünsche (Header Zeile 1, Daten ab Zeile 2) ----
    public const int WunschLehrer = 1;      // A
    public const int WunschKlasse = 3;      // C
    public const int WunschPrio = 4;        // D
    public const int WunschAntiKlasse = 5;  // E
    public const int WunschAntiPrio = 6;    // F

    // ---- Parameter (Werte in Spalte B) ----
    public const int ParamSpalte = 2;
    public const int ParamToleranz = 3;
    public const int ParamKl = 5;
    public const int ParamW3 = 6;
    public const int ParamW2 = 7;
    public const int ParamW1 = 8;
    public const int ParamA2 = 9;
    public const int ParamA1 = 10;
    public const int ParamKont = 11;
    public const int ParamFreiF = 12;
    public const int ParamUeberF = 13;
    public const int ParamUnterF = 14;
    public const int ParamZeitlimit = 15;
    public const int ParamSchutzFg1 = 17;
    public const int ParamSchutzFg2 = 18;
    public const int ParamSchutzMalus = 19;
    public const int ParamSaIter = 21;
    public const int ParamSimIter = 22;
    public const int ParamSimNrep = 23;
    public const int ParamSimVerbSek = 24;
    public const int ParamCpUeber = 26;        // CP-SAT Überlast-Strafe (Score/Std)
    public const int ParamCpUnter = 27;        // CP-SAT Unterlast-Strafe (Score/Std)
    public const int ParamCpUnbesetzt = 28;    // CP-SAT Unbesetzt-Strafe (Score/Eintrag)
    public const int ParamHarteGrenzen = 29;   // Harte Toleranzgrenzen (0/1)
    public const int ParamGlobalMaxUeber = 30; // Globale Max-Überlast (WSt, 0=aus)
}
