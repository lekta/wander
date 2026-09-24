using Wander.Core.Operations;

namespace Wander.Core.Tests;

public class PathClaimsTests {
    private const string Album = @"C:\photos\album";
    private const string Shot = @"C:\photos\album\shot.cr3";


    [Fact]
    public void Nothing_IsClaimed_ByDefault() {
        var claims = new PathClaims();

        Assert.Empty(claims.Covering(Shot));
        Assert.False(claims.IsClaimed(Shot));
    }

    [Fact]
    public void Claim_CoversThePathItself_CaseInsensitively_UntilDisposed() {
        var claims = new PathClaims();

        var token = claims.Claim(new[] { Shot }, ClaimKind.UserOperation, OperationVerbs.Copy);

        var found = Assert.Single(claims.Covering(Shot.ToUpperInvariant()));
        Assert.Equal(OperationVerbs.Copy, found.Owner);
        Assert.True(claims.IsClaimed(Shot));

        token.Dispose();
        token.Dispose();

        Assert.Empty(claims.Covering(Shot));
    }

    [Fact]
    public void ClaimOnAFolder_CoversWhatIsInside() {
        var claims = new PathClaims();
        using var token = claims.Claim(new[] { Album + @"\" }, ClaimKind.UserOperation, OperationVerbs.Move);

        Assert.Single(claims.Covering(Shot));
        Assert.True(claims.IsClaimed(Shot));
    }

    [Fact]
    public void ClaimInsideAFolder_IsInTheWayOfTheFolder_ButPutsNoBadgeOnIt() {
        var claims = new PathClaims();
        using var token = claims.Claim(new[] { Shot }, ClaimKind.Background, "preview");

        Assert.Single(claims.Covering(Album));
        Assert.False(claims.IsClaimed(Album));
    }

    [Fact]
    public void ANameThatOnlyStartsTheSame_IsNotCovered() {
        var claims = new PathClaims();
        using var token = claims.Claim(new[] { Album }, ClaimKind.UserOperation, OperationVerbs.Copy);

        Assert.Empty(claims.Covering(@"C:\photos\album 2\shot.cr3"));
        Assert.Empty(claims.Covering(@"C:\photos\album 2"));
    }

    [Fact]
    public void Yield_CancelsBackgroundReaders_AndLeavesUserOperationsAlone() {
        var claims = new PathClaims();
        using var reader = new CancellationTokenSource();
        using var copy = new CancellationTokenSource();
        using var a = claims.Claim(new[] { Shot }, ClaimKind.Background, "thumbnail", reader);
        using var b = claims.Claim(new[] { Album }, ClaimKind.UserOperation, OperationVerbs.Copy, copy);

        var asked = claims.Yield(Shot);

        Assert.Equal("thumbnail", Assert.Single(asked).Owner);
        Assert.True(reader.IsCancellationRequested);
        Assert.False(copy.IsCancellationRequested);
        Assert.Equal(2, claims.Covering(Shot).Count);
    }

    [Fact]
    public void Yield_SurvivesAReaderThatHasAlreadyGone() {
        var claims = new PathClaims();
        var reader = new CancellationTokenSource();
        using var token = claims.Claim(new[] { Shot }, ClaimKind.Background, "thumbnail", reader);
        reader.Dispose();

        Assert.Single(claims.Yield(Shot));
    }

    [Fact]
    public void Covering_LeavesOutTheAskersOwnClaim() {
        var claims = new PathClaims();
        using var own = claims.Claim(new[] { Album }, ClaimKind.UserOperation, OperationVerbs.Move);
        using var other = claims.Claim(new[] { Shot }, ClaimKind.UserOperation, OperationVerbs.Copy);

        var found = Assert.Single(claims.Covering(Shot, except: own));
        Assert.Equal(OperationVerbs.Copy, found.Owner);
        Assert.Equal(2, claims.Covering(Shot).Count);
    }

    [Fact]
    public void IsClaimed_ByKind_CountsOnlyThatKind() {
        var claims = new PathClaims();
        using var reader = claims.Claim(new[] { Shot }, ClaimKind.Background, ClaimOwners.Thumbnail);

        Assert.True(claims.IsClaimed(Shot));
        Assert.False(claims.IsClaimed(Shot, ClaimKind.UserOperation));

        using var copy = claims.Claim(new[] { Album }, ClaimKind.UserOperation, OperationVerbs.Copy);

        Assert.True(claims.IsClaimed(Shot, ClaimKind.UserOperation));
    }

    [Fact]
    public void Count_IsTheNumberOfClaimedPaths() {
        var claims = new PathClaims();
        using var a = claims.Claim(new[] { Shot, Album }, ClaimKind.UserOperation, OperationVerbs.Copy);
        using var b = claims.Claim(new[] { Shot }, ClaimKind.Background, ClaimOwners.Thumbnail);

        Assert.Equal(2, claims.Count);
    }

    [Fact]
    public void Changed_IsQuiet_ForBackgroundReaders() {
        // A thumbnail claims and lets go of a file for every row scrolled
        // past; nothing on screen shows those, so nobody is told.
        var claims = new PathClaims();
        int fired = 0;
        claims.Changed += (_, _) => fired++;

        claims.Claim(new[] { Shot }, ClaimKind.Background, ClaimOwners.Thumbnail).Dispose();

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Changed_FiresOnClaimAndOnRelease() {
        var claims = new PathClaims();
        int fired = 0;
        claims.Changed += (_, _) => fired++;

        claims.Claim(new[] { Shot }, ClaimKind.UserOperation, OperationVerbs.Recycle).Dispose();

        Assert.Equal(2, fired);
    }


    // --- The badge's wait ---------------------------------------------------

    [Fact]
    public void IsClaimed_WithAnAge_CountsAClaimOnlyOnceItIsThatOld() {
        long now = 1000;
        var claims = new PathClaims(() => now);
        using var copy = claims.Claim(new[] { Album }, ClaimKind.UserOperation, OperationVerbs.Copy);

        now += PathClaims.BadgeDelayMs - 1;
        Assert.False(claims.IsClaimed(Shot, ClaimKind.UserOperation, PathClaims.BadgeDelayMs));
        Assert.True(claims.IsClaimed(Shot, ClaimKind.UserOperation));

        now += 1;
        Assert.True(claims.IsClaimed(Shot, ClaimKind.UserOperation, PathClaims.BadgeDelayMs));
    }

    [Fact]
    public void AClaimLetGoBeforeItsAge_IsNeverShown() {
        long now = 1000;
        var claims = new PathClaims(() => now);

        var delete = claims.Claim(new[] { Shot }, ClaimKind.UserOperation, OperationVerbs.Recycle);
        now += 300;
        delete.Dispose();
        now += 300;

        Assert.False(claims.IsClaimed(Shot, ClaimKind.UserOperation, PathClaims.BadgeDelayMs));
        Assert.Null(claims.DueInMs(ClaimKind.UserOperation, PathClaims.BadgeDelayMs));
    }

    [Fact]
    public void AnOlderClaimOnTheSamePath_Counts() {
        long now = 1000;
        var claims = new PathClaims(() => now);
        using var copy = claims.Claim(new[] { Shot }, ClaimKind.UserOperation, OperationVerbs.Copy);
        now += 500;
        using var action = claims.Claim(new[] { Shot }, ClaimKind.UserOperation, OperationVerbs.Copy);

        Assert.True(claims.IsClaimed(Shot, ClaimKind.UserOperation, PathClaims.BadgeDelayMs));
    }

    [Fact]
    public void DueInMs_IsWhenTheNextYoungClaimComesOfAge() {
        long now = 1000;
        var claims = new PathClaims(() => now);
        using var reader = claims.Claim(new[] { Album }, ClaimKind.Background, ClaimOwners.Thumbnail);
        using var first = claims.Claim(new[] { Shot }, ClaimKind.UserOperation, OperationVerbs.Copy);
        now += 100;
        using var second = claims.Claim(new[] { Album }, ClaimKind.UserOperation, OperationVerbs.Move);

        now += 100;
        Assert.Equal(200, claims.DueInMs(ClaimKind.UserOperation, PathClaims.BadgeDelayMs));

        now += 250;
        Assert.Equal(50, claims.DueInMs(ClaimKind.UserOperation, PathClaims.BadgeDelayMs));

        now += 50;
        Assert.Null(claims.DueInMs(ClaimKind.UserOperation, PathClaims.BadgeDelayMs));
    }
}
