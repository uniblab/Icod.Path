namespace Icod.Path;

/// <summary>Identifies the physical pathname-indirection or reparse-point mechanism observed without dereferencing it.</summary>
public enum PathIndirectionKind {
	/// <summary>The pathname object has no link or reparse-point character.</summary>
	None = 0,
	/// <summary>A POSIX symbolic link.</summary>
	PosixSymbolicLink = 1,
	/// <summary>A Windows symbolic link represented by <see cref="WindowsReparseTags.SymbolicLink"/>.</summary>
	WindowsSymbolicLink = 2,
	/// <summary>A Windows directory junction represented by <see cref="WindowsReparseTags.MountPoint"/>.</summary>
	WindowsJunction = 3,
	/// <summary>A Windows mounted volume represented by <see cref="WindowsReparseTags.MountPoint"/>.</summary>
	WindowsVolumeMountPoint = 4,
	/// <summary>A Windows name-surrogate reparse point whose target format is not decoded.</summary>
	WindowsOtherNameSurrogate = 5,
	/// <summary>A Windows Cloud Files placeholder reparse point.</summary>
	WindowsCloudPlaceholder = 6,
	/// <summary>A Windows reparse point that is not a name surrogate or recognized placeholder.</summary>
	WindowsOpaqueReparsePoint = 7,
	/// <summary>The host reports indirection but its mechanism cannot be characterized.</summary>
	Unknown = 8,
}

/// <summary>Contains Windows reparse-tag constants and tag-bit helpers.</summary>
public static class WindowsReparseTags {
	/// <summary>The Microsoft symbolic-link reparse tag.</summary>
	public const uint SymbolicLink = 0xa000000c;
	/// <summary>The Microsoft mount-point reparse tag used by junctions and mounted folders.</summary>
	public const uint MountPoint = 0xa0000003;
	/// <summary>The base Microsoft Cloud Files placeholder tag.</summary>
	public const uint Cloud = 0x9000001a;
	/// <summary>The bits varied by the Cloud Files placeholder tag family.</summary>
	public const uint CloudMask = 0x0000f000;
	/// <summary>The high-order bit identifying Microsoft-owned tags.</summary>
	public const uint Microsoft = 0x80000000;
	/// <summary>The tag bit identifying name-surrogate reparse points.</summary>
	public const uint NameSurrogate = 0x20000000;

	/// <summary>Returns whether a reparse tag belongs to Microsoft.</summary>
	/// <param name="tag">The reparse tag.</param>
	/// <returns><see langword="true"/> when the Microsoft bit is set.</returns>
	public static bool IsMicrosoftTag( uint tag ) => (tag & Microsoft) != 0;

	/// <summary>Returns whether a reparse tag redirects pathname resolution.</summary>
	/// <param name="tag">The reparse tag.</param>
	/// <returns><see langword="true"/> when the name-surrogate bit is set.</returns>
	public static bool IsNameSurrogateTag( uint tag ) => (tag & NameSurrogate) != 0;

	/// <summary>Returns whether a reparse tag belongs to the Cloud Files placeholder family.</summary>
	/// <param name="tag">The reparse tag.</param>
	/// <returns><see langword="true"/> for <c>IO_REPARSE_TAG_CLOUD</c> through <c>IO_REPARSE_TAG_CLOUD_F</c>.</returns>
	public static bool IsCloudTag( uint tag ) => (tag & ~CloudMask) == Cloud;
}

/// <summary>
/// Describes one physical pathname object's link or Windows reparse-point character without
/// silently equating every reparse point with a symbolic link.
/// </summary>
public sealed class PathIndirectionInfo {
	private const int RecallOnOpenAttribute = 0x00040000;
	private const int RecallOnDataAccessAttribute = 0x00400000;

	private PathIndirectionInfo(
		PathIndirectionKind kind,
		bool isReparsePoint,
		uint? reparseTag,
		bool isDirectory,
		bool isRelativeTarget,
		string? rawTarget,
		string? displayTarget,
		string? volumeGuidPath,
		FileAttributes attributes
	) {
		Kind = kind;
		IsReparsePoint = isReparsePoint;
		ReparseTag = reparseTag;
		IsDirectory = isDirectory;
		IsRelativeTarget = isRelativeTarget;
		RawTarget = rawTarget;
		DisplayTarget = displayTarget;
		VolumeGuidPath = volumeGuidPath;
		Attributes = attributes;
	}

	/// <summary>Gets an observation for an ordinary pathname object.</summary>
	public static PathIndirectionInfo None { get; } = new(
		PathIndirectionKind.None,
		false,
		null,
		false,
		false,
		null,
		null,
		null,
		default
	);

	/// <summary>Gets the characterized indirection kind.</summary>
	public PathIndirectionKind Kind { get; }

	/// <summary>Gets whether the object carries the Windows reparse-point attribute.</summary>
	public bool IsReparsePoint { get; }

	/// <summary>Gets the Windows reparse tag when available.</summary>
	public uint? ReparseTag { get; }

	/// <summary>Gets whether the observed object has directory semantics at the physical pathname.</summary>
	public bool IsDirectory { get; }

	/// <summary>Gets whether the stored target is explicitly relative to the link's parent.</summary>
	public bool IsRelativeTarget { get; }

	/// <summary>Gets the native substitute-name target when available.</summary>
	public string? RawTarget { get; }

	/// <summary>Gets the provider-normalized target suitable for pathname resolution when available.</summary>
	public string? DisplayTarget { get; }

	/// <summary>Gets the volume GUID pathname for a mounted volume when available.</summary>
	public string? VolumeGuidPath { get; }

	/// <summary>Gets the immediate target preferred for pathname resolution.</summary>
	public string? Target => DisplayTarget ?? RawTarget;

	/// <summary>Gets the physical host attributes used during characterization.</summary>
	public FileAttributes Attributes { get; }

	/// <summary>Gets whether the object is specifically a POSIX or Windows symbolic link.</summary>
	public bool IsSymbolicLink => Kind is PathIndirectionKind.PosixSymbolicLink
		or PathIndirectionKind.WindowsSymbolicLink;

	/// <summary>Gets whether the object is a Windows directory junction.</summary>
	public bool IsJunction => Kind == PathIndirectionKind.WindowsJunction;

	/// <summary>Gets whether the object is a mounted Windows volume.</summary>
	public bool IsVolumeMountPoint => Kind == PathIndirectionKind.WindowsVolumeMountPoint;

	/// <summary>Gets whether the object uses the Windows mount-point tag.</summary>
	public bool IsMountPoint => IsJunction || IsVolumeMountPoint;

	/// <summary>Gets whether the object is a recognized Cloud Files placeholder.</summary>
	public bool IsCloudPlaceholder => Kind == PathIndirectionKind.WindowsCloudPlaceholder;

	/// <summary>Gets whether the Windows tag belongs to Microsoft.</summary>
	public bool IsMicrosoftTag => ReparseTag is uint tag && WindowsReparseTags.IsMicrosoftTag( tag );

	/// <summary>Gets whether the object redirects pathname resolution to another named object.</summary>
	public bool IsNameSurrogate => Kind is PathIndirectionKind.WindowsSymbolicLink
		or PathIndirectionKind.WindowsJunction
		or PathIndirectionKind.WindowsVolumeMountPoint
		or PathIndirectionKind.WindowsOtherNameSurrogate;

	/// <summary>Gets whether the object participates in pathname indirection.</summary>
	public bool IsPathIndirection => IsSymbolicLink || IsNameSurrogate;

	/// <summary>Gets whether the shared resolver can safely expand the stored target.</summary>
	public bool CanResolveAsPath => (Kind is PathIndirectionKind.PosixSymbolicLink
		or PathIndirectionKind.WindowsSymbolicLink
		or PathIndirectionKind.WindowsJunction
		or PathIndirectionKind.WindowsVolumeMountPoint)
		&& !string.IsNullOrEmpty( Target );

	/// <summary>Gets whether the entry is a characterized opaque, non-name-surrogate Windows reparse point.</summary>
	public bool IsOpaqueReparsePoint => Kind is PathIndirectionKind.WindowsCloudPlaceholder
		or PathIndirectionKind.WindowsOpaqueReparsePoint;

	/// <summary>Gets whether opening the entry may recall remote data.</summary>
	public bool RecallOnOpen => ((int)Attributes & RecallOnOpenAttribute) != 0;

	/// <summary>Gets whether reading entry data may recall remote content.</summary>
	public bool RecallOnDataAccess => ((int)Attributes & RecallOnDataAccessAttribute) != 0;

	/// <summary>Gets whether the host marks the entry as offline.</summary>
	public bool IsOffline => (Attributes & FileAttributes.Offline) != 0;

	/// <summary>Creates a POSIX symbolic-link characterization.</summary>
	/// <param name="target">The stored link target.</param>
	/// <param name="isDirectory">Whether the host reports directory-like attributes for the link object.</param>
	/// <param name="attributes">The physical host attributes.</param>
	/// <returns>The characterized link.</returns>
	public static PathIndirectionInfo PosixSymbolicLink(
		string? target,
		bool isDirectory = false,
		FileAttributes attributes = default
	) => new(
		PathIndirectionKind.PosixSymbolicLink,
		false,
		null,
		isDirectory,
		!string.IsNullOrEmpty( target ) && !System.IO.Path.IsPathRooted( target ),
		target,
		target,
		null,
		attributes
	);

	/// <summary>Creates a Windows reparse-point characterization from a tag and decoded target data.</summary>
	/// <param name="tag">The Windows reparse tag, or zero when the tag is unavailable.</param>
	/// <param name="isDirectory">Whether the physical object has directory attributes.</param>
	/// <param name="rawTarget">The native substitute-name target.</param>
	/// <param name="displayTarget">The target suitable for pathname resolution.</param>
	/// <param name="isRelativeTarget">Whether the target is relative to the link parent.</param>
	/// <param name="attributes">The physical Windows attributes.</param>
	/// <param name="volumeGuidPath">The volume GUID pathname when the mount-point tag represents a mounted volume.</param>
	/// <returns>The characterized reparse point.</returns>
	public static PathIndirectionInfo WindowsReparsePoint(
		uint tag,
		bool isDirectory,
		string? rawTarget = null,
		string? displayTarget = null,
		bool isRelativeTarget = false,
		FileAttributes attributes = default,
		string? volumeGuidPath = null
	) {
		var kind = tag switch {
			0 => PathIndirectionKind.Unknown,
			WindowsReparseTags.SymbolicLink => PathIndirectionKind.WindowsSymbolicLink,
			WindowsReparseTags.MountPoint when !string.IsNullOrEmpty( volumeGuidPath ) =>
				PathIndirectionKind.WindowsVolumeMountPoint,
			WindowsReparseTags.MountPoint => PathIndirectionKind.WindowsJunction,
			_ when WindowsReparseTags.IsCloudTag( tag ) => PathIndirectionKind.WindowsCloudPlaceholder,
			_ when tag != 0 && WindowsReparseTags.IsNameSurrogateTag( tag ) =>
				PathIndirectionKind.WindowsOtherNameSurrogate,
			_ => PathIndirectionKind.WindowsOpaqueReparsePoint,
		};
		return new PathIndirectionInfo(
			kind,
			true,
			tag == 0 ? null : tag,
			isDirectory,
			isRelativeTarget,
			rawTarget,
			displayTarget,
			volumeGuidPath,
			attributes | FileAttributes.ReparsePoint
		);
	}

	/// <summary>Creates an uncharacterized host-indirection observation.</summary>
	/// <param name="target">The target text when available.</param>
	/// <param name="attributes">The physical host attributes.</param>
	/// <returns>The uncharacterized observation.</returns>
	public static PathIndirectionInfo Unknown(
		string? target = null,
		FileAttributes attributes = default
	) => new(
		PathIndirectionKind.Unknown,
		(attributes & FileAttributes.ReparsePoint) != 0,
		null,
		(attributes & FileAttributes.Directory) != 0,
		!string.IsNullOrEmpty( target ) && !System.IO.Path.IsPathRooted( target ),
		target,
		target,
		null,
		attributes
	);
}

/// <summary>Inspects one physical pathname object without dereferencing a terminal indirection.</summary>
public interface IPathIndirectionInspector {
	/// <summary>Characterizes a pathname object's indirection or reparse-point state.</summary>
	/// <param name="path">The pathname to inspect.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The physical indirection characterization.</returns>
	ValueTask<PathIndirectionInfo> InspectAsync(
		string path,
		CancellationToken cancellationToken = default
	);
}
