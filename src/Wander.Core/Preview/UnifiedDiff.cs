namespace Wander.Core.Preview;

/// <summary>What a line of a unified diff is - the colour its row is painted with.</summary>
public enum DiffLineKind {
    /// <summary>Outside any file's changes: a mail header, a commit message, a note between files.</summary>
    Plain,

    /// <summary>Which file the changes below are about: <c>diff --git</c>, <c>index</c>, <c>---</c>, <c>+++</c>.</summary>
    FileHeader,

    /// <summary>Where a block of changes starts: <c>@@ -a,b +c,d @@</c>.</summary>
    Hunk,

    /// <summary>The same in both files, there for orientation.</summary>
    Context,

    Added,

    Removed,

    /// <summary><c>\ No newline at end of file</c>.</summary>
    Note,
}


/// <summary>
/// Reads a unified diff - a <c>.patch</c>, a <c>.diff</c>, what
/// <c>git diff</c> prints - line by line into what each line is.
///
/// <para>
/// The first character alone does not tell: a removed line that was itself
/// <c>-- comment</c> reads <c>--- comment</c>, which is a file header
/// outside a block. So the block's own header is believed - it says how
/// many old and new lines follow - and only once both are counted out is
/// a line read as a header again.
/// </para>
/// </summary>
public static class UnifiedDiff {
    /// <summary>The extensions a patch comes in.</summary>
    public static bool IsPatchExtension(string? extension) {
        return string.Equals(extension, ".diff", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".patch", StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>What each of <paramref name="lines"/> is, one kind per line.</summary>
    public static DiffLineKind[] Classify(IReadOnlyList<string> lines) {
        var kinds = new DiffLineKind[lines.Count];
        int oldLeft = 0;
        int newLeft = 0;
        for (int i = 0; i < lines.Count; i++) {
            string line = lines[i];
            if (oldLeft > 0 || newLeft > 0) {
                kinds[i] = InBlock(line, ref oldLeft, ref newLeft);

                continue;
            }

            if (TryReadHunk(line, out oldLeft, out newLeft)) {
                kinds[i] = DiffLineKind.Hunk;
            } else if (line.StartsWith('\\')) {
                // After the last line of a block, which is where it stands.
                kinds[i] = DiffLineKind.Note;
            } else if (IsFileHeader(line)) {
                kinds[i] = DiffLineKind.FileHeader;
            } else {
                kinds[i] = DiffLineKind.Plain;
            }
        }

        return kinds;
    }

    /// <summary>
    /// The old and new line counts of a block header,
    /// <c>@@ -start[,count] +start[,count] @@ text</c>; a missing count is one.
    /// </summary>
    public static bool TryReadHunk(string line, out int oldLines, out int newLines) {
        oldLines = 0;
        newLines = 0;
        if (!line.StartsWith("@@ -", StringComparison.Ordinal)) {
            return false;
        }

        int at = 4;
        if (!TryReadRange(line, ref at, out oldLines) || at >= line.Length || line[at] != ' ') {
            return false;
        }

        at++;
        if (at >= line.Length || line[at] != '+') {
            return false;
        }

        at++;
        if (!TryReadRange(line, ref at, out newLines)) {
            return false;
        }

        return line.AsSpan(at).StartsWith(" @@", StringComparison.Ordinal);
    }


    private static DiffLineKind InBlock(string line, ref int oldLeft, ref int newLeft) {
        char first = line.Length > 0 ? line[0] : ' ';
        switch (first) {
            case '+':
                newLeft--;

                return DiffLineKind.Added;
            case '-':
                oldLeft--;

                return DiffLineKind.Removed;
            case '\\':
                return DiffLineKind.Note;
            default:
                // A context line - an empty one too: some tools strip the
                // single space of an empty context line.
                oldLeft--;
                newLeft--;

                return DiffLineKind.Context;
        }
    }

    private static bool IsFileHeader(string line) {
        return line.StartsWith("diff ", StringComparison.Ordinal)
            || line.StartsWith("--- ", StringComparison.Ordinal)
            || line.StartsWith("+++ ", StringComparison.Ordinal)
            || line.StartsWith("index ", StringComparison.Ordinal)
            || line.StartsWith("new file mode", StringComparison.Ordinal)
            || line.StartsWith("deleted file mode", StringComparison.Ordinal)
            || line.StartsWith("old mode", StringComparison.Ordinal)
            || line.StartsWith("new mode", StringComparison.Ordinal)
            || line.StartsWith("similarity index", StringComparison.Ordinal)
            || line.StartsWith("dissimilarity index", StringComparison.Ordinal)
            || line.StartsWith("rename from", StringComparison.Ordinal)
            || line.StartsWith("rename to", StringComparison.Ordinal)
            || line.StartsWith("copy from", StringComparison.Ordinal)
            || line.StartsWith("copy to", StringComparison.Ordinal)
            || line.StartsWith("Binary files", StringComparison.Ordinal);
    }

    /// <summary><c>start[,count]</c> at <paramref name="at"/>, leaving it after the range.</summary>
    private static bool TryReadRange(string line, ref int at, out int count) {
        count = 0;
        if (!TryReadNumber(line, ref at, out _)) {
            return false;
        }
        if (at < line.Length && line[at] == ',') {
            at++;

            return TryReadNumber(line, ref at, out count);
        }

        count = 1;

        return true;
    }

    private static bool TryReadNumber(string line, ref int at, out int number) {
        number = 0;
        int start = at;
        while (at < line.Length && char.IsAsciiDigit(line[at]) && at - start < 9) {
            number = number * 10 + (line[at] - '0');
            at++;
        }

        return at > start;
    }
}
