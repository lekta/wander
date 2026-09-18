using Wander.App.Dialogs;
using Wander.Core.FileSystem;

namespace Wander.Harness.Host;

/// <summary>
/// Answers every modal question by policy and remembers what was asked.
/// Default: accept (confirmations go through, so a scenario that deletes
/// deletes). A scenario changes the policy per kind with a <c>dialogs</c>
/// step; the report lists every question in order so a run that answered
/// something it should not have is visible, not silent.
/// </summary>
public sealed class ScriptedDialogs : IDialogs {
    /// <summary>
    /// Kinds answered "accept" whatever <see cref="DefaultAnswer"/> says - a
    /// scenario that turns the default to "cancel" must not leave the run
    /// hanging on its way out. A <c>dialogs</c> step still overrides them.
    /// </summary>
    private readonly Dictionary<DialogKind, bool> _answers = new() {
        [DialogKind.ExitWithOperations] = true,
    };
    private readonly List<string> _records = new();

    /// <summary>
    /// Kinds that answer Cancel whatever <see cref="ChoiceAnswer"/> says:
    /// "try again" repeated by policy would never end while the file stays
    /// held. A <c>dialogs</c> step with <c>kind</c> and <c>choice</c> overrides.
    /// </summary>
    private readonly Dictionary<DialogKind, int> _choices = new() {
        [DialogKind.DeleteInUse] = -1,
    };


    public bool DefaultAnswer { get; set; } = true;

    public ConflictResolution Conflict { get; set; } = ConflictResolution.Replace;

    /// <summary>What <see cref="Choose"/> returns: the answer's index, -1 cancels.</summary>
    public int ChoiceAnswer { get; set; }

    /// <summary>What <see cref="Prompt"/> returns; null cancels.</summary>
    public string? PromptAnswer { get; set; }

    /// <summary>What <see cref="PickFolder"/> returns; null cancels.</summary>
    public string? FolderAnswer { get; set; }

    public IReadOnlyList<string> Records {
        get {
            lock (_records) {
                return _records.ToList();
            }
        }
    }


    public void Answer(DialogKind kind, bool accept) {
        _answers[kind] = accept;
    }

    public bool Ask(DialogRequest request) {
        bool accept = request.Buttons == DialogButtons.Ok
            || (_answers.TryGetValue(request.Kind, out bool policy) ? policy : DefaultAnswer);
        Record($"{request.Kind} [{request.Buttons}] -> {(accept ? "accept" : "cancel")}: {OneLine(request.Message)}");

        return accept;
    }

    public void AnswerChoice(DialogKind kind, int choice) {
        _choices[kind] = choice;
    }

    public int Choose(ChoiceRequest request) {
        int pick = _choices.TryGetValue(request.Kind, out int policy) ? policy : ChoiceAnswer;
        if (pick >= request.Choices.Count) {
            pick = -1;
        }
        string answer = pick >= 0 ? request.Choices[pick] : "cancel";
        Record($"{request.Kind} [{string.Join(" | ", request.Choices)}] -> {answer}: {OneLine(request.Message)}");

        return pick;
    }

    public string? Prompt(string title, string label, string initial, bool filenameMode) {
        Record($"Prompt '{title}' initial='{initial}' -> {PromptAnswer ?? "(cancel)"}");

        return PromptAnswer;
    }

    public string? PickFolder(string title, string? startAt = null) {
        Record($"PickFolder '{title}' at='{startAt}' -> {FolderAnswer ?? "(cancel)"}");

        return FolderAnswer;
    }

    public IConflictResolver CreateConflictResolver(bool skipIdentical) {
        return new PolicyConflictResolver(this);
    }


    private void Record(string text) {
        lock (_records) {
            _records.Add(text);
        }
    }

    private static string OneLine(string text) {
        string flat = text.Replace("\r", "").Replace("\n", " | ");

        return flat.Length > 160 ? flat[..160] + "..." : flat;
    }


    private sealed class PolicyConflictResolver : IConflictResolver {
        private readonly ScriptedDialogs _owner;


        public PolicyConflictResolver(ScriptedDialogs owner) {
            _owner = owner;
        }


        public IReadOnlyList<ConflictAnswer>? ResolveAll(ConflictRequest request) {
            _owner.Record($"Conflicts: {request.Conflicts.Count} of {request.ItemCount} -> {_owner.Conflict}");

            return _owner.Conflict == ConflictResolution.Cancel
                ? null
                : request.Conflicts.Select(c => new ConflictAnswer(c, _owner.Conflict)).ToList();
        }
    }
}
