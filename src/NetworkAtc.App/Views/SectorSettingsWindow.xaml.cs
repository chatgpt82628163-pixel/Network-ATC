using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NetworkAtc.Core.Customization;
using NetworkAtc.Core.Sectors;
using NetworkAtc.Core.Session;

namespace NetworkAtc.App.Views;

/// <summary>
/// The sector the controller works with: the file and what it holds, the airspace sectors with who owns each one now
/// (and the ones the controller takes by hand), and the positions of the sector file. Changes apply at once.
/// </summary>
public partial class SectorSettingsWindow : Window
{
    private const int MaxRows = 400;

    private readonly Profile _profile;
    private readonly Workspace _workspace;
    private readonly Func<string?> _path;
    private readonly Func<string> _me;
    private readonly Action _changed;
    private readonly Action _openSector;
    private readonly Action _reload;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string _filter = "";
    private StackPanel? _sectorRows, _positionRows;
    private TextBlock? _ownSummary;
    private string _ownersSeen = "";

    /// <param name="path">The file of the loaded sector.</param>
    /// <param name="me">Our callsign while online, "" offline.</param>
    /// <param name="changed">Ownership or display settings changed: work out the owners again, redraw, save.</param>
    /// <param name="openSector">Opens the sector selection to load another file.</param>
    /// <param name="reload">Loads the current file again (after it was edited).</param>
    public SectorSettingsWindow(Profile profile, Workspace workspace, Func<string?> path, Func<string> me, Action changed,
        Action openSector, Action reload)
    {
        InitializeComponent();
        (_profile, _workspace, _path, _me, _changed, _openSector, _reload) = (profile, workspace, path, me, changed, openSector, reload);
        Build();
        // Owners change as controllers come and go: the list is redrawn only when they do.
        _timer.Tick += (_, _) =>
        {
            string seen = OwnersSignature();
            if (seen == _ownersSeen) return;
            _ownersSeen = seen;
            FillSectors();
        };
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private string OwnersSignature() =>
        _me() + "|" + string.Join(";", (_workspace.Ownership?.Owners ?? new Dictionary<string, string>())
            .OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase).Select(o => o.Key + "=" + o.Value));

    private TextBlock SectionHeading(string text, double top = 22) => new() { Text = text, Style = (Style)FindResource("SectionTitle"), Margin = new Thickness(0, top, 0, 8) };

    private TextBlock Note(string text) => new()
    {
        Text = text,
        Style = (Style)FindResource("Muted"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
    };

    /// <summary>A table cell; with a font (the mono font for callsigns, levels, frequencies) it is set, else inherited.</summary>
    private static TextBlock Cell(string text, FontFamily? font = null)
    {
        var cell = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (font != null) cell.FontFamily = font;
        return cell;
    }

    private void Build()
    {
        Page.Children.Clear();
        var sector = _workspace.Sector;
        BuildFile(sector);
        if (sector == null) return;
        BuildAirspace(sector);
        BuildPositions(sector);
    }

    // ---- the file ------------------------------------------------------------------------------------------

    private void BuildFile(SectorFile? sector)
    {
        Page.Children.Add(SectionHeading("SECTOR FILE", 0));
        var open = new Button { Content = "Open another sector…", Style = (Style)FindResource("Primary"), Margin = new Thickness(0, 0, 8, 0) };
        open.Click += (_, _) =>
        {
            _openSector();
            Build();
        };
        var reload = new Button { Content = "Reload file", Margin = new Thickness(0, 0, 8, 0), IsEnabled = sector != null && File.Exists(_path() ?? "") };
        reload.Click += (_, _) =>
        {
            _reload();
            Build();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Children = { open, reload } };

        if (sector == null)
        {
            Page.Children.Add(Note("No sector is loaded."));
            Page.Children.Add(buttons);
            return;
        }

        var facts = new Grid();
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        facts.ColumnDefinitions.Add(new ColumnDefinition());
        void Fact(string name, string value)
        {
            int row = facts.RowDefinitions.Count;
            facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = name, Style = (Style)FindResource("Muted"), Margin = new Thickness(0, 3, 12, 3) };
            var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };
            Grid.SetRow(label, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            facts.Children.Add(label);
            facts.Children.Add(text);
        }
        Fact("Name", sector.Name.Length > 0 ? sector.Name : "—");
        Fact("File", _path() ?? "—");
        Fact("Default callsign and airport", $"{(sector.DefaultCallsign.Length > 0 ? sector.DefaultCallsign : "—")} · {(sector.DefaultAirport.Length > 0 ? sector.DefaultAirport : "—")}");
        Fact("Centre", $"{Coordinate(sector.Center.Latitude, 'N', 'S')} {Coordinate(sector.Center.Longitude, 'E', 'W')}");
        Fact("Magnetic variation", sector.MagneticVariation == 0 ? "not given (bearings are true)"
            : $"{Math.Abs(sector.MagneticVariation).ToString("0.#", CultureInfo.InvariantCulture)}° {(sector.MagneticVariation < 0 ? "east" : "west")}");
        int sids = sector.Procedures.Count(p => p.Kind == ProcedureKind.Sid), stars = sector.Procedures.Count - sids;
        Fact("Contents", $"{sector.Positions.Count} positions · {sector.Sectors.Count} airspace sectors · {sector.Airports.Count} airports · " +
                         $"{sector.Runways.Count} runways · {sector.Fixes.Count} fixes · {sids} SIDs · {stars} STARs");
        Page.Children.Add(new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(14, 10, 14, 10), Child = facts });

        if (sector.Warnings.Count > 0)
        {
            var list = new TextBlock
            {
                Text = string.Join("\n", sector.Warnings.Take(100)) + (sector.Warnings.Count > 100 ? $"\n… and {sector.Warnings.Count - 100} more" : ""),
                FontFamily = (FontFamily)FindResource("MonoFont"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            };
            Page.Children.Add(new Expander
            {
                Header = $"{sector.Warnings.Count} lines of the file could not be read",
                Foreground = Res("DangerBrush"),
                Margin = new Thickness(0, 8, 0, 0),
                Content = list,
            });
        }
        Page.Children.Add(buttons);
    }

    private static string Coordinate(double value, char positive, char negative)
    {
        char h = value >= 0 ? positive : negative;
        value = Math.Abs(value);
        int d = (int)value, m = (int)((value - d) * 60);
        double s = ((value - d) * 60 - m) * 60;
        return $"{h}{d:000}°{m:00}'{s.ToString("00", CultureInfo.InvariantCulture)}\"";
    }

    // ---- airspace ------------------------------------------------------------------------------------------

    private void BuildAirspace(SectorFile sector)
    {
        Page.Children.Add(SectionHeading("AIRSPACE AND WHO OWNS IT"));
        if (sector.Sectors.Count == 0)
        {
            Page.Children.Add(Note("This sector file has no airspace sectors (the .ese [AIRSPACE] section). Ownership, handoff suggestions and the SIL/SEL lists need them."));
            return;
        }
        Page.Children.Add(Note("Each sector is owned by the first online position of its owner list, as in EuroScope. Tick \"Mine\" to take a sector " +
                               "yourself while you are online: tags, handoff suggestions and the lists then treat it as yours."));

        var highlight = new CheckBox { Content = "Tint the airspace I own on the radar", IsChecked = _profile.HighlightOwnAirspace, Margin = new Thickness(0, 0, 0, 8) };
        highlight.Click += (_, _) =>
        {
            _profile.HighlightOwnAirspace = highlight.IsChecked == true;
            _changed();
        };
        Page.Children.Add(highlight);

        var filter = new TextBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 3, 6, 3), ToolTip = "Sector, owner or position" };
        filter.TextChanged += (_, _) =>
        {
            _filter = filter.Text.Trim();
            FillSectors();
            FillPositions();
        };
        var mineOnly = new Button { Content = "Clear my choices", Style = (Style)FindResource("Ghost"), Margin = new Thickness(12, 0, 0, 0) };
        mineOnly.Click += (_, _) =>
        {
            _profile.MySectors.Clear();
            _changed();
            FillSectors();
        };
        Page.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { new TextBlock { Text = "Filter", Style = (Style)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, filter, mineOnly },
        });

        _ownSummary = new TextBlock { Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
        Page.Children.Add(_ownSummary);
        Page.Children.Add(Row(Header("Mine"), Header("Sector"), Header("Levels"), Header("Owner now"), Header("Owner order")));
        _sectorRows = new StackPanel();
        Page.Children.Add(new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(8, 4, 8, 4), Child = _sectorRows });
        FillSectors();
    }

    private TextBlock Header(string text) => new() { Text = text, Style = (Style)FindResource("FieldLabel"), Margin = new Thickness(0, 0, 0, 2) };

    /// <summary>A table row: Mine, Sector, Levels, Owner now, Owner order.</summary>
    private static Grid Row(params UIElement[] cells)
    {
        var grid = new Grid { Margin = new Thickness(8, 2, 8, 2) };
        foreach (var width in new[] { 56.0, 230, 130, 150 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }
        return grid;
    }

    private static string Level(int feet) => feet <= 0 ? "GND" : feet >= 66000 ? "UNL" : $"FL{feet / 100:000}";

    private bool Matches(params string[] texts) =>
        _filter.Length == 0 || texts.Any(t => t.Contains(_filter, StringComparison.OrdinalIgnoreCase));

    private void FillSectors()
    {
        if (_sectorRows == null || _workspace.Sector is not { } sector) return;
        var owners = _workspace.Ownership?.Owners ?? new Dictionary<string, string>();
        string me = _me();
        var mono = (FontFamily)FindResource("MonoFont");
        var mine = new HashSet<string>(_profile.MySectors, StringComparer.OrdinalIgnoreCase);
        _sectorRows.Children.Clear();
        int shown = 0;
        foreach (var s in sector.Sectors.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            string owner = owners.TryGetValue(s.Name, out var cs) ? cs : "";
            string order = string.Join(", ", s.Owners);
            if (!Matches(s.Name, owner, order)) continue;
            if (++shown > MaxRows) break;
            var box = new CheckBox { IsChecked = mine.Contains(s.Name), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Take this sector while I am online" };
            string name = s.Name;
            box.Click += (_, _) =>
            {
                _profile.MySectors.RemoveAll(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (box.IsChecked == true) _profile.MySectors.Add(name);
                _changed();
                FillSectors();
            };
            var ownerCell = Cell(owner.Length > 0 ? owner : "—", mono);
            if (owner.Length > 0 && owner.Equals(me, StringComparison.OrdinalIgnoreCase))
            {
                ownerCell.Foreground = Res("AccentBrush");
                ownerCell.FontWeight = FontWeights.SemiBold;
            }
            else if (owner.Length == 0) ownerCell.Foreground = Res("MutedBrush");
            _sectorRows.Children.Add(Row(box, Cell(s.Name), Cell($"{Level(s.Floor)} – {Level(s.Ceiling)}", mono), ownerCell,
                Cell(order, mono)));
        }
        if (shown == 0) _sectorRows.Children.Add(Note("No sector matches the filter."));
        if (shown > MaxRows) _sectorRows.Children.Add(Note($"Only the first {MaxRows} sectors are listed; narrow the filter."));

        if (_ownSummary != null)
        {
            int count = me.Length > 0 ? owners.Count(o => o.Value.Equals(me, StringComparison.OrdinalIgnoreCase)) : 0;
            _ownSummary.Text = me.Length == 0
                ? (mine.Count > 0 ? $"You have taken {mine.Count} sector(s); they become yours once you are connected." : "Connect to the network to own sectors.")
                : $"{me} owns {count} of {sector.Sectors.Count} sectors" + (mine.Count > 0 ? $" ({mine.Count} taken by hand)." : ".");
        }
    }

    // ---- positions -----------------------------------------------------------------------------------------

    private void BuildPositions(SectorFile sector)
    {
        Page.Children.Add(SectionHeading("POSITIONS"));
        if (sector.Positions.Count == 0)
        {
            Page.Children.Add(Note("This sector file has no positions (the .ese [POSITIONS] section)."));
            return;
        }
        Page.Children.Add(Note("Positions from the sector file. Connect with one of these callsigns and frequencies so that your sectors are found."));
        _positionRows = new StackPanel();
        Page.Children.Add(new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(8, 4, 8, 4), Child = _positionRows });
        FillPositions();
    }

    private void FillPositions()
    {
        if (_positionRows == null || _workspace.Sector is not { } sector) return;
        var mono = (FontFamily)FindResource("MonoFont");
        _positionRows.Children.Clear();
        _positionRows.Children.Add(PositionRow(Header("Callsign"), Header("Name"), Header("Frequency"), Header("Id"), Header("Squawks")));
        int shown = 0;
        foreach (var p in sector.Positions)
        {
            if (!Matches(p.Callsign, p.RadioName, p.Frequency, p.Identifier)) continue;
            if (++shown > MaxRows) break;
            string squawks = p.SquawkStart is { } a && p.SquawkEnd is { } b ? $"{a:0000}–{b:0000}" : "—";
            _positionRows.Children.Add(PositionRow(Cell(p.Callsign, mono), Cell(p.RadioName), Cell(p.Frequency, mono),
                Cell(p.Identifier, mono), Cell(squawks, mono)));
        }
        if (shown == 0) _positionRows.Children.Add(Note("No position matches the filter."));
        if (shown > MaxRows) _positionRows.Children.Add(Note($"Only the first {MaxRows} positions are listed; narrow the filter."));
    }

    /// <summary>A positions row: Callsign, Name, Frequency, Id, Squawks.</summary>
    private static Grid PositionRow(params UIElement[] cells)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var width in new[] { 150.0, 260, 110, 70 })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }
        return grid;
    }
}
