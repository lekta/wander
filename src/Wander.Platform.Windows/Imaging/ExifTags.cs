using Wander.Core.Icons;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace Wander.Platform.Windows.Imaging;

/// <summary>Where a picture's EXIF lives, by container - the only two WIC writes it to.</summary>
internal enum ExifContainer {
    Jpeg,
    Tiff,
}


/// <summary>
/// Turns <see cref="ImageMetadata"/> into the property set a WIC encoder
/// takes: EXIF tags by number under the container's IFD path, because the
/// numbered form is the one both containers accept for every field
/// (measured 2026-09-16, ImagingProbe stand: rationals, GPS arrays and
/// strings all land and read back). Rationals are packed the way WIC
/// expects a <c>VT_UI8</c>: numerator in the low half, denominator in the
/// high half.
/// </summary>
internal static class ExifTags {
    // IFD0
    private const ushort Make = 271;
    private const ushort Model = 272;
    private const ushort Copyright = 33432;
    private const ushort Rating = 18246;

    // Exif sub-IFD
    private const ushort ExposureTime = 33434;
    private const ushort FNumber = 33437;
    private const ushort Iso = 34855;
    private const ushort FocalLength = 37386;

    // GPS IFD
    private const ushort LatitudeRef = 1;
    private const ushort Latitude = 2;
    private const ushort LongitudeRef = 3;
    private const ushort Longitude = 4;
    private const ushort AltitudeRef = 5;
    private const ushort Altitude = 6;

    /// <summary>Metadata policies, container-independent; WIC maps them to the right tag itself.</summary>
    public const string OrientationPolicy = "System.Photo.Orientation";
    private const string DateTakenPolicy = "System.Photo.DateTaken";

    /// <summary>
    /// What is copied from the source's own property store when the
    /// decoder can read it - the words a person typed into the file,
    /// which EXIF numbers know nothing about.
    /// </summary>
    public static readonly string[] TextPolicies = {
        "System.Title", "System.Subject", "System.Comment", "System.Author", "System.Keywords",
    };


    /// <summary>
    /// Everything <paramref name="shot"/> carries, plus the orientation the
    /// pixels are written in - 1 once they have been turned upright, the
    /// camera's own value when they have not.
    /// </summary>
    public static BitmapPropertySet Build(ImageMetadata? shot, ExifContainer container, int orientation) {
        string ifd0 = container == ExifContainer.Jpeg ? "/app1/ifd/" : "/ifd/";
        string exif = ifd0 + "exif/";
        string gps = ifd0 + "gps/";

        var props = new BitmapPropertySet {
            [OrientationPolicy] = new BitmapTypedValue((ushort)Math.Clamp(orientation, 1, 8), PropertyType.UInt16),
        };
        if (shot is null) {
            return props;
        }

        Put(props, ifd0, Make, shot.CameraMake);
        Put(props, ifd0, Model, shot.CameraModel);
        Put(props, ifd0, Copyright, shot.Copyright);
        if (shot.Rating is int rating and >= 1 and <= 5) {
            props[Tag(ifd0, Rating)] = new BitmapTypedValue((ushort)rating, PropertyType.UInt16);
        }
        if (shot.DateTaken is { } taken) {
            // A camera's time has no zone: unspecified kind reads as local,
            // which is what WIC writes back as the same wall-clock digits.
            props[DateTakenPolicy] = new BitmapTypedValue(
                new DateTimeOffset(DateTime.SpecifyKind(taken, DateTimeKind.Unspecified)), PropertyType.DateTime);
        }

        if (shot.Iso is int iso and > 0) {
            props[Tag(exif, Iso)] = new BitmapTypedValue((ushort)Math.Min(iso, ushort.MaxValue), PropertyType.UInt16);
        }
        Put(props, exif, ExposureTime, shot.ExposureSeconds);
        Put(props, exif, FNumber, shot.FNumber);
        Put(props, exif, FocalLength, shot.FocalLengthMm);

        if (shot.Position is { } where) {
            props[Tag(gps, LatitudeRef)] = new BitmapTypedValue(GeoPosition.ReferenceOf(where.Latitude, isLatitude: true), PropertyType.String);
            props[Tag(gps, Latitude)] = new BitmapTypedValue(Coordinate(where.Latitude), PropertyType.UInt64Array);
            props[Tag(gps, LongitudeRef)] = new BitmapTypedValue(GeoPosition.ReferenceOf(where.Longitude, isLatitude: false), PropertyType.String);
            props[Tag(gps, Longitude)] = new BitmapTypedValue(Coordinate(where.Longitude), PropertyType.UInt64Array);
            if (where.AltitudeMeters is { } altitude) {
                props[Tag(gps, AltitudeRef)] = new BitmapTypedValue((byte)(altitude < 0 ? 1 : 0), PropertyType.UInt8);
                props[Tag(gps, Altitude)] = new BitmapTypedValue(Rational(Math.Abs(altitude)), PropertyType.UInt64);
            }
        }

        return props;
    }


    private static string Tag(string ifd, ushort tag) {
        return $"{ifd}{{ushort={tag}}}";
    }

    private static void Put(BitmapPropertySet props, string ifd, ushort tag, string? text) {
        if (!string.IsNullOrWhiteSpace(text)) {
            props[Tag(ifd, tag)] = new BitmapTypedValue(text, PropertyType.String);
        }
    }

    private static void Put(BitmapPropertySet props, string ifd, ushort tag, double? value) {
        if (value is > 0) {
            props[Tag(ifd, tag)] = new BitmapTypedValue(Rational(value.Value), PropertyType.UInt64);
        }
    }

    /// <summary>Degrees, minutes, seconds - three rationals, the seconds to a ten-thousandth.</summary>
    private static ulong[] Coordinate(double value) {
        var (degrees, minutes, seconds) = GeoPosition.Dms(value);

        return new[] {
            Pack((uint)degrees, 1),
            Pack((uint)minutes, 1),
            Pack((uint)Math.Round(seconds * 10000), 10000),
        };
    }

    /// <summary>
    /// A positive number as an EXIF rational. A fraction of a second keeps
    /// its "1/250" shape where the reciprocal is whole - that is how every
    /// reader prints an exposure - and everything else goes to thousandths.
    /// </summary>
    private static ulong Rational(double value) {
        if (value < 1) {
            double inverse = 1 / value;
            if (Math.Abs(inverse - Math.Round(inverse)) < 1e-6 && inverse < uint.MaxValue) {
                return Pack(1, (uint)Math.Round(inverse));
            }
        }

        return Pack((uint)Math.Min(Math.Round(value * 1000), uint.MaxValue), 1000);
    }

    private static ulong Pack(uint numerator, uint denominator) {
        return ((ulong)denominator << 32) | numerator;
    }
}
