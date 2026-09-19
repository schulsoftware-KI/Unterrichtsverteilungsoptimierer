using System.ComponentModel;

namespace UvOptimierer.Core.Modell;

/// <summary>
/// Score- und Ablaufparameter aus dem Blatt „Parameter" (Werte in Spalte B).
/// Die Standardwerte entsprechen exakt <c>ParameterEinlesen</c> im VBA-Original.
/// Zeilenangaben beziehen sich auf das Parameter-Blatt. Die <see cref="DisplayNameAttribute"/>-
/// Annotationen dienen dem Parameter-Editor in der Oberfläche.
/// </summary>
public sealed class ScoreParameter
{
    // ---- Score-Gewichte ----
    [Category("1 Allgemein"), DisplayName("Toleranz (WSt)"),
     Description("Erlaubte Ist-Soll-Abweichung, bevor Über-/Unterlast wirkt.")]
    public double Toleranz { get; set; } = 2;      // Zeile 3

    [Category("2 Score-Gewichte"), DisplayName("Klassenleitung")]
    public double ScoreKl { get; set; } = 100;     // Zeile 5

    [Category("2 Score-Gewichte"), DisplayName("Wunsch Prio 3")]
    public double ScoreW3 { get; set; } = 80;      // Zeile 6

    [Category("2 Score-Gewichte"), DisplayName("Wunsch Prio 2")]
    public double ScoreW2 { get; set; } = 40;      // Zeile 7

    [Category("2 Score-Gewichte"), DisplayName("Wunsch Prio 1")]
    public double ScoreW1 { get; set; } = 15;      // Zeile 8

    [Category("2 Score-Gewichte"), DisplayName("Anti-Wunsch Prio 2")]
    public double ScoreA2 { get; set; } = 40;      // Zeile 9

    [Category("2 Score-Gewichte"), DisplayName("Anti-Wunsch Prio 1")]
    public double ScoreA1 { get; set; } = 15;      // Zeile 10

    [Category("2 Score-Gewichte"), DisplayName("Kontinuität")]
    public double ScoreKont { get; set; } = 8;     // Zeile 11

    [Category("2 Score-Gewichte"), DisplayName("Faktor freie Stunden")]
    public double ScoreFreiF { get; set; } = 3;    // Zeile 12

    [Category("2 Score-Gewichte"), DisplayName("Faktor Überlastung")]
    public double ScoreUeberF { get; set; } = 10;  // Zeile 13

    [Category("2 Score-Gewichte"), DisplayName("Faktor Unterbesetzung")]
    public double ScoreUnterF { get; set; } = 5;   // Zeile 14

    // ---- Ablauf / Zeitlimits ----
    [Category("1 Allgemein"), DisplayName("Zeitlimit (Sek.)"),
     Description("Zeitlimit für CP-SAT bzw. die BT-Phase und die Nachverbesserung je Lösung.")]
    public double Zeitlimit { get; set; } = 60;    // Zeile 15

    [Category("1 Allgemein"), DisplayName("Globale Max-Überlast (WSt, 0=aus)"),
     Description("Obergrenze für die Überlastung JEDER Lehrkraft (IstWst ≤ SollWst + Wert). " +
                 "Gilt als Vorgabe für alle, die keine eigene Max-Überlast-Spalte haben. " +
                 "Hart in CP-SAT und Backtracking, im SA als Strafe. 0 = keine globale Grenze.")]
    public double GlobalMaxUeberlast { get; set; } = 0;   // Zeile 30

    // ---- Schutzfachgruppen ----
    [Category("3 Schutzfachgruppen"), DisplayName("Schutzfachgruppe 1")]
    public string SchutzFg1 { get; set; } = "";    // Zeile 17

    [Category("3 Schutzfachgruppen"), DisplayName("Schutzfachgruppe 2")]
    public string SchutzFg2 { get; set; } = "";    // Zeile 18

    [Category("3 Schutzfachgruppen"), DisplayName("Schutz-Malus")]
    public double SchutzMalus { get; set; } = 20;  // Zeile 19

    // ---- SA-Iterationen ----
    [Category("4 Simulated Annealing"), DisplayName("SA-Iterationen (0=auto)")]
    public long SaIter { get; set; } = 0;          // Zeile 21

    [Category("4 Simulated Annealing"), DisplayName("Sim-Iterationen")]
    public long SimIter { get; set; } = 12000;     // Zeile 22

    [Category("4 Simulated Annealing"), DisplayName("Sim-Wiederholungen")]
    public long SimNrep { get; set; } = 3;         // Zeile 23

    [Category("4 Simulated Annealing"), DisplayName("Sim-Verbesserung (Sek.)")]
    public double SimVerbSek { get; set; } = 3;    // Zeile 24

    // ---- CP-SAT-spezifische Gewichte (Zeile 26–29) ----
    [Category("5 CP-SAT"), DisplayName("Überlast-Strafe (Score/Std)"),
     Description("Strafe je Stunde über der oberen Toleranzgrenze. Standard 50 = bisheriges Verhalten.")]
    public double CpUeberGewicht { get; set; } = 50;   // Zeile 26

    [Category("5 CP-SAT"), DisplayName("Unterlast-Strafe (Score/Std)"),
     Description("Strafe je Stunde unter der unteren Toleranzgrenze. 0 = aus (bisheriges Verhalten).")]
    public double CpUnterGewicht { get; set; } = 0;    // Zeile 27

    [Category("5 CP-SAT"), DisplayName("Unbesetzt-Strafe (Score/Eintrag)"),
     Description("Strafe je nicht besetztem Eintrag. Standard 500.")]
    public double CpUnbesetztGewicht { get; set; } = 500;  // Zeile 28

    [Category("5 CP-SAT"), DisplayName("Harte Toleranzgrenzen"),
     Description("Wenn wahr, erzwingt CP-SAT die Toleranzgrenzen hart (statt sie zu bestrafen). " +
                 "Achtung: kann das Modell unlösbar machen.")]
    public bool HarteGrenzen { get; set; } = false;    // Zeile 29

    /// <summary>Flache Kopie (alle Felder sind Werttypen bzw. string) – für den Parameter-Editor.</summary>
    public ScoreParameter Clone() => (ScoreParameter)MemberwiseClone();
}
