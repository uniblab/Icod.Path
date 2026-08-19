using Icod.Path;

using Xunit;

namespace Icod.Path.Tests;

/// <summary>Tests platform-explicit lexical normalization.</summary>
public sealed class PathLexicalNormalizerTests {
	/// <summary>Verifies POSIX separator, dot, and parent normalization.</summary>
	[Fact]
	public void NormalizesPosixComponents() {
		var result = PathLexicalNormalizer.Normalize(
			"../alpha//./beta/../gamma",
			"/work/base",
			PathPlatformSemantics.Posix
		);

		Assert.Equal( "/work/alpha/gamma", result.Path );
		Assert.Equal( "/", result.Root!.RootPath );
	}

	/// <summary>Verifies that POSIX parent components cannot escape the slash root.</summary>
	[Fact]
	public void ClampsPosixParentsAtRoot() {
		var result = PathLexicalNormalizer.Normalize(
			"/../../alpha",
			"/work",
			PathPlatformSemantics.Posix
		);

		Assert.Equal( "/alpha", result.Path );
	}

	/// <summary>Verifies drive-rooted Windows normalization and canonical separators.</summary>
	[Fact]
	public void NormalizesWindowsDrivePath() {
		var result = PathLexicalNormalizer.Normalize(
			@"c:/work/./one/../two",
			@"C:\base",
			PathPlatformSemantics.Windows
		);

		Assert.Equal( @"C:\work\two", result.Path );
		Assert.Equal( "C:", result.Root!.VolumeName );
	}

	/// <summary>Verifies UNC root parsing and case-insensitive volume identity.</summary>
	[Fact]
	public void NormalizesWindowsUncPath() {
		var result = PathLexicalNormalizer.Normalize(
			@"\\Server\Share\one\..\two",
			@"C:\base",
			PathPlatformSemantics.Windows
		);

		Assert.Equal( @"\\Server\Share\two", result.Path );
		Assert.Equal( @"\\Server\Share", result.Root!.VolumeName );
	}

	/// <summary>Verifies extended drive-root parsing without losing the extended prefix.</summary>
	[Fact]
	public void NormalizesWindowsExtendedDrivePath() {
		var result = PathLexicalNormalizer.Normalize(
			@"\\?\c:\work\one\..\two",
			@"C:\base",
			PathPlatformSemantics.Windows
		);

		Assert.Equal( @"\\?\C:\work\two", result.Path );
		Assert.Equal( "C:", result.Root!.VolumeName );
	}

	/// <summary>Verifies extended UNC-root parsing and normalized volume identity.</summary>
	[Fact]
	public void NormalizesWindowsExtendedUncPath() {
		var result = PathLexicalNormalizer.Normalize(
			@"\\?\UNC\Server\Share\one\..\two",
			@"C:\base",
			PathPlatformSemantics.Windows
		);

		Assert.Equal( @"\\?\UNC\Server\Share\two", result.Path );
		Assert.Equal( @"\\Server\Share", result.Root!.VolumeName );
	}

	/// <summary>Verifies current-volume rooted Windows input.</summary>
	[Fact]
	public void ResolvesWindowsCurrentVolumeRoot() {
		var result = PathLexicalNormalizer.Normalize(
			@"\alpha\beta",
			@"D:\work\base",
			PathPlatformSemantics.Windows
		);

		Assert.Equal( @"D:\alpha\beta", result.Path );
	}

	/// <summary>Verifies same-volume drive-relative input against the explicit base directory.</summary>
	[Fact]
	public void ResolvesWindowsDriveRelativePath() {
		var result = PathLexicalNormalizer.Normalize(
			@"d:alpha\beta",
			@"D:\work\base",
			PathPlatformSemantics.Windows
		);

		Assert.Equal( @"D:\work\base\alpha\beta", result.Path );
	}

	/// <summary>Verifies deterministic failure for a drive-relative path on another volume.</summary>
	[Fact]
	public void RejectsWindowsDriveRelativePathOnDifferentVolume() {
		var result = PathLexicalNormalizer.Normalize(
			@"E:alpha",
			@"D:\work",
			PathPlatformSemantics.Windows
		);

		Assert.Equal( CanonicalPathFailureCode.DriveRelativePath, result.Failure!.Code );
		Assert.Null( result.Path );
	}

	/// <summary>Verifies deterministic failure for a malformed UNC root.</summary>
	[Fact]
	public void RejectsMalformedWindowsUncRoot() {
		var result = PathLexicalNormalizer.Normalize(
			@"\\server",
			@"C:\work",
			PathPlatformSemantics.Windows
		);

		Assert.Equal( CanonicalPathFailureCode.InvalidPath, result.Failure!.Code );
		Assert.Null( result.Path );
	}

	/// <summary>Verifies deterministic failure for invalid Windows component characters.</summary>
	[Fact]
	public void RejectsInvalidWindowsComponent() {
		var result = PathLexicalNormalizer.Normalize(
			@"C:\work\bad?name",
			@"C:\work",
			PathPlatformSemantics.Windows
		);

		Assert.Equal( CanonicalPathFailureCode.InvalidPath, result.Failure!.Code );
		Assert.Null( result.Path );
	}
}
