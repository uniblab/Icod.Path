using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Icod.Path;

/// <summary>
/// Characterizes POSIX symbolic links and Windows reparse points without dereferencing the
/// terminal pathname object or opening file content.
/// </summary>
public sealed class SystemPathIndirectionInspector : IPathIndirectionInspector {
	private const uint FileFlagBackupSemantics = 0x02000000;
	private const uint FileFlagOpenReparsePoint = 0x00200000;
	private const uint OpenExisting = 3;
	private const int FileAttributeTagInformationClass = 9;
	private const uint FsctlGetReparsePoint = 0x000900a8;
	private const int MaximumReparseDataBufferSize = 16 * 1024;
	private const uint SymbolicLinkFlagRelative = 0x00000001;

	private SystemPathIndirectionInspector() {
	}

	/// <summary>Gets the shared host inspector.</summary>
	public static SystemPathIndirectionInspector Instance { get; } = new();

	/// <inheritdoc/>
	public ValueTask<PathIndirectionInfo> InspectAsync(
		string path,
		CancellationToken cancellationToken = default
	) {
		ArgumentException.ThrowIfNullOrEmpty( path );
		cancellationToken.ThrowIfCancellationRequested();
		var attributes = File.GetAttributes( path );
		string? managedTarget;
		if ( !OperatingSystem.IsWindows() ) {
			managedTarget = TryGetManagedLinkTarget( path, attributes );
			if ( managedTarget is not null ) {
				return ValueTask.FromResult(
					PathIndirectionInfo.PosixSymbolicLink(
						managedTarget,
						(attributes & FileAttributes.Directory) != 0,
						attributes
					)
				);
			}
			return ValueTask.FromResult(
				(attributes & FileAttributes.ReparsePoint) != 0
					? PathIndirectionInfo.Unknown( null, attributes )
					: PathIndirectionInfo.None
			);
		}
		if ( (attributes & FileAttributes.ReparsePoint) == 0 ) {
			return ValueTask.FromResult( PathIndirectionInfo.None );
		}

		using var handle = CreateFileW(
			path,
			0,
			FileShare.Read | FileShare.Write | FileShare.Delete,
			IntPtr.Zero,
			OpenExisting,
			FileFlagBackupSemantics | FileFlagOpenReparsePoint,
			IntPtr.Zero
		);
		if ( handle.IsInvalid ) {
			throw new IOException(
				"The Windows reparse point could not be opened without dereferencing it.",
				new Win32Exception( Marshal.GetLastPInvokeError() )
			);
		}
		if ( !GetFileInformationByHandleEx(
			handle,
			FileAttributeTagInformationClass,
			out var attributeTag,
			(uint)Marshal.SizeOf<FileAttributeTagInfo>()
		) ) {
			return ValueTask.FromResult( PathIndirectionInfo.Unknown( null, attributes ) );
		}

		var tag = attributeTag.ReparseTag;
		managedTarget = tag is WindowsReparseTags.SymbolicLink or WindowsReparseTags.MountPoint
			? TryGetManagedLinkTarget( path, attributes )
			: null;
		string? rawTarget = null;
		string? nativeDisplayTarget = null;
		var isRelative = false;
		if ( tag is WindowsReparseTags.SymbolicLink or WindowsReparseTags.MountPoint ) {
			TryReadMicrosoftTarget(
				handle,
				tag,
				out rawTarget,
				out nativeDisplayTarget,
				out isRelative
			);
		}
		var displayTarget = managedTarget
			?? nativeDisplayTarget
			?? NormalizeSubstituteName( rawTarget );
		var volumeGuidPath = tag == WindowsReparseTags.MountPoint
			&& IsVolumeGuidTarget( rawTarget, displayTarget )
			? TryGetMountedVolumeGuidPath( path ) ?? NormalizeSubstituteName( rawTarget )
			: null;
		return ValueTask.FromResult(
			PathIndirectionInfo.WindowsReparsePoint(
				tag,
				(attributes & FileAttributes.Directory) != 0,
				rawTarget,
				displayTarget,
				isRelative,
				attributes,
				volumeGuidPath
			)
		);
	}

	private static string? TryGetManagedLinkTarget( string path, FileAttributes attributes ) {
		try {
			if ( (attributes & FileAttributes.Directory) != 0 ) {
				return new DirectoryInfo( path ).LinkTarget;
			}
			return new FileInfo( path ).LinkTarget ?? new DirectoryInfo( path ).LinkTarget;
		} catch ( Exception exception ) when (
			exception is IOException
				or UnauthorizedAccessException
				or System.Security.SecurityException
				or NotSupportedException
		) {
			return null;
		}
	}

	private static void TryReadMicrosoftTarget(
		SafeFileHandle handle,
		uint tag,
		out string? rawTarget,
		out string? displayTarget,
		out bool isRelative
	) {
		rawTarget = null;
		displayTarget = null;
		isRelative = false;
		var buffer = new byte[MaximumReparseDataBufferSize];
		if (
			!DeviceIoControl(
				handle,
				FsctlGetReparsePoint,
				IntPtr.Zero,
				0,
				buffer,
				(uint)buffer.Length,
				out var bytesReturned,
				IntPtr.Zero
			)
			|| bytesReturned < 16
		) {
			return;
		}
		var returnedTag = BitConverter.ToUInt32( buffer, 0 );
		if ( returnedTag != tag ) {
			return;
		}
		var substituteOffset = BitConverter.ToUInt16( buffer, 8 );
		var substituteLength = BitConverter.ToUInt16( buffer, 10 );
		var printOffset = BitConverter.ToUInt16( buffer, 12 );
		var printLength = BitConverter.ToUInt16( buffer, 14 );
		var pathBufferOffset = tag == WindowsReparseTags.SymbolicLink ? 20 : 16;
		if ( tag == WindowsReparseTags.SymbolicLink ) {
			if ( bytesReturned < 20 ) {
				return;
			}
			isRelative = (BitConverter.ToUInt32( buffer, 16 ) & SymbolicLinkFlagRelative) != 0;
		}
		rawTarget = ReadUnicodeSlice(
			buffer,
			pathBufferOffset + substituteOffset,
			substituteLength,
			bytesReturned
		);
		displayTarget = ReadUnicodeSlice(
			buffer,
			pathBufferOffset + printOffset,
			printLength,
			bytesReturned
		);
		if ( string.IsNullOrEmpty( displayTarget ) ) {
			displayTarget = NormalizeSubstituteName( rawTarget );
		}
	}

	private static string? ReadUnicodeSlice(
		byte[] buffer,
		int offset,
		int length,
		uint bytesReturned
	) {
		var returnedLength = checked( (int)bytesReturned );
		if (
			length == 0
			|| offset < 0
			|| length < 0
			|| offset > returnedLength
			|| length > returnedLength - offset
		) {
			return null;
		}
		return Encoding.Unicode.GetString( buffer, offset, length );
	}

	private static string? NormalizeSubstituteName( string? target ) {
		if ( string.IsNullOrEmpty( target ) ) {
			return target;
		}
		const string ntUncPrefix = @"\??\UNC\";
		if ( target.StartsWith( ntUncPrefix, StringComparison.OrdinalIgnoreCase ) ) {
			return string.Concat( @"\\", target[ntUncPrefix.Length..] );
		}
		const string ntPrefix = @"\??\";
		if ( target.StartsWith( ntPrefix, StringComparison.Ordinal ) ) {
			var suffix = target[ntPrefix.Length..];
			if (
				suffix.StartsWith( "Volume{", StringComparison.OrdinalIgnoreCase )
				|| suffix.StartsWith( @"GLOBALROOT\", StringComparison.OrdinalIgnoreCase )
			) {
				return string.Concat( @"\\?\", suffix );
			}
			return suffix;
		}
		return target;
	}

	private static bool IsVolumeGuidTarget( string? rawTarget, string? displayTarget ) =>
		IsExactVolumeGuidPath( NormalizeSubstituteName( rawTarget ) )
		|| IsExactVolumeGuidPath( displayTarget )
	;

	private static bool IsExactVolumeGuidPath( string? path ) {
		const string prefix = @"\\?\Volume{";
		if ( string.IsNullOrEmpty( path ) || !path.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) ) {
			return false;
		}
		var closingBrace = path.IndexOf( '}', prefix.Length );
		return closingBrace >= prefix.Length
			&& (
				closingBrace == path.Length - 1
				|| (
					closingBrace == path.Length - 2
					&& System.IO.Path.DirectorySeparatorChar == path[^1]
				)
			);
	}

	private static string? TryGetMountedVolumeGuidPath( string path ) {
		var mountPath = System.IO.Path.EndsInDirectorySeparator( path )
			? path
			: string.Concat( path, System.IO.Path.DirectorySeparatorChar );
		var buffer = new StringBuilder( 64 );
		return GetVolumeNameForVolumeMountPointW( mountPath, buffer, (uint)buffer.Capacity )
			? buffer.ToString()
			: null;
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct FileAttributeTagInfo {
		/// <summary>Retains the native file-attribute field.</summary>
		internal FileAttributes FileAttributes;
		/// <summary>Retains the native reparse-tag field.</summary>
		internal uint ReparseTag;
	}

	[DllImport( "kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true )]
	private static extern SafeFileHandle CreateFileW(
		string fileName,
		uint desiredAccess,
		FileShare shareMode,
		IntPtr securityAttributes,
		uint creationDisposition,
		uint flagsAndAttributes,
		IntPtr templateFile
	);

	[DllImport( "kernel32.dll", SetLastError = true )]
	[return: MarshalAs( UnmanagedType.Bool )]
	private static extern bool GetFileInformationByHandleEx(
		SafeFileHandle file,
		int fileInformationClass,
		out FileAttributeTagInfo fileInformation,
		uint bufferSize
	);

	[DllImport( "kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true )]
	[return: MarshalAs( UnmanagedType.Bool )]
	private static extern bool GetVolumeNameForVolumeMountPointW(
		string volumeMountPoint,
		StringBuilder volumeName,
		uint bufferLength
	);

	[DllImport( "kernel32.dll", SetLastError = true )]
	[return: MarshalAs( UnmanagedType.Bool )]
	private static extern bool DeviceIoControl(
		SafeFileHandle device,
		uint controlCode,
		IntPtr inputBuffer,
		uint inputBufferSize,
		[Out] byte[] outputBuffer,
		uint outputBufferSize,
		out uint bytesReturned,
		IntPtr overlapped
	);
}
