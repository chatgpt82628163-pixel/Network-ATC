using NetworkAtc.Core.Fsd;
using NetworkAtc.Core.Geo;
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

    /// <summary>
    /// The range sent to the network (traffic seen, radio reach): <paramref name="rangeNm"/> beyond the border of the
    /// position's own airspace, i.e. the range plus the farthest point of that airspace from <paramref name="at"/>.
    /// Airport positions and positions without sectors count from <paramref name="at"/> itself. At most 10000 NM.
    /// </summary>
    public static int CoverageNm(SectorFile? file, string callsign, string frequency, Facility facility, GeoPoint at, int rangeNm)
    {
        double extent = 0;
        if (file != null && facility is not (Facility.Delivery or Facility.Ground or Facility.Tower))
            foreach (var p in OwnAirspace(file, callsign, frequency))
                extent = Math.Max(extent, GeoMath.DistanceNm(at, p));
        return (int)Math.Min(FacilityExtensions.MaxRangeNm, Math.Ceiling(Math.Max(1, rangeNm) + extent));
    }

    /// <summary>Border points of the sectors whose first owner is this position (its own airspace), else of those it may own at all.</summary>
    private static List<GeoPoint> OwnAirspace(SectorFile file, string callsign, string frequency)
    {
        var ownership = new SectorOwnership(file);
        if (ownership.PositionOf(new OnlineStation(callsign, frequency)) is not { } pos) return [];
        var own = ownership.Areas.Where(a => a.Sector.Owners.Count > 0 && a.Sector.Owners[0].Equals(pos.Identifier, StringComparison.OrdinalIgnoreCase)).ToList();
        if (own.Count == 0) own = ownership.Areas.Where(a => a.Sector.Owners.Contains(pos.Identifier, StringComparer.OrdinalIgnoreCase)).ToList();
        return own.SelectMany(a => a.Polygon).ToList();
    }

    /// <summary>Middle of the position's own airspace.</summary>
    private static GeoPoint? SectorsCenter(SectorFile file, string callsign, string frequency)
    {
        var points = OwnAirspace(file, callsign, frequency);
        if (points.Count == 0) return null;
        return new GeoPoint((points.Min(p => p.Latitude) + points.Max(p => p.Latitude)) / 2,
                            (points.Min(p => p.Longitude) + points.Max(p => p.Longitude)) / 2);
    }
}
