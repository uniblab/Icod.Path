using System.Diagnostics;
using Xunit;
using SystemPath = System.IO.Path;

namespace Icod.Path.Tests.Platform.Ubuntu;

/// <summary>Exercises native Ubuntu symbolic-link and hard-link behavior.</summary>
public sealed class UbuntuPathLinkIntegrationTests {
	/// <summary>Verifies relative file and directory symbolic links are reported as POSIX links and physically resolved.</summary>
	[Fact]
	public async Task ResolvesRelativeSymbolicLinksOnUbuntuWhenSupported() {
		if ( !IsUbuntuHost() ) {
			return;
		}

		var root = CreateTemporaryDirectory();
		var links = Directory.CreateDirectory( SystemPath.Combine( root, "links" ) ).FullName;
		var fileLink = SystemPath.Combine( links, "file-link" );
		var directoryLink = SystemPath.Combine( links, "directory-link" );
		try {
			var targets = Directory.CreateDirectory( SystemPath.Combine( root, "targets" ) ).FullName;
			var targetFile = SystemPath.Combine( targets, "target.txt" );
			var nestedFile = SystemPath.Combine( targets, "nested.txt" );
			await File.WriteAllTextAsync( targetFile, "file" );
			await File.WriteAllTextAsync( nestedFile, "directory" );
			try {
				_ = File.CreateSymbolicLink(
					fileLink,
					SystemPath.Combine( "..", "targets", "target.txt" )
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

			Assert.True( fileInspection.Succeeded );
			Assert.True( fileInspection.IsSymbolicLink );
			Assert.False( fileInspection.IsReparsePoint );
			Assert.Equal( PathIndirectionKind.PosixSymbolicLink, fileInspection.Indirection.Kind );
			Assert.True( fileInspection.Indirection.IsRelativeTarget );
			Assert.Equal(
				SystemPath.Combine( "..", "targets", "target.txt" ),
				fileInspection.Target
			);

			Assert.True( directoryInspection.Succeeded );
			Assert.True( directoryInspection.IsSymbolicLink );
			Assert.Equal(
				PathIndirectionKind.PosixSymbolicLink,
				directoryInspection.Indirection.Kind
			);
			Assert.True( resolvedFile.Succeeded );
			Assert.Equal( SystemPath.GetFullPath( targetFile ), resolvedFile.Path );
			Assert.True( resolvedNested.Succeeded );
			Assert.Equal( SystemPath.GetFullPath( nestedFile ), resolvedNested.Path );
		} finally {
			Directory.Delete( root, true );
		}
	}

	/// <summary>Verifies a dangling Ubuntu symbolic link remains inspectable while physical resolution reports a missing target.</summary>
	[Fact]
	public async Task InspectsDanglingSymbolicLinkOnUbuntuWhenSupported() {
		if ( !IsUbuntuHost() ) {
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

	/// <summary>Verifies an Ubuntu hard link is an ordinary file pathname and contributes no resolver link hop.</summary>
	[Fact]
	public async Task DoesNotTreatUbuntuHardLinkAsPathIndirectionWhenSupported() {
		if ( !IsUbuntuHost() ) {
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
			var resolved = await resolver.ResolvePhysicalAsync( hardLink );

			Assert.True( inspection.Succeeded );
			Assert.Equal( CanonicalPathEntryKind.File, inspection.Kind );
			Assert.False( inspection.IsSymbolicLink );
			Assert.False( inspection.IsPathIndirection );
			Assert.False( inspection.IsReparsePoint );
			Assert.Equal( PathIndirectionKind.None, inspection.Indirection.Kind );
			Assert.True( resolved.Succeeded );
			Assert.Equal( SystemPath.GetFullPath( hardLink ), resolved.Path );
			Assert.Empty( resolved.ResolvedLinks );
		} finally {
			Directory.Delete( root, true );
		}
	}

	private static bool IsUbuntuHost() {
		if ( !OperatingSystem.IsLinux() ) {
			return false;
		}
		try {
			foreach ( var line in File.ReadLines( "/etc/os-release" ) ) {
				if ( !line.StartsWith( "ID=", StringComparison.Ordinal ) ) {
					continue;
				}
				return string.Equals(
					line[3..].Trim().Trim( '"' ),
					"ubuntu",
					StringComparison.OrdinalIgnoreCase
				);
			}
		} catch ( IOException ) {
			return false;
		} catch ( UnauthorizedAccessException ) {
			return false;
		}
		return false;
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
			string.Concat( "Icod.Path.Ubuntu.Links.", Guid.NewGuid().ToString( "N" ) )
		);
		Directory.CreateDirectory( path );
		return path;
	}
}
