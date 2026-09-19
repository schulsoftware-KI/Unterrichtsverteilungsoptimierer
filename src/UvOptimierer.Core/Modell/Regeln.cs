namespace UvOptimierer.Core.Modell;

/// <summary>Kleine gemeinsam genutzte Prädikate (1:1 aus dem VBA-Original).</summary>
public static class Regeln
{
    /// <summary>VBA <c>IstFixiert</c>: ein Lehrerwert gilt als fixiert, wenn er nicht leer und nicht „?" ist.</summary>
    public static bool IstFixiert(string? v) => !string.IsNullOrEmpty(v) && v != "?";

    /// <summary>Ist das Fach ein UV-Eintrag (Sonderregel: max. 2 WSt je Lehrkraft, keine Konsistenzbindung).</summary>
    public static bool IstUv(string fach) => string.Equals(fach.Trim(), "uv", StringComparison.OrdinalIgnoreCase);

    /// <summary>Kürzt einen zugewiesenen Lehrernamen auf „belegt/unbelegt".</summary>
    public static bool IstBelegt(string? l) => !string.IsNullOrEmpty(l) && l != "?";
}
