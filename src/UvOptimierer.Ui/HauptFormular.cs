using System.Diagnostics;
using UvOptimierer.Core.Modell;
using UvOptimierer.Core.Solver;
using UvOptimierer.CpSat;
using UvOptimierer.Excel;

namespace UvOptimierer.Ui;

/// <summary>
/// Einfache Bedienoberfläche: Datei wählen, Verfahren wählen, optimieren und die beiden
/// Lösungen zurückschreiben. Die eigentliche Logik liegt unverändert in Core/Excel/CpSat.
/// </summary>
public sealed class HauptFormular : Form
{
    private readonly TextBox _txtDatei = new() { Width = 420, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly Button _btnDurchsuchen = new() { Text = "Durchsuchen …", AutoSize = true };
    private readonly ComboBox _cmbVerfahren = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly CheckBox _chkSeed = new() { Text = "Seed", AutoSize = true };
    private readonly NumericUpDown _numSeed = new() { Minimum = 0, Maximum = int.MaxValue, Value = 42, Width = 120, Enabled = false };
    private readonly CheckBox _chkVerbessern = new() { Text = "Nachträgliche Verbesserung (L3/L4)", AutoSize = true, Margin = new Padding(16, 3, 3, 3) };
    private readonly CheckBox _chkNurBeste = new() { Text = "Nur beste Lösung (spart Diagnose 2–4)", AutoSize = true, Checked = true, Margin = new Padding(16, 3, 3, 3) };
    private readonly Button _btnParameter = new() { Text = "Parameter …", AutoSize = true, Margin = new Padding(16, 3, 3, 3) };
    private readonly Button _btnAnalyse = new() { Text = "Bedarfsanalyse …", AutoSize = true, Margin = new Padding(8, 3, 3, 3) };
    private readonly Button _btnSimulation = new() { Text = "Simulation …", AutoSize = true, Margin = new Padding(8, 3, 3, 3) };
    private readonly Button _btnSimKombi = new() { Text = "Sim-Kombi …", AutoSize = true, Margin = new Padding(8, 3, 3, 3) };
    private ScoreParameter? _parameterOverride;
    private readonly Button _btnStart = new() { Text = "Optimieren", AutoSize = true };
    private readonly TextBox _txtLog = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = new Font(FontFamily.GenericMonospace, 9f)
    };

    public HauptFormular()
    {
        Text = "UV-Optimierer";
        Width = 960;
        Height = 560;
        MinimumSize = new Size(820, 420);
        StartPosition = FormStartPosition.CenterScreen;

        _cmbVerfahren.Items.AddRange(new object[]
        {
            "Simulated Annealing",
            "Backtracking + CP",
            "CP-SAT (OR-Tools)"
        });
        _cmbVerfahren.SelectedIndex = 2;   // Standard: CP-SAT (OR-Tools)

        _btnDurchsuchen.Click += (_, _) => DateiWaehlen();
        _btnParameter.Click += (_, _) => ParameterBearbeiten();
        _btnAnalyse.Click += async (_, _) => await BedarfsanalyseAsync();
        _btnSimulation.Click += async (_, _) => await SimulationAsync(false);
        _btnSimKombi.Click += async (_, _) => await SimulationAsync(true);
        _txtDatei.TextChanged += (_, _) => _parameterOverride = null;
        _chkSeed.CheckedChanged += (_, _) => _numSeed.Enabled = _chkSeed.Checked;
        _btnStart.Click += async (_, _) => await StartenAsync();

        Controls.Add(BaueLayout());
    }

    private Control BaueLayout()
    {
        var wurzel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };
        wurzel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wurzel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var oben = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 5
        };
        oben.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        oben.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        oben.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        oben.Controls.Add(new Label { Text = "Datei:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, 0);
        oben.Controls.Add(_txtDatei, 1, 0);
        oben.Controls.Add(_btnDurchsuchen, 2, 0);

        oben.Controls.Add(new Label { Text = "Verfahren:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, 1);
        var verfahrenZeile = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        verfahrenZeile.Controls.Add(_cmbVerfahren);
        verfahrenZeile.Controls.Add(_chkSeed);
        verfahrenZeile.Controls.Add(_numSeed);
        oben.Controls.Add(verfahrenZeile, 1, 1);

        oben.Controls.Add(new Label { Text = "Optionen:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) }, 0, 2);
        var optionenZeile = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.TopDown };
        optionenZeile.Controls.Add(_chkVerbessern);
        optionenZeile.Controls.Add(_chkNurBeste);
        oben.Controls.Add(optionenZeile, 1, 2);

        oben.Controls.Add(new Label { Text = "Werkzeuge:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, 3);
        var werkzeugZeile = new FlowLayoutPanel { AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Fill };
        werkzeugZeile.Controls.Add(_btnParameter);
        werkzeugZeile.Controls.Add(_btnAnalyse);
        werkzeugZeile.Controls.Add(_btnSimulation);
        werkzeugZeile.Controls.Add(_btnSimKombi);
        oben.Controls.Add(werkzeugZeile, 1, 3);

        _btnStart.Anchor = AnchorStyles.Right;
        oben.Controls.Add(_btnStart, 2, 4);

        wurzel.Controls.Add(oben, 0, 0);
        wurzel.Controls.Add(_txtLog, 0, 1);
        return wurzel;
    }

    private void DateiWaehlen()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Excel-Mappen (*.xlsm;*.xlsx)|*.xlsm;*.xlsx|Alle Dateien (*.*)|*.*",
            Title = "UV-Arbeitsmappe wählen"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _txtDatei.Text = dlg.FileName;
    }

    private void ParameterBearbeiten()
    {
        string datei = _txtDatei.Text.Trim();
        if (!File.Exists(datei))
        {
            MessageBox.Show(this, "Bitte zuerst eine vorhandene Arbeitsmappe wählen.",
                "Datei fehlt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ScoreParameter p;
        try
        {
            p = (_parameterOverride ?? new ArbeitsmappeLeser().LiesParameter(datei)).Clone();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Parameter konnten nicht gelesen werden:\n" + ex.Message,
                "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var dlg = new Form
        {
            Text = "Parameter (nur für den nächsten Lauf)",
            Width = 480,
            Height = 600,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.Sizable
        };
        var grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            SelectedObject = p,
            PropertySort = PropertySort.Categorized,
            ToolbarVisible = false,
            HelpVisible = true
        };
        var leiste = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(6)
        };
        var ok = new Button { Text = "Übernehmen", DialogResult = DialogResult.OK, AutoSize = true };
        var abbrechen = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        leiste.Controls.Add(ok);
        leiste.Controls.Add(abbrechen);
        dlg.Controls.Add(grid);
        dlg.Controls.Add(leiste);
        dlg.AcceptButton = ok;
        dlg.CancelButton = abbrechen;

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _parameterOverride = p;
            Anhaengen("Parameter für den nächsten Lauf übernommen (die Datei selbst bleibt unverändert).");
        }
    }

    private async Task BedarfsanalyseAsync()
    {
        // Vorschlag: die _Ergebnis-Datei zur aktuell gewählten Eingabe, sonst die Eingabe selbst.
        string vorschlag = "";
        string eingabe = _txtDatei.Text.Trim();
        if (File.Exists(eingabe))
        {
            string ausg = AusgabePfad(eingabe);
            vorschlag = File.Exists(ausg) ? ausg : eingabe;
        }

        string datei;
        using (var dlg = new OpenFileDialog
        {
            Filter = "Excel-Mappen (*.xlsm;*.xlsx)|*.xlsm;*.xlsx|Alle Dateien (*.*)|*.*",
            Title = "Ergebnisdatei für die Bedarfsanalyse wählen (mit Diagnose-Blättern)",
            FileName = vorschlag
        })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            datei = dlg.FileName;
        }

        SteuerungAktiv(false);
        _txtLog.Clear();
        Anhaengen($"Bedarfsanalyse für {Path.GetFileName(datei)} …");
        try
        {
            var (diag, diagName) = await Task.Run(() => new NeueinstellungsSchreiber().Erstelle(datei));
            Anhaengen(diag
                ? $"Fertig. Überlast-Signal aus {diagName}."
                : "Fertig. Kein Diagnose-Blatt gefunden – nur Bedarf und Qualifikationsdichte (bitte zuerst optimieren).");
            MessageBox.Show(this,
                "Bedarfsanalyse erstellt.\n\n" +
                (diag ? $"Überlast gelesen aus: {diagName}" : "Hinweis: Für das Überlast-Signal zuerst die Optimierung ausführen.") +
                "\n\nKandidatenprofile im Blatt 'Neueinstellung_Eingabe' eintragen und erneut ausführen.",
                "Bedarfsanalyse", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Anhaengen("FEHLER: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SteuerungAktiv(true);
        }
    }

    private async Task SimulationAsync(bool kombi)
    {
        string vorschlag = "";
        string eingabe = _txtDatei.Text.Trim();
        if (File.Exists(eingabe))
        {
            string ausg = AusgabePfad(eingabe);
            vorschlag = File.Exists(ausg) ? ausg : eingabe;
        }

        string datei;
        using (var dlg = new OpenFileDialog
        {
            Filter = "Excel-Mappen (*.xlsm;*.xlsx)|*.xlsm;*.xlsx|Alle Dateien (*.*)|*.*",
            Title = "Ergebnisdatei für die Simulation wählen (mit Blatt 'Neueinstellung_Eingabe')",
            FileName = vorschlag
        })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            datei = dlg.FileName;
        }

        SteuerungAktiv(false);
        _txtLog.Clear();
        Anhaengen((kombi ? "Kombi-Simulation" : "Simulation") + $" für {Path.GetFileName(datei)} (CP-SAT) …");
        Anhaengen("Das kann je nach Schulgröße, Profilzahl und Zeitlimit einige Minuten dauern.");
        var log = new Progress<string>(Anhaengen);
        try
        {
            var solver = new CpSatSolver();
            int n = await Task.Run(() => kombi
                ? new NeueinstellungsSimulator().ErstelleKombi(datei, solver, log)
                : new NeueinstellungsSimulator().Erstelle(datei, solver, log));
            string blatt = kombi ? "Neueinstellung_Sim_Kombi" : "Neueinstellung_Simulation";
            Anhaengen("");
            Anhaengen("════════════════════════════════════════════");
            Anhaengen($"Fertig – {n} Profil(e) simuliert (CP-SAT).");
            Anhaengen("Ergebnis gespeichert in Datei:");
            Anhaengen("   " + datei);
            Anhaengen("Blatt: " + blatt);
            Anhaengen("════════════════════════════════════════════");
        }
        catch (Exception ex)
        {
            Anhaengen("FEHLER: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SteuerungAktiv(true);
        }
    }

    private async Task StartenAsync()
    {
        string eingabe = _txtDatei.Text.Trim();
        if (!File.Exists(eingabe))
        {
            MessageBox.Show(this, "Bitte zuerst eine vorhandene Arbeitsmappe wählen.",
                "Datei fehlt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int verfahren = _cmbVerfahren.SelectedIndex;
        int? seed = _chkSeed.Checked ? (int)_numSeed.Value : null;
        bool verbessern = _chkVerbessern.Checked;
        bool nurBeste = _chkNurBeste.Checked;
        string ausgabe = AusgabePfad(eingabe);

        SteuerungAktiv(false);
        _txtLog.Clear();
        var log = new Progress<string>(Anhaengen);

        try
        {
            await Task.Run(() => Optimieren(eingabe, ausgabe, verfahren, seed, verbessern, nurBeste, _parameterOverride, log));
            MessageBox.Show(this, $"Fertig. Ergebnis gespeichert:\n{ausgabe}",
                "Optimierung", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Anhaengen("FEHLER: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SteuerungAktiv(true);
        }
    }

    private static void Optimieren(string eingabe, string ausgabe, int verfahren, int? seed, bool verbessern,
                                   bool nurBeste, ScoreParameter? parameterOverride, IProgress<string> log)
    {
        log.Report($"Lese {Path.GetFileName(eingabe)} …");
        var leser = new ArbeitsmappeLeser();
        Kontext k = leser.Lies(eingabe, parameterOverride);
        if (parameterOverride != null)
            log.Report("  (bearbeitete Parameter aus dem Editor werden verwendet)");
        int fixiert = k.Eintraege.Count(e => Regeln.IstFixiert(e.UrsprungsLehrer));
        log.Report($"  {k.NL} Lehrkräfte, {k.NE} Einträge ({fixiert} fixiert), {k.NW} Wünsche.");

        Optimierer optimierer = verfahren switch
        {
            1 => Optimierer.FuerVerfahren(Verfahren.Backtracking, seed),
            2 => new Optimierer(new CpSatSolver()),
            _ => Optimierer.FuerVerfahren(Verfahren.SimulatedAnnealing, seed),
        };

        log.Report("Optimiere …");
        var sw = Stopwatch.StartNew();
        OptimierungsErgebnis erg = optimierer.Loese(k, nurBeste);
        sw.Stop();

        log.Report($"  Lösung 1: Score={erg.Loesung1.Score:0.0}, unbesetzt={erg.Loesung1.Unbesetzt}, "
                   + $"vollständig={(erg.Loesung1.Vollstaendig ? "ja" : "nein")}");
        if (!nurBeste)
            log.Report($"  Lösung 2: Score={erg.Loesung2.Score:0.0}, unbesetzt={erg.Loesung2.Unbesetzt}, "
                       + $"vollständig={(erg.Loesung2.Vollstaendig ? "ja" : "nein")}");
        log.Report($"  Dauer: {sw.Elapsed.TotalSeconds:0.0} s");

        if (erg.Warnung != null)
        {
            log.Report("");
            log.Report("════════════════════════════════════════════");
            log.Report("ACHTUNG: " + erg.Warnung);
            log.Report("════════════════════════════════════════════");
        }

        if (nurBeste)
            log.Report("Nur beste Lösung: Diagnose2–4 und die zweite Verteilung werden übersprungen.");
        else if (verbessern)
            log.Report($"Nachträgliche Verbesserung aktiv (L1→L3, L2→L4, Zeitlimit {k.P.Zeitlimit:0} s je Lösung) …");

        log.Report($"Schreibe {Path.GetFileName(ausgabe)} …");
        new ArbeitsmappeSchreiber().SchreibeUndSpeichere(eingabe, ausgabe, k, erg, verbessern, nurBeste);
        log.Report("Fertig.");
    }

    private static string AusgabePfad(string eingabe)
    {
        string dir = Path.GetDirectoryName(eingabe) ?? ".";
        string name = Path.GetFileNameWithoutExtension(eingabe);
        string ext = Path.GetExtension(eingabe);
        return Path.Combine(dir, $"{name}_Ergebnis{ext}");
    }

    private void SteuerungAktiv(bool an)
    {
        _txtDatei.Enabled = an;
        _btnDurchsuchen.Enabled = an;
        _btnParameter.Enabled = an;
        _btnAnalyse.Enabled = an;
        _btnSimulation.Enabled = an;
        _btnSimKombi.Enabled = an;
        _cmbVerfahren.Enabled = an;
        _chkSeed.Enabled = an;
        _numSeed.Enabled = an && _chkSeed.Checked;
        _chkVerbessern.Enabled = an;
        _chkNurBeste.Enabled = an;
        _btnStart.Enabled = an;
        _btnStart.Text = an ? "Optimieren" : "läuft …";
    }

    private void Anhaengen(string zeile)
    {
        _txtLog.AppendText(zeile + Environment.NewLine);
    }
}
