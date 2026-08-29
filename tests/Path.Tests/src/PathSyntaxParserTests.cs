using Icod.Path;

using Xunit;

namespace Icod.Path.Tests;

/// <summary>Tests platform-explicit pathname syntax decomposition.</summary>
public sealed class PathSyntaxParserTests {
	/// <summary>Verifies that POSIX pattern-like component text is preserved.</summary>
	[Fact]
	public void ParsesPosixComponentsWithoutNormalization() {
		var result = PathSyntaxParser.Parse(
			"/src/**/../foo?.cs",
			PathPlatformSemantics.Posix
		);

		Assert.True( result.IsAbsolute );
		Assert.True( result.HasRoot );
		Assert.Equal( "/", result.RootPath );
		Assert.Equal( "/", result.VolumeName );
		Assert.Equal(
			new string[] {
				"src",
				"**",
				"..",
				"foo?.cs"
			},
			result.Components
		);
	}

	/// <summary>Verifies that a relative POSIX dot component remains structural input.</summary>
	[Fact]
	public void PreservesRelativePosixDotComponent() {
		var result = PathSyntaxParser.Parse(
			"./src/*.txt",
			PathPlatformSemantics.Posix
		);

		Assert.False( result.IsAbsolute );
		Assert.False( result.HasRoot );
		Assert.Equal( string.Empty, result.RootPath );
		Assert.Equal(
			new string[] {
				".",
				"src",
				"*.txt"
			},
			result.Components
		);
	}

	/// <summary>Verifies canonical Windows drive-root spelling and wildcard preservation.</summary>
	[Fact]
	public void ParsesWindowsDriveRootWithoutInterpretingPatterns() {
		var result = PathSyntaxParser.Parse(
			@"c:/src/**/foo?.cs",
			PathPlatformSemantics.Windows
		);

		Assert.True( result.IsAbsolute );
		Assert.False( result.IsDriveRelative );
		Assert.False( result.IsCurrentVolumeRooted );
		Assert.Equal( @"C:\", result.RootPath );
		Assert.Equal( "C:", result.VolumeName );
		Assert.Equal(
			new string[] {
				"src",
				"**",
				"foo?.cs"
			},
			result.Components
		);
	}

	/// <summary>Verifies Windows drive-relative structure without resolving a base path.</summary>
	[Fact]
	public void ParsesWindowsDriveRelativePath() {
		var result = PathSyntaxParser.Parse(
			@"d:src\*.cs",
			PathPlatformSemantics.Windows
		);

		Assert.False( result.IsAbsolute );
		Assert.True( result.IsDriveRelative );
		Assert.True( result.HasRoot );
		Assert.Equal( "D:", result.RootPath );
		Assert.Equal( "D:", result.VolumeName );
		Assert.Equal(
			new string[] {
				"src",
				"*.cs"
			},
			result.Components
		);
	}

	/// <summary>Verifies Windows current-volume-rooted structure without selecting a volume.</summary>
	[Fact]
	public void ParsesWindowsCurrentVolumeRootedPath() {
		var result = PathSyntaxParser.Parse(
			@"\src\?.txt",
			PathPlatformSemantics.Windows
		);

		Assert.False( result.IsAbsolute );
		Assert.False( result.IsDriveRelative );
		Assert.True( result.IsCurrentVolumeRooted );
		Assert.True( result.HasRoot );
		Assert.Equal( @"\", result.RootPath );
		Assert.Equal( string.Empty, result.VolumeName );
		Assert.Equal(
			new string[] {
				"src",
				"?.txt"
			},
			result.Components
		);
	}

	/// <summary>Verifies UNC root identity while preserving pattern text below the share.</summary>
	[Fact]
	public void ParsesWindowsUncPath() {
		var result = PathSyntaxParser.Parse(
			@"\\Server\Share\src\**\*.cs",
			PathPlatformSemantics.Windows
		);

		Assert.True( result.IsAbsolute );
		Assert.Equal( @"\\Server\Share\", result.RootPath );
		Assert.Equal( @"\\Server\Share", result.VolumeName );
		Assert.Equal(
			new string[] {
				"src",
				"**",
				"*.cs"
			},
			result.Components
		);
	}

	/// <summary>Verifies deterministic rejection of malformed Windows roots.</summary>
	[Fact]
	public void RejectsMalformedWindowsUncRoot() {
		var exception = Assert.Throws<ArgumentException>(
			() => PathSyntaxParser.Parse(
				@"\\server",
				PathPlatformSemantics.Windows
			)
		);

		Assert.Equal( "path", exception.ParamName );
	}

	/// <summary>Verifies that NUL remains invalid pathname syntax.</summary>
	[Fact]
	public void RejectsNulCharacter() {
		var exception = Assert.Throws<ArgumentException>(
			() => PathSyntaxParser.Parse(
				"alpha\0beta",
				PathPlatformSemantics.Posix
			)
		);

		Assert.Equal( "path", exception.ParamName );
	}
}
