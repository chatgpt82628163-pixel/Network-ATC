using System.Globalization;
using System.Text.RegularExpressions;
using NetworkAtc.Core.Geo;
using NetworkAtc.Plugins;

namespace NetworkAtc.Core.Sectors;

/// <summary>
/// One line of the SID or STAR menu: a procedure of the sector (all its runways together) and the diagrams the .sct
/// draws for it. A diagram that no procedure claims is a line of its own.
/// </summary>
public sealed record ProcedureEntry(ProcedureKind Kind, string Airport, string Name, IReadOnlyList<string> Runways,
    IReadOnlyList<Procedure> Procedures, IReadOnlyList<string> Diagrams)
{
    /// <summary>What the profile keeps for a chosen line: "UUEE GENPA1B".</summary>
    public string Key => Airport.Length > 0 ? $"{Airport} {Name}" : Name;
}

/// <summary>A point of a procedure on the scope: its name and the limits there, ready to print ("≥6000 ≤250 kt").</summary>
public sealed record ProcedurePoint(string Name, GeoPoint Position, string Limits);

/// <summary>The SIDs and STARs of a sector for the menus, and the named points of the ones picked there.</summary>
public static class ProcedureCatalog
{
    /// <summary>The map layer with the diagrams of this kind of procedure.</summary>
    public static string Layer(ProcedureKind kind) => kind == ProcedureKind.Sid ? "SID" : "STAR";

    public static IReadOnlyList<ProcedureEntry> Entries(SectorFile s, ProcedureKind kind)
    {
        var airports = new HashSet<string>(s.Airports.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
        airports.UnionWith(s.Procedures.Select(p => p.Airport));
        var diagrams = s.Lines.TryGetValue(Layer(kind), out var lines)
            ? lines.Select(l => l.Name.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        // Diagrams by the words of their names: "UUEE STAR GENPA1B" is found under UUEE, STAR and GENPA1B.
        var byWord = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in diagrams)
            foreach (var w in Words(d).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!byWord.TryGetValue(w, out var list)) byWord[w] = list = [];
                list.Add(d);
            }
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ProcedureEntry>();
        foreach (var group in s.Procedures.Where(p => p.Kind == kind).GroupBy(p => (p.Airport, p.Name)))
        {
            var (airport, name) = group.Key;
            // A diagram belongs to the procedure when its name has the procedure's name as a word and no other airport in it.
            var mine = (byWord.GetValueOrDefault(name) ?? [])
                .Where(d => (AirportOf(d, airports) ?? airport).Equals(airport, StringComparison.OrdinalIgnoreCase)).ToList();
            claimed.UnionWith(mine);
            entries.Add(new ProcedureEntry(kind, airport, name, group.Select(p => p.Runway).Where(r => r.Length > 0).Distinct().ToList(),
                group.ToList(), mine));
        }
        foreach (var d in diagrams.Where(d => !claimed.Contains(d)))
            entries.Add(new ProcedureEntry(kind, AirportOf(d, airports) ?? "", d, [], [], [d]));
        return entries.OrderBy(e => e.Airport.Length == 0).ThenBy(e => e.Airport, StringComparer.Ordinal)
            .ThenBy(e => e.Name, StringComparer.Ordinal).ToList();
    }

    private static string[] Words(string text) => Regex.Split(text, "[^A-Za-z0-9]+").Where(w => w.Length > 0).ToArray();

    private static string? AirportOf(string diagram, HashSet<string> airports) =>
        Words(diagram).FirstOrDefault(w => w.Length == 4 && airports.Contains(w))?.ToUpperInvariant();

    /// <summary>
    /// The named points of a line of the menu: the fixes of its procedures with their limits, then the corners of its
    /// diagrams that sit on a fix, VOR or NDB of the sector.
    /// </summary>
    public static IReadOnlyList<ProcedurePoint> Points(SectorFile s, ProcedureEntry entry)
    {
        var result = new List<ProcedurePoint>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var near = s.Airports.FirstOrDefault(a => a.Name.Equals(entry.Airport, StringComparison.OrdinalIgnoreCase))?.Position ?? s.Center;
        foreach (var p in entry.Procedures)
            foreach (var point in Resolve(s, p, near))
                if (seen.Add(point.Name)) result.Add(point);
        if (entry.Diagrams.Count == 0 || !s.Lines.TryGetValue(Layer(entry.Kind), out var lines)) return result;

        // Corners by cells of 0.01°: each point is then compared with the corners of its own and the next cells only.
        var names = new HashSet<string>(entry.Diagrams, StringComparer.OrdinalIgnoreCase);
        var cells = new Dictionary<(int, int), List<GeoPoint>>();
        foreach (var l in lines.Where(l => names.Contains(l.Name.Trim())))
            foreach (var c in new[] { l.From, l.To })
            {
                var cell = Cell(c);
                if (!cells.TryGetValue(cell, out var list)) cells[cell] = list = [];
                list.Add(c);
            }
        foreach (var list in new[] { s.Fixes, s.Vors, s.Ndbs })
            foreach (var f in list)
            {
                if (seen.Contains(f.Name)) continue;
                var (x, y) = Cell(f.Position);
                bool onDiagram = false;
                for (int dx = -1; dx <= 1 && !onDiagram; dx++)
                    for (int dy = -1; dy <= 1 && !onDiagram; dy++)
                        onDiagram = cells.TryGetValue((x + dx, y + dy), out var corners) && corners.Exists(c => GeoMath.DistanceNm(c, f.Position) < 0.05);
                if (onDiagram && seen.Add(f.Name)) result.Add(new ProcedurePoint(f.Name, f.Position, ""));
            }
        return result;
    }

    private static (int, int) Cell(GeoPoint p) => ((int)Math.Floor(p.Latitude * 100), (int)Math.Floor(p.Longitude * 100));

    /// <summary>The route of a procedure as points of the sector, each with the limits written after it.</summary>
    public static IReadOnlyList<ProcedurePoint> Resolve(SectorFile s, Procedure p, GeoPoint near)
    {
        var result = new List<ProcedurePoint>();
        var last = near;
        for (int i = 0; i < p.Route.Count; i++)
        {
            if (ProcedureAssigner.Find(s, p.Route[i], last) is not { } at) continue;
            result.Add(new ProcedurePoint(p.Route[i], at, Describe(i < p.Limits.Count ? p.Limits[i] : "")));
            last = at;
        }
        return result;
    }

    /// <summary>
    /// Limits as a controller reads them: "A6000+/K250-" → "≥6000 ≤250 kt", "FL070" → "FL070", "A5000-" → "≤5000".
    /// Anything else is shown as written.
    /// </summary>
    public static string Describe(string limits)
    {
        if (limits.Length == 0) return "";
        var parts = new List<string>();
        foreach (var raw in limits.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string limit = raw.ToUpperInvariant();
            string sign = limit.EndsWith('+') ? "≥" : limit.EndsWith('-') ? "≤" : "";
            string value = sign.Length > 0 ? limit[..^1] : limit;
            if (value.StartsWith("FL", StringComparison.Ordinal) && IsNumber(value[2..])) parts.Add(sign + value);
            else if (value.StartsWith('A') && IsNumber(value[1..])) parts.Add(sign + value[1..]);
            else if (value.StartsWith('K') && IsNumber(value[1..])) parts.Add($"{sign}{value[1..]} kt");
            else parts.Add(raw);
        }
        return string.Join(' ', parts);
    }

    private static bool IsNumber(string s) => s.Length > 0 && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
