using System.Diagnostics;
using Xunit;
using SystemPath = System.IO.Path;

namespace Icod.Path.Tests.Platform.Windows;

/// <summary>Exercises native Windows symbolic-link, hard-link, and junction behavior.</summary>
public sealed class WindowsPathLinkIntegrationTests {
	/// <summary>Verifies Windows file and directory symbolic links retain their reparse identity and resolve relative targets.</summary>
	[Fact]
	public async Task RecognizesWindowsSymbolicLinksWhenSupported() {
		if ( !OperatingSystem.IsWindows() ) {
			return;
		}

		var root = CreateTemporaryDirectory();
		var fileLink = SystemPath.Combine( root, "file-link.txt" );
		var directoryLink = SystemPath.Combine( root, "directory-link" );
		try {
			var targetFile = SystemPath.Combine( root, "target.txt" );
			var targetDirectory = Directory.CreateDirectory(
				SystemPath.Combine( root, "target-directory" )
			).FullName;
			var nestedFile = SystemPath.Combine( targetDirectory, "nested.txt" );
			await File.WriteAllTextAsync( targetFile, "file" );
			await File.WriteAllTextAsync( nestedFile, "directory" );

			try {
				_ = File.CreateSymbolicLink( fileLink, "target.txt" );
				_ = Directory.CreateSymbolicLink(
					directoryLink,
					"target-directory"
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
			Assert.True( fileInspection.IsPathIndirection );
			Assert.True( fileInspection.IsReparsePoint );
			Assert.Equal( PathIndirectionKind.WindowsSymbolicLink, fileInspection.Indirection.Kind );
			Assert.Equal( WindowsReparseTags.SymbolicLink, fileInspection.Indirection.ReparseTag );
			Assert.True( fileInspection.Indirection.IsRelativeTarget );

			Assert.True( directoryInspection.Succeeded );
			Assert.True( directoryInspection.IsSymbolicLink );
			Assert.Equal(
				PathIndirectionKind.WindowsSymbolicLink,
				directoryInspection.Indirection.Kind
			);
			Assert.Equal( WindowsReparseTags.SymbolicLink, directoryInspection.Indirection.ReparseTag );

			Assert.True( resolvedFile.Succeeded );
			Assert.Equal( SystemPath.GetFullPath( targetFile ), resolvedFile.Path );
			Assert.True( resolvedNested.Succeeded );
			Assert.Equal( SystemPath.GetFullPath( nestedFile ), resolvedNested.Path );
		} finally {
			TryDeleteFile( fileLink );
			TryDeleteDirectoryLink( directoryLink );
			Directory.Delete( root, true );
		}
	}

	/// <summary>Verifies a Windows junction is distinct from a symbolic link and deleting it preserves the target directory.</summary>
	[Fact]
	public async Task RecognizesWindowsJunctionAndPreservesTargetWhenRemoved() {
		if ( !OperatingSystem.IsWindows() ) {
			return;
		}

		var root = CreateTemporaryDirectory();
		var junction = SystemPath.Combine( root, "junction" );
		try {
			var target = Directory.CreateDirectory( SystemPath.Combine( root, "target" ) ).FullName;
			var targetFile = SystemPath.Combine( target, "inside.txt" );
			await File.WriteAllTextAsync( targetFile, "content" );
			if ( !TryCreateJunction( junction, target ) ) {
				return;
			}

			var resolver = new CanonicalPathResolver();
			var inspection = await resolver.InspectLinkAsync( junction );
			var resolved = await resolver.ResolvePhysicalAsync(
				SystemPath.Combine( junction, "inside.txt" )
			);

			Assert.True( inspection.Succeeded );
			Assert.False( inspection.IsSymbolicLink );
			Assert.True( inspection.IsPathIndirection );
			Assert.True( inspection.IsReparsePoint );
			Assert.Equal( PathIndirectionKind.WindowsJunction, inspection.Indirection.Kind );
			Assert.Equal( WindowsReparseTags.MountPoint, inspection.Indirection.ReparseTag );
			Assert.True( inspection.Indirection.CanResolveAsPath );
			Assert.True( resolved.Succeeded );
			Assert.Equal( SystemPath.GetFullPath( targetFile ), resolved.Path );

			Directory.Delete( junction );
			Assert.False( Directory.Exists( junction ) );
			Assert.True( Directory.Exists( target ) );
			Assert.True( File.Exists( targetFile ) );
		} finally {
			TryDeleteDirectoryLink( junction );
			Directory.Delete( root, true );
		}
	}

	/// <summary>Verifies modeled Windows mounted-volume, Cloud, opaque, and unknown name-surrogate tags remain distinct.</summary>
	[Fact]
	public void ClassifiesAdditionalWindowsReparsePointKinds() {
		if ( !OperatingSystem.IsWindows() ) {
			return;
		}

		var volumeGuid = "\\\\?\\Volume{00000000-0000-0000-0000-000000000000}\\";
		var mountedVolume = PathIndirectionInfo.WindowsReparsePoint(
			WindowsReparseTags.MountPoint,
			true,
			"\\??\\Volume{00000000-0000-0000-0000-000000000000}\\",
			"C:\\",
			false,
			FileAttributes.Directory | FileAttributes.ReparsePoint,
			volumeGuid
		);
		var cloud = PathIndirectionInfo.WindowsReparsePoint(
			WindowsReparseTags.Cloud,
			false,
			attributes: FileAttributes.ReparsePoint | (FileAttributes)0x00400000
		);
		var opaque = PathIndirectionInfo.WindowsReparsePoint(
			0x80000099,
			false,
			attributes: FileAttributes.ReparsePoint
		);
		var unknownNameSurrogate = PathIndirectionInfo.WindowsReparsePoint(
			0xa0001234,
			true,
			attributes: FileAttributes.Directory | FileAttributes.ReparsePoint
		);

		Assert.Equal( PathIndirectionKind.WindowsVolumeMountPoint, mountedVolume.Kind );
		Assert.True( mountedVolume.IsVolumeMountPoint );
		Assert.True( mountedVolume.IsPathIndirection );
		Assert.True( mountedVolume.CanResolveAsPath );
		Assert.Equal( volumeGuid, mountedVolume.VolumeGuidPath );

		Assert.Equal( PathIndirectionKind.WindowsCloudPlaceholder, cloud.Kind );
		Assert.True( cloud.IsCloudPlaceholder );
		Assert.True( cloud.IsOpaqueReparsePoint );
		Assert.False( cloud.IsPathIndirection );
		Assert.True( cloud.RecallOnDataAccess );

		Assert.Equal( PathIndirectionKind.WindowsOpaqueReparsePoint, opaque.Kind );
		Assert.True( opaque.IsOpaqueReparsePoint );
		Assert.False( opaque.IsNameSurrogate );
		Assert.False( opaque.CanResolveAsPath );

		Assert.Equal(
			PathIndirectionKind.WindowsOtherNameSurrogate,
			unknownNameSurrogate.Kind
		);
		Assert.True( unknownNameSurrogate.IsNameSurrogate );
		Assert.True( unknownNameSurrogate.IsPathIndirection );
		Assert.False( unknownNameSurrogate.CanResolveAsPath );
	}

	/// <summary>Verifies a Windows hard link remains an ordinary file pathname rather than pathname indirection.</summary>
	[Fact]
	public async Task DoesNotTreatWindowsHardLinkAsPathIndirectionWhenSupported() {
		if ( !OperatingSystem.IsWindows() ) {
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
			Assert.Equal( await File.ReadAllTextAsync( target ), await File.ReadAllTextAsync( hardLink ) );
		} finally {
			Directory.Delete( root, true );
		}
	}

	private static bool TryCreateJunction( string junctionPath, string targetPath ) => RunMklink(
		"/J",
		junctionPath,
		targetPath,
		() => Directory.Exists( junctionPath )
	);

	private static bool TryCreateHardLink( string linkPath, string targetPath ) => RunMklink(
		"/H",
		linkPath,
		targetPath,
		() => File.Exists( linkPath )
	);

	private static bool RunMklink(
		string option,
		string linkPath,
		string targetPath,
		Func<bool> created
	) {
		try {
			var startInfo = new ProcessStartInfo( "cmd.exe" ) {
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			startInfo.ArgumentList.Add( "/d" );
			startInfo.ArgumentList.Add( "/c" );
			startInfo.ArgumentList.Add( "mklink" );
			startInfo.ArgumentList.Add( option );
			startInfo.ArgumentList.Add( linkPath );
			startInfo.ArgumentList.Add( targetPath );
			using var process = Process.Start( startInfo );
			if ( process is null ) {
				return false;
			}
			process.WaitForExit();
			return 0 == process.ExitCode && created();
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

	private static void TryDeleteFile( string path ) {
		try {
			File.Delete( path );
		} catch ( IOException ) {
			// Best-effort cleanup after a capability-gated test.
		} catch ( UnauthorizedAccessException ) {
			// Best-effort cleanup after a capability-gated test.
		}
	}

	private static void TryDeleteDirectoryLink( string path ) {
		try {
			if ( Directory.Exists( path ) ) {
				Directory.Delete( path );
			}
		} catch ( IOException ) {
			// Best-effort cleanup after a capability-gated test.
		} catch ( UnauthorizedAccessException ) {
			// Best-effort cleanup after a capability-gated test.
		}
	}

	private static string CreateTemporaryDirectory() {
		var path = SystemPath.Combine(
			SystemPath.GetTempPath(),
			string.Concat( "Icod.Path.Windows.Links.", Guid.NewGuid().ToString( "N" ) )
		);
		Directory.CreateDirectory( path );
		return path;
	}
}
