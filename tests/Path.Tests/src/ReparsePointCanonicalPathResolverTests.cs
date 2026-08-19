using Xunit;

namespace Icod.Path.Tests;

/// <summary>Exercises deterministic canonicalization policy for characterized reparse points.</summary>
public sealed class ReparsePointCanonicalPathResolverTests {
	/// <summary>Verifies recognized opaque reparse points remain inspectable ordinary path objects.</summary>
	[Fact]
	public async Task PreservesRecognizedOpaqueReparsePoint() {
		var provider = new ReparsePointProvider(
			new Dictionary<string, PathComponentObservation>( StringComparer.Ordinal ) {
				["/"] = PathComponentObservation.Existing(
					"/",
					CanonicalPathEntryKind.Directory,
					PathIndirectionInfo.None
				),
				["/cloud"] = PathComponentObservation.Existing(
					"/cloud",
					CanonicalPathEntryKind.File,
					PathIndirectionInfo.WindowsReparsePoint(
						WindowsReparseTags.Cloud,
						false
					)
				),
			}
		);
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync( "/cloud" );

		Assert.True( result.Succeeded );
		Assert.Equal( "/cloud", result.Path );
		Assert.Empty( result.ResolvedLinks );
	}

	/// <summary>Verifies an unknown name surrogate cannot be traversed as an ordinary directory.</summary>
	[Fact]
	public async Task RejectsNonterminalUnknownNameSurrogate() {
		const uint customNameSurrogate = WindowsReparseTags.NameSurrogate | 0x00000042;
		var provider = new ReparsePointProvider(
			new Dictionary<string, PathComponentObservation>( StringComparer.Ordinal ) {
				["/"] = PathComponentObservation.Existing(
					"/",
					CanonicalPathEntryKind.Directory,
					PathIndirectionInfo.None
				),
				["/provider"] = PathComponentObservation.Existing(
					"/provider",
					CanonicalPathEntryKind.Directory,
					PathIndirectionInfo.WindowsReparsePoint(
						customNameSurrogate,
						true,
						rawTarget: "provider-defined"
					)
				),
			}
		);
		var resolver = new CanonicalPathResolver( provider );

		var result = await resolver.ResolvePhysicalAsync(
			"/provider/child",
			new CanonicalPathResolutionOptions {
				FollowSymbolicLinks = false,
			}
		);

		Assert.False( result.Succeeded );
		Assert.Equal( CanonicalPathFailureCode.UnsupportedReparsePoint, result.Failure!.Code );
		Assert.Equal( "/provider", result.Failure.Path );
	}

	private sealed class ReparsePointProvider : ICanonicalPathFileSystemProvider {
		private readonly IReadOnlyDictionary<string, PathComponentObservation> observations;

		/// <summary>Initializes the deterministic provider.</summary>
		/// <param name="observations">The observations keyed by absolute pathname.</param>
		public ReparsePointProvider(
			IReadOnlyDictionary<string, PathComponentObservation> observations
		) {
			this.observations = observations;
		}

		/// <inheritdoc/>
		public PathPlatformSemantics Semantics => PathPlatformSemantics.Posix;

		/// <inheritdoc/>
		public string CurrentDirectory => "/";

		/// <inheritdoc/>
		public ValueTask<PathComponentObservation> ObserveAsync(
			string path,
			CancellationToken cancellationToken = default
		) {
			cancellationToken.ThrowIfCancellationRequested();
			return ValueTask.FromResult(
				this.observations.TryGetValue( path, out var observation )
					? observation
					: PathComponentObservation.Missing( path )
			);
		}
	}
}
