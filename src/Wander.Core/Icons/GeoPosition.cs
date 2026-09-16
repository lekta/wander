namespace Wander.Core.Icons;

/// <summary>
/// A place on Earth as a camera records it: decimal degrees, north and
/// east positive, and metres above sea level when known.
/// </summary>
public sealed record GeoPosition(double Latitude, double Longitude, double? AltitudeMeters = null) {
    /// <summary>
    /// Degrees, minutes and seconds of a coordinate, all non-negative - the
    /// three EXIF rationals a GPS tag is made of. The sign goes into the
    /// reference letter ("N" / "S", "E" / "W"), which is
    /// <see cref="ReferenceOf"/>'s business.
    /// </summary>
    public static (int Degrees, int Minutes, double Seconds) Dms(double coordinate) {
        double value = Math.Abs(coordinate);
        int degrees = (int)Math.Floor(value);
        double minutesWhole = (value - degrees) * 60;
        int minutes = (int)Math.Floor(minutesWhole);
        double seconds = (minutesWhole - minutes) * 60;

        // 59.99999 seconds is a rounding artefact of the multiplication,
        // and a reader that prints it as 60" is right to look surprised.
        seconds = Math.Round(seconds, 4);
        if (seconds >= 60) {
            seconds = 0;
            minutes++;
        }
        if (minutes >= 60) {
            minutes = 0;
            degrees++;
        }

        return (degrees, minutes, seconds);
    }


    /// <summary>The hemisphere letter EXIF pairs with a coordinate.</summary>
    public static string ReferenceOf(double coordinate, bool isLatitude) {
        return isLatitude
            ? coordinate < 0 ? "S" : "N"
            : coordinate < 0 ? "W" : "E";
    }
}
