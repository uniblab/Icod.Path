using Icod.Path;

using Xunit;

namespace Icod.Path.Tests;

/// <summary>Tests physical canonicalization over deterministic filesystem observations.</summary>
public sealed class CanonicalPathResolverTests {
	/// <summary>Verifies resolution of the platform root without manufacturing a component.</summary>
	[Fact]
	public async Task ResolvesPosixRoot() {
		var resolver = new CanonicalPathResolver( CreatePosixProvider() );

		var result = await resolver.ResolvePhysicalAsync( "/" );

		Assert.Equal( "/", result.Path );
		Assert.Empty( result.ResolvedLinks );
	}

	/// <summary>Verifies physical resolution relative to the provider current directory.</summary>
	[Fact]
	public async Task ResolvesRelativePathFromProviderCurrentDirectory() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddFile( "/work/file" );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync( "file" );

		Assert.Equal( "/work/file", result.Path );
	}

	/// <summary>Verifies relative symbolic-link target resolution.</summary>
	[Fact]
	public async Task ResolvesRelativeSymbolicLinkTarget() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddDirectory( "/work/target" )
			.AddFile( "/work/target/file" )
			.AddLink( "/work/link", "target" );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync( "/work/link/file" );

		Assert.Equal( "/work/target/file", result.Path );
		Assert.Equal( "/work/link", Assert.Single( result.ResolvedLinks ).LinkPath );
	}

	/// <summary>Verifies absolute symbolic-link target resolution.</summary>
	[Fact]
	public async Task ResolvesAbsoluteSymbolicLinkTarget() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddDirectory( "/target" )
			.AddFile( "/target/file" )
			.AddLink( "/work/link", "/target" );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync( "/work/link/file" );

		Assert.Equal( "/target/file", result.Path );
	}

	/// <summary>Verifies that parent components are processed after preceding links.</summary>
	[Fact]
	public async Task ProcessesParentAfterResolvingPrecedingLink() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddDirectory( "/target" )
			.AddDirectory( "/target/child" )
			.AddFile( "/target/file" )
			.AddLink( "/work/link", "/target/child" );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync( "/work/link/../file" );

		Assert.Equal( "/target/file", result.Path );
	}

	/// <summary>Verifies strict missing-component failure and the absence of a false-success path.</summary>
	[Fact]
	public async Task StrictResolutionDoesNotEchoMissingInputAsSuccess() {
		var provider = CreatePosixProvider().AddDirectory( "/work" );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync( "/work/missing" );

		Assert.Equal( CanonicalPathFailureCode.NotFound, result.Failure!.Code );
		Assert.Null( result.Path );
	}

	/// <summary>Verifies the final-component missing policy.</summary>
	[Fact]
	public async Task AllowsOnlyMissingFinalComponent() {
		var provider = CreatePosixProvider().AddDirectory( "/work" );
		var resolver = new CanonicalPathResolver( provider );
		var options = new CanonicalPathResolutionOptions {
			MissingComponentPolicy = MissingPathComponentPolicy.AllowFinalComponent
		};

		var accepted = await resolver.ResolvePhysicalAsync( "/work/new", options );
		var rejected = await resolver.ResolvePhysicalAsync( "/work/new/child", options );

		Assert.Equal( "/work/new", accepted.Path );
		Assert.Equal( 1, accepted.MissingComponentCount );
		Assert.Equal( CanonicalPathFailureCode.NotFound, rejected.Failure!.Code );
	}

	/// <summary>Verifies lexical completion of a missing suffix.</summary>
	[Fact]
	public async Task AllowsAndNormalizesMissingSuffix() {
		var provider = CreatePosixProvider().AddDirectory( "/work" );
		var resolver = new CanonicalPathResolver( provider );
		var options = new CanonicalPathResolutionOptions {
			MissingComponentPolicy = MissingPathComponentPolicy.AllowMissingSuffix
		};

		var result = await resolver.ResolvePhysicalAsync(
			"/work/missing/child/../leaf",
			options
		);

		Assert.Equal( "/work/missing/leaf", result.Path );
		Assert.Equal( 2, result.MissingComponentCount );
	}

	/// <summary>Verifies that a missing suffix can return to an existing prefix and resume physical resolution.</summary>
	[Fact]
	public async Task ResumesPhysicalResolutionAfterMissingSuffixParentTraversal() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddDirectory( "/target" )
			.AddFile( "/target/file" )
			.AddLink( "/work/link", "/target" );
		var resolver = new CanonicalPathResolver( provider );
		var options = new CanonicalPathResolutionOptions {
			MissingComponentPolicy = MissingPathComponentPolicy.AllowMissingSuffix
		};

		var result = await resolver.ResolvePhysicalAsync(
			"/work/missing/../link/file",
			options
		);

		Assert.Equal( "/target/file", result.Path );
		Assert.Equal( 0, result.MissingComponentCount );
		Assert.Equal( "/work/link", Assert.Single( result.ResolvedLinks ).LinkPath );
	}

	/// <summary>Verifies that a trailing dot does not turn a missing final component into a missing suffix.</summary>
	[Fact]
	public async Task AllowsMissingFinalComponentFollowedByDot() {
		var provider = CreatePosixProvider().AddDirectory( "/work" );
		var resolver = new CanonicalPathResolver( provider );
		var options = new CanonicalPathResolutionOptions {
			MissingComponentPolicy = MissingPathComponentPolicy.AllowFinalComponent
		};

		var result = await resolver.ResolvePhysicalAsync( "/work/new/.", options );

		Assert.Equal( "/work/new", result.Path );
		Assert.Equal( 1, result.MissingComponentCount );
	}

	/// <summary>Verifies failure when a nonfinal component is not a directory.</summary>
	[Fact]
	public async Task RejectsTraversalThroughFile() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddFile( "/work/file" );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync( "/work/file/child" );

		Assert.Equal( CanonicalPathFailureCode.NotDirectory, result.Failure!.Code );
	}

	/// <summary>Verifies deterministic symbolic-link loop detection.</summary>
	[Fact]
	public async Task DetectsSymbolicLinkLoop() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddLink( "/work/a", "b" )
			.AddLink( "/work/b", "a" );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync( "/work/a" );

		Assert.Equal( CanonicalPathFailureCode.SymbolicLinkLoop, result.Failure!.Code );
		Assert.Null( result.Path );
	}

	/// <summary>Verifies the configured symbolic-link traversal limit.</summary>
	[Fact]
	public async Task EnforcesSymbolicLinkLimit() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddLink( "/work/a", "b" )
			.AddFile( "/work/b" );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync(
			"/work/a",
			new CanonicalPathResolutionOptions { MaximumSymbolicLinks = 0 }
		);

		Assert.Equal( CanonicalPathFailureCode.TooManySymbolicLinks, result.Failure!.Code );
	}

	/// <summary>Verifies controlled failure for unsupported reparse-point semantics.</summary>
	[Fact]
	public async Task RejectsUnsupportedReparsePoint() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddUnsupportedReparsePoint( "/work/object" );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync( "/work/object" );

		Assert.Equal( CanonicalPathFailureCode.UnsupportedReparsePoint, result.Failure!.Code );
	}

	/// <summary>Verifies terminal no-follow link inspection.</summary>
	[Fact]
	public async Task InspectsRawLinkTargetWithoutFollowingIt() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddLink( "/work/link", "missing-target" );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.InspectLinkAsync( "/work/link" );

		Assert.Equal( "missing-target", result.Target );
		Assert.True( result.IsSymbolicLink );
	}

	/// <summary>Verifies that no-follow terminal inspection still resolves intermediate links physically.</summary>
	[Fact]
	public async Task InspectsTerminalLinkAfterResolvingIntermediateLinks() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddDirectory( "/target" )
			.AddDirectory( "/target/child" )
			.AddLink( "/work/middle", "/target/child" )
			.AddLink( "/target/terminal", "destination" );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.InspectLinkAsync( "/work/middle/../terminal" );

		Assert.True( result.Succeeded );
		Assert.Equal( "/target/terminal", result.Path );
		Assert.True( result.IsSymbolicLink );
		Assert.Equal( "destination", result.Target );
	}

	/// <summary>Verifies inspection of an unsupported final reparse point without inventing link semantics.</summary>
	[Fact]
	public async Task InspectsUnsupportedFinalReparsePoint() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddUnsupportedReparsePoint( "/work/object" );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.InspectLinkAsync( "/work/object" );

		Assert.True( result.Succeeded );
		Assert.True( result.IsReparsePoint );
		Assert.False( result.IsSymbolicLink );
		Assert.Null( result.Target );
	}

	/// <summary>Verifies that no-link resolution preserves a symbolic-link pathname component.</summary>
	[Fact]
	public async Task PreservesSymbolicLinkWhenFollowingIsDisabled() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddDirectory( "/target" )
			.AddLink( "/work/link", "/target" );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync(
			"/work/link",
			new CanonicalPathResolutionOptions {
				MissingComponentPolicy = MissingPathComponentPolicy.RequireExisting,
				FollowSymbolicLinks = false
			}
		);

		Assert.Equal( "/work/link", result.Path );
		Assert.Empty( result.ResolvedLinks );
	}

	/// <summary>Verifies strict no-link resolution still rejects a dangling terminal link.</summary>
	[Fact]
	public async Task StrictNoLinkResolutionRejectsDanglingFinalLink() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddLink( "/work/link", "missing" );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync(
			"/work/link",
			new CanonicalPathResolutionOptions {
				MissingComponentPolicy = MissingPathComponentPolicy.RequireExisting,
				FollowSymbolicLinks = false
			}
		);

		Assert.Equal( CanonicalPathFailureCode.NotFound, result.Failure!.Code );
		Assert.Null( result.Path );
	}

	/// <summary>Verifies a requested terminal directory is checked independently of existence.</summary>
	[Fact]
	public async Task RequiresFinalDirectoryWhenRequested() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddFile( "/work/file" );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync(
			"/work/file",
			new CanonicalPathResolutionOptions {
				MissingComponentPolicy = MissingPathComponentPolicy.RequireExisting,
				RequireFinalDirectory = true
			}
		);

		Assert.Equal( CanonicalPathFailureCode.NotDirectory, result.Failure!.Code );
		Assert.Null( result.Path );
	}

	/// <summary>Verifies POSIX relative-path calculation.</summary>
	[Fact]
	public void CalculatesPosixRelativePath() {
		var resolver = new CanonicalPathResolver( CreatePosixProvider() );

		var result = resolver.GetRelativePath( "/work/one", "/work/two/file" );

		Assert.Equal( "../two/file", result.Path );
	}

	/// <summary>Verifies Windows case-insensitive relative-path calculation.</summary>
	[Fact]
	public void CalculatesWindowsRelativePathCaseInsensitively() {
		var provider = new SyntheticCanonicalPathFileSystemProvider(
			PathPlatformSemantics.Windows,
			@"C:\work"
		);
		var resolver = new CanonicalPathResolver( provider );

		var result = resolver.GetRelativePath(
			@"C:\Work\One",
			@"c:\work\Two\File"
		);

		Assert.Equal( @"..\Two\File", result.Path );
	}

	/// <summary>Verifies that extended and ordinary Windows roots share one volume identity.</summary>
	[Fact]
	public void CalculatesRelativePathAcrossEquivalentWindowsRootSpellings() {
		var provider = new SyntheticCanonicalPathFileSystemProvider(
			PathPlatformSemantics.Windows,
			@"C:\work"
		);
		var resolver = new CanonicalPathResolver( provider );

		var result = resolver.GetRelativePath(
			@"C:\work\one",
			@"\\?\C:\work\two"
		);

		Assert.Equal( @"..\two", result.Path );
	}

	/// <summary>Verifies physical Windows link resolution with case-insensitive component observation.</summary>
	[Fact]
	public async Task ResolvesWindowsLinkLikeReparsePoint() {
		var provider = new SyntheticCanonicalPathFileSystemProvider(
			PathPlatformSemantics.Windows,
			@"C:\work"
		)
			.AddDirectory( @"C:\work" )
			.AddDirectory( @"C:\target" )
			.AddFile( @"C:\target\file" )
			.AddLink( @"C:\work\link", @"..\target", isReparsePoint: true );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync( @"c:\WORK\link\file" );

		Assert.Equal( @"C:\target\file", result.Path );
		Assert.EndsWith(
			@"WORK\link",
			Assert.Single( result.ResolvedLinks ).LinkPath,
			StringComparison.OrdinalIgnoreCase
		);
	}

	/// <summary>Verifies that provider failures retain their structured code and no success path.</summary>
	[Fact]
	public async Task PropagatesProviderFailure() {
		var provider = CreatePosixProvider()
			.AddDirectory( "/work" )
			.AddFailure( "/work/denied", CanonicalPathFailureCode.AccessDenied );
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync( "/work/denied" );

		Assert.Equal( CanonicalPathFailureCode.AccessDenied, result.Failure!.Code );
		Assert.Null( result.Path );
	}

	/// <summary>Verifies controlled relative-path failure across Windows volumes.</summary>
	[Fact]
	public void RejectsRelativePathAcrossWindowsVolumes() {
		var provider = new SyntheticCanonicalPathFileSystemProvider(
			PathPlatformSemantics.Windows,
			@"C:\work"
		);
		var resolver = new CanonicalPathResolver( provider );

		var result = resolver.GetRelativePath( @"C:\work", @"D:\work" );

		Assert.Equal( CanonicalPathFailureCode.DifferentRoot, result.Failure!.Code );
	}

	/// <summary>Verifies component-aware containment rather than textual prefix matching.</summary>
	[Fact]
	public void EvaluatesContainmentByComponents() {
		var resolver = new CanonicalPathResolver( CreatePosixProvider() );

		var child = resolver.EvaluateContainment( "/safe/root", "/safe/root/child" );
		var siblingPrefix = resolver.EvaluateContainment( "/safe/root", "/safe/rooted" );

		Assert.True( child.IsContained );
		Assert.False( siblingPrefix.IsContained );
	}

	/// <summary>Verifies cancellation before provider observation completes.</summary>
	[Fact]
	public async Task ObservesCancellation() {
		var resolver = new CanonicalPathResolver( CreatePosixProvider() );
		using var source = new CancellationTokenSource();
		source.Cancel();

		await Assert.ThrowsAsync<OperationCanceledException>( async () => {
			_ = await resolver.ResolvePhysicalAsync(
				"/work",
				cancellationToken: source.Token
			);
		} );
	}

	private static SyntheticCanonicalPathFileSystemProvider CreatePosixProvider() =>
		new( PathPlatformSemantics.Posix, "/work" )
	;
}
