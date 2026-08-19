namespace Icod.Path;

/// <summary>
/// Resolves absolute lexical and physical paths over an injectable no-follow filesystem provider.
/// The resolver returns structured failures and never returns unresolved input as a successful path.
/// </summary>
public sealed class CanonicalPathResolver {
	private readonly ICanonicalPathFileSystemProvider provider;

	/// <summary>Initializes a resolver over the system filesystem.</summary>
	public CanonicalPathResolver()
		: this( SystemCanonicalPathFileSystemProvider.Instance ) {
	}

	/// <summary>Initializes a resolver over an injected filesystem provider.</summary>
	/// <param name="provider">The no-follow filesystem provider.</param>
	public CanonicalPathResolver( ICanonicalPathFileSystemProvider provider ) {
		ArgumentNullException.ThrowIfNull( provider );
		this.provider = provider;
	}

	/// <summary>Gets the pathname grammar used by this resolver.</summary>
	public PathPlatformSemantics Semantics => this.provider.Semantics;

	/// <summary>Normalizes a pathname lexically without observing the filesystem.</summary>
	/// <param name="path">The input pathname.</param>
	/// <param name="basePath">The absolute base directory, or <see langword="null"/> for the provider current directory.</param>
	/// <returns>The absolute lexical pathname or a structured failure.</returns>
	public CanonicalPathResult NormalizeLexically(
		string path,
		string? basePath = null
	) {
		ArgumentNullException.ThrowIfNull( path );
		return PathLexicalNormalizer.Normalize(
			path,
			basePath ?? this.provider.CurrentDirectory,
			this.provider.Semantics
		);
	}

	/// <summary>Resolves a pathname physically by processing components in order and following every supported link.</summary>
	/// <param name="path">The input pathname.</param>
	/// <param name="options">The resolution options, or <see langword="null"/> for strict defaults.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The physical canonical path or a structured failure.</returns>
	public async ValueTask<CanonicalPathResult> ResolvePhysicalAsync(
		string path,
		CanonicalPathResolutionOptions? options = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( path );
		options ??= new CanonicalPathResolutionOptions();
		if ( 0 > options.MaximumSymbolicLinks ) {
			throw new ArgumentOutOfRangeException(
				nameof( CanonicalPathResolutionOptions.MaximumSymbolicLinks ),
				"maximum symbolic links cannot be negative"
			);
		}
		var prepared = PathLexicalNormalizer.PrepareForPhysicalResolution(
			path,
			options.BasePath ?? this.provider.CurrentDirectory,
			this.provider.Semantics
		);
		if ( !prepared.Succeeded ) {
			return CanonicalPathResult.Failed( prepared.Failure! );
		}
		var preparedParts = prepared.Parts!;
		var rootPath = preparedParts.RootPath;
		var volumeName = preparedParts.VolumeName;
		var remaining = new LinkedList<string>( preparedParts.Components );
		var resolved = new List<string>();
		var missingComponentCount = 0;
		var followedLinks = new List<ResolvedPathLink>();
		var visitedStates = new HashSet<string>( this.provider.Semantics.PathComparer );
		var rootFailure = await this.ObserveRootAsync(
			rootPath,
			cancellationToken
		).ConfigureAwait( false );
		if ( null != rootFailure ) {
			return CanonicalPathResult.Failed( rootFailure );
		}
		while ( 0 < remaining.Count ) {
			cancellationToken.ThrowIfCancellationRequested();
			var state = BuildResolutionState(
				rootPath,
				resolved,
				remaining,
				missingComponentCount
			);
			if ( !visitedStates.Add( state ) ) {
				return CanonicalPathResult.Failed(
					new CanonicalPathFailure(
						CanonicalPathFailureCode.SymbolicLinkLoop,
						ComposePath(
							rootPath,
							resolved.Append( remaining.First!.Value ),
							this.provider.Semantics
						),
						"the symbolic-link chain contains a resolution loop"
					)
				);
			}
			var component = remaining.First!.Value;
			remaining.RemoveFirst();
			if ( "." == component || 0 == component.Length ) {
				continue;
			}
			if ( ".." == component ) {
				if ( 0 < resolved.Count ) {
					resolved.RemoveAt( resolved.Count - 1 );
					if ( 0 < missingComponentCount ) {
						missingComponentCount--;
					}
				}
				continue;
			}
			if ( 0 < missingComponentCount ) {
				resolved.Add( component );
				missingComponentCount++;
				continue;
			}
			var candidateComponents = new List<string>( resolved.Count + 1 );
			candidateComponents.AddRange( resolved );
			candidateComponents.Add( component );
			var candidate = ComposePath(
				rootPath,
				candidateComponents,
				this.provider.Semantics
			);
			var observation = await this.provider.ObserveAsync(
				candidate,
				cancellationToken
			).ConfigureAwait( false );
			if ( !observation.ObservationSucceeded ) {
				return CanonicalPathResult.Failed(
					observation.Failure
					?? new CanonicalPathFailure(
						CanonicalPathFailureCode.IoError,
						candidate,
						"the pathname could not be inspected"
					)
				);
			}
			if ( !observation.Exists ) {
				if (
					MissingPathComponentPolicy.RequireExisting
					== options.MissingComponentPolicy
				) {
					return MissingFailure( candidate );
				}
				if (
					MissingPathComponentPolicy.AllowFinalComponent
					== options.MissingComponentPolicy
					&& remaining.Any( value => "." != value && 0 != value.Length )
				) {
					return MissingFailure( candidate );
				}
				resolved.Add( component );
				missingComponentCount = 1;
				continue;
			}
			var unsupportedReparsePoint = observation.IsReparsePoint
				&& observation.Indirection.Kind is (
					PathIndirectionKind.WindowsOtherNameSurrogate
					or PathIndirectionKind.Unknown
				);
			if ( unsupportedReparsePoint ) {
				if ( 0 < remaining.Count ) {
					return CanonicalPathResult.Failed(
						new CanonicalPathFailure(
							CanonicalPathFailureCode.UnsupportedReparsePoint,
							candidate,
							"the nonterminal reparse point cannot be traversed without supported target semantics"
						)
					);
				}
				if ( !options.FollowSymbolicLinks ) {
					if ( options.RequireFinalDirectory ) {
						return CanonicalPathResult.Failed(
							new CanonicalPathFailure(
								CanonicalPathFailureCode.UnsupportedReparsePoint,
								candidate,
								"the reparse point cannot be verified as a directory"
							)
						);
					}
					resolved.Add( component );
					continue;
				}
				if ( !options.RejectUnsupportedFinalReparsePoint ) {
					resolved.Add( component );
					continue;
				}
				return CanonicalPathResult.Failed(
					new CanonicalPathFailure(
						CanonicalPathFailureCode.UnsupportedReparsePoint,
						candidate,
						"the reparse point does not expose a supported pathname target"
					)
				);
			}
			if ( observation.IsPathIndirection ) {
				if ( !options.FollowSymbolicLinks ) {
					if (
						0 == remaining.Count
						&& (
							MissingPathComponentPolicy.RequireExisting
							== options.MissingComponentPolicy
							|| options.RequireFinalDirectory
						)
					) {
						var retainedLinkFailure = await this.ValidateRetainedFinalLinkAsync(
							candidate,
							options,
							cancellationToken
						).ConfigureAwait( false );
						if ( null != retainedLinkFailure ) {
							return CanonicalPathResult.Failed( retainedLinkFailure );
						}
					}
					resolved.Add( component );
					continue;
				}
				if ( 0 == remaining.Count && !options.FollowFinalSymbolicLink ) {
					resolved.Add( component );
					continue;
				}
				if ( null == observation.LinkTarget ) {
					return CanonicalPathResult.Failed(
						new CanonicalPathFailure(
							CanonicalPathFailureCode.LinkTargetUnavailable,
							candidate,
							"the pathname-indirection target is unavailable"
						)
					);
				}
				if ( followedLinks.Count >= options.MaximumSymbolicLinks ) {
					return CanonicalPathResult.Failed(
						new CanonicalPathFailure(
							CanonicalPathFailureCode.TooManySymbolicLinks,
							candidate,
							"the pathname-indirection traversal limit was exceeded"
						)
					);
				}
				var parent = ComposePath(
					rootPath,
					resolved,
					this.provider.Semantics
				);
				var target = PathLexicalNormalizer.PrepareForPhysicalResolution(
					observation.LinkTarget,
					parent,
					this.provider.Semantics
				);
				if ( !target.Succeeded ) {
					return CanonicalPathResult.Failed(
						new CanonicalPathFailure(
							CanonicalPathFailureCode.LinkTargetUnavailable,
							candidate,
							"the pathname-indirection target is not a valid pathname"
						)
					);
				}
				var lexicalTarget = PathLexicalNormalizer.Normalize(
					observation.LinkTarget,
					parent,
					this.provider.Semantics
				);
				if ( !lexicalTarget.Succeeded ) {
					return CanonicalPathResult.Failed(
						new CanonicalPathFailure(
							CanonicalPathFailureCode.LinkTargetUnavailable,
							candidate,
							"the pathname-indirection target is not a valid pathname"
						)
					);
				}
				followedLinks.Add(
					new ResolvedPathLink(
						candidate,
						observation.LinkTarget,
						lexicalTarget.Path!
					)
				);
				var targetParts = target.Parts!;
				var originalRemainder = remaining.ToArray();
				remaining.Clear();
				if ( targetParts.UsesBaseComponents ) {
					foreach ( var targetComponent in targetParts.Components.Skip( resolved.Count ) ) {
						remaining.AddLast( targetComponent );
					}
				} else {
					rootPath = targetParts.RootPath;
					volumeName = targetParts.VolumeName;
					resolved.Clear();
					missingComponentCount = 0;
					foreach ( var targetComponent in targetParts.Components ) {
						remaining.AddLast( targetComponent );
					}
					rootFailure = await this.ObserveRootAsync(
						rootPath,
						cancellationToken
					).ConfigureAwait( false );
					if ( null != rootFailure ) {
						return CanonicalPathResult.Failed( rootFailure );
					}
				}
				foreach ( var remainderComponent in originalRemainder ) {
					remaining.AddLast( remainderComponent );
				}
				continue;
			}
			if (
				0 < remaining.Count
				&& CanonicalPathEntryKind.Directory != observation.Kind
			) {
				return CanonicalPathResult.Failed(
					new CanonicalPathFailure(
						CanonicalPathFailureCode.NotDirectory,
						candidate,
						"a nonfinal pathname component is not a directory"
					)
				);
			}
			if (
				0 == remaining.Count
				&& options.RequireFinalDirectory
				&& CanonicalPathEntryKind.Directory != observation.Kind
			) {
				return CanonicalPathResult.Failed(
					new CanonicalPathFailure(
						CanonicalPathFailureCode.NotDirectory,
						candidate,
						"the final pathname component is not a directory"
					)
				);
			}
			resolved.Add( component );
		}
		return CanonicalPathResult.Success(
			ComposePath( rootPath, resolved, this.provider.Semantics ),
			new PathRootInfo( rootPath, volumeName ),
			followedLinks,
			missingComponentCount
		);
	}

	/// <summary>Inspects a terminal pathname object without dereferencing pathname indirection.</summary>
	/// <param name="path">The input pathname.</param>
	/// <param name="basePath">The absolute base directory, or <see langword="null"/> for the provider current directory.</param>
	/// <param name="cancellationToken">The cancellation token.</param>
	/// <returns>The link and reparse-point inspection.</returns>
	public async ValueTask<PathLinkInspectionResult> InspectLinkAsync(
		string path,
		string? basePath = null,
		CancellationToken cancellationToken = default
	) {
		ArgumentNullException.ThrowIfNull( path );
		var resolved = await this.ResolvePhysicalAsync(
			path,
			new CanonicalPathResolutionOptions {
				BasePath = basePath,
				MissingComponentPolicy = MissingPathComponentPolicy.RequireExisting,
				FollowFinalSymbolicLink = false,
				RejectUnsupportedFinalReparsePoint = false
			},
			cancellationToken
		).ConfigureAwait( false );
		if ( !resolved.Succeeded ) {
			return PathLinkInspectionResult.Failed( resolved.Failure! );
		}
		var observation = await this.provider.ObserveAsync(
			resolved.Path!,
			cancellationToken
		).ConfigureAwait( false );
		if ( !observation.ObservationSucceeded ) {
			return PathLinkInspectionResult.Failed(
				observation.Failure
				?? new CanonicalPathFailure(
					CanonicalPathFailureCode.IoError,
					resolved.Path!,
					"the pathname could not be inspected"
				)
			);
		}
		if ( !observation.Exists ) {
			return PathLinkInspectionResult.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.NotFound,
					resolved.Path!,
					"the pathname object does not exist"
				)
			);
		}
		return PathLinkInspectionResult.Success(
			resolved.Path!,
			observation.Kind,
			observation.Indirection
		);
	}

	/// <summary>Calculates a lexical relative pathname from one absolute directory to another path.</summary>
	/// <param name="relativeToDirectory">The source directory.</param>
	/// <param name="targetPath">The target pathname.</param>
	/// <returns>The relative pathname or a structured different-root failure.</returns>
	public RelativePathResult GetRelativePath(
		string relativeToDirectory,
		string targetPath
	) {
		ArgumentNullException.ThrowIfNull( relativeToDirectory );
		ArgumentNullException.ThrowIfNull( targetPath );
		var source = PathLexicalNormalizer.NormalizeToParts(
			relativeToDirectory,
			this.provider.CurrentDirectory,
			this.provider.Semantics
		);
		if ( !source.Succeeded ) {
			return RelativePathResult.Failed( source.Failure! );
		}
		var target = PathLexicalNormalizer.NormalizeToParts(
			targetPath,
			this.provider.CurrentDirectory,
			this.provider.Semantics
		);
		if ( !target.Succeeded ) {
			return RelativePathResult.Failed( target.Failure! );
		}
		var sourceParts = source.Parts!;
		var targetParts = target.Parts!;
		if ( !HaveSameRoot( sourceParts, targetParts, this.provider.Semantics ) ) {
			return RelativePathResult.Failed(
				new CanonicalPathFailure(
					CanonicalPathFailureCode.DifferentRoot,
					targetParts.Path,
					"a relative pathname cannot cross roots or volumes"
				)
			);
		}
		var common = GetCommonComponentCount(
			sourceParts,
			targetParts,
			this.provider.Semantics
		);
		var result = new List<string>();
		for ( var index = common; index < sourceParts.Components.Count; index++ ) {
			result.Add( ".." );
		}
		for ( var index = common; index < targetParts.Components.Count; index++ ) {
			result.Add( targetParts.Components[index] );
		}
		return RelativePathResult.Success(
			0 == result.Count
				? "."
				: string.Join(
					this.provider.Semantics.DirectorySeparator,
					result
				)
		);
	}

	/// <summary>Evaluates component-aware containment between two canonical or lexically normalizable paths.</summary>
	/// <param name="rootDirectory">The proposed containing directory.</param>
	/// <param name="candidatePath">The proposed contained pathname.</param>
	/// <returns>A containment result or a structured normalization failure.</returns>
	public PathContainmentResult EvaluateContainment(
		string rootDirectory,
		string candidatePath
	) {
		ArgumentNullException.ThrowIfNull( rootDirectory );
		ArgumentNullException.ThrowIfNull( candidatePath );
		var root = PathLexicalNormalizer.NormalizeToParts(
			rootDirectory,
			this.provider.CurrentDirectory,
			this.provider.Semantics
		);
		if ( !root.Succeeded ) {
			return PathContainmentResult.Failed( root.Failure! );
		}
		var candidate = PathLexicalNormalizer.NormalizeToParts(
			candidatePath,
			this.provider.CurrentDirectory,
			this.provider.Semantics
		);
		if ( !candidate.Succeeded ) {
			return PathContainmentResult.Failed( candidate.Failure! );
		}
		var rootParts = root.Parts!;
		var candidateParts = candidate.Parts!;
		if ( !HaveSameRoot( rootParts, candidateParts, this.provider.Semantics ) ) {
			return PathContainmentResult.Success( false );
		}
		if ( rootParts.Components.Count > candidateParts.Components.Count ) {
			return PathContainmentResult.Success( false );
		}
		for ( var index = 0; index < rootParts.Components.Count; index++ ) {
			if ( !string.Equals(
				rootParts.Components[index],
				candidateParts.Components[index],
				this.provider.Semantics.PathComparison
			) ) {
				return PathContainmentResult.Success( false );
			}
		}
		return PathContainmentResult.Success( true );
	}

	private async ValueTask<CanonicalPathFailure?> ValidateRetainedFinalLinkAsync(
		string candidate,
		CanonicalPathResolutionOptions options,
		CancellationToken cancellationToken
	) {
		var validation = await this.ResolvePhysicalAsync(
			candidate,
			new CanonicalPathResolutionOptions {
				MissingComponentPolicy = MissingPathComponentPolicy.RequireExisting,
				MaximumSymbolicLinks = options.MaximumSymbolicLinks,
				FollowSymbolicLinks = true,
				FollowFinalSymbolicLink = true,
				RequireFinalDirectory = options.RequireFinalDirectory,
				RejectUnsupportedFinalReparsePoint = options.RejectUnsupportedFinalReparsePoint
			},
			cancellationToken
		).ConfigureAwait( false );
		return validation.Failure;
	}

	private async ValueTask<CanonicalPathFailure?> ObserveRootAsync(
		string rootPath,
		CancellationToken cancellationToken
	) {
		cancellationToken.ThrowIfCancellationRequested();
		var observation = await this.provider.ObserveAsync(
			rootPath,
			cancellationToken
		).ConfigureAwait( false );
		if ( !observation.ObservationSucceeded ) {
			return observation.Failure
				?? new CanonicalPathFailure(
					CanonicalPathFailureCode.IoError,
					rootPath,
					"the pathname root could not be inspected"
				)
			;
		}
		if ( !observation.Exists ) {
			return new CanonicalPathFailure(
				CanonicalPathFailureCode.NotFound,
				rootPath,
				"the pathname root does not exist"
			);
		}
		if ( CanonicalPathEntryKind.Directory != observation.Kind ) {
			return new CanonicalPathFailure(
				CanonicalPathFailureCode.NotDirectory,
				rootPath,
				"the pathname root is not a directory"
			);
		}
		return null;
	}

	private static CanonicalPathResult MissingFailure( string path ) =>
		CanonicalPathResult.Failed(
			new CanonicalPathFailure(
				CanonicalPathFailureCode.NotFound,
				path,
				"a required pathname component does not exist"
			)
		)
	;

	private static string ComposePath(
		string rootPath,
		IEnumerable<string> components,
		PathPlatformSemantics semantics
	) {
		var values = components as IReadOnlyCollection<string> ?? components.ToArray();
		return 0 == values.Count
			? rootPath
			: string.Concat(
				rootPath,
				string.Join( semantics.DirectorySeparator, values )
			)
		;
	}

	private static string BuildResolutionState(
		string rootPath,
		IReadOnlyList<string> resolved,
		IEnumerable<string> remaining,
		int missingComponentCount
	) {
		var remainder = remaining as IReadOnlyCollection<string> ?? remaining.ToArray();
		var builder = new System.Text.StringBuilder();
		AppendStatePart( builder, rootPath );
		builder.Append( missingComponentCount );
		builder.Append( ';' );
		builder.Append( resolved.Count );
		builder.Append( ';' );
		foreach ( var component in resolved ) {
			AppendStatePart( builder, component );
		}
		builder.Append( remainder.Count );
		builder.Append( ';' );
		foreach ( var component in remainder ) {
			AppendStatePart( builder, component );
		}
		return builder.ToString();
	}

	private static void AppendStatePart(
		System.Text.StringBuilder builder,
		string value
	) {
		builder.Append( value.Length );
		builder.Append( ':' );
		builder.Append( value );
		builder.Append( ';' );
	}

	private static bool HaveSameRoot(
		LexicalPathParts first,
		LexicalPathParts second,
		PathPlatformSemantics semantics
	) => string.Equals(
		first.VolumeName,
		second.VolumeName,
		semantics.PathComparison
	);

	private static int GetCommonComponentCount(
		LexicalPathParts first,
		LexicalPathParts second,
		PathPlatformSemantics semantics
	) {
		var common = 0;
		while (
			common < first.Components.Count
			&& common < second.Components.Count
			&& string.Equals(
				first.Components[common],
				second.Components[common],
				semantics.PathComparison
			)
		) {
			common++;
		}
		return common;
	}
}
