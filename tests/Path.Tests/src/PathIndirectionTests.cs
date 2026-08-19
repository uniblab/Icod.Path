using Xunit;

namespace Icod.Path.Tests;

/// <summary>Exercises platform-neutral pathname-indirection classification.</summary>
public sealed class PathIndirectionTests {
	/// <summary>Verifies that Windows reparse points are not all classified as symbolic links.</summary>
	[Fact]
	public void DistinguishesWindowsReparsePointKinds() {
		var symbolicLink = PathIndirectionInfo.WindowsReparsePoint(
			WindowsReparseTags.SymbolicLink,
			false,
			displayTarget: "target.txt"
		);
		var junction = PathIndirectionInfo.WindowsReparsePoint(
			WindowsReparseTags.MountPoint,
			true,
			displayTarget: @"C:\target"
		);
		var mountedVolume = PathIndirectionInfo.WindowsReparsePoint(
			WindowsReparseTags.MountPoint,
			true,
			displayTarget: @"\\?\Volume{00000000-0000-0000-0000-000000000000}\",
			volumeGuidPath: @"\\?\Volume{00000000-0000-0000-0000-000000000000}\"
		);
		var cloudPlaceholder = PathIndirectionInfo.WindowsReparsePoint(
			WindowsReparseTags.Cloud,
			false
		);
		var opaque = PathIndirectionInfo.WindowsReparsePoint( 0x80000020, false );

		Assert.Equal( PathIndirectionKind.WindowsSymbolicLink, symbolicLink.Kind );
		Assert.True( symbolicLink.IsSymbolicLink );
		Assert.True( symbolicLink.CanResolveAsPath );

		Assert.Equal( PathIndirectionKind.WindowsJunction, junction.Kind );
		Assert.True( junction.IsJunction );
		Assert.True( junction.IsNameSurrogate );
		Assert.False( junction.IsSymbolicLink );
		Assert.True( junction.CanResolveAsPath );

		Assert.Equal( PathIndirectionKind.WindowsVolumeMountPoint, mountedVolume.Kind );
		Assert.True( mountedVolume.IsVolumeMountPoint );
		Assert.True( mountedVolume.IsNameSurrogate );
		Assert.Equal( mountedVolume.Target, mountedVolume.VolumeGuidPath );

		Assert.Equal( PathIndirectionKind.WindowsCloudPlaceholder, cloudPlaceholder.Kind );
		Assert.True( cloudPlaceholder.IsCloudPlaceholder );
		Assert.True( cloudPlaceholder.IsOpaqueReparsePoint );
		Assert.False( cloudPlaceholder.IsPathIndirection );

		Assert.Equal( PathIndirectionKind.WindowsOpaqueReparsePoint, opaque.Kind );
		Assert.True( opaque.IsOpaqueReparsePoint );
		Assert.False( opaque.IsSymbolicLink );
	}

	/// <summary>Verifies all Cloud Files tag variants are recognized without decoding provider data.</summary>
	[Fact]
	public void RecognizesCloudTagFamily() {
		for ( uint variant = 0; variant <= 0xf; variant++ ) {
			var tag = WindowsReparseTags.Cloud | (variant << 12);
			Assert.True( WindowsReparseTags.IsCloudTag( tag ) );
			Assert.Equal(
				PathIndirectionKind.WindowsCloudPlaceholder,
				PathIndirectionInfo.WindowsReparsePoint( tag, false ).Kind
			);
		}
		Assert.False( WindowsReparseTags.IsCloudTag( WindowsReparseTags.SymbolicLink ) );
	}

	/// <summary>Confirms that an unavailable Windows tag remains uncharacterized rather than becoming an ordinary opaque provider point.</summary>
	[Fact]
	public void MissingWindowsTagRemainsUnknown() {
		var information = PathIndirectionInfo.WindowsReparsePoint(
			0,
			true,
			attributes: FileAttributes.Directory | FileAttributes.ReparsePoint
		);

		Assert.Equal( PathIndirectionKind.Unknown, information.Kind );
		Assert.True( information.IsReparsePoint );
		Assert.False( information.IsOpaqueReparsePoint );
		Assert.Null( information.ReparseTag );
		Assert.False( information.CanResolveAsPath );
	}

	/// <summary>Verifies unknown name-surrogate tags remain distinct and are not followed without a decoder.</summary>
	[Fact]
	public void PreservesUnknownNameSurrogateTag() {
		const uint customNameSurrogate = WindowsReparseTags.NameSurrogate | 0x00000042;
		var information = PathIndirectionInfo.WindowsReparsePoint(
			customNameSurrogate,
			true,
			rawTarget: "provider-defined"
		);

		Assert.Equal( PathIndirectionKind.WindowsOtherNameSurrogate, information.Kind );
		Assert.True( information.IsNameSurrogate );
		Assert.True( information.IsPathIndirection );
		Assert.False( information.IsSymbolicLink );
		Assert.False( information.CanResolveAsPath );
		Assert.Equal( customNameSurrogate, information.ReparseTag );
	}
}
