using Wander.Core.Rename;

namespace Wander.Core.Tests;

public class RenameTemplateHistoryTests {
    [Fact]
    public void NewestFirst_AndARepeatMovesToTheFront() {
        var history = RenameTemplateHistory.Add(Array.Empty<string>(), "a_[C]");
        history = RenameTemplateHistory.Add(history, "b_[C]");
        history = RenameTemplateHistory.Add(history, "a_[C]");

        Assert.Equal(new[] { "a_[C]", "b_[C]" }, history);
    }

    [Fact]
    public void KeepsFive_DroppingTheOldest() {
        IReadOnlyList<string> history = Array.Empty<string>();
        for (int i = 1; i <= 7; i++) {
            history = RenameTemplateHistory.Add(history, $"t{i}_[C]");
        }

        Assert.Equal(new[] { "t7_[C]", "t6_[C]", "t5_[C]", "t4_[C]", "t3_[C]" }, history);
    }

    [Fact]
    public void TheDefaultAndTheEmptyTemplate_AreNotRemembered() {
        var history = new[] { "a_[C]" };

        Assert.Same(history, RenameTemplateHistory.Add(history, RenameRules.IdentityTemplate));
        Assert.Same(history, RenameTemplateHistory.Add(history, ""));
    }

    [Fact]
    public void TemplatesDifferingInCase_AreDifferent() {
        // "[c]" is not a token; the two do different things.
        var history = RenameTemplateHistory.Add(new[] { "x_[C]" }, "x_[c]");

        Assert.Equal(new[] { "x_[c]", "x_[C]" }, history);
    }
}
