using Icod.Path;

using Xunit;

namespace Icod.Path.Tests;

/// <summary>Tests deterministic POSIX and Windows pathname platform semantics.</summary>
public sealed class PathPlatformSemanticsTests {
	/// <summary>Verifies the POSIX separator and comparison contract.</summary>
	[Fact]
	public void PosixSemanticsAreCaseSensitiveAndSlashDelimited() {
		var semantics = PathPlatformSemantics.Posix;

		Assert.Equal( PathPlatformKind.Posix, semantics.Kind );
		Assert.Equal( '/', semantics.DirectorySeparator );
		Assert.Null( semantics.AlternateDirectorySeparator );
		Assert.Equal( StringComparison.Ordinal, semantics.PathComparison );
		Assert.True( semantics.IsDirectorySeparator( '/' ) );
		Assert.False( semantics.IsDirectorySeparator( '\\' ) );
		Assert.True( semantics.PathComparer.Equals( "alpha", "alpha" ) );
		Assert.False( semantics.PathComparer.Equals( "alpha", "ALPHA" ) );
	}

	/// <summary>Verifies the Windows separator and comparison contract.</summary>
	[Fact]
	public void WindowsSemanticsAreCaseInsensitiveAndAcceptBothSeparators() {
		var semantics = PathPlatformSemantics.Windows;

		Assert.Equal( PathPlatformKind.Windows, semantics.Kind );
		Assert.Equal( '\\', semantics.DirectorySeparator );
		Assert.Equal( '/', semantics.AlternateDirectorySeparator );
		Assert.Equal( StringComparison.OrdinalIgnoreCase, semantics.PathComparison );
		Assert.True( semantics.IsDirectorySeparator( '\\' ) );
		Assert.True( semantics.IsDirectorySeparator( '/' ) );
		Assert.True( semantics.PathComparer.Equals( "alpha", "ALPHA" ) );
	}

	/// <summary>Verifies that host semantics select the current operating-system grammar.</summary>
	[Fact]
	public void HostSemanticsMatchCurrentOperatingSystem() {
		var expected = OperatingSystem.IsWindows()
			? PathPlatformSemantics.Windows
			: PathPlatformSemantics.Posix
		;

		Assert.Same( expected, PathPlatformSemantics.Host );
	}
}
