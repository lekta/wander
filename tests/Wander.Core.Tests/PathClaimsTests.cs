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
    public void Changed_FiresOnClaimAndOnRelease() {
        var claims = new PathClaims();
        int fired = 0;
        claims.Changed += (_, _) => fired++;

        claims.Claim(new[] { Shot }, ClaimKind.UserOperation, OperationVerbs.Recycle).Dispose();

        Assert.Equal(2, fired);
    }
}
