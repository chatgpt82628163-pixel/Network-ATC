using NetworkAtc.Core.Fsd;
using NetworkAtc.Plugins;

namespace NetworkAtc.Core.Sectors;

/// <summary>
/// The point a controller reports to the network (the website map draws them there): the airport for
/// delivery, ground, tower and approach, the middle of the .ese sectors the position owns for a centre,
/// and the middle of the sector file only when neither is known.
/// </summary>
public static class ReportedPosition
{
    public static GeoPoint For(SectorFile file, string callsign, string frequency, Facility facility)
    {
        string cs = callsign.ToUpperInvariant();
        int us = cs.IndexOf('_');
        string prefix = us > 0 ? cs[..us] : cs;
        if (facility is Facility.Delivery or Facility.Ground or Facility.Tower or Facility.Approach &&
            file.Airports.FirstOrDefault(a => a.Name.Equals(prefix, StringComparison.OrdinalIgnoreCase)) is { } airport)
            return airport.Position;
        if (SectorsCenter(file, callsign, frequency) is { } center) return center;
        return file.Airports.FirstOrDefault(a => a.Name.Equals(prefix, StringComparison.OrdinalIgnoreCase))?.Position ?? file.Center;
    }

    /// <summary>Middle of the sectors whose first owner is this position (its own airspace), else of those it may own at all.</summary>
    private static GeoPoint? SectorsCenter(SectorFile file, string callsign, string frequency)
    {
        var ownership = new SectorOwnership(file);
        if (ownership.PositionOf(new OnlineStation(callsign, frequency)) is not { } pos) return null;
        var own = ownership.Areas.Where(a => a.Sector.Owners.Count > 0 && a.Sector.Owners[0].Equals(pos.Identifier, StringComparison.OrdinalIgnoreCase)).ToList();
        if (own.Count == 0) own = ownership.Areas.Where(a => a.Sector.Owners.Contains(pos.Identifier, StringComparer.OrdinalIgnoreCase)).ToList();
        var points = own.SelectMany(a => a.Polygon).ToList();
        if (points.Count == 0) return null;
        return new GeoPoint((points.Min(p => p.Latitude) + points.Max(p => p.Latitude)) / 2,
                            (points.Min(p => p.Longitude) + points.Max(p => p.Longitude)) / 2);
    }
}
