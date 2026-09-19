# UV-Optimierer

Portierung des VBA-Systems zur **Unterrichtsverteilung** nach C# / .NET 8.
Excel bleibt Datenformat: Das Programm liest die Blätter `Lehrerliste`, `Klassen`,
`Parameter` und `Lehrerwünsche`, optimiert die Verteilung und schreibt zwei
alternative Lösungen zurück in `Klassen` (Spalten **D** = Lösung 1, **E** = Lösung 2).

## Projektaufbau

```
UvOptimierer.sln
├─ src/
│  ├─ UvOptimierer.Core     Modell + Bewertung + Solver (keine Excel-/UI-Abhängigkeit)
│  ├─ UvOptimierer.Excel    Einlesen/Zurückschreiben via ClosedXML
│  ├─ UvOptimierer.CpSat    CP-SAT-Solver auf Basis von Google OR-Tools
│  ├─ UvOptimierer.App      Konsolen-Runner
│  └─ UvOptimierer.Ui       WinForms-Oberfläche (net8.0-windows)
└─ tests/
   └─ UvOptimierer.Tests    xUnit-Tests (Fachlogik, Score, Solver)
```

`Core` bleibt bewusst frei von externen Abhängigkeiten. OR-Tools steckt nur in
`CpSat`, Excel nur in `Excel`, WinForms nur in `Ui`.

### Core im Detail
- `Modell/` – `Lehrer`, `Eintrag`, `Wunsch`, `ScoreParameter`, `Regeln`, `Kontext`
  (`Kontext` ersetzt die globalen VBA-Variablen und trägt den veränderlichen Laufzeitzustand).
- `Fachlogik/FachLogik` – `FachStamm`, `KannFach`, `IstOberstufenFach`, Präfix-Erkennung
  (1:1-Portierung inkl. G-/L-Gruppen-Matching).
- `Bewertung/Score` – `Berechne` (= `BerechneScore`) und `GesamtScore` (= `SA_GesamtScore`).
- `Solver/` – `ILoesungsSolver`, `BacktrackingSolver` (Backtracking + Constraint-Propagation),
  `SaSolver` (Simulated Annealing mit Greedy-MRV-Start), `Optimierer`
  (2-Lösungen-Orchestrierung, SA mit Backtracking-Warmstart).

## Verfahren
- **Backtracking + CP** – iteratives Backtracking, MRV-Heuristik, Arc-Consistency,
  Toleranz-Fallback. Deterministisch.
- **Simulated Annealing** – Greedy-MRV-Start (bzw. Backtracking-Warmstart), dann
  Reassign/Swap-Nachbarschaft mit geometrischer Abkühlung; Konsistenz-Nachbearbeitung.
- **CP-SAT (OR-Tools)** – ganzzahliges Constraint-Programm mit harten Bedingungen
  (Fach-Eignung, UV ≤ 2, Konsistenz, individuelle Überlastgrenze) und linearer
  Ersatz-Zielfunktion (Klassenleitung + Wünsche minus Kapazitäts-/Unbesetzt-Strafen).
  Die last­abhängigen Score-Terme sind nicht exakt linearisierbar; das Ergebnis wird
  daher mit demselben `GesamtScore` wie die übrigen Verfahren bewertet und bleibt so
  vergleichbar. Optional lässt sich eine CP-SAT-Lösung später per SA nachpolieren.

Beide Verfahren erzeugen zwei Lösungen: Lösung 2 sperrt den ersten nicht-fixierten
belegten Eintrag der ersten Lösung und erzwingt so eine Alternative.

## Individuelle Überlastgrenze
Neue optionale Spalte **„Max-Überlast"** in `Lehrerliste` (Header wird dynamisch gesucht;
fehlt sie, gilt keine Grenze). Wo gesetzt, gilt `IstWst ≤ SollWst + Max-Überlast`:
im Backtracking als **harte** Bedingung, im Simulated Annealing als **Strafterm**.

## Build & Ausführung

> Voraussetzungen: .NET SDK 8.0 und Internetzugang beim ersten Build (NuGet stellt
> `ClosedXML` und `Google.OrTools` wieder her). Das `Ui`-Projekt zielt auf
> `net8.0-windows` und baut/läuft nur unter Windows; `Core`, `Excel`, `CpSat` und `App`
> sind plattformunabhängig.

```bash
dotnet restore
dotnet build -c Release
dotnet test                      # Tests ausführen

# Optimieren (SA ist Standard):
dotnet run --project src/UvOptimierer.App -- "UV-Muster_anon_Claude.xlsm" --sa
# Backtracking bzw. CP-SAT, eigener Ausgabepfad:
dotnet run --project src/UvOptimierer.App -- "eingabe.xlsm" --bt -o "ergebnis.xlsm"
dotnet run --project src/UvOptimierer.App -- "eingabe.xlsm" --cpsat

# Grafische Oberfläche (nur Windows):
dotnet run --project src/UvOptimierer.Ui
```

Optionen: `--sa` / `--bt` / `--cpsat`, `-o <ausgabe>`, `--seed <n>` (reproduzierbare
SA-Läufe). Ohne `-o` wird `<datei>_Ergebnis.<ext>` geschrieben. Das Zeitlimit der
Solver kommt aus dem Parameter-Blatt (Zeile 15).

## Status
Umgesetzt: alle drei Verfahren (SA, Backtracking, CP-SAT), Excel-I/O, Konsolen-Runner
und WinForms-Oberfläche. Als Nächstes sinnvoll: Import/Konsistenzprüfung,
Auswertungs-/Diagnose-Sheets und optionales SA-Nachpolieren der CP-SAT-Lösung.

---

## Unterrichtsverteilung (UV) erstellen und nach Untis zurückspielen

Dieser Abschnitt beschreibt den vollständigen Ablauf von den Untis-Rohdaten bis zur
fertigen Verteilung und zurück nach Untis.

### Überblick über die Datenflüsse

Das C#-Programm ist die **Optimierungsstufe**. Es liest die aufbereiteten Blätter
`Lehrerliste`, `Klassen`, `Lehrerwünsche`, `Parameter` und `Fachgruppen` und schreibt die
Verteilung sowie die Auswertungen zurück. Die **Aufbereitung** der Untis-Rohdaten in diese
Blätter erfolgt vorgelagert (in der Beispieldatei über die Blätter `Lehrer Stamm Untis`
und `KlassenUV` samt zugehörigem VBA-Vorverarbeitungsschritt).

```
Untis  ──►  "Lehrer Stamm Untis"  ──►  Soll-Anrechnungen  ──►  "Lehrerliste"  ┐
Untis  ──►  "KlassenUV"           ──────────────────────────►  "Klassen"      ├─►  C#-Optimierung
                                                                              ┘        │
                                                                                       ▼
                                    "Klassen" Spalte D (Lehrer L1)  ──►  zurück nach Untis
```

### Schritt für Schritt

1. **Lehrer-Stammdaten aus Untis exportieren** und in das Blatt `Lehrer Stamm Untis`
   einfügen. Daraus werden die Soll-Anrechnungen (Soll/Woche − Anrechnung) berechnet und in
   die Spalte `Soll-Anrechnungen` der `Lehrerliste` geschrieben.
2. **Unterrichtsverteilung aus Untis exportieren** und in das Blatt `KlassenUV` einfügen;
   daraus wird das Blatt `Klassen` befüllt (Klasse, Fach, Wochenstunden, ggf. fixierte
   Lehrkräfte in Spalte F).
3. **Lehrerliste vervollständigen**: je Lehrkraft die unterrichtbaren Fächer, die
   Klassenleitung und – optional – individuelle Grenzen (siehe unten).
4. **Optimieren** (Oberfläche: Datei wählen, Verfahren steht auf CP-SAT, „Start"; oder
   Konsole `UvOptimierer.App <datei.xlsm>`). Das Ergebnis landet in `<datei>_Ergebnis.<ext>`.
5. **Ergebnis nach Untis zurückspielen**: Die zugeteilten Lehrkräfte stehen im Blatt
   `Klassen` in **Spalte D** („Lehrer (L1)"). Diese Spalte wird zusammen mit Klasse und Fach
   in das Untis-Import-Format übernommen (in der Regel manuell bzw. über die gewohnte
   Untis-Importmaske für die Unterrichtsverteilung). Einen automatischen Rück-Export nach
   Untis enthält das Programm derzeit **nicht** – der Rückweg ist der gleiche wie beim
   Import, nur mit der jetzt gefüllten Lehrer-Spalte.

### Zwingend benötigte Spalten in „Lehrer Stamm Untis"

Diese Spalten werden **über die Kopfzeile** gesucht (Position egal, Überschrift muss exakt
passen; alternative Schreibweisen in Klammern):

| Überschrift        | Bedeutung                                  |
|--------------------|--------------------------------------------|
| `Name`             | Lehrerkürzel/-name (Schlüssel)             |
| `Soll/Woche` (`Soll/Wo`) | Soll-Deputat pro Woche               |
| `Anrechnung` (`Anrechnungen`) | Anrechnungsstunden                |

Fehlt eine dieser Überschriften, bricht die Vorverarbeitung mit einer Meldung ab.

### Spaltenerkennung über die Kopfzeile

Das Programm findet die Spalten inzwischen **über die Überschriften in Zeile 1** (bzw. bei
den Parametern über das Label in Spalte A). Spalten dürfen also verschoben werden, solange
die Überschrift stimmt. Feste Positionen dienen nur noch als Rückfall, falls eine Überschrift
fehlt. Erkannt werden (Groß-/Kleinschreibung, Umlaute, Leer- und Sonderzeichen egal):

**Blatt `Lehrerliste`:** `Name`; `Fach 1` … `Fach 20`; `Klassenleitung`; `Soll-Anrechnungen`
(auch „Soll-Anrechnung"/„Soll Anrechnung"); optional `MaxUeberlast`/`Max-Überlast` und
`MaxUnterlast`/`Max-Unterlast` (an beliebiger Stelle).

**Blatt `Klassen`:** `Klasse`; `Fach`; `WSt` (auch „Wochenstunden"); `Fix`; `Wert`;
`Lehrer (L1)` … `Lehrer (L4)` (Ergebnisspalten – werden gefunden oder, falls nicht vorhanden,
an fester Position ergänzt).

**Blatt `Lehrerwünsche`:** `Lehrer`; `Klasse` (Wunschklasse); `nicht in Klasse` (Anti-Klasse);
die beiden `Prio`-Spalten werden der Wunsch- bzw. Anti-Klasse anhand ihrer Reihenfolge zugeordnet.

**Blatt `Parameter`:** Jede Zeile wird über ihr Label in Spalte A erkannt (z. B. „Toleranz…",
„Faktor Überlastung", „CP-SAT Überlast-Strafe…", „Globale Max-Überlast…"), der Wert steht in
der Spalte mit der Überschrift `Wert`.

**Blatt `Fachgruppen`:** Kopfzeile mit `Fach (Kuerzel)` und `Fachgruppe` wird automatisch gesucht.

> Damit ist der frühere Zwang zu festen Spaltenpositionen entfallen. Wichtig bleibt nur, dass
> die **Überschriften** vorhanden und eindeutig sind.
