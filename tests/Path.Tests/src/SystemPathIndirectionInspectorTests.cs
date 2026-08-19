using System.Diagnostics;
using Xunit;
using SystemPath = System.IO.Path;

namespace Icod.Path.Tests;

/// <summary>Exercises host reparse-point characterization where Windows capabilities permit.</summary>
public sealed class SystemPathIndirectionInspectorTests {
	/// <summary>Verifies a Windows junction remains distinct from a symbolic link and is physically resolvable.</summary>
	[Fact]
	public async Task CharacterizesAndResolvesWindowsJunctionWhenSupported() {
		if ( !OperatingSystem.IsWindows() ) {
			return;
		}
		var root = CreateTemporaryDirectory();
		var junction = SystemPath.Combine( root, "junction" );
		try {
			var target = Directory.CreateDirectory( SystemPath.Combine( root, "target" ) ).FullName;
			var file = SystemPath.Combine( target, "file.txt" );
			await File.WriteAllTextAsync( file, "content" );
			if ( !TryCreateJunction( junction, target ) ) {
				return;
			}

			var resolver = new CanonicalPathResolver();
			var inspection = await resolver.InspectLinkAsync( junction );
			var resolved = await resolver.ResolvePhysicalAsync( SystemPath.Combine( junction, "file.txt" ) );

			Assert.True( inspection.Succeeded );
			Assert.False( inspection.IsSymbolicLink );
			Assert.True( inspection.IsPathIndirection );
			Assert.True( inspection.IsReparsePoint );
			Assert.Equal( PathIndirectionKind.WindowsJunction, inspection.Indirection.Kind );
			Assert.Equal( WindowsReparseTags.MountPoint, inspection.Indirection.ReparseTag );
			Assert.NotNull( inspection.Target );
			Assert.Equal( SystemPath.GetFullPath( file ), resolved.Path );
		} finally {
			if ( Directory.Exists( junction ) ) {
				Directory.Delete( junction );
			}
			Directory.Delete( root, true );
		}
	}

	private static bool TryCreateJunction( string junctionPath, string targetPath ) {
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
			startInfo.ArgumentList.Add( "/J" );
			startInfo.ArgumentList.Add( junctionPath );
			startInfo.ArgumentList.Add( targetPath );
			using var process = Process.Start( startInfo );
			if ( process is null ) {
				return false;
			}
			process.WaitForExit();
			return process.ExitCode == 0 && Directory.Exists( junctionPath );
		} catch ( Exception exception ) when (
			exception is InvalidOperationException
				or System.ComponentModel.Win32Exception
				or PlatformNotSupportedException
				or NotSupportedException
				or IOException
		) {
			return false;
		}
	}

	private static string CreateTemporaryDirectory() {
		var path = SystemPath.Combine(
			SystemPath.GetTempPath(),
			string.Concat( "Icod.Path.Reparse.Tests.", Guid.NewGuid().ToString( "N" ) )
		);
		Directory.CreateDirectory( path );
		return path;
	}
}
