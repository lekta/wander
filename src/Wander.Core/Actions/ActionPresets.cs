using Wander.Core.Icons;

namespace Wander.Core.Actions;

/// <summary>A program the presets rely on: its name on disk, its product name, and the winget package that installs it.</summary>
public sealed record ExternalTool(string Name, string Title, string WingetId);


/// <summary>
/// The actions Wander ships: conversions that are one command line away
/// for a tool most people either have or can get with one winget call,
/// plus the picture conversions Wander does itself. Data only - they are
/// rows of the same catalog as the user's own (<see cref="ActionCatalog"/>
/// merges them by <see cref="CustomAction.Id"/>), and a preset that needs
/// a program names it in <see cref="CustomAction.RequiredTool"/> so a
/// missing one is a greyed row with a hint, not a failed run.
///
/// <para>
/// Every output is declared: it is what makes Ctrl+Z work and what keeps
/// a program from writing over an existing file. The one exception is
/// LibreOffice, which names its output itself and overwrites whatever is
/// there - declaring a name it will not use would be a lie the undo step
/// then acts on.
/// </para>
/// </summary>
public static class ActionPresets {
    public const string Ffmpeg = "ffmpeg";
    public const string LibreOffice = "soffice";
    public const string Pandoc = "pandoc";

    /// <summary>The built-in picture encoder's name.</summary>
    public const string ImageConvert = "image-convert";

    /// <summary>The built-in "hold this file open" debug handler's name.</summary>
    public const string HoldFile = HoldFileAction.ActionName;

    /// <summary>The programs the settings page lists, in its order.</summary>
    public static readonly IReadOnlyList<ExternalTool> Tools = new[] {
        new ExternalTool(Ffmpeg, "FFmpeg", "Gyan.FFmpeg"),
        new ExternalTool(LibreOffice, "LibreOffice", "TheDocumentFoundation.LibreOffice"),
        new ExternalTool(Pandoc, "Pandoc", "JohnMacFarlane.Pandoc"),
    };

    /// <summary>
    /// Every RAW extension Wander lists, as a mask - sorted, so the preset
    /// reads the same on every start and an untouched one is never taken
    /// for an edited one.
    /// </summary>
    private static readonly string _rawMask = string.Join(';', ImageFormats.Raw.Order(StringComparer.OrdinalIgnoreCase).Select(e => "*" + e));

    public static readonly IReadOnlyList<CustomAction> All = new[] {
        // --- RAW ---
        // First in the list: the conversions a RAW is right-clicked for.
        // The JPEGs the camera put inside the RAW, taken out as they are.
        // Only a CR3 carries a small one apart from the big one (1620 px
        // against full size); a TIFF-shaped RAW gives its biggest either
        // way, so there the small row would repeat the big one.
        Builtin("preset:raw-preview", "format=jpeg;source=preview", "{name}_spv.jpg") with {
            TitleKey = "ActionPresetRawPreview",
            Types = new FileTypeSelector(FileTypeGroup.All, "*.cr3"),
        },
        Builtin("preset:raw-preview-full", "format=jpeg;source=preview-full", "{name}_lpv.jpg") with {
            TitleKey = "ActionPresetRawPreviewFull",
            Types = new FileTypeSelector(FileTypeGroup.All, _rawMask),
        },

        // --- Video ---
        Tool("preset:video-mp4", Ffmpeg, FileTypeGroup.Video,
            "-i {path} -c:v libx264 -crf 23 -preset medium -c:a aac -movflags +faststart {out}", "{name}.mp4") with {
            TitleKey = "ActionPresetVideoMpeg",
        },
        Tool("preset:video-compress", Ffmpeg, FileTypeGroup.Video,
            "-i {path} -c:v libx264 -crf 28 -preset medium -c:a aac -movflags +faststart {out}", "{name}_small.mp4") with {
            TitleKey = "ActionPresetVideoCompress",
        },
        Tool("preset:video-audio-m4a", Ffmpeg, FileTypeGroup.Video,
            "-i {path} -vn -c:a aac -b:a 192k {out}", "{name}.m4a") with {
            TitleKey = "ActionPresetVideoAudioAac",
        },
        Tool("preset:video-audio-mp3", Ffmpeg, FileTypeGroup.Video,
            "-i {path} -vn -c:a libmp3lame -q:a 2 {out}", "{name}.mp3") with {
            TitleKey = "ActionPresetVideoAudioLame",
        },
        Tool("preset:video-frame", Ffmpeg, FileTypeGroup.Video,
            "-ss 1 -i {path} -frames:v 1 {out}", "{name}.png") with {
            TitleKey = "ActionPresetVideoFrame",
        },

        // --- Audio ---
        // -vn everywhere: a music file's cover is a video stream to ffmpeg,
        // and an audio container asked to take it fails or grows a track.
        Tool("preset:audio-mp3", Ffmpeg, FileTypeGroup.Audio,
            "-i {path} -vn -c:a libmp3lame -b:a 192k {out}", "{name}.mp3") with {
            TitleKey = "ActionPresetAudioLame",
        },
        Tool("preset:audio-m4a", Ffmpeg, FileTypeGroup.Audio,
            "-i {path} -vn -c:a aac -b:a 192k {out}", "{name}.m4a") with {
            TitleKey = "ActionPresetAudioAac",
        },
        Tool("preset:audio-flac", Ffmpeg, FileTypeGroup.Audio,
            "-i {path} -vn -c:a flac {out}", "{name}.flac") with {
            TitleKey = "ActionPresetAudioFlac",
        },
        Tool("preset:audio-wav", Ffmpeg, FileTypeGroup.Audio,
            "-i {path} -vn -c:a pcm_s16le {out}", "{name}.wav") with {
            TitleKey = "ActionPresetAudioWav",
        },

        // --- Pictures ---
        Builtin("preset:image-jpeg", "format=jpeg;quality=90", "{name}.jpg") with {
            TitleKey = "ActionPresetImageJpeg",
        },
        Builtin("preset:image-png", "format=png", "{name}.png") with {
            TitleKey = "ActionPresetImagePng",
        },
        Builtin("preset:image-shrink", "format=jpeg;quality=85;maxside=1920", "{name}_1920.jpg") with {
            TitleKey = "ActionPresetImageShrink",
        },
        // ffmpeg reads the ordinary picture formats and none of the RAW
        // containers, so this one is offered by mask rather than for every
        // picture - a RAW would only fail with a report.
        Tool("preset:image-webp", Ffmpeg, FileTypeGroup.Images,
            "-i {path} -c:v libwebp -quality 85 {out}", "{name}.webp") with {
            TitleKey = "ActionPresetImageWebp",
            Types = new FileTypeSelector(FileTypeGroup.Images, "*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.gif;*.webp"),
        },

        // --- Documents ---
        Tool("preset:document-pdf", LibreOffice, FileTypeGroup.Documents,
            "--headless --convert-to pdf --outdir {dir} {path}", output: string.Empty) with {
            TitleKey = "ActionPresetDocumentPdf",
        },
        Tool("preset:markdown-docx", Pandoc, FileTypeGroup.All,
            "{path} -o {out}", "{name}.docx") with {
            TitleKey = "ActionPresetMarkdownDocx",
            Types = new FileTypeSelector(FileTypeGroup.All, "*.md;*.markdown"),
        },
        Tool("preset:docx-markdown", Pandoc, FileTypeGroup.All,
            "{path} -t gfm -o {out}", "{name}.md") with {
            TitleKey = "ActionPresetDocxMarkdown",
            Types = new FileTypeSelector(FileTypeGroup.All, "*.docx"),
        },

        // --- Debug ---
        // Behind the debug menu (PLAN AI2) and out of the actions table:
        // they produce nothing and are there to make a file busy on demand.
        Hold("preset:debug-hold-short", 5) with {
            TitleKey = "ActionPresetDebugHoldShort",
        },
        Hold("preset:debug-hold-long", 30) with {
            TitleKey = "ActionPresetDebugHoldLong",
        },
    };


    /// <summary>The listed program of that name; null for one only a user's row names.</summary>
    public static ExternalTool? KnownTool(string name) {
        return Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }


    private static CustomAction Tool(string id, string tool, FileTypeGroup group, string arguments, string output) {
        return new CustomAction {
            Id = id,
            Types = new FileTypeSelector(group),
            Kind = ActionKind.Command,
            Program = tool,
            Arguments = arguments,
            Output = output,
            Category = ActionCategory.Convert,
            IsPreset = true,
            RequiredTool = tool,
        };
    }

    /// <summary>A debug hold of <paramref name="seconds"/>: any file, no output, no tool.</summary>
    private static CustomAction Hold(string id, int seconds) {
        return new CustomAction {
            Id = id,
            Types = new FileTypeSelector(FileTypeGroup.All),
            Kind = ActionKind.Builtin,
            Program = HoldFile,
            Arguments = $"{HoldFileAction.SecondsKey}={seconds}",
            Output = string.Empty,
            Category = ActionCategory.Actions,
            IsPreset = true,
            DebugOnly = true,
        };
    }

    private static CustomAction Builtin(string id, string arguments, string output) {
        return new CustomAction {
            Id = id,
            Types = new FileTypeSelector(FileTypeGroup.Images),
            Kind = ActionKind.Builtin,
            Program = ImageConvert,
            Arguments = arguments,
            Output = output,
            Category = ActionCategory.Convert,
            IsPreset = true,
        };
    }
}
