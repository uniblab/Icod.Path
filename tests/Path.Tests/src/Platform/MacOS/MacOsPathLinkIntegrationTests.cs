using System.Diagnostics;
using Xunit;
using SystemPath = System.IO.Path;

namespace Icod.Path.Tests.Platform.MacOS;

/// <summary>Exercises native macOS symbolic-link and hard-link behavior.</summary>
public sealed class MacOsPathLinkIntegrationTests {
	/// <summary>Verifies relative file and directory symbolic links are reported as POSIX links and physically resolved.</summary>
	[Fact]
	public async Task ResolvesRelativeSymbolicLinksOnMacOsWhenSupported() {
		if ( !OperatingSystem.IsMacOS() ) {
			return;
		}

		var root = CreateTemporaryDirectory();
		var links = Directory.CreateDirectory( SystemPath.Combine( root, "links" ) ).FullName;
		try {
			var targets = Directory.CreateDirectory( SystemPath.Combine( root, "targets" ) ).FullName;
			var targetFile = SystemPath.Combine( targets, "café.txt" );
			var nestedFile = SystemPath.Combine( targets, "nested.txt" );
			var fileLink = SystemPath.Combine( links, "file-link" );
			var directoryLink = SystemPath.Combine( links, "directory-link" );
			await File.WriteAllTextAsync( targetFile, "file" );
			await File.WriteAllTextAsync( nestedFile, "directory" );
			try {
				_ = File.CreateSymbolicLink(
					fileLink,
					SystemPath.Combine( "..", "targets", "café.txt" )
				);
				_ = Directory.CreateSymbolicLink(
					directoryLink,
					SystemPath.Combine( "..", "targets" )
				);
			} catch ( Exception exception ) when ( IsUnsupportedLinkCreation( exception ) ) {
				return;
			}

			var resolver = new CanonicalPathResolver();
			var fileInspection = await resolver.InspectLinkAsync( fileLink );
			var directoryInspection = await resolver.InspectLinkAsync( directoryLink );
			var resolvedFile = await resolver.ResolvePhysicalAsync( fileLink );
			var resolvedNested = await resolver.ResolvePhysicalAsync(
				SystemPath.Combine( directoryLink, "nested.txt" )
			);
			var expectedFile = await resolver.ResolvePhysicalAsync( targetFile );
			var expectedNested = await resolver.ResolvePhysicalAsync( nestedFile );

			Assert.True( fileInspection.Succeeded );
			Assert.True( fileInspection.IsSymbolicLink );
			Assert.False( fileInspection.IsReparsePoint );
			Assert.Equal( PathIndirectionKind.PosixSymbolicLink, fileInspection.Indirection.Kind );
			Assert.True( fileInspection.Indirection.IsRelativeTarget );
			Assert.True( directoryInspection.Succeeded );
			Assert.True( directoryInspection.IsSymbolicLink );
			Assert.Equal(
				PathIndirectionKind.PosixSymbolicLink,
				directoryInspection.Indirection.Kind
			);
			Assert.True( expectedFile.Succeeded );
			Assert.True( resolvedFile.Succeeded );
			Assert.Equal( expectedFile.Path, resolvedFile.Path );
			Assert.True( expectedNested.Succeeded );
			Assert.True( resolvedNested.Succeeded );
			Assert.Equal( expectedNested.Path, resolvedNested.Path );
		} finally {
			Directory.Delete( root, true );
		}
	}

	/// <summary>Verifies a dangling macOS symbolic link remains inspectable while physical resolution reports a missing target.</summary>
	[Fact]
	public async Task InspectsDanglingSymbolicLinkOnMacOsWhenSupported() {
		if ( !OperatingSystem.IsMacOS() ) {
			return;
		}

		var root = CreateTemporaryDirectory();
		try {
			var link = SystemPath.Combine( root, "dangling" );
			try {
				_ = File.CreateSymbolicLink( link, "missing-target" );
			} catch ( Exception exception ) when ( IsUnsupportedLinkCreation( exception ) ) {
				return;
			}

			var resolver = new CanonicalPathResolver();
			var inspection = await resolver.InspectLinkAsync( link );
			var resolved = await resolver.ResolvePhysicalAsync( link );

			Assert.True( inspection.Succeeded );
			Assert.True( inspection.IsSymbolicLink );
			Assert.Equal( PathIndirectionKind.PosixSymbolicLink, inspection.Indirection.Kind );
			Assert.Equal( "missing-target", inspection.Target );
			Assert.False( resolved.Succeeded );
			Assert.NotNull( resolved.Failure );
			Assert.Equal( CanonicalPathFailureCode.NotFound, resolved.Failure!.Code );
		} finally {
			Directory.Delete( root, true );
		}
	}

	/// <summary>Verifies a macOS hard link is an ordinary file pathname and contributes no resolver link hop.</summary>
	[Fact]
	public async Task DoesNotTreatMacOsHardLinkAsPathIndirectionWhenSupported() {
		if ( !OperatingSystem.IsMacOS() ) {
			return;
		}

		var root = CreateTemporaryDirectory();
		try {
			var target = SystemPath.Combine( root, "target.txt" );
			var hardLink = SystemPath.Combine( root, "hard-link.txt" );
			await File.WriteAllTextAsync( target, "content" );
			if ( !TryCreateHardLink( hardLink, target ) ) {
				return;
			}

			var resolver = new CanonicalPathResolver();
			var inspection = await resolver.InspectLinkAsync( hardLink );
			var resolvedRoot = await resolver.ResolvePhysicalAsync( root );
			var resolved = await resolver.ResolvePhysicalAsync( hardLink );

			Assert.True( inspection.Succeeded );
			Assert.Equal( CanonicalPathEntryKind.File, inspection.Kind );
			Assert.False( inspection.IsSymbolicLink );
			Assert.False( inspection.IsPathIndirection );
			Assert.False( inspection.IsReparsePoint );
			Assert.Equal( PathIndirectionKind.None, inspection.Indirection.Kind );
			Assert.True( resolvedRoot.Succeeded );
			Assert.True( resolved.Succeeded );
			Assert.Equal(
				SystemPath.Combine( resolvedRoot.Path!, SystemPath.GetFileName( hardLink ) ),
				resolved.Path
			);
			Assert.Equal( resolvedRoot.ResolvedLinks.Count, resolved.ResolvedLinks.Count );
		} finally {
			Directory.Delete( root, true );
		}
	}

	private static bool TryCreateHardLink( string linkPath, string targetPath ) {
		try {
			var startInfo = new ProcessStartInfo( "/bin/ln" ) {
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			startInfo.ArgumentList.Add( targetPath );
			startInfo.ArgumentList.Add( linkPath );
			using var process = Process.Start( startInfo );
			if ( process is null ) {
				return false;
			}
			process.WaitForExit();
			return 0 == process.ExitCode && File.Exists( linkPath );
		} catch ( Exception exception ) when ( IsUnsupportedLinkCreation( exception ) ) {
			return false;
		}
	}

	private static bool IsUnsupportedLinkCreation( Exception exception ) => exception is
		UnauthorizedAccessException
		or IOException
		or InvalidOperationException
		or System.ComponentModel.Win32Exception
		or PlatformNotSupportedException
		or NotSupportedException;

	private static string CreateTemporaryDirectory() {
		var path = SystemPath.Combine(
			SystemPath.GetTempPath(),
			string.Concat( "Icod.Path.MacOS.Links.", Guid.NewGuid().ToString( "N" ) )
		);
		Directory.CreateDirectory( path );
		return path;
	}
}
