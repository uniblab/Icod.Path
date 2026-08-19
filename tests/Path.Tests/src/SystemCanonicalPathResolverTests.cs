using Icod.Path;
using SystemPath = System.IO.Path;

using Xunit;

namespace Icod.Path.Tests;

/// <summary>Exercises canonical resolution against the host filesystem where capabilities permit.</summary>
public sealed class SystemCanonicalPathResolverTests {
	/// <summary>Verifies ordinary host-directory and file resolution.</summary>
	[Fact]
	public async Task ResolvesOrdinaryHostPath() {
		var root = CreateTemporaryDirectory();
		try {
			var nested = Directory.CreateDirectory( SystemPath.Combine( root, "nested" ) ).FullName;
			var file = SystemPath.Combine( nested, "file.txt" );
			await File.WriteAllTextAsync( file, "content" );
			var resolver = new CanonicalPathResolver();

			var result = await resolver.ResolvePhysicalAsync(
				SystemPath.Combine( root, ".", "nested", "..", "nested", "file.txt" )
			);

			Assert.Equal( SystemPath.GetFullPath( file ), result.Path );
		} finally {
			Directory.Delete( root, recursive: true );
		}
	}

	/// <summary>Verifies host symbolic-link resolution when link creation is available.</summary>
	[Fact]
	public async Task ResolvesHostSymbolicLinkWhenSupported() {
		var root = CreateTemporaryDirectory();
		var link = SystemPath.Combine( root, "link" );
		try {
			var target = Directory.CreateDirectory( SystemPath.Combine( root, "target" ) ).FullName;
			var file = SystemPath.Combine( target, "file.txt" );
			await File.WriteAllTextAsync( file, "content" );
			if ( !TryCreateDirectoryLink( link, "target" ) ) {
				return;
			}
			var resolver = new CanonicalPathResolver();

			var inspection = await resolver.InspectLinkAsync( link );
			var resolved = await resolver.ResolvePhysicalAsync(
				SystemPath.Combine( link, "file.txt" )
			);

			Assert.True( inspection.IsSymbolicLink );
			Assert.Equal( "target", inspection.Target );
			Assert.Equal( SystemPath.GetFullPath( file ), resolved.Path );
		} finally {
			if ( Directory.Exists( link ) ) {
				Directory.Delete( link, recursive: false );
			}
			Directory.Delete( root, recursive: true );
		}
	}

	/// <summary>Verifies dangling-link inspection and controlled physical failure when supported.</summary>
	[Fact]
	public async Task InspectsDanglingHostLinkWhenSupported() {
		var root = CreateTemporaryDirectory();
		try {
			var link = SystemPath.Combine( root, "dangling" );
			if ( !TryCreateFileLink( link, "missing" ) ) {
				return;
			}
			var resolver = new CanonicalPathResolver();

			var inspection = await resolver.InspectLinkAsync( link );
			var resolved = await resolver.ResolvePhysicalAsync( link );

			Assert.True( inspection.IsSymbolicLink );
			Assert.Equal( "missing", inspection.Target );
			Assert.Equal( CanonicalPathFailureCode.NotFound, resolved.Failure!.Code );
			Assert.Null( resolved.Path );
		} finally {
			Directory.Delete( root, recursive: true );
		}
	}

	/// <summary>Verifies host symbolic-link loop detection when link creation is available.</summary>
	[Fact]
	public async Task DetectsHostLinkLoopWhenSupported() {
		var root = CreateTemporaryDirectory();
		var first = SystemPath.Combine( root, "first" );
		var second = SystemPath.Combine( root, "second" );
		try {
			if (
				!TryCreateFileLink( first, "second" )
				|| !TryCreateFileLink( second, "first" )
			) {
				return;
			}
			var resolver = new CanonicalPathResolver();

			var result = await resolver.ResolvePhysicalAsync( first );

			Assert.Equal( CanonicalPathFailureCode.SymbolicLinkLoop, result.Failure!.Code );
		} finally {
			File.Delete( first );
			File.Delete( second );
			Directory.Delete( root, recursive: false );
		}
	}

	private static string CreateTemporaryDirectory() {
		var path = SystemPath.Combine(
			Directory.GetCurrentDirectory(),
			string.Concat( ".icod-canonical-path-", Guid.NewGuid().ToString( "N" ) )
		);
		Directory.CreateDirectory( path );
		return path;
	}

	private static bool TryCreateDirectoryLink( string path, string target ) {
		try {
			Directory.CreateSymbolicLink( path, target );
			return true;
		} catch ( Exception exception ) when (
			exception is IOException
			or UnauthorizedAccessException
			or PlatformNotSupportedException
		) {
			return false;
		}
	}

	private static bool TryCreateFileLink( string path, string target ) {
		try {
			File.CreateSymbolicLink( path, target );
			return true;
		} catch ( Exception exception ) when (
			exception is IOException
			or UnauthorizedAccessException
			or PlatformNotSupportedException
		) {
			return false;
		}
	}
}
